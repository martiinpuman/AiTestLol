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
/// every executed shape of the tenant-takeover finding that lives in these two tables: variant 1
/// (ADR-0034 §1), a second tenant row on the victim's cluster copying the victim's
/// <c>database_name</c>; variant 3, a second <c>database_cluster</c> row on the victim's cluster's
/// host and port with a tenant on it copying the victim's <c>database_name</c>, once with an
/// <c>Active</c> attacker and once with a <c>Provisioning</c> one (PR #18 M-3); and variant 3 with
/// the host spelled in another case (PR #18 M-1). Each attempt is either admitted or refused by a
/// constraint — SQLSTATE <c>23505</c> or <c>23514</c>; the refusal is recorded and printed, not
/// asserted, and a failure for any other reason propagates. Then the property is asserted over
/// every non-deleted tenant the catalog now holds — the attacker's rows included, if any were
/// admitted. Before the <c>ClusterEndpointUniqueness</c> migration, variant 3 was admitted and this
/// test failed with the victim and the attacker on one endpoint; that run is recorded in B-20's
/// handback.
/// </para>
/// <para>
/// <b>What "resolve" is.</b> The production registration — <c>AddCatalogDatabase</c> over the
/// request path's <c>aurora_app</c> connection and <c>AddTenantConnectionResolver</c> — with one
/// scope per tenant, as one request would have. A tenant the application path may connect to
/// (<c>Active</c>, <c>Suspended</c>; ADR-0007 §11.4) is resolved, and its endpoint is read back out
/// of the resolved string. A tenant the resolver refuses (<c>Provisioning</c>,
/// <c>ProvisioningFailed</c>, <c>SchemaBlocked</c>, <c>Exporting</c>, <c>PendingDeletion</c>) still
/// has a routing row, and a <c>Provisioning</c> row is exactly what B-07.1's adoption rule acts on,
/// so those are compared too: their endpoint is taken from the routing row the resolver reads
/// (<c>ITenantRoutingReader</c>, from the same scope), and for every resolved tenant the test also
/// reads that row and requires the two endpoints to agree, so the row-derived endpoint is shown to
/// be what the resolver composes rather than assumed to be. The one substitution is the secret
/// store: the fixture's clusters carry vault references no store in this process can answer, and
/// the property is about the endpoint rather than the credential, so the store here answers every
/// reference with a placeholder. This test replaced B-06.1's inertness guard
/// (<c>Two_cluster_rows_on_one_server_still_route_two_tenants_to_one_physical_database</c>), which
/// asserted the hole was still open and was deleted, not repaired, the day it closed.
/// </para>
/// <para>
/// <b>What is compared, and what that establishes.</b> Host, port and database name, read back
/// through Npgsql's own parser, with the host folded to lower case because a host name is
/// case-insensitive — not the whole connection string, which carries the tenant's key in
/// <c>Application Name</c> and so never collides. The property therefore establishes that no two
/// non-deleted tenants name the same host (in any spelling), port and database; a further variant
/// that differs in none of those fails here, whichever index it walked around. It does <em>not</em>
/// establish that no two tenants reach the same server: two different names for one machine — an
/// IP literal beside a host name, a CNAME, a second DNS record, a failover alias — are different
/// strings here and different rows in the catalog (executed in PR #18's review: <c>localhost</c>
/// and <c>127.0.0.1</c> as two rows, one database, <c>collisions: 0</c>). No comparison of stored
/// names can close that; <c>TenantIdentityStamp</c> is the control (ADR-0034 §4), on every physical
/// connection, and no test over catalog rows can claim it.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogRoutingUniquenessTests
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

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
        // resolved tenants and one pair of its own to compare whatever else the shared catalog holds.
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
                "variant 3: a second cluster row on the victim's cluster's host and port, and an Active tenant on it copying its database_name",
                (owner, attempt) => SecondClusterRowOnTheSameEndpointAsync(owner, attempt, victim, victimCluster, "Active", HostSpelling.AsStored)),
            await AttemptAsync(
                "variant 3 with a Provisioning attacker: the same second cluster row, and a Provisioning tenant on it copying its database_name",
                (owner, attempt) => SecondClusterRowOnTheSameEndpointAsync(owner, attempt, victim, victimCluster, "Provisioning", HostSpelling.AsStored)),
            await AttemptAsync(
                "variant 3 in another case: a second cluster row on the victim's cluster's host in upper case and its port, and an Active tenant on it copying its database_name",
                (owner, attempt) => SecondClusterRowOnTheSameEndpointAsync(owner, attempt, victim, victimCluster, "Active", HostSpelling.UpperCased)),
        ];

        Resolution resolution = await ResolveEveryNonDeletedTenantAsync();
        IReadOnlyList<ResolvedTenant> compared = [.. resolution.Resolved, .. resolution.FromRoutingRow];
        (int pairsCompared, IReadOnlyList<(ResolvedTenant, ResolvedTenant)> collisions) = CompareEveryPair(compared);

        _output.WriteLine($"takeover shapes attempted: {attempts.Length}");
        foreach (TakeoverAttempt attempt in attempts)
        {
            _output.WriteLine($"  {attempt.Shape}: {(attempt.RefusedBy is null ? "ADMITTED" : $"refused, {attempt.RefusedBy}")}");
        }

        _output.WriteLine(
            $"tenants: {resolution.NonDeleted} non-deleted; {resolution.Resolved.Count} resolved through the resolver;"
            + $" {resolution.FromRoutingRow.Count} not routable, taken from the routing row the resolver reads"
            + $" ({string.Join(", ", resolution.FromRoutingRow.GroupBy(tenant => tenant.State).Select(group => $"{group.Key}: {group.Count()}"))});"
            + $" resolver and routing row agree on {resolution.Agreements} of {resolution.Resolved.Count};"
            + $" pairs compared: {pairsCompared}; collisions: {collisions.Count}");
        foreach ((ResolvedTenant first, ResolvedTenant second) in collisions)
        {
            _output.WriteLine($"  {first.Key} ({first.State}) and {second.Key} ({second.State}) -> {first.PhysicalDatabase}");
        }

        resolution.Resolved.Count.ShouldBeGreaterThanOrEqualTo(2, "the property is vacuous over fewer than two resolved tenants");
        pairsCompared.ShouldBeGreaterThan(0, "the property is vacuous over zero pairs");
        compared.Count.ShouldBe(resolution.NonDeleted, "every non-deleted tenant is compared, routable or not");
        resolution.Agreements.ShouldBe(resolution.Resolved.Count, "the routing row must yield the endpoint the resolver composes, or it cannot stand in for the resolver on the tenants it refuses");
        resolution.Resolved.Select(tenant => tenant.Id).ShouldContain(victim.Id, "the victim was not among the tenants resolved");
        resolution.Resolved.Select(tenant => tenant.Id).ShouldContain(bystander.Id, "the bystander was not among the tenants resolved");
        collisions.ShouldBeEmpty(
            "ADR-0034 §3.3: two non-deleted tenants resolve to one physical database - "
            + string.Join("; ", collisions.Select(pair => $"{pair.Item1.Key} ({pair.Item1.State}) and {pair.Item2.Key} ({pair.Item2.State}) -> {pair.Item1.PhysicalDatabase}"))
            + ". Whatever the catalog constrained, it was not the endpoint the resolver composes.");
    }

    /// <summary>
    /// Every non-deleted tenant, one scope each from the production registration: resolved through
    /// the resolver where the application path may connect, and taken from the routing row the
    /// resolver reads where it may not — with the two shown to agree wherever both exist. See the
    /// remarks on the class.
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
        List<ResolvedTenant> fromRoutingRow = [];
        int agreements = 0;
        foreach ((TenantId id, TenantKey key) in tenants)
        {
            await using AsyncServiceScope scope = requestPath.CreateAsyncScope();
            TenantRouting routing = await scope.ServiceProvider.GetRequiredService<ITenantRoutingReader>().ReadAsync(id, CancellationToken.None)
                ?? throw new InvalidOperationException($"catalog.tenant {key} was listed a moment ago and now has no routing row.");
            string fromRow = PhysicalDatabaseOf(routing);

            try
            {
                TenantConnection connection = await scope.ServiceProvider
                    .GetRequiredService<ITenantConnectionResolver>()
                    .ResolveAsync(id, CancellationToken.None);
                string fromResolver = PhysicalDatabaseOf(connection);
                if (string.Equals(fromResolver, fromRow, StringComparison.Ordinal))
                {
                    agreements++;
                }

                resolved.Add(new ResolvedTenant(id, key, routing.State, fromResolver));
            }
            catch (TenantNotRoutableException)
            {
                fromRoutingRow.Add(new ResolvedTenant(id, key, routing.State, fromRow));
            }
        }

        return new Resolution(tenants.Count, resolved, fromRoutingRow, agreements);
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

    /// <summary>What the server would be asked to open, read back out of the resolved string by Npgsql's own parser.</summary>
    private static string PhysicalDatabaseOf(TenantConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString.Reveal());
        return PhysicalDatabaseOf(parsed.Host!, parsed.Port, parsed.Database!);
    }

    /// <summary>What the resolver would compose from the row it read, for a tenant it refuses to resolve.</summary>
    private static string PhysicalDatabaseOf(TenantRouting routing)
    {
        if (routing.Cluster is null || routing.DatabaseName is null)
        {
            throw new InvalidOperationException(
                $"catalog.tenant {routing.TenantKey} is {routing.State} yet has no cluster or database name; "
                + "ck_tenant_routing_present_unless_deleted requires both in every state but Deleted.");
        }

        return PhysicalDatabaseOf(routing.Cluster.Host, routing.Cluster.Port, routing.DatabaseName);
    }

    /// <summary>
    /// Host folded to lower case, because a host name is case-insensitive and two spellings reach
    /// one server (PR #18 M-1); port and database name as they are, because a PostgreSQL database
    /// name is not. Nothing here resolves a name to an address — see the remarks on the class.
    /// </summary>
    private static string PhysicalDatabaseOf(string host, int port, string database) =>
        $"{host.ToLowerInvariant()}:{port}/{database}";

    /// <summary>Every unordered pair, compared; the count is what the test reports, so it is the count of comparisons made.</summary>
    private static (int PairsCompared, IReadOnlyList<(ResolvedTenant, ResolvedTenant)> Collisions) CompareEveryPair(IReadOnlyList<ResolvedTenant> compared)
    {
        int pairs = 0;
        List<(ResolvedTenant, ResolvedTenant)> collisions = [];
        for (int first = 0; first < compared.Count; first++)
        {
            for (int second = first + 1; second < compared.Count; second++)
            {
                pairs++;
                if (string.Equals(compared[first].PhysicalDatabase, compared[second].PhysicalDatabase, StringComparison.Ordinal))
                {
                    collisions.Add((compared[first], compared[second]));
                }
            }
        }

        return (pairs, collisions);
    }

    /// <summary>
    /// One takeover shape, written as the owner inside one transaction: committed if the catalog
    /// admits every row of it, rolled back and recorded if a unique index or a check constraint
    /// refuses one. Any other failure is not a refusal and propagates.
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
        catch (PostgresException refusal) when (refusal.SqlState is UniqueViolation or CheckViolation)
        {
            await attempt.RollbackAsync();
            return new TakeoverAttempt(shape, $"{refusal.SqlState} on {refusal.ConstraintName ?? refusal.MessageText}");
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

    private static async Task SecondClusterRowOnTheSameEndpointAsync(
        NpgsqlConnection owner,
        NpgsqlTransaction attempt,
        Tenant victim,
        DatabaseCluster victimCluster,
        string attackerState,
        HostSpelling spelling)
    {
        string secondRow = Unique.ClusterId().Value;
        string host = spelling == HostSpelling.UpperCased ? "upper(c.host)" : "c.host";

        await InsertExactlyOneAsync(
            owner,
            attempt,
            "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) "
            + $"SELECT @id, c.region, {host}, c.port, c.maintenance_database, c.admin_secret_ref, c.migrator_secret_ref, c.app_secret_ref, c.max_tenants, c.state "
            + "FROM catalog.database_cluster c WHERE c.id = @victim_cluster",
            ("id", secondRow),
            ("victim_cluster", victimCluster.Id.Value));

        await InsertExactlyOneAsync(
            owner,
            attempt,
            "INSERT INTO catalog.tenant (id, key, display_name, state, cluster_id, database_name, residency_region, plan, created_at) "
            + "SELECT @id, @key, 'Attacker', @state, @second_row, t.database_name, t.residency_region, 'standard', @created_at "
            + "FROM catalog.tenant t WHERE t.id = @victim",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value),
            ("state", attackerState),
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

    private enum HostSpelling
    {
        AsStored,
        UpperCased,
    }

    private sealed record TakeoverAttempt(string Shape, string? RefusedBy);

    private sealed record ResolvedTenant(TenantId Id, TenantKey Key, TenantState State, string PhysicalDatabase);

    private sealed record Resolution(
        int NonDeleted,
        IReadOnlyList<ResolvedTenant> Resolved,
        IReadOnlyList<ResolvedTenant> FromRoutingRow,
        int Agreements);

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
