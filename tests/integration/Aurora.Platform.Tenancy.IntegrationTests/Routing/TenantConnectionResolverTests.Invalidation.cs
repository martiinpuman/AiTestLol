using System;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests.Routing;

/// <summary>
/// The non-negotiable proof of B-06.1: the routing entry is served from the cache, and a tenant
/// state change makes the next resolve read the catalog again.
/// </summary>
/// <remarks>
/// <para>
/// <b>The chain, link by link.</b> The request path is the composition root's own registration
/// over the <c>aurora_app</c> connection, with a statement counter attached to the very
/// <c>CatalogDbContext</c> the resolver reads through — so a count that moves is a statement that
/// left this process for PostgreSQL, sent by the resolver's reader, because the resolver missed its
/// cache. The operator side is the same registration over the owner's connection (only the owner
/// may write <c>suspended_at</c>), which is what attaches the invalidator to the context the
/// suspend is saved through. The two share one <c>HybridCache</c> instance, as two scopes of one
/// process do.
/// </para>
/// <para>
/// The first resolve costs one statement; the second, none — the hit. The suspend is written
/// through the entity and a real <c>SaveChangesAsync</c>; the third resolve costs one more
/// statement — the miss — and the row the cache now holds says <c>Suspended</c>. Remove the
/// invalidator's removal and the third count stays at one; that is the failure this test exists
/// to produce, and it was produced before this file was committed.
/// </para>
/// <para>
/// <b>One cluster row for the container, not one per bed.</b> Every bed places its tenants on the
/// row the fixture keeps for this container's endpoint
/// (<see cref="CatalogDatabaseFixture.ThisServerAsClusterAsync"/>). Until B-20 each bed seeded a
/// cluster row of its own for that endpoint, and this file carried an inertness guard asserting
/// that the catalog still admitted a second such row — PR #14 H-1, ADR-0034 §1 variant 3. The
/// catalog refuses it now (<c>ux_database_cluster_host_port</c>); the guard was deleted rather than
/// repaired, as its own message directed, and the property it stood in for — no two non-deleted
/// tenant rows resolve to one physical database, computed by this resolver over real rows — is
/// <c>CatalogRoutingUniquenessTests</c>.
/// </para>
/// </remarks>
public sealed partial class TenantConnectionResolverTests
{
    [Fact]
    public async Task Suspending_a_tenant_invalidates_its_routing_entry_so_the_next_resolve_reads_the_catalog_again()
    {
        await using RoutingTestBed bed = await RoutingTestBed.WithActiveTenantAsync(_catalog);

        await bed.ResolveAsync();
        int afterFirst = bed.CatalogStatements.Count;
        await bed.ResolveAsync();
        int afterSecond = bed.CatalogStatements.Count;

        await bed.SuspendThroughTheOperatorConsoleAsync(save: context => context.SaveChangesAsync());

        TenantConnection afterSuspend = await bed.ResolveAsync();
        int afterThird = bed.CatalogStatements.Count;
        TenantRouting nowCached = await bed.CachedRowAsync();

        _output.WriteLine($"catalog statements: after 1st resolve = {afterFirst}, after 2nd (hit) = {afterSecond}, after suspend + 3rd (miss) = {afterThird}");
        _output.WriteLine($"row in the cache after the 3rd resolve: state = {nowCached.State}");
        afterFirst.ShouldBe(1, "the first resolve reads the catalog");
        afterSecond.ShouldBe(1, "the second resolve is a cache hit and sends nothing");
        afterThird.ShouldBe(2, "the resolve after the suspend must not be served the cached row");
        nowCached.State.ShouldBe(TenantState.Suspended, "what the cache holds now is the re-read row");
        afterSuspend.DatabaseName.ShouldBe(bed.Tenant.DatabaseName, "a suspended tenant still routes (ADR-0007 §11.4)");
    }

