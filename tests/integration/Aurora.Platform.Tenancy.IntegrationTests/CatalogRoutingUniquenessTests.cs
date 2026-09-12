using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Secrets;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// ADR-0034 §3.3, the acceptance criterion of B-20 — the property, not the index: <em>no two
/// non-deleted <c>catalog.tenant</c> rows resolve to the same physical database</em>, computed by
/// the real <c>ITenantConnectionResolver</c> over the real catalog rows, reporting how many
/// tenants were resolved and how many pairs were compared, and failing on zero of either.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the test constructs the attack before it asserts the property.</b> A property asserted
/// over whatever rows happen to be in the catalog passes on an empty catalog and on a catalog
/// nobody has attacked; it cannot fail, and a test that cannot fail is not a check. So this test
/// first asks the catalog, as the owner — the one principal that may write routing rows — to store
/// every executed variant of the tenant-takeover finding that lives in these two tables
/// (ADR-0034 §1): variant 1, a second tenant row on the victim's cluster copying the victim's
/// <c>database_name</c>; variant 3, a second <c>database_cluster</c> row on the victim's cluster's
/// host and port, with a tenant on it copying the victim's <c>database_name</c>. Each attempt is
/// either admitted or refused with SQLSTATE <c>23505</c>; the refusal is recorded and printed, not
/// asserted, and a failure for any other reason propagates. Then the property is asserted over
/// every non-deleted tenant the catalog now holds — the attacker's rows included, if any were
/// admitted. Before the <c>ClusterEndpointUniqueness</c> migration, variant 3 was admitted and this
/// test failed with the victim and the attacker resolving to one endpoint; that run is recorded in
/// B-20's handback. A fourth variant the catalog admits fails here the same way, whichever index it
/// walked around — which is why this, and not a check that an index exists, is the criterion.
/// </para>
/// <para>
/// <b>What "resolve" is.</b> The production registration — <c>AddCatalogDatabase</c> over the
/// request path's <c>aurora_app</c> connection and <c>AddTenantConnectionResolver</c> — with one
/// scope per resolve, as one request would have. The one substitution is the secret store: the
/// fixture's clusters carry vault references no store in this process can answer, and the
/// property is about the endpoint rather than the credential, so the store here answers every
/// reference with a placeholder. The resolver refuses a tenant in a state the application path may
/// not connect to (ADR-0007 §11.4: everything but <c>Active</c> and <c>Suspended</c>); those are
/// counted and printed rather than resolved, and the attack shapes are written <c>Active</c> so
/// that an admitted one is. This test replaced B-06.1's inertness guard
/// (<c>Two_cluster_rows_on_one_server_still_route_two_tenants_to_one_physical_database</c>), which
/// asserted the hole was still open and was deleted, not repaired, the day it closed.
/// </para>
/// <para>
/// <b>Why the comparison is over the physical endpoint and not the whole string.</b> The composer
/// stamps each tenant's key into <c>Application Name</c>, so two tenants' complete connection
/// strings differ even when they open one database — compared whole, the property could never
/// fail. What must be unique is what the server is asked to open: host, port and database, read
/// back out of the resolved string through Npgsql's own parser.
/// </para>
/// <para>
/// <b>What this does not cover</b> (ADR-0034 §2, §3.2): two names for one server — a CNAME, a
/// second DNS record, a failover alias, an IP literal beside a host name, a host spelled in another
/// case. The catalog stores what it was told; a constraint over a name narrows what can be stored
/// and never establishes identity. That is <c>TenantIdentityStamp</c>'s job (ADR-0034 §4), on every
/// physical connection, and no test over catalog rows can claim it.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogRoutingUniquenessTests
{
    private const string UniqueViolation = "23505";

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogRoutingUniquenessTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task No_two_non_deleted_tenants_resolve_to_the_same_physical_database_whatever_takeover_shape_the_catalog_was_asked_to_store()
    {
        // Two legitimate, routable tenants on two clusters, so the property has at least two
        // tenants and one pair of its own to compare whatever else the shared catalog holds.
        DatabaseCluster victimCluster = Unique.Cluster();
        Tenant victim = Unique.Tenant(victimCluster);
        victim.Activate(1, Unique.Now);
        DatabaseCluster otherCluster = Unique.Cluster();
        Tenant bystander = Unique.Tenant(otherCluster);
        bystander.Activate(1, Unique.Now);
        await _catalog.SeedAsync(owner =>
        {
            owner.DatabaseClusters.AddRange(victimCluster, otherCluster);
            owner.Tenants.AddRange(victim, bystander);
        });

        TakeoverAttempt[] attempts =
        [
            await AttemptAsync(
                "variant 1: a second tenant row on the victim's cluster, copying its database_name",
                (owner, attempt) => SecondTenantOnTheSameClusterAsync(owner, attempt, victim)),
            await AttemptAsync(
                "variant 3: a second cluster row on the victim's cluster's host and port, and a tenant on it copying its database_name",
                (owner, attempt) => SecondClusterRowOnTheSameEndpointAsync(owner, attempt, victim, victimCluster)),
        ];

        Resolution resolution = await ResolveEveryNonDeletedTenantAsync();
        IReadOnlyList<ResolvedTenant> resolved = resolution.Resolved;
        (int pairsCompared, IReadOnlyList<(ResolvedTenant, ResolvedTenant)> collisions) = CompareEveryPair(resolved);

        _output.WriteLine($"takeover shapes attempted: {attempts.Length}");
        foreach (TakeoverAttempt attempt in attempts)
        {
            _output.WriteLine($"  {attempt.Shape}: {(attempt.RefusedBy is null ? "ADMITTED" : $"refused, {UniqueViolation} on {attempt.RefusedBy}")}");
        }

        _output.WriteLine(
            $"tenants: {resolution.NonDeleted} non-deleted, {resolved.Count} resolved, {resolution.NotRoutable.Count} not routable"
            + $" ({string.Join(", ", resolution.NotRoutable.GroupBy(tenant => tenant.State).Select(group => $"{group.Key}: {group.Count()}"))});"
            + $" pairs compared: {pairsCompared}; collisions: {collisions.Count}");
        foreach ((ResolvedTenant first, ResolvedTenant second) in collisions)
        {
            _output.WriteLine($"  {first.Key} and {second.Key} -> {first.PhysicalDatabase}");
        }

        resolved.Count.ShouldBeGreaterThanOrEqualTo(2, "the property is vacuous over fewer than two resolved tenants");
        pairsCompared.ShouldBeGreaterThan(0, "the property is vacuous over zero pairs");
        resolved.Select(tenant => tenant.Id).ShouldContain(victim.Id, "the victim was not among the tenants resolved");
        resolved.Select(tenant => tenant.Id).ShouldContain(bystander.Id, "the bystander was not among the tenants resolved");
        collisions.ShouldBeEmpty(
            "ADR-0034 §3.3: two non-deleted tenants resolve to one physical database - "
            + string.Join("; ", collisions.Select(pair => $"{pair.Item1.Key} and {pair.Item2.Key} -> {pair.Item1.PhysicalDatabase}"))
            + ". Whatever the catalog constrained, it was not the endpoint the resolver composes.");
    }

    /// <summary>
    /// Every non-deleted tenant, resolved through the resolver the production registration hands
    /// out, one scope per resolve. See the remarks on the class for the one substitution and for
    /// what the resolver refuses.
    /// </summary>
    private async Task<Resolution> ResolveEveryNonDeletedTenantAsync()
    {
        IReadOnlyList<(TenantId Id, TenantKey Key)> tenants = await NonDeletedTenantsAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ISecretStore>(new AnyReferenceSecretStore());
        services.AddCatalogDatabase(_catalog.AppConnectionString);
        services.AddTenantConnectionResolver(TenantPoolProfile.Web);
        await using ServiceProvider requestPath = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        List<ResolvedTenant> resolved = [];
        List<(TenantKey Key, TenantState State)> notRoutable = [];
        foreach ((TenantId id, TenantKey key) in tenants)
        {
            await using AsyncServiceScope scope = requestPath.CreateAsyncScope();
            try
            {
                TenantConnection connection = await scope.ServiceProvider
                    .GetRequiredService<ITenantConnectionResolver>()
                    .ResolveAsync(id, CancellationToken.None);
                resolved.Add(new ResolvedTenant(id, key, PhysicalDatabaseOf(connection)));
            }
            catch (TenantNotRoutableException refused)
            {
                notRoutable.Add((key, refused.State));
            }
        }

        return new Resolution(tenants.Count, resolved, notRoutable);
    }

    /// <summary>The domain of the property: every tenant row that is not a tombstone, read as the request path reads.</summary>
    private async Task<IReadOnlyList<(TenantId Id, TenantKey Key)>> NonDeletedTenantsAsync()
    {
        await using CatalogDbContext catalog = _catalog.OpenAsApp();
        var rows = await catalog.Tenants
            .Where(tenant => tenant.State != TenantState.Deleted)
            .Select(tenant => new { tenant.Id, tenant.Key })
            .ToListAsync();
        return rows.Select(row => (row.Id, row.Key)).ToList();
    }

    /// <summary>
    /// What the server would be asked to open: host, port and database, read back out of the
    /// resolved string by Npgsql's own parser.
    /// </summary>
    private static string PhysicalDatabaseOf(TenantConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString.Reveal());
        return $"{parsed.Host}:{parsed.Port}/{parsed.Database}";
    }

    /// <summary>Every unordered pair, compared; the count is what the test reports, so it is the count of comparisons made.</summary>
    private static (int PairsCompared, IReadOnlyList<(ResolvedTenant, ResolvedTenant)> Collisions) CompareEveryPair(IReadOnlyList<ResolvedTenant> resolved)
    {
        int pairs = 0;
        List<(ResolvedTenant, ResolvedTenant)> collisions = [];
        for (int first = 0; first < resolved.Count; first++)
        {
            for (int second = first + 1; second < resolved.Count; second++)
            {
                pairs++;
                if (string.Equals(resolved[first].PhysicalDatabase, resolved[second].PhysicalDatabase, StringComparison.Ordinal))
                {
                    collisions.Add((resolved[first], resolved[second]));
                }
            }
        }

        return (pairs, collisions);
    }

    /// <summary>
    /// One takeover shape, written as the owner inside one transaction: committed if the catalog
    /// admits every row of it, rolled back and recorded if a unique index refuses one. Any other
    /// failure is not a refusal and propagates.
    /// </summary>
    private async Task<TakeoverAttempt> AttemptAsync(string shape, Func<NpgsqlConnection, NpgsqlTransaction, Task> store)
    {
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction attempt = await owner.BeginTransactionAsync();
        try
        {
            await store(owner, attempt);
            await attempt.CommitAsync();
            return new TakeoverAttempt(shape, RefusedBy: null);
        }
        catch (PostgresException refusal) when (refusal.SqlState == UniqueViolation)
        {
            await attempt.RollbackAsync();
            return new TakeoverAttempt(shape, refusal.ConstraintName ?? refusal.MessageText);
        }
    }

    private static Task SecondTenantOnTheSameClusterAsync(NpgsqlConnection owner, NpgsqlTransaction attempt, Tenant victim) =>
        InsertExactlyOneAsync(
            owner,
            attempt,
            "INSERT INTO catalog.tenant (id, key, display_name, state, cluster_id, database_name, residency_region, plan, created_at) "
            + "SELECT @id, @key, 'Attacker', 'Active', t.cluster_id, t.database_name, t.residency_region, 'standard', @created_at "
            + "FROM catalog.tenant t WHERE t.id = @victim",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value),
            ("created_at", Unique.Now),
            ("victim", victim.Id.Value));

    private static async Task SecondClusterRowOnTheSameEndpointAsync(NpgsqlConnection owner, NpgsqlTransaction attempt, Tenant victim, DatabaseCluster victimCluster)
    {
        string secondRow = Unique.ClusterId().Value;

        await InsertExactlyOneAsync(
            owner,
            attempt,
            "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) "
            + "SELECT @id, c.region, c.host, c.port, c.maintenance_database, c.admin_secret_ref, c.migrator_secret_ref, c.app_secret_ref, c.max_tenants, c.state "
            + "FROM catalog.database_cluster c WHERE c.id = @victim_cluster",
            ("id", secondRow),
            ("victim_cluster", victimCluster.Id.Value));

        await InsertExactlyOneAsync(
            owner,
            attempt,
            "INSERT INTO catalog.tenant (id, key, display_name, state, cluster_id, database_name, residency_region, plan, created_at) "
            + "SELECT @id, @key, 'Attacker', 'Active', @second_row, t.database_name, t.residency_region, 'standard', @created_at "
            + "FROM catalog.tenant t WHERE t.id = @victim",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value),
            ("second_row", secondRow),
            ("created_at", Unique.Now),
            ("victim", victim.Id.Value));
    }

    /// <summary>
    /// An <c>INSERT … SELECT</c> whose <c>SELECT</c> matches nothing inserts nothing and succeeds,
    /// which would read as "admitted" with no row to resolve. So every statement of a shape must
    /// store exactly one row, or the attempt is a broken test rather than a refused attack.
    /// </summary>
    private static async Task InsertExactlyOneAsync(NpgsqlConnection owner, NpgsqlTransaction attempt, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, owner, attempt);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        int inserted = await command.ExecuteNonQueryAsync();
        if (inserted != 1)
        {
            throw new InvalidOperationException($"The takeover shape stored {inserted} rows where it meant to store one: {sql}");
        }
    }

    private sealed record TakeoverAttempt(string Shape, string? RefusedBy);

    private sealed record ResolvedTenant(TenantId Id, TenantKey Key, string PhysicalDatabase);

    private sealed record Resolution(
        int NonDeleted,
        IReadOnlyList<ResolvedTenant> Resolved,
        IReadOnlyList<(TenantKey Key, TenantState State)> NotRoutable);

    /// <summary>
    /// Answers every reference with the same placeholder. The property compares endpoints, and a
    /// credential is not part of one; what this store must not do is decide which tenants get
    /// resolved, so it refuses none.
    /// </summary>
    private sealed class AnyReferenceSecretStore : ISecretStore
    {
        public ValueTask<string> ReadAsync(SecretReference reference, CancellationToken ct) =>
            reference.IsSpecified
                ? ValueTask.FromResult("placeholder-not-a-credential")
                : throw new ArgumentException("The secret reference is unassigned.", nameof(reference));
    }
}
