using System;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
            context.Tenants.Add(Catalog.Tenant.Reserve(
                Aurora.SharedKernel.TenantId.Create(), bed.Tenant.Key, "Duplicate", bed.Cluster, "standard", Unique.Now));
            await context.SaveChangesAsync();
        }));
        await bed.ResolveAsync();

        bed.CatalogStatements.Count.ShouldBe(1, "nothing changed in the catalog, so the entry is still good");
        (await bed.CachedRowAsync()).State.ShouldBe(TenantState.Active);
    }

    /// <summary>
    /// Two composition roots over one cache: the request path as <c>aurora_app</c> with a statement
    /// counter on its catalog context, and the operator console as the owner. Both are built with
    /// the production DI extensions; nothing about the resolver, the cache or the invalidator is
    /// hand-wired.
    /// </summary>
    private sealed class RoutingTestBed : IAsyncDisposable
    {
        private readonly ServiceProvider _cacheOwner;
        private readonly ServiceProvider _requestPath;
        private readonly ServiceProvider _operatorConsole;
        private readonly string _secretName;

        private RoutingTestBed(CatalogDatabaseFixture catalog, DatabaseCluster cluster, Tenant tenant, string secretName)
        {
            Cluster = cluster;
            Tenant = tenant;
            _secretName = secretName;

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

        public CatalogStatementCounter CatalogStatements { get; } = new();

        public static async Task<RoutingTestBed> WithActiveTenantAsync(CatalogDatabaseFixture catalog)
        {
            var endpoint = new NpgsqlConnectionStringBuilder(catalog.AppConnectionString);
            string secretName = "AURORA_TEST_APP_SECRET_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(secretName, endpoint.Password);

            DatabaseCluster cluster = DatabaseCluster.Register(
                Unique.ClusterId(),
                Region.Parse("nz", null),
                endpoint.Host!,
                endpoint.Port,
                CatalogDatabaseFixture.MaintenanceDatabaseName,
                SecretReference.Of("vault://kv/aurora/test/admin"),
                SecretReference.Of("vault://kv/aurora/test/migrator"),
                SecretReference.Of("env:" + secretName),
                1_000);
            Tenant tenant = Unique.Tenant(cluster);
            tenant.Activate(1, Unique.Now);
            await catalog.SeedAsync(owner =>
            {
                owner.DatabaseClusters.Add(cluster);
                owner.Tenants.Add(tenant);
            });

            return new RoutingTestBed(catalog, cluster, tenant, secretName);
        }

        /// <summary>One request: a fresh scope, the resolver from the container, one resolve.</summary>
        public async Task<TenantConnection> ResolveAsync()
        {
            await using AsyncServiceScope scope = _requestPath.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ResolveAsync(Tenant.Id, default);
        }

        /// <summary>What the cache holds for the tenant right now, read through the same cache the request path uses.</summary>
        public async Task<TenantRouting> CachedRowAsync()
        {
            await using AsyncServiceScope scope = _requestPath.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TenantRoutingCache>()
                .GetOrReadAsync(Tenant.Id, scope.ServiceProvider.GetRequiredService<ITenantRoutingReader>(), default);
        }

        /// <summary>The operator suspends the tenant through the entity and the catalog context the container hands out, saving as <paramref name="save"/> says.</summary>
        public async Task SuspendThroughTheOperatorConsoleAsync(Func<CatalogDbContext, Task> save)
        {
            await using AsyncServiceScope scope = _operatorConsole.CreateAsyncScope();
            CatalogDbContext catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Tenant tenant = await catalog.Tenants.AsTracking().SingleAsync(candidate => candidate.Id == Tenant.Id);
            tenant.Suspend(Unique.Now);
            await save(catalog);
        }

        public async Task TouchActivityThroughTheOperatorConsoleAsync()
        {
            await using AsyncServiceScope scope = _operatorConsole.CreateAsyncScope();
            CatalogDbContext catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Tenant tenant = await catalog.Tenants.AsTracking().SingleAsync(candidate => candidate.Id == Tenant.Id);
            tenant.RecordActivity(Unique.Now);
            await catalog.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(_secretName, null);
            await _requestPath.DisposeAsync();
            await _operatorConsole.DisposeAsync();
            await _cacheOwner.DisposeAsync();
        }
    }
}
