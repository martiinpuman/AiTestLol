using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// What a <see cref="TenantScope"/> carries and what its constructor refuses (ADR-0007 §3.4,
/// ADR-0027 §1). Built here through the internal constructor, which this assembly may call.
/// </summary>
/// <remarks>
/// The lease - disposal clearing <c>IsActive</c> and a reused scope throwing - is B-06.3's and is
/// not asserted here either way. What is asserted is the state this row can produce: a scope is
/// active when built, and every fact it carries was checked on the way in.
/// </remarks>
public sealed class TenantScopeTests
{
    private static readonly TenantId Id = TenantId.Create();
    private static readonly TenantKey Key = TenantKey.Parse("acme-trading", null);
    private static readonly Region Nz = Region.Parse("nz", null);
    private static readonly SchemaVersion Version = SchemaVersion.Of(3);

    private static readonly InstalledPackages Packages = InstalledPackages.Of(
        [new InstalledPackageEntry("nz", "1.2.0", InstalledPackageState.Active)]);

    public static IEnumerable<object[]> EveryReason() =>
        Enum.GetValues<TenantAccessReason>().Select(reason => new object[] { reason });

    [Fact]
    public void A_scope_carries_what_it_was_built_with_and_is_active()
    {
        TenantScope scope = new(Id, Key, Nz, Version, Packages, TenantAccessReason.Job);

        scope.TenantId.ShouldBe(Id);
        scope.TenantKey.ShouldBe(Key);
        scope.ResidencyRegion.ShouldBe(Nz);
        scope.SchemaVersion.ShouldBe(Version);
        scope.Packages.ShouldBeSameAs(Packages);
        scope.Reason.ShouldBe(TenantAccessReason.Job);
        scope.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void A_scope_is_a_tenant_access_and_the_base_view_names_the_same_tenant()
    {
        TenantAccess access = Build();

        access.TenantId.ShouldBe(Id);
        access.TenantKey.ShouldBe(Key);
    }

    [Theory]
    [MemberData(nameof(EveryReason))]
    public void Every_reason_ADR_0007_names_is_accepted(TenantAccessReason reason)
    {
        Build(reason).Reason.ShouldBe(reason);
    }

    [Fact]
    public void The_theory_above_covers_all_six_reasons()
    {
        EveryReason().Count().ShouldBe(6);
    }

    [Fact]
    public void A_reason_outside_the_enum_is_refused()
    {
        // A cast from a stale integer - a job payload written by an older build, say - must not
        // produce a scope with a reason nobody defined.
        const TenantAccessReason undefined = (TenantAccessReason)6;

        ArgumentOutOfRangeException refused = Should.Throw<ArgumentOutOfRangeException>(() => Build(undefined));

        refused.ParamName.ShouldBe("reason");
    }

    [Fact]
    public void An_unassigned_tenant_id_is_refused()
    {
        ArgumentException refused = Should.Throw<ArgumentException>(
            () => new TenantScope(default, Key, Nz, Version, Packages, TenantAccessReason.Request));

        refused.ParamName.ShouldBe("tenantId");
        refused.Message.ShouldContain("unassigned");
    }

    [Fact]
    public void An_unspecified_tenant_key_is_refused()
    {
        ArgumentException refused = Should.Throw<ArgumentException>(
            () => new TenantScope(Id, default, Nz, Version, Packages, TenantAccessReason.Request));

        refused.ParamName.ShouldBe("tenantKey");
        refused.Message.ShouldContain("unspecified");
    }

    [Fact]
    public void An_unspecified_region_is_refused()
    {
        ArgumentException refused = Should.Throw<ArgumentException>(
            () => new TenantScope(Id, Key, default, Version, Packages, TenantAccessReason.Request));

        refused.ParamName.ShouldBe("residencyRegion");
        refused.Message.ShouldContain("unspecified");
    }

    [Fact]
    public void An_unspecified_schema_version_is_refused()
    {
        // The §7.5 gate compares this value; a scope whose version nobody set must not reach it.
        ArgumentException refused = Should.Throw<ArgumentException>(
            () => new TenantScope(Id, Key, Nz, default, Packages, TenantAccessReason.Request));

        refused.ParamName.ShouldBe("schemaVersion");
        refused.Message.ShouldContain("unspecified");
    }

    [Fact]
    public void A_null_package_set_is_refused_so_that_no_packages_is_always_InstalledPackages_None()
    {
        Should.Throw<ArgumentNullException>(
            () => new TenantScope(Id, Key, Nz, Version, null!, TenantAccessReason.Request));
    }

    [Fact]
    public void ToString_names_the_key_and_the_id_for_a_log_line()
    {
        string rendered = Build().ToString();

        rendered.ShouldContain(Key.Value);
        rendered.ShouldContain(Id.ToString());
    }

    private static TenantScope Build(TenantAccessReason reason = TenantAccessReason.Request) =>
        new(Id, Key, Nz, Version, Packages, reason);
}
