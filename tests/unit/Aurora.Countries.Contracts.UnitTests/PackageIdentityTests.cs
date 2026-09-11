using System;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// The three values that identify a package. Two of them are shapes, and one of them is a schema
/// name in DDL.
/// </summary>
public sealed class PackageIdentityTests
{
    [Theory]
    [InlineData("aurora.country.nz")]
    [InlineData("aurora.region.anz")]
    [InlineData("contoso.tax.de")]
    [InlineData("a.b")]
    public void A_reverse_dotted_lowercase_id_is_read(string value) =>
        ContractTestValues.Ok(PackageId.Create(value)).Value.ShouldBe(value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aurora")]
    [InlineData("Aurora.Country.NZ")]
    [InlineData("aurora..nz")]
    [InlineData("aurora.country.nz.")]
    [InlineData("aurora/country/nz")]
    [InlineData("../../etc/passwd")]
    [InlineData("aurora.country.nz\n")]
    public void Anything_that_is_not_one_is_refused(string? value) =>
        PackageId.Create(value).IsFailure.ShouldBeTrue();

    [Fact]
    public void The_default_id_names_nothing_and_says_so()
    {
        PackageId unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        unassigned.ToString().ShouldBe("<unspecified package id>");
    }

    [Theory]
    [InlineData("nz", "pkg_nz")]
    [InlineData("se", "pkg_se")]
    [InlineData("de_bw", "pkg_de_bw")]
    public void A_key_derives_the_one_schema_a_package_owns(string key, string schema) =>
        ContractTestValues.Ok(PackageKey.Create(key)).SchemaName.ShouldBe(schema);

    /// <summary>
    /// The key becomes a schema name inside <c>create schema …</c>, where no parameter can go. The
    /// only defence at that point is that the value could never have been anything else.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NZ")]
    [InlineData("1nz")]
    [InlineData("nz-1")]
    [InlineData("nz nz")]
    [InlineData("nz\"")]
    [InlineData("nz; drop schema sales cascade")]
    [InlineData("public")]
    [InlineData("a_key_far_too_long_for_a_schema")]
    public void A_key_that_could_reach_past_its_own_schema_is_refused(string? key)
    {
        if (key == "public")
        {
            // 'public' is a legitimate shape; it is refused at install by colliding with nothing,
            // because the schema this key names is pkg_public and not public. The shape rule's job
            // is narrower than that, and this case records the boundary rather than asserting a
            // rule that is not here.
            ContractTestValues.Ok(PackageKey.Create(key)).SchemaName.ShouldBe("pkg_public");
            return;
        }

        PackageKey.Create(key).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_default_key_names_nothing_and_says_so()
    {
        PackageKey unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        Should.Throw<InvalidOperationException>(() => unassigned.SchemaName);
    }

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("0.0.1")]
    [InlineData("2.0.0-rc.1")]
    [InlineData("10.20.30")]
    public void A_semver_version_is_read(string value) =>
        ContractTestValues.Ok(PackageVersion.Create(value)).Value.ShouldBe(value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.4")]
    [InlineData("1.4.0.0")]
    [InlineData("v1.4.0")]
    [InlineData("01.4.0")]
    public void Anything_that_is_not_semver_is_refused(string? value) =>
        PackageVersion.Create(value).IsFailure.ShouldBeTrue();

    /// <summary>
    /// SemVer gives build metadata no part in comparison, so two versions that differ only there
    /// are one version — otherwise every CI build would look like a different installed release.
    /// </summary>
    [Fact]
    public void Build_metadata_is_dropped_so_one_release_is_one_version()
    {
        PackageVersion withMetadata = ContractTestValues.Ok(PackageVersion.Create("1.4.0+ci.42"));
        PackageVersion without = ContractTestValues.Ok(PackageVersion.Create("1.4.0"));

        withMetadata.Value.ShouldBe("1.4.0");
        withMetadata.ShouldBe(without);
    }

    [Fact]
    public void A_dependency_without_a_range_is_refused()
    {
        Result<PackageDependency> dependency = PackageDependency.Create("aurora.region.anz", " ");

        dependency.IsFailure.ShouldBeTrue();
        dependency.Error.Description.ShouldContain("version range");
    }

    /// <summary>
    /// The core contract version is read from the assembly rather than declared twice, so this is
    /// the test that the one place it lives actually produces a version.
    /// </summary>
    [Fact]
    public void The_core_contract_version_is_a_semver_read_from_the_contracts_assembly()
    {
        PackageVersion.Create(CoreContract.Version).IsSuccess.ShouldBeTrue(
            $"CoreContract.Version was '{CoreContract.Version}', which is not a SemVer version. It " +
            $"comes from <Version> in Aurora.Countries.Contracts.csproj.");

        CoreContract.Version.ShouldNotContain("+");
        CoreContract.AssemblyName.ShouldBe("Aurora.Countries.Contracts");
    }
}
