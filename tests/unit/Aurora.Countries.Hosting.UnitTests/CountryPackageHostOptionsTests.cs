using System;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// The configuration test ADR-0008 §9.3 asks for by name, "because a flag that only a comment
/// prevents from reaching production will reach production".
/// </summary>
public sealed class CountryPackageHostOptionsTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("production")]
    public void Allowing_unsigned_packages_outside_development_is_refused(string environment)
    {
        Result<CountryPackageHostOptions> options = CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            environment,
            allowUnsigned: true,
            trustedKeys: [],
            routesTenants: false);

        options.IsFailure.ShouldBeTrue();
        options.Error.Code.ShouldBe(HostingErrors.HostConfigurationCode);
        options.Error.Description.ShouldContain(environment);
        options.Error.Description.ShouldContain("AllowUnsigned");
    }

    /// <summary>
    /// The one case ADR-0008 §9.3 honours — and, since ADR-0033 §5.2, only on a host that routes
    /// no tenants. The tenant-routing case is <see cref="A_tenant_routing_host_that_allows_unsigned_packages_is_refused_in_every_environment"/>.
    /// </summary>
    [Fact]
    public void Allowing_unsigned_packages_in_development_is_honoured_on_a_host_that_routes_no_tenants()
    {
        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            CountryPackageHostOptions.DevelopmentEnvironmentName,
            allowUnsigned: true,
            trustedKeys: [],
            routesTenants: false));

        options.AllowUnsigned.ShouldBeTrue();
        options.IsDevelopment.ShouldBeTrue();
        options.RoutesTenants.ShouldBeFalse();
        options.AdmissionFloor.ShouldBe(PackageTrustLevel.Unsigned);
    }

    /// <summary>
    /// No keys and no unsigned packages means nothing could ever load. That is a configuration
    /// mistake rather than a lockdown, and a host that started anyway would look healthy and install
    /// nothing.
    /// </summary>
    [Fact]
    public void A_host_that_could_never_load_anything_is_refused()
    {
        CountryPackageHostOptions.Create("/srv/aurora/packages", "Production", false, [], routesTenants: false)
            .IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "Production")]
    [InlineData("", "Production")]
    [InlineData("/srv/aurora/packages", null)]
    [InlineData("/srv/aurora/packages", " ")]
    public void A_host_that_does_not_know_where_or_what_it_is_is_refused(string? directory, string? environment) =>
        CountryPackageHostOptions.Create(directory, environment, false, [], routesTenants: false)
            .IsFailure.ShouldBeTrue();

    [Fact]
    public void The_same_key_configured_twice_is_refused()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey key) = store.Add(PackageTrustLevel.FirstParty);

        Result<CountryPackageHostOptions> options = CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            "Production",
            allowUnsigned: false,
            trustedKeys: [key, key],
            routesTenants: true);

        options.IsFailure.ShouldBeTrue();
        options.Error.Description.ShouldContain(key.Thumbprint);
    }

    /// <summary>
    /// ADR-0033 §5.6 D3: the admission floor cannot be configured away. A host that routes tenants
    /// and is told to load unsigned packages refuses to start, in every environment — Development
    /// included, because there is no environment in which a package below the floor is outside the
    /// tenancy trust boundary. Same shape and same reason as <c>AllowUnsigned</c> outside
    /// Development (ADR-0008 §9.3): a flag that only a comment prevents from reaching production
    /// will reach production.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_tenant_routing_host_that_allows_unsigned_packages_is_refused_in_every_environment(string environment)
    {
        Result<CountryPackageHostOptions> options = CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            environment,
            allowUnsigned: true,
            trustedKeys: [],
            routesTenants: true);

        options.IsFailure.ShouldBeTrue(
            $"a host that routes tenants was allowed to start with AllowUnsigned in '{environment}'");
        options.Error.Code.ShouldBe(HostingErrors.HostConfigurationCode);
        options.Error.Description.ShouldContain("AllowUnsigned");
        options.Error.Description.ShouldContain("routes tenants");
        options.Error.Description.ShouldContain(nameof(PackageTrustLevel.FirstParty));
        options.Error.Description.ShouldContain("ADR-0033");
    }

    /// <summary>
    /// The existing "could never load anything" rule, applied with the floor: a tenant-routing host
    /// whose only trusted key establishes <see cref="PackageTrustLevel.Partner"/> could inspect and
    /// list packages but never load one. That is a configuration mistake, not a lockdown, and a
    /// host that started anyway would look healthy and install nothing.
    /// </summary>
    [Fact]
    public void A_tenant_routing_host_with_no_key_at_the_floor_could_never_load_anything_and_is_refused()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey partner) = store.Add(PackageTrustLevel.Partner);

        Result<CountryPackageHostOptions> options = CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            "Production",
            allowUnsigned: false,
            trustedKeys: [partner],
            routesTenants: true);

        options.IsFailure.ShouldBeTrue();
        options.Error.Code.ShouldBe(HostingErrors.HostConfigurationCode);
        options.Error.Description.ShouldContain(nameof(PackageTrustLevel.FirstParty));
        options.Error.Description.ShouldContain("no package could ever be loaded");
    }

    [Fact]
    public void A_tenant_routing_host_with_a_first_party_key_starts_with_its_floor_at_first_party()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey firstParty) = store.Add(PackageTrustLevel.FirstParty);

        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            "Production",
            allowUnsigned: false,
            trustedKeys: [firstParty],
            routesTenants: true));

        options.RoutesTenants.ShouldBeTrue();
        options.AdmissionFloor.ShouldBe(PackageTrustLevel.FirstParty);
    }

    /// <summary>
    /// ADR-0033 §5.2 narrows <i>where</i> a Partner key takes effect; it does not remove the level.
    /// A Partner key may be configured on a tenant-routing host so that partner packages are
    /// inspected and listed. What such a host will not do is load one — that refusal is
    /// <see cref="CountryPackageLoader.Load"/>'s, and <c>PackageAdmissionFloorTests</c> proves it.
    /// </summary>
    [Fact]
    public void A_partner_key_may_still_be_configured_on_a_tenant_routing_host_beside_a_first_party_one()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey partner) = store.Add(PackageTrustLevel.Partner);
        (_, TrustedPackageKey firstParty) = store.Add(PackageTrustLevel.FirstParty);

        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            "Production",
            allowUnsigned: false,
            trustedKeys: [partner, firstParty],
            routesTenants: true));

        options.TrustedKeys.Count.ShouldBe(2);
        options.AdmissionFloor.ShouldBe(PackageTrustLevel.FirstParty);
    }

    /// <summary>
    /// A host that routes no tenants keeps ADR-0008 §9.3's rules unchanged: a Partner key admits
    /// partner packages there, because there is no tenant database for a loaded package to reach.
    /// </summary>
    [Fact]
    public void A_host_that_routes_no_tenants_has_no_floor_above_the_signature_rules()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey partner) = store.Add(PackageTrustLevel.Partner);

        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            "Production",
            allowUnsigned: false,
            trustedKeys: [partner],
            routesTenants: false));

        options.RoutesTenants.ShouldBeFalse();
        options.AdmissionFloor.ShouldBe(PackageTrustLevel.Unsigned);
    }

    /// <summary>
    /// The pin is the point: configuration carries the key and the thumbprint it must have, so
    /// replacing the key material is a deliberate two-part edit rather than one environment variable.
    /// </summary>
    [Fact]
    public void A_key_whose_thumbprint_is_not_the_one_pinned_is_refused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Result<TrustedPackageKey> trusted = TrustedPackageKey.Create(
            "0000000000000000000000000000000000000000000000000000000000000000",
            PackageTrustLevel.FirstParty,
            key.ExportSubjectPublicKeyInfo());

        trusted.IsFailure.ShouldBeTrue();
        trusted.Error.Description.ShouldContain("actually has thumbprint");
    }

    /// <summary>
    /// One curve. Accepting a second would mean accepting the weakest one on the list, and an
    /// attacker picks the list.
    /// </summary>
    [Fact]
    public void A_key_on_another_curve_is_refused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        string thumbprint = Convert.ToHexStringLower(SHA256.HashData(publicKey));

        Result<TrustedPackageKey> trusted =
            TrustedPackageKey.Create(thumbprint, PackageTrustLevel.FirstParty, publicKey);

        trusted.IsFailure.ShouldBeTrue();
        trusted.Error.Description.ShouldContain("P-256");
    }

    /// <summary>
    /// The bit length is not the curve. secp256k1 is a 256-bit curve too, and a check on
    /// <c>KeySize</c> alone accepted a key on it; the curve's OID is what says P-256.
    /// </summary>
    [Fact]
    public void A_256_bit_key_on_a_curve_other_than_P_256_is_refused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.CreateFromValue(Secp256k1Oid));
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();

        key.KeySize.ShouldBe(
            PackageSignature.KeySizeInBits,
            "this case only bites if the key size alone cannot tell the curves apart");

        Result<TrustedPackageKey> trusted = TrustedPackageKey.Create(
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            PackageTrustLevel.FirstParty,
            publicKey);

        trusted.IsFailure.ShouldBeTrue();
        trusted.Error.Description.ShouldContain("P-256");
        trusted.Error.Description.ShouldContain(Secp256k1Oid);
    }

    private const string Secp256k1Oid = "1.3.132.0.10";

    [Fact]
    public void A_key_that_is_not_a_key_is_refused() =>
        TrustedPackageKey.Create("abcd", PackageTrustLevel.FirstParty, [1, 2, 3])
            .IsFailure.ShouldBeTrue();

    /// <summary>
    /// <see cref="PackageTrustLevel.Unsigned"/> is the absence of a signature, so a key that
    /// establishes it is a contradiction.
    /// </summary>
    [Fact]
    public void A_key_cannot_establish_that_a_package_is_unsigned()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();

        TrustedPackageKey.Create(
                Convert.ToHexStringLower(SHA256.HashData(publicKey)),
                PackageTrustLevel.Unsigned,
                publicKey)
            .IsFailure.ShouldBeTrue();
    }

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Expected success, got: {result.Error}");
}
