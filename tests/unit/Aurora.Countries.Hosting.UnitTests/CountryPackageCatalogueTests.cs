using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// Discovery: what this deployment has available, read at startup without running any of it
/// (ADR-0008 §3.3).
/// </summary>
public sealed class CountryPackageCatalogueTests
{
    [Fact]
    public void A_signed_package_under_the_packages_root_is_available()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        CountryPackageCatalogue catalogue = Ok(CountryPackageCatalogue.Scan(Options(package, trusted)));

        catalogue.DirectoriesInspected.ShouldBe(1);
        catalogue.Available.Count.ShouldBe(1);
        catalogue.Rejected.ShouldBeEmpty();
        catalogue.Available[0].Manifest.Id.Value.ShouldBe("aurora.country.testland");
        catalogue.Available[0].Trust.Level.ShouldBe(PackageTrustLevel.FirstParty);
    }

    /// <summary>
    /// A package the host cannot use is recorded, not skipped. A package that quietly fails to
    /// appear is noticed when a tenant asks why their return is missing, which is months later and
    /// in the wrong conversation.
    /// </summary>
    [Fact]
    public void A_directory_the_host_cannot_use_is_recorded_with_the_reason()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        Directory.CreateDirectory(Path.Combine(package.Root, "aurora.country.nowhere", "1.0.0"));

        CountryPackageCatalogue catalogue = Ok(CountryPackageCatalogue.Scan(Options(package, trusted)));

        catalogue.DirectoriesInspected.ShouldBe(2);
        catalogue.Available.Count.ShouldBe(1);
        catalogue.Rejected.Count.ShouldBe(1);
        catalogue.Rejected[0].Reason.Code.ShouldBe(HostingErrors.MalformedPackageCode);
        catalogue.Rejected[0].Directory.ShouldContain("aurora.country.nowhere");
    }

    /// <summary>
    /// An unsigned package in production is refused at discovery, so it never reaches the list an
    /// operator can install from.
    /// </summary>
    [Fact]
    public void An_unsigned_package_is_rejected_at_discovery_in_production()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();

        CountryPackageCatalogue catalogue = Ok(CountryPackageCatalogue.Scan(Options(package, trusted)));

        catalogue.Available.ShouldBeEmpty();
        catalogue.Rejected.Count.ShouldBe(1);
        catalogue.Rejected[0].Reason.Code.ShouldBe(HostingErrors.UntrustedCode);
    }

    /// <summary>
    /// An empty catalogue and a catalogue of packages that all failed look identical from the
    /// outside unless the counts are reported, which is why they are.
    /// </summary>
    [Fact]
    public void An_empty_packages_root_is_an_empty_catalogue_and_says_so()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        string root = Path.Combine(Path.GetTempPath(), "aurora-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CountryPackageCatalogue catalogue = Ok(CountryPackageCatalogue.Scan(Ok(
                CountryPackageHostOptions.Create(root, "Production", false, [trusted], routesTenants: true))));

            catalogue.DirectoriesInspected.ShouldBe(0);
            catalogue.Available.ShouldBeEmpty();
            catalogue.Rejected.ShouldBeEmpty();
            catalogue.ToString().ShouldContain("0 package(s) available");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A packages directory that is not there is a packaging mistake, not an empty catalogue, and
    /// the two must not read the same.
    /// </summary>
    [Fact]
    public void A_packages_root_that_does_not_exist_is_a_configuration_failure()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        Result<CountryPackageCatalogue> catalogue = CountryPackageCatalogue.Scan(Ok(
            CountryPackageHostOptions.Create(
                Path.Combine(Path.GetTempPath(), "no-such-packages-root-" + Guid.NewGuid().ToString("N")),
                "Production",
                false,
                [trusted],
                routesTenants: true)));

        catalogue.IsFailure.ShouldBeTrue();
        catalogue.Error.Code.ShouldBe(HostingErrors.HostConfigurationCode);
    }

    /// <summary>
    /// The catalogue can answer "which versions of this package do we have", which is what the
    /// compatibility refusal needs in order to name a version that would work.
    /// </summary>
    [Fact]
    public void The_catalogue_lists_the_versions_it_holds_of_one_package()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        CountryPackageCatalogue catalogue = Ok(CountryPackageCatalogue.Scan(Options(package, trusted)));

        PackageId id = catalogue.Available[0].Manifest.Id;

        catalogue.VersionsOf(id).Select(manifest => manifest.Version.Value).ShouldBe(["1.4.0"]);
        catalogue.VersionsOf(Ok(PackageId.Create("aurora.country.elsewhere"))).ShouldBeEmpty();
    }

    private static CountryPackageHostOptions Options(PackageOnDisk package, TrustedPackageKey trusted) =>
        Ok(CountryPackageHostOptions.Create(package.Root, "Production", false, [trusted], routesTenants: true));

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Expected success, got: {result.Error}");
}
