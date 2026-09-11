using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

public sealed class InstalledPackageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_package_whose_install_has_begun_is_installing_not_active()
    {
        TenantId tenantId = TenantId.Create();

        InstalledPackage package = InstalledPackage.Begin(tenantId, "nz", "1.2.0", "user:7f3a", Now);

        package.TenantId.ShouldBe(tenantId);
        package.PackageId.ShouldBe("nz");
        package.Version.ShouldBe("1.2.0");
        package.State.ShouldBe(InstalledPackageState.Installing);
        package.InstalledAt.ShouldBe(Now);
        package.InstalledBy.ShouldBe("user:7f3a");
    }

    [Theory]
    [InlineData("NZ")]
    [InlineData("nz-1")]
    [InlineData("1nz")]
    [InlineData("")]
    [InlineData("new zealand")]
    public void A_package_id_that_cannot_stem_a_pkg_schema_is_refused(string packageId)
    {
        Should.Throw<ArgumentException>(() => InstalledPackage.Begin(TenantId.Create(), packageId, "1.0.0", "user:7f3a", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0 .0")]
    public void A_blank_version_or_one_with_whitespace_is_refused(string version)
    {
        Should.Throw<ArgumentException>(() => InstalledPackage.Begin(TenantId.Create(), "nz", version, "user:7f3a", Now));
    }

    [Fact]
    public void A_blank_installer_reference_is_refused()
    {
        Should.Throw<ArgumentException>(() => InstalledPackage.Begin(TenantId.Create(), "nz", "1.0.0", " ", Now));
    }

    [Fact]
    public void An_unassigned_tenant_id_is_refused()
    {
        Should.Throw<ArgumentException>(() => InstalledPackage.Begin(default, "nz", "1.0.0", "user:7f3a", Now));
    }
}