    [Fact]
    public async Task A_synchronous_save_invalidates_too()
    {
        await using RoutingTestBed bed = await RoutingTestBed.WithActiveTenantAsync(_catalog);

        await bed.ResolveAsync();
        await bed.ResolveAsync();
        int beforeSuspend = bed.CatalogStatements.Count;

        await bed.SuspendThroughTheOperatorConsoleAsync(save: context =>
        {
            context.SaveChanges();
            return Task.CompletedTask;
        });
        await bed.ResolveAsync();

        beforeSuspend.ShouldBe(1);
        bed.CatalogStatements.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_save_that_changes_only_the_activity_stamp_keeps_the_entry()
    {
        await using RoutingTestBed bed = await RoutingTestBed.WithActiveTenantAsync(_catalog);

        await bed.ResolveAsync();
        await bed.TouchActivityThroughTheOperatorConsoleAsync();
        await bed.ResolveAsync();

        bed.CatalogStatements.Count.ShouldBe(1, "an activity stamp is not a routing change and must not cost a catalog read");
    }

    [Fact]
    public async Task A_save_that_fails_leaves_the_entry_in_place()
    {
        await using RoutingTestBed bed = await RoutingTestBed.WithActiveTenantAsync(_catalog);

        await bed.ResolveAsync();
        await Should.ThrowAsync<DbUpdateException>(() => bed.SuspendThroughTheOperatorConsoleAsync(save: async context =>
        {
            // A second tenant with the same key violates ux_tenant_key, so the whole save - the
            // suspend included - is rejected by PostgreSQL and the row does not change.
            context.Tenants.Add(Catalog.Tenant.Reserve(TenantId.Create(), bed.Tenant.Key, "Duplicate", bed.Cluster, "standard", Unique.Now));
            await context.SaveChangesAsync();
        }));
        await bed.ResolveAsync();

        bed.CatalogStatements.Count.ShouldBe(1, "nothing changed in the catalog, so the entry is still good");
        (await bed.CachedRowAsync()).State.ShouldBe(TenantState.Active);
    }

    [Fact]
    public async Task A_failed_save_leaves_no_pending_eviction_for_the_next_successful_save_on_the_same_context()
    {
        // PR #14 L-1. The interceptor remembers a save's tenants before it runs and acts after it
        // succeeds; a failed save never reaches the acting half, and the next save on the same
        // context *replaces* what was remembered before acting. So an operator who abandons a failed
        // suspend and suspends a different tenant on the same context evicts that tenant only. A
        // keep-first memory would evict the abandoned tenant instead - the fault this pins.
        await using RoutingTestBed bed = await RoutingTestBed.WithActiveTenantAsync(_catalog);
        Tenant abandoned = bed.Tenant;
        Tenant suspended = bed.OtherTenant;

        await bed.ResolveAsync(abandoned.Id);
        await bed.ResolveAsync(suspended.Id);
        int bothCached = bed.CatalogStatements.Count;

        await bed.AsOperatorAsync(async catalog =>
        {
            Tenant first = await RoutingTestBed.LoadTrackedAsync(catalog, abandoned.Id);
            first.Suspend(Unique.Now);
            Tenant duplicate = Catalog.Tenant.Reserve(TenantId.Create(), abandoned.Key, "Duplicate", bed.Cluster, "standard", Unique.Now);
            catalog.Tenants.Add(duplicate);
            await Should.ThrowAsync<DbUpdateException>(() => catalog.SaveChangesAsync());

            // The failed attempt is abandoned on the same context: the duplicate detached, the
            // first tenant's pending suspend discarded. Then the other tenant is suspended.
            catalog.Entry(duplicate).State = EntityState.Detached;
            catalog.Entry(first).State = EntityState.Unchanged;
            Tenant second = await RoutingTestBed.LoadTrackedAsync(catalog, suspended.Id);
            second.Suspend(Unique.Now);
            await catalog.SaveChangesAsync();
        });

        await bed.ResolveAsync(abandoned.Id);
        int afterAbandoned = bed.CatalogStatements.Count;
        await bed.ResolveAsync(suspended.Id);
        int afterSuspended = bed.CatalogStatements.Count;

        _output.WriteLine($"catalog statements: both cached = {bothCached}, after resolving the abandoned tenant = {afterAbandoned}, after resolving the suspended one = {afterSuspended}");
        bothCached.ShouldBe(2);
        afterAbandoned.ShouldBe(2, "the tenant whose suspend failed is still served from the cache");
        afterSuspended.ShouldBe(3, "the tenant whose suspend succeeded was evicted");
        (await bed.CachedRowAsync(abandoned.Id)).State.ShouldBe(TenantState.Active);
        (await bed.CachedRowAsync(suspended.Id)).State.ShouldBe(TenantState.Suspended);
    }

    /// <summary>
    /// Two composition roots over one cache: the request path as <c>aurora_app</c> with a statement
    /// counter on its catalog context, and the operator console as the owner. Both are built with
    /// the production DI extensions; nothing about the resolver, the cache or the invalidator is
    /// hand-wired. Seeds two active tenants on the fixture's cluster row for the test container.
    /// </summary>
    private sealed class RoutingTestBed : IAsyncDisposable
    {
        private readonly ServiceProvider _cacheOwner;
        private readonly ServiceProvider _requestPath;
        private readonly ServiceProvider _operatorConsole;

        private RoutingTestBed(CatalogDatabaseFixture catalog, DatabaseCluster cluster, Tenant tenant, Tenant otherTenant)
        {
            Cluster = cluster;
            Tenant = tenant;
            OtherTenant = otherTenant;

            var shared = new ServiceCollection();
            shared.AddHybridCache();
            _cacheOwner = shared.BuildServiceProvider();
            HybridCache cache = _cacheOwner.GetRequiredService<HybridCache>();

            var requestPath = new ServiceCollection();
            requestPath.AddSingleton(cache);
            requestPath.AddCatalogDatabase(catalog.AppConnectionString);
            requestPath.AddTenantConnectionResolver(TenantPoolProfile.Web);
            requestPath.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(CatalogStatements));
            _requestPath = requestPath.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            var operatorConsole = new ServiceCollection();
            operatorConsole.AddSingleton(cache);
            operatorConsole.AddCatalogDatabase(catalog.MigratorConnectionString);
            _operatorConsole = operatorConsole.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public DatabaseCluster Cluster { get; }

        public Tenant Tenant { get; }

        public Tenant OtherTenant { get; }

        public CatalogStatementCounter CatalogStatements { get; } = new();

        public static async Task<RoutingTestBed> WithActiveTenantAsync(CatalogDatabaseFixture catalog)
        {
            DatabaseCluster cluster = await catalog.ThisServerAsClusterAsync();
            Tenant tenant = ActiveTenantOn(cluster);
            Tenant otherTenant = ActiveTenantOn(cluster);
            await catalog.SeedAsync(owner => owner.Tenants.AddRange(tenant, otherTenant));

            return new RoutingTestBed(catalog, cluster, tenant, otherTenant);
        }

        /// <summary>An active tenant reserved on <paramref name="cluster"/>, not yet seeded.</summary>
        public static Tenant ActiveTenantOn(DatabaseCluster cluster)
        {
            Tenant tenant = Unique.Tenant(cluster);
            tenant.Activate(1, Unique.Now);
            return tenant;
        }

        public static Task<Tenant> LoadTrackedAsync(CatalogDbContext catalog, TenantId tenantId) =>
            catalog.Tenants.AsTracking().SingleAsync(candidate => candidate.Id == tenantId);

        /// <summary>One request: a fresh scope, the resolver from the container, one resolve.</summary>
        public Task<TenantConnection> ResolveAsync() => ResolveAsync(Tenant.Id);

        public async Task<TenantConnection> ResolveAsync(TenantId tenantId)
        {
            await using AsyncServiceScope scope = _requestPath.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ResolveAsync(tenantId, default);
        }

        /// <summary>What the cache holds for the tenant right now, read through the same cache the request path uses.</summary>
        public Task<TenantRouting> CachedRowAsync() => CachedRowAsync(Tenant.Id);

        public async Task<TenantRouting> CachedRowAsync(TenantId tenantId)
        {
            await using AsyncServiceScope scope = _requestPath.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TenantRoutingCache>()
                .GetOrReadAsync(tenantId, scope.ServiceProvider.GetRequiredService<ITenantRoutingReader>(), default);
        }

        /// <summary>One operator action: a fresh scope over the owner's registration, one catalog context the container hands out.</summary>
        public async Task AsOperatorAsync(Func<CatalogDbContext, Task> work)
        {
            await using AsyncServiceScope scope = _operatorConsole.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
        }

        /// <summary>The operator suspends the tenant through the entity, saving as <paramref name="save"/> says.</summary>
        public Task SuspendThroughTheOperatorConsoleAsync(Func<CatalogDbContext, Task> save) => AsOperatorAsync(async catalog =>
        {
            Tenant tenant = await LoadTrackedAsync(catalog, Tenant.Id);
            tenant.Suspend(Unique.Now);
            await save(catalog);
        });

        public Task TouchActivityThroughTheOperatorConsoleAsync() => AsOperatorAsync(async catalog =>
        {
            Tenant tenant = await LoadTrackedAsync(catalog, Tenant.Id);
            tenant.RecordActivity(Unique.Now);
            await catalog.SaveChangesAsync();
        });

        public async ValueTask DisposeAsync()
        {
            await _requestPath.DisposeAsync();
            await _operatorConsole.DisposeAsync();
            await _cacheOwner.DisposeAsync();
        }
    }
}
