using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_reserved_tenant_is_provisioning_on_its_cluster_in_that_clusters_region_with_a_database_named_after_its_key()
    {
        DatabaseCluster cluster = new ACluster().WithId("nz-1").InRegion("nz").Build();
        TenantId id = TenantId.Create();

        Tenant tenant = Tenant.Reserve(id, TenantKey.Parse("acme-trading", null), "Acme Trading Ltd", cluster, "standard", Now);

        tenant.Id.ShouldBe(id);
        tenant.Key.Value.ShouldBe("acme-trading");
        tenant.DisplayName.ShouldBe("Acme Trading Ltd");
        tenant.State.ShouldBe(TenantState.Provisioning);
        tenant.ClusterId.ShouldBe(cluster.Id);
        tenant.ResidencyRegion.ShouldBe(cluster.Region);
        tenant.DatabaseName.ShouldBe("aurora_t_acme_trading");
        tenant.Plan.ShouldBe("standard");
        tenant.CreatedAt.ShouldBe(Now);
        tenant.CoreSchemaVersion.ShouldBeNull();
        tenant.ActivatedAt.ShouldBeNull();
        tenant.LastActivityAt.ShouldBeNull();
    }

    [Fact]
    public void The_database_name_needs_no_quoting_and_two_keys_cannot_share_one()
    {
        // Keys never contain underscores, so turning hyphens into underscores is injective.
        Tenant.DatabaseNameFor(TenantKey.Parse("acme-trading", null)).ShouldBe("aurora_t_acme_trading");
        Tenant.DatabaseNameFor(TenantKey.Parse("acme", null)).ShouldBe("aurora_t_acme");
        Tenant.DatabaseNameFor(TenantKey.Parse(new string('k', TenantKey.MaxLength), null)).Length
            .ShouldBeLessThanOrEqualTo(PostgresIdentifier.MaxLength);
    }

    [Fact]
    public void A_tenant_cannot_be_reserved_on_a_cluster_that_is_not_accepting()
    {
        DatabaseCluster cluster = new ACluster().Build();
        cluster.StopAccepting();

        Should.Throw<InvalidOperationException>(() => Reserve(cluster));
    }

    [Fact]
    public void An_unassigned_id_or_key_is_refused()
    {
        DatabaseCluster cluster = new ACluster().Build();

        Should.Throw<ArgumentException>(() =>
            Tenant.Reserve(default, TenantKey.Parse("acme", null), "Acme", cluster, "standard", Now));
        Should.Throw<ArgumentException>(() =>
            Tenant.Reserve(TenantId.Create(), default, "Acme", cluster, "standard", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Acme")]
    public void A_blank_or_untrimmed_display_name_is_refused(string displayName)
    {
        Should.Throw<ArgumentException>(() =>
            Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme", null), displayName, new ACluster().Build(), "standard", Now));
    }

    [Fact]
    public void A_display_name_longer_than_the_column_is_refused()
    {
        string tooLong = new('a', Tenant.MaxDisplayNameLength + 1);

        Should.Throw<ArgumentException>(() =>
            Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme", null), tooLong, new ACluster().Build(), "standard", Now));
    }

    [Fact]
    public void A_blank_plan_is_refused()
    {
        Should.Throw<ArgumentException>(() =>
            Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme", null), "Acme", new ACluster().Build(), "", Now));
    }

    [Fact]
    public void An_instant_that_is_not_UTC_is_refused_where_it_is_produced_not_at_SaveChanges()
    {
        DateTimeOffset local = new(2026, 9, 11, 22, 0, 0, TimeSpan.FromHours(12));

        Should.Throw<ArgumentException>(() =>
            Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme", null), "Acme", new ACluster().Build(), "standard", local));
        Should.Throw<ArgumentException>(() => Reserve(new ACluster().Build()).RecordActivity(local));
        Should.Throw<ArgumentException>(() => Reserve(new ACluster().Build()).Activate(1, local));
    }

    [Fact]
    public void Activating_a_provisioning_tenant_makes_it_active_at_a_schema_version()
    {
        Tenant tenant = Reserve(new ACluster().Build());
        DateTimeOffset later = Now.AddSeconds(42);

        tenant.Activate(coreSchemaVersion: 3, later);

        tenant.State.ShouldBe(TenantState.Active);
        tenant.CoreSchemaVersion.ShouldBe(3);
        tenant.ActivatedAt.ShouldBe(later);
    }

    [Fact]
    public void A_tenant_that_is_not_provisioning_cannot_be_activated_again()
    {
        Tenant tenant = Reserve(new ACluster().Build());
        tenant.Activate(1, Now);

        Should.Throw<InvalidOperationException>(() => tenant.Activate(2, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_negative_schema_version_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Reserve(new ACluster().Build()).Activate(-1, Now));
    }

    [Fact]
    public void Recording_activity_stamps_when_the_tenant_was_last_seen()
    {
        Tenant tenant = Reserve(new ACluster().Build());

        tenant.RecordActivity(Now.AddHours(1));

        tenant.LastActivityAt.ShouldBe(Now.AddHours(1));
    }

    private static Tenant Reserve(DatabaseCluster cluster) =>
        Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme", null), "Acme", cluster, "standard", Now);
}
