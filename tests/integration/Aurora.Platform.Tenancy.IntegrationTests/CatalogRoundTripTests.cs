using System;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// Every entity round-trips through PostgreSQL, typed identifiers and value objects included —
/// written as the role that may write it (a cluster, a subscription, a tenant and its hosts as
/// <c>aurora_migrator</c>, standing in for the provisioning saga's principal; a tenant's activity
/// and an installed package as <c>aurora_app</c>) and read back as <c>aurora_app</c> — which is
/// also where EF Core is shown to accept the generic <c>EntityIdConverter</c> in materialisation
/// and in a <c>WHERE</c>.
/// </summary>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogRoundTripTests
{
    private readonly CatalogDatabaseFixture _catalog;

    public CatalogRoundTripTests(CatalogDatabaseFixture catalog) => _catalog = catalog;

    [Fact]
    public async Task A_cluster_and_a_reserved_tenant_read_back_exactly_as_written()
    {
        DatabaseCluster cluster = Unique.Cluster("eu-west");
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        await using CatalogDbContext reader = _catalog.OpenAsApp();
        Tenant readBack = await reader.Tenants.SingleAsync(t => t.Id == tenant.Id);
        DatabaseCluster clusterReadBack = await reader.DatabaseClusters.SingleAsync(c => c.Id == cluster.Id);

        readBack.Key.ShouldBe(tenant.Key);
        readBack.DisplayName.ShouldBe("Acme Trading Ltd");
        readBack.State.ShouldBe(TenantState.Provisioning);
        readBack.ClusterId.ShouldBe(cluster.Id);
        readBack.ResidencyRegion.ShouldBe(Region.Parse("eu-west", null));
        readBack.DatabaseName.ShouldBe(Tenant.DatabaseNameFor(tenant.Key));
        readBack.Plan.ShouldBe("standard");
        readBack.CreatedAt.ShouldBe(Unique.Now);
        readBack.CoreSchemaVersion.ShouldBeNull();
        readBack.ActivatedAt.ShouldBeNull();

        clusterReadBack.Region.ShouldBe(Region.Parse("eu-west", null));
        clusterReadBack.Host.ShouldBe(cluster.Host);
        clusterReadBack.Port.ShouldBe(5432);
        clusterReadBack.MaintenanceDatabase.ShouldBe("postgres");
        clusterReadBack.AdminSecretRef.ShouldBe(cluster.AdminSecretRef);
        clusterReadBack.MigratorSecretRef.ShouldBe(cluster.MigratorSecretRef);
        clusterReadBack.AppSecretRef.ShouldBe(cluster.AppSecretRef);
        clusterReadBack.MaxTenants.ShouldBe(1_000);
        clusterReadBack.State.ShouldBe(DatabaseClusterState.Accepting);
    }

    [Fact]
    public async Task A_tenant_is_found_by_its_key_and_by_its_state_through_the_value_converters()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        await using CatalogDbContext reader = _catalog.OpenAsApp();
        TenantKey key = tenant.Key;

        Tenant byKey = await reader.Tenants.SingleAsync(t => t.Key == key);
        bool amongProvisioning = await reader.Tenants
            .Where(t => t.State == TenantState.Provisioning)
            .AnyAsync(t => t.Id == tenant.Id);
        bool amongActive = await reader.Tenants
            .Where(t => t.State == TenantState.Active)
            .AnyAsync(t => t.Id == tenant.Id);

        byKey.Id.ShouldBe(tenant.Id);
        amongProvisioning.ShouldBeTrue();
        amongActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Activating_a_tracked_tenant_persists_its_new_state_and_schema_version()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        // Activation is the saga's write (ADR-0007 §8 step 8) and activated_at is not the request
        // path's to set; recording activity is the request path's own (§10.1). Each as its role.
        await using (CatalogDbContext saga = _catalog.OpenAsMigrator())
        {
            // Queries do not track by default (ADR-0003 rule 4); a unit of work that writes opts in.
            Tenant tracked = await saga.Tenants.AsTracking().SingleAsync(t => t.Id == tenant.Id);
            tracked.Activate(coreSchemaVersion: 1, Unique.Now.AddSeconds(30));
            await saga.SaveChangesAsync();
        }

        await using (CatalogDbContext request = _catalog.OpenAsApp())
        {
            Tenant tracked = await request.Tenants.AsTracking().SingleAsync(t => t.Id == tenant.Id);
            tracked.RecordActivity(Unique.Now.AddMinutes(1));
            await request.SaveChangesAsync();
        }

        await using CatalogDbContext reader = _catalog.OpenAsApp();
        Tenant readBack = await reader.Tenants.SingleAsync(t => t.Id == tenant.Id);

        readBack.State.ShouldBe(TenantState.Active);
        readBack.CoreSchemaVersion.ShouldBe(1);
        readBack.ActivatedAt.ShouldBe(Unique.Now.AddSeconds(30));
        readBack.LastActivityAt.ShouldBe(Unique.Now.AddMinutes(1));
    }

    [Fact]
    public async Task Hosts_subscriptions_and_installed_packages_round_trip_against_their_tenant()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        string primaryHost = Unique.Host();
        string customHost = Unique.Host();
        SubscriptionId subscriptionId = SubscriptionId.Create();
        DateOnly from = new(2026, 10, 1);

        await SaveAsync(cluster, tenant);
        await _catalog.SeedAsync(owner =>
        {
            owner.TenantHosts.Add(TenantHost.Register(primaryHost, tenant.Id, isPrimary: true, verifiedAt: Unique.Now));
            owner.TenantHosts.Add(TenantHost.Register(customHost, tenant.Id, isPrimary: false, verifiedAt: null));
            owner.Subscriptions.Add(Subscription.Start(subscriptionId, tenant.Id, "standard", 25, from, from.AddYears(1)));
        });

        // The installer's write (ADR-0008 §5.1 step 9) is the one of these the request path makes.
        await using (CatalogDbContext installer = _catalog.OpenAsApp())
        {
            installer.InstalledPackages.Add(InstalledPackage.Begin(tenant.Id, "nz", "1.2.0", "user:7f3a", Unique.Now));
            await installer.SaveChangesAsync();
        }

        await using CatalogDbContext reader = _catalog.OpenAsApp();
        TenantId tenantId = tenant.Id;

        TenantHost primary = await reader.TenantHosts.SingleAsync(h => h.TenantId == tenantId && h.IsPrimary);
        TenantHost custom = await reader.TenantHosts.SingleAsync(h => h.Host == customHost);
        Subscription subscription = await reader.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        InstalledPackage package = await reader.InstalledPackages.SingleAsync(p => p.TenantId == tenantId && p.PackageId == "nz");

        primary.Host.ShouldBe(primaryHost);
        primary.VerifiedAt.ShouldBe(Unique.Now);
        custom.IsPrimary.ShouldBeFalse();
        custom.VerifiedAt.ShouldBeNull();
        subscription.TenantId.ShouldBe(tenantId);
        subscription.Plan.ShouldBe("standard");
        subscription.Seats.ShouldBe(25);
        subscription.ValidFrom.ShouldBe(from);
        subscription.ValidTo.ShouldBe(from.AddYears(1));
        package.Version.ShouldBe("1.2.0");
        package.State.ShouldBe(InstalledPackageState.Installing);
        package.InstalledAt.ShouldBe(Unique.Now);
        package.InstalledBy.ShouldBe("user:7f3a");
    }

    /// <summary>The cluster and the tenant as the owner: the request path reads both and creates neither.</summary>
    private Task SaveAsync(DatabaseCluster cluster, Tenant tenant) =>
        _catalog.SeedAsync(owner =>
        {
            owner.DatabaseClusters.Add(cluster);
            owner.Tenants.Add(tenant);
        });
}
