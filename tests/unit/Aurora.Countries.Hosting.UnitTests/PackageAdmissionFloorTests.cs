using System;
using System.IO;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// ADR-0033 §5.6 D1: the admission floor fires, and fires <b>before execution</b>.
/// </summary>
/// <remarks>
/// <para>
/// A package whose signature does not verify, or whose established trust is below a tenant-routing
/// host's floor, is refused without its code running. The assertion is never "a failed
/// <see cref="Result"/> came back" — a result says the loader returned a failure, not that the
/// assembly never got a thread. It is that the hostile fixture's <b>module initialiser did not
/// execute</b>: a process-wide record the fixture writes on its first execution, observed absent.
/// The positive control below proves the record is written when the same package is admitted, so
/// "absent" cannot mean "the witness is broken".
/// </para>
/// <para>
/// <b>What this class does not demonstrate — the non-claim, recorded deliberately.</b> None of these
/// tests shows that a package cannot reach tenant data, because that is not true. A package that
/// is admitted runs in-process with the full permissions of the process: it can read the
/// configuration and secret material the process can read and open its own connection to any
/// tenant database, naming no tenancy type at all (ADR-0033 §5.4 R1–R3). .NET provides no
/// in-process privilege boundary against loaded managed code (ADR-0033 §4). These tests
/// demonstrate the admission control only: that what is below the floor never executes here. They
/// close nothing about what executes once admitted. A reader who takes them as a sandbox proof has
/// read a claim this suite does not make.
/// </para>
/// </remarks>
public sealed class PackageAdmissionFloorTests
{
    /// <summary>
    /// The convention <c>Aurora.Countries.HostilePackage.ModuleInitialiserWitness</c> writes under:
    /// this prefix, then the full path of the assembly file that ran. Duplicated here rather than
    /// referenced, because referencing a type from the fixture would load it into this context and
    /// run the very initialiser under observation. The positive control keeps the two in step.
    /// </summary>
    private const string WitnessKeyPrefix = "Aurora.Countries.HostilePackage.ModuleInitialiserRan|";

    private readonly ITestOutputHelper _output;

