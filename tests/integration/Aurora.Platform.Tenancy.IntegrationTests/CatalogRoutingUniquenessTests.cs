using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Secrets;
using Aurora.Platform.Tenancy.Tests;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// ADR-0036 §2.3, the acceptance criterion of B-20 — the property, not the index: <em>no two
/// non-deleted <c>catalog.tenant</c> rows resolve to the same physical endpoint</em>, the endpoint
/// being the <c>(host, port, database)</c> triple parsed out of the string the real
/// <c>ITenantConnectionResolver</c> produced, host compared ignoring case; reporting how many
/// tenants were resolved and how many pairs were compared, and failing on zero of either. This is
/// ADR-0036 §2.4's D2, the fleet scan; the comparator itself is proven over synthetic strings in
/// <c>ResolvedEndpointComparisonTests</c> (D1), because once the catalog refuses the shapes below
/// this scan can no longer be made to fail through the normal write path, and D1 always can.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the test constructs the attack before it asserts the property.</b> A property asserted
/// over whatever rows happen to be in the catalog passes on an empty catalog and on a catalog
/// nobody has attacked. So this test first asks the catalog, as the owner — the one principal that
/// may write routing rows — to store every executed shape of the tenant-takeover finding that
/// lives in these two tables: variant 1 (ADR-0034 §1), a second tenant row on the victim's
/// cluster copying the victim's <c>database_name</c>; variant 3, a second <c>database_cluster</c>
/// row on the victim's cluster's host and port with a tenant on it copying the victim's
/// <c>database_name</c>, once with an <c>Active</c> attacker and once with a <c>Provisioning</c>
/// one (PR #18 M-3); and variant 3 with the host spelled in another case (PR #18 M-1,
/// ADR-0036 §3). Each attempt is either admitted or refused by a constraint — SQLSTATE
/// <c>23505</c> or <c>23514</c>; the refusal is recorded and printed, not asserted, and a failure
/// for any other reason propagates. Then the property is asserted over every non-deleted tenant
/// the catalog now holds — the attacker's rows included, if any were admitted. Before the
/// <c>ClusterEndpointUniqueness</c> migration, variant 3 was admitted and this test failed with the
/// victim and the attacker on one endpoint; that run is ADR-0036 §2.4's D3, recorded on the pull
/// request and in this module's README because the fix removed the evidence.
/// </para>
/// <para>
/// <b>What "resolve" is.</b> The production registration — <c>AddCatalogDatabase</c> over the
/// request path's <c>aurora_app</c> connection and <c>AddTenantConnectionResolver</c> — with one
/// scope per tenant, as one request would have. A tenant the application path may connect to
/// (<c>Active</c>, <c>Suspended</c>; ADR-0007 §11.4) is resolved, and its endpoint is parsed out
/// of the resolved string. A tenant the resolver refuses (<c>Provisioning</c>,
/// <c>ProvisioningFailed</c>, <c>SchemaBlocked</c>, <c>Exporting</c>, <c>PendingDeletion</c>) still
/// has a routing row, and a <c>Provisioning</c> row is exactly what B-07.1's adoption rule acts on,
/// so those are compared too: the row the real reader read (<c>ITenantRoutingReader</c>, same
/// scope) is put through the real composer (<c>TenantConnectionStringComposer</c>), which is what
/// the resolver does after its state gate, and the endpoint is parsed out of that string. For
/// every tenant the resolver does resolve, the same composition is run beside it and the two
/// endpoints are required to agree, so the composed-from-row endpoint is shown to be what the
/// resolver emits rather than assumed to be — the assertion stays a property of the resolver's
/// output, not of its input (ADR-0036 §2.3 point 1). The one substitution is the secret store: the
/// fixture's clusters carry vault references no store in this process can answer, and the
/// property is about the endpoint rather than the credential, so the store here answers every
/// reference with a placeholder. This test replaced B-06.1's inertness guard
/// (<c>Two_cluster_rows_on_one_server_still_route_two_tenants_to_one_physical_database</c>), which
/// asserted the hole was still open and was deleted, not repaired, the day it closed.
/// </para>
/// <para>
/// <b>What the comparison establishes, and what it does not</b> (ADR-0036 §2.4, the link D1 and
/// D2 stop at). Host folded, port and database exact — never the whole string, which carries the
/// tenant's key in <c>Application Name</c> and so never collides (executed both ways over one
/// catalog in PR #18's review: endpoints <c>collisions: 1</c>, whole strings <c>collisions: 0</c>;
/// kept executable in D1). The property therefore establishes that no two non-deleted tenants
/// name the same host in any spelling, port and database, so a further variant that differs in
/// none of those fails here whichever index it walked around. It does <em>not</em> establish that
/// no two tenants reach the same server: two different names for one machine — an IP literal
/// beside a host name, a CNAME, a second DNS record, a failover alias — are different triples here
/// and different rows in the catalog (executed in the same review: <c>localhost</c> and
/// <c>127.0.0.1</c> as rows, one database, <c>collisions: 0</c>). No comparison of stored names
/// can close that; <c>TenantIdentityStamp</c> is the control (ADR-0034 §4), on every physical
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
    public async Task No_two_non_deleted_tenants_resolve_to_the_same_physical_endpoint_whatever_takeover_shape_the_catalog_was_asked_to_store()
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
        IReadOnlyList<ResolvedTenant> compared = [.. resolution.Resolved, .. resolution.Composed];
        EndpointComparison<ResolvedTenant> comparison = EndpointCollisions.Find(compared, tenant => tenant.Endpoint);

        _output.WriteLine($"takeover shapes attempted: {attempts.Length}");
        foreach (TakeoverAttempt attempt in attempts)
        {
            _output.WriteLine($"  {attempt.Shape}: {(attempt.RefusedBy is null ? "ADMITTED" : $"refused, {attempt.RefusedBy}")}");
        }

        _output.WriteLine(
            $"tenants: {resolution.NonDeleted} non-deleted; {resolution.Resolved.Count} resolved through the resolver;"
            + $" {resolution.Composed.Count} refused by its state gate and composed from the row it read"
            + $" ({string.Join(", ", resolution.Composed.GroupBy(tenant => tenant.State).Select(group => $"{group.Key}: {group.Count()}"))});"
            + $" resolver and composition agree on {resolution.Agreements} of {resolution.Resolved.Count};"
            + $" pairs compared: {comparison.PairsCompared}; collisions: {comparison.Collisions.Count}");
        foreach ((ResolvedTenant first, ResolvedTenant second) in comparison.Collisions)
        {
            _output.WriteLine($"  {first.Key} ({first.State}) and {second.Key} ({second.State}) -> {first.Endpoint}");
        }

        resolution.Resolved.Count.ShouldBeGreaterThanOrEqualTo(2, "the property is vacuous over fewer than two resolved tenants");
        comparison.PairsCompared.ShouldBeGreaterThan(0, "the property is vacuous over zero pairs");
        compared.Count.ShouldBe(resolution.NonDeleted, "every non-deleted tenant is compared, routable or not");
        resolution.Agreements.ShouldBe(resolution.Resolved.Count, "the composition over the row must yield the endpoint the resolver emits, or it cannot stand in for the resolver on the tenants it refuses");
        resolution.Resolved.Select(tenant => tenant.Id).ShouldContain(victim.Id, "the victim was not among the tenants resolved");
        resolution.Resolved.Select(tenant => tenant.Id).ShouldContain(bystander.Id, "the bystander was not among the tenants resolved");
        comparison.Collisions.ShouldBeEmpty(
            "ADR-0036 §2.3: two non-deleted tenants resolve to one physical endpoint - "
            + string.Join("; ", comparison.Collisions.Select(pair => $"{pair.First.Key} ({pair.First.State}) and {pair.Second.Key} ({pair.Second.State}) -> {pair.First.Endpoint}"))
            + ". Whatever the catalog constrained, it was not the endpoint the resolver composes.");
    }

    /// <summary>
    /// Every non-deleted tenant, one scope each from the production registration: resolved through
    /// the resolver where the application path may connect, and composed from the row the resolver
    /// read where it may not — with the two shown to agree wherever both exist. See the remarks on
    /// the class.
    /// </summary>
    private async Task<Resolution> ResolveEveryNonDeletedTenantAsync()
    {
        IReadOnlyList<(TenantId Id, TenantKey Key)> tenants = await NonDeletedTenantsAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ISecretStore>(new AnyReferenceSecretStore());
        services.AddCatalogDatabase(_catalog.AppConnectionString);
        services.AddTenantConnectionResolver(TenantPoolProfile.Web);
        await using ServiceProvider requestPath = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        TenantPoolSettings pool = requestPath.GetRequiredService<TenantPoolSettings>();
        ISecretStore secrets = requestPath.GetRequiredService<ISecretStore>();

        List<ResolvedTenant> resolved = [];
        List<ResolvedTenant> composed = [];
        int agreements = 0;
        foreach ((TenantId id, TenantKey key) in tenants)
        {
            await using AsyncServiceScope scope = requestPath.CreateAsyncScope();
            TenantRouting routing = await scope.ServiceProvider.GetRequiredService<ITenantRoutingReader>().ReadAsync(id, CancellationToken.None)
                ?? throw new InvalidOperationException($"catalog.tenant {key} was listed a moment ago and now has no routing row.");
            ResolvedEndpoint fromComposition = await ComposeAsEverythingAfterTheStateGateAsync(routing, secrets, pool);

            try
            {
                TenantConnection connection = await scope.ServiceProvider
                    .GetRequiredService<ITenantConnectionResolver>()
                    .ResolveAsync(id, CancellationToken.None);
                ResolvedEndpoint fromResolver = ResolvedEndpoint.Parse(connection.ConnectionString.Reveal());
                if (fromResolver.Equals(fromComposition))
                {
                    agreements++;
                }

                resolved.Add(new ResolvedTenant(id, key, routing.State, fromResolver));
            }
            catch (TenantNotRoutableException)
            {
                composed.Add(new ResolvedTenant(id, key, routing.State, fromComposition));
            }
        }

        return new Resolution(tenants.Count, resolved, composed, agreements);
    }

    /// <summary>
    /// What <c>TenantConnectionResolver.ResolveAsync</c> does once the state gate is passed: the
    /// app credential through the secret store, then the real composer over the row the real
    /// reader read; the endpoint is parsed out of the string that comes back, as it is for a
    /// resolved tenant.
    /// </summary>
    private static async Task<ResolvedEndpoint> ComposeAsEverythingAfterTheStateGateAsync(TenantRouting routing, ISecretStore secrets, TenantPoolSettings pool)
    {
        if (routing.Cluster is null || routing.DatabaseName is null)
        {
            throw new InvalidOperationException(
                $"catalog.tenant {routing.TenantKey} is {routing.State} yet has no cluster or database name; "
                + "ck_tenant_routing_present_unless_deleted requires both in every state but Deleted.");
        }

        string appPassword = await secrets.ReadAsync(SecretReference.Of(routing.Cluster.AppSecretRef), CancellationToken.None);
        string connectionString = TenantConnectionStringComposer.Compose(routing.Cluster, routing.DatabaseName, routing.TenantKey, appPassword, pool);
        return ResolvedEndpoint.Parse(connectionString);
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

    private sealed record ResolvedTenant(TenantId Id, TenantKey Key, TenantState State, ResolvedEndpoint Endpoint);

    private sealed record Resolution(
        int NonDeleted,
        IReadOnlyList<ResolvedTenant> Resolved,
        IReadOnlyList<ResolvedTenant> Composed,
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
