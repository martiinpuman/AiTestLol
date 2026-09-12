using System;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests.Routing;

/// <summary>
/// The non-negotiable proof of B-06.1: the routing entry is served from the cache, and a tenant
/// state change makes the next resolve read the catalog again. Plus an inertness guard over the
/// one shape the catalog does not yet refuse (PR #14 H-1): green while the hole is open, red the day
/// it closes, to be deleted then rather than repaired.
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
    private const string UniqueViolation = "23505";

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

    [Fact]
    public async Task Two_cluster_rows_on_one_server_still_route_two_tenants_to_one_physical_database()
    {
        // INERTNESS GUARD: green while the hole is open, red the day it closes - and then DELETED,
        // not repaired. The pattern of B-04's rule over the not-yet-existing kernel types and
        // B-19's PartitionPrivileges guard: a test that asserts a gap exists, so that whoever closes
        // the gap is stopped here and made to write the real assertion in its place.
        //
        // The hole (PR #14 H-1): ux_tenant_cluster_id_database_name is keyed on cluster_id; the
        // identity of a physical database is (host, port, database_name); and nothing makes
        // database_cluster (host, port) unique. Two cluster rows for one server - a replica row, a
        // re-registration after a failover, a Draining row kept beside its replacement - one tenant
        // each, the attacker's database_name copied from the victim's, and both resolve to one
        // physical database. The single-cluster shape is refused correctly by the B-05 index; this
        // one walks around it. Reachability: aurora_app cannot write either row; this needs the owner.
        //
        // The fix is ADR-0034 §3.1's index, ux_database_cluster_host_port - a CatalogDbContext
        // migration this branch must not add while the catalog migration chain is one branch at a
        // time. When it lands, the catalog refuses one of the two inserts below with 23505, this
        // test fails, and its message says what to do: delete it and write the real property -
        // no two non-deleted tenant rows produce the same resolved connection string, computed by
        // the real resolver over real rows (ADR-0034 §3.3) - in its place.
        //
        // It will not be the only red that day (PR #14 n-4): RoutingTestBed registers a cluster row
        // per bed, every bed on this one container's endpoint, so the index reds the five other
        // tests in this file as well - ADR-0034 §3.2 forbids exactly that fixture shape. Before the
        // real property can be written, the bed needs one shared cluster row across beds, or a
        // second container. One planned change, not six surprises.
        await using RoutingTestBed bed = await RoutingTestBed.WithActiveTenantAsync(_catalog);
        Tenant victim = bed.Tenant;
        DatabaseCluster secondRow = bed.AnotherClusterRowForTheSameServer();
        Tenant attacker = RoutingTestBed.ActiveTenantOn(secondRow);

        string? refusedBy = null;
        try
        {
            await bed.SeedAsync(owner =>
            {
                owner.DatabaseClusters.Add(secondRow);
                owner.Tenants.Add(attacker);
            });
            await CopyDatabaseNameAsync(from: victim, to: attacker);
        }
        catch (Exception refusal) when (UniqueViolationIn(refusal) is { } constraint)
        {
            refusedBy = constraint;
        }

        refusedBy.ShouldBeNull(
            $"the catalog now refuses a second cluster row on one server ({UniqueViolation} on {refusedBy}): the PR #14 H-1 "
            + "hole is closed and this inertness guard is obsolete. Delete it and write the real property in its place - "
            + "no two non-deleted tenant rows produce the same resolved connection string, computed by the real resolver "
            + "over real rows (ADR-0034).");

        string victimEndpoint = PhysicalEndpointOf(await bed.ResolveAsync(victim.Id));
        string attackerEndpoint = PhysicalEndpointOf(await bed.ResolveAsync(attacker.Id));

        _output.WriteLine($"victim   -> {victimEndpoint}");
        _output.WriteLine($"attacker -> {attackerEndpoint}");
        _output.WriteLine("the PR #14 H-1 hole is still open: two tenants resolve to one physical database (inertness guard, green)");
        attackerEndpoint.ShouldBe(
            victimEndpoint,
            "the two tenants no longer resolve to one physical database, so something closed the PR #14 H-1 hole "
            + "without a 23505 at either insert: this inertness guard is obsolete. Delete it and write the real property "
            + "in its place (ADR-0034).");
    }

    /// <summary>The constraint a unique violation names, whether PostgreSQL threw directly or through EF's save.</summary>
    private static string? UniqueViolationIn(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: UniqueViolation } violation
            ? violation.ConstraintName
            : null;

    /// <summary>What the server would be asked to open: host, port and database, read back by Npgsql's own parser.</summary>
    private static string PhysicalEndpointOf(TenantConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString.Reveal());
        return $"{parsed.Host}:{parsed.Port}/{parsed.Database}";
    }

    /// <summary>As the owner, the one principal that may rewrite where a tenant resolves: the attacker's row takes the victim's database name.</summary>
    private async Task CopyDatabaseNameAsync(Tenant from, Tenant to)
    {
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand("UPDATE catalog.tenant SET database_name = @name WHERE id = @id", owner);
        command.Parameters.AddWithValue("name", from.DatabaseName!);
        command.Parameters.AddWithValue("id", to.Id.Value);
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    /// <summary>
    /// Two composition roots over one cache: the request path as <c>aurora_app</c> with a statement
    /// counter on its catalog context, and the operator console as the owner. Both are built with
    /// the production DI extensions; nothing about the resolver, the cache or the invalidator is
    /// hand-wired. Seeds one cluster row for the test container and two active tenants on it.
    /// </summary>
    private sealed class RoutingTestBed : IAsyncDisposable
    {
        private readonly CatalogDatabaseFixture _catalog;
        private readonly NpgsqlConnectionStringBuilder _server;
        private readonly string _secretName;
        private readonly ServiceProvider _cacheOwner;
        private readonly ServiceProvider _requestPath;
        private readonly ServiceProvider _operatorConsole;

        private RoutingTestBed(
            CatalogDatabaseFixture catalog,
            NpgsqlConnectionStringBuilder server,
            string secretName,
            DatabaseCluster cluster,
            Tenant tenant,
            Tenant otherTenant)
        {
            _catalog = catalog;
            _server = server;
            _secretName = secretName;
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
            var server = new NpgsqlConnectionStringBuilder(catalog.AppConnectionString);
            string secretName = "AURORA_TEST_APP_SECRET_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(secretName, server.Password);

            DatabaseCluster cluster = ClusterRowFor(server, secretName);
            Tenant tenant = ActiveTenantOn(cluster);
            Tenant otherTenant = ActiveTenantOn(cluster);
            await catalog.SeedAsync(owner =>
            {
                owner.DatabaseClusters.Add(cluster);
                owner.Tenants.Add(tenant);
                owner.Tenants.Add(otherTenant);
            });

            return new RoutingTestBed(catalog, server, secretName, cluster, tenant, otherTenant);
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

        /// <summary>A second <c>catalog.database_cluster</c> row for the same server: a different id, the same host and port.</summary>
        public DatabaseCluster AnotherClusterRowForTheSameServer() => ClusterRowFor(_server, _secretName);

        public Task SeedAsync(Action<CatalogDbContext> seed) => _catalog.SeedAsync(seed);

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
            Environment.SetEnvironmentVariable(_secretName, null);
            await _requestPath.DisposeAsync();
            await _operatorConsole.DisposeAsync();
            await _cacheOwner.DisposeAsync();
        }

        private static DatabaseCluster ClusterRowFor(NpgsqlConnectionStringBuilder server, string secretName) =>
            DatabaseCluster.Register(
                Unique.ClusterId(),
                Region.Parse("nz", null),
                server.Host!,
                server.Port,
                CatalogDatabaseFixture.MaintenanceDatabaseName,
                SecretReference.Of("vault://kv/aurora/test/admin"),
                SecretReference.Of("vault://kv/aurora/test/migrator"),
                SecretReference.Of("env:" + secretName),
                1_000);
    }
}