    public PackageAdmissionFloorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The positive control, and the reason every "not observed" assertion below means something:
    /// an admitted package's module initialiser does run, and the witness does see it. Take this
    /// test away and the refusal tests could pass with a witness that never fires.
    /// </summary>
    [Fact]
    public void The_witness_is_live_an_admitted_package_s_module_initialiser_runs()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey firstParty) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.DeployHostile();
        package.SignWith(key);

        ModuleInitialiserRan(package).ShouldBeFalse("the witness was set before anything was loaded");

        using LoadedCountryPackage loaded =
            Ok(TenantRoutingLoader(package, [firstParty]).Load(package.Directory));

        loaded.Trust.Level.ShouldBe(PackageTrustLevel.FirstParty);
        ModuleInitialiserRan(package).ShouldBeTrue(
            "the package was admitted and constructed, yet its module initialiser left no record; " +
            "either module initialisers do not run on load, or the witness key convention has drifted");

        _output.WriteLine($"D1 positive control: admitted as {loaded.Trust}; module initialiser ran: true");
    }

    /// <summary>
    /// The floor itself. On a host that routes tenants, a package whose signature establishes
    /// <see cref="PackageTrustLevel.Partner"/> is inspected and listed (ADR-0033 §5.2 narrows where
    /// the level takes effect, it does not remove it) — and refused at load, before its module
    /// initialiser has run.
    /// </summary>
    [Fact]
    public void A_partner_signed_package_is_listed_but_refused_by_the_floor_before_its_module_initialiser_runs()
    {
        using TestTrustStore store = new();
        (ECDsa partnerKey, TrustedPackageKey partner) = store.Add(PackageTrustLevel.Partner);
        (_, TrustedPackageKey firstParty) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.DeployHostile();
        package.SignWith(partnerKey);

        CountryPackageLoader loader = TenantRoutingLoader(package, [partner, firstParty]);

        InspectedPackage listed = Ok(loader.Inspect(package.Directory));
        listed.Trust.Level.ShouldBe(PackageTrustLevel.Partner, "inspection establishes trust; it does not apply the floor");

        Result<LoadedCountryPackage> loaded = loader.Load(package.Directory);

        loaded.IsFailure.ShouldBeTrue("a tenant-routing host loaded a package whose signature establishes only Partner");
        loaded.Error.Code.ShouldBe(HostingErrors.BelowAdmissionFloorCode);
        loaded.Error.Description.ShouldContain(nameof(PackageTrustLevel.Partner));
        loaded.Error.Description.ShouldContain(nameof(PackageTrustLevel.FirstParty));
        loaded.Error.Description.ShouldContain("routes tenants");
        loaded.Error.Description.ShouldContain("ADR-0033");

        ModuleInitialiserRan(package).ShouldBeFalse(
            "the refusal came back, but the package's module initialiser had already run: the floor " +
            "fired after execution, which is not a refusal (ADR-0033 §5.6 D1)");

        _output.WriteLine($"D1: refused by {loaded.Error.Code}; module initialiser ran: false");
    }

    /// <summary>
    /// The other half of D1: a package whose signature does not verify at all is refused by the
    /// signature check, before execution, on the same host. Three ways for a signature not to
    /// verify, each a case; which of them is in play is the fixed dimension, the key is generated.
    /// </summary>
    [Theory]
    [InlineData(SignatureFault.Missing)]
    [InlineData(SignatureFault.SignedByAKeyThisHostDoesNotTrust)]
    [InlineData(SignatureFault.TamperedAfterSigning)]
    public void A_package_whose_signature_does_not_verify_is_refused_before_its_module_initialiser_runs(
        SignatureFault fault)
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey firstParty) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.DeployHostile();
        switch (fault)
        {
            case SignatureFault.Missing:
                break;
            case SignatureFault.SignedByAKeyThisHostDoesNotTrust:
                using (ECDsa stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    package.SignWith(stranger);
                }

                break;
            case SignatureFault.TamperedAfterSigning:
                package.SignWith(key);
                package.TamperWithTheAssembly();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, "Not a signature fault this test knows.");
        }

        Result<LoadedCountryPackage> loaded =
            TenantRoutingLoader(package, [firstParty]).Load(package.Directory);

        loaded.IsFailure.ShouldBeTrue($"a package with signature fault {fault} was loaded");
        loaded.Error.Code.ShouldBe(HostingErrors.UntrustedCode);

        ModuleInitialiserRan(package).ShouldBeFalse(
            $"the signature check refused the package ({fault}), but its module initialiser had " +
            $"already run: the refusal happened after execution");

        _output.WriteLine($"D1 ({fault}): refused by {loaded.Error.Code}; module initialiser ran: false");
    }

    /// <summary>
    /// The narrowing, pinned from the other side: the same Partner-signed package loads on a host
    /// that routes no tenants, so the refusal above is the floor's and not a general refusal of
    /// Partner. Without this, the floor test could pass because Partner had quietly stopped being
    /// loadable anywhere.
    /// </summary>
    [Fact]
    public void The_same_partner_package_loads_on_a_host_that_routes_no_tenants()
    {
        using TestTrustStore store = new();
        (ECDsa partnerKey, TrustedPackageKey partner) = store.Add(PackageTrustLevel.Partner);

        using PackageOnDisk package = PackageOnDisk.DeployHostile();
        package.SignWith(partnerKey);

        CountryPackageLoader loader = new(Ok(CountryPackageHostOptions.Create(
            package.Root,
            "Production",
            allowUnsigned: false,
            trustedKeys: [partner],
            routesTenants: false)));

        using LoadedCountryPackage loaded = Ok(loader.Load(package.Directory));

        loaded.Trust.Level.ShouldBe(PackageTrustLevel.Partner);
        ModuleInitialiserRan(package).ShouldBeTrue("the package was admitted, so its module initialiser ran");
    }

    private static bool ModuleInitialiserRan(PackageOnDisk package) =>
        AppDomain.CurrentDomain.GetData(WitnessKeyPrefix + Path.GetFullPath(package.AssemblyPath)) is true;

    private static CountryPackageLoader TenantRoutingLoader(
        PackageOnDisk package,
        TrustedPackageKey[] keys) =>
        new(Ok(CountryPackageHostOptions.Create(
            package.Root,
            "Production",
            allowUnsigned: false,
            trustedKeys: keys,
            routesTenants: true)));

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Expected success, got: {result.Error}");
}

/// <summary>The ways a signature can fail to verify that D1 exercises.</summary>
public enum SignatureFault
{
    /// <summary>No <c>package.sig</c> at all.</summary>
    Missing,

    /// <summary>Signed, by a key that is in nobody's trust store.</summary>
    SignedByAKeyThisHostDoesNotTrust,

    /// <summary>Signed by a trusted key, then one byte of the assembly changed.</summary>
    TamperedAfterSigning,
}
