using System;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The rules the catalog database keeps on its own, each shown to bite. Where the entities
/// already refuse the bad value, the row is written with SQL — the point is that the database
/// refuses it even when code did not — as a role that may write that table: <c>aurora_app</c>
/// for the lifecycle column it may move, <c>aurora_migrator</c> for everything the request path
/// only reads — a cluster, a subscription, a tenant, a host
/// (<c>CatalogSchemaAllowlist.AppRolePrivileges</c>). A constraint proven as the owner is proven
/// for every principal, the one that may insert included.
/// </summary>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogConstraintTests
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string CheckViolation = "23514";
    private const string ExclusionViolation = "23P01";

    private readonly CatalogDatabaseFixture _catalog;

    public CatalogConstraintTests(CatalogDatabaseFixture catalog) => _catalog = catalog;

    [Fact]
    public async Task Two_tenants_cannot_share_a_key()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant first = Unique.Tenant(cluster);
        await SaveAsync(cluster, first);

        // On another cluster, so that only the key collides: a key is unique across the fleet, not
        // per cluster, and the same key on the same cluster would derive the same database name and
        // trip ux_tenant_cluster_id_database_name first.
        DatabaseCluster otherCluster = Unique.Cluster();
        Tenant second = Tenant.Reserve(TenantId.Create(), first.Key, "Another Acme", otherCluster, "standard", Unique.Now);

        PostgresException refused = await ShouldBeRefusedAsync(() => SaveAsync(otherCluster, second));
        refused.SqlState.ShouldBe(UniqueViolation);
        refused.ConstraintName.ShouldBe("ux_tenant_key");
    }

    [Fact]
    public async Task Two_tenants_cannot_resolve_to_one_database_on_one_cluster()
    {
        // The first shape of the security re-review's H-4: a second tenant row carrying another
        // tenant's cluster_id and database_name, so that a host bound to it reaches that tenant's
        // data. The grants stop the request path from inserting it at all; this is the rule the
        // database keeps whoever inserts - one database on one cluster is one tenant's - so a bug
        // in the principal that may insert cannot create it either. The entity derives the name
        // from the key, so the only way to write this row is SQL, as the owner.
        DatabaseCluster cluster = Unique.Cluster();
        Tenant globex = Unique.Tenant(cluster);
        await SaveAsync(cluster, globex);

        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.tenant (id, key, display_name, state, cluster_id, database_name, residency_region, plan, created_at) " +
            "SELECT @id, @key, 'Attacker', 'Active', t.cluster_id, t.database_name, t.residency_region, 'standard', now() " +
            "FROM catalog.tenant t WHERE t.id = @globex",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value),
            ("globex", globex.Id.Value)));

        refused.SqlState.ShouldBe(UniqueViolation);
        refused.ConstraintName.ShouldBe("ux_tenant_cluster_id_database_name");
    }

    [Fact]
    public async Task A_tenant_cannot_be_routed_to_a_cluster_outside_its_region()
    {
        DatabaseCluster cluster = Unique.Cluster("nz");
        await SaveAsync(cluster);

        // The entity copies the cluster's region, so the only way to write this row is to bypass it.
        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.tenant (id, key, display_name, state, cluster_id, database_name, residency_region, plan, created_at) " +
            "VALUES (@id, @key, 'Misrouted', 'Provisioning', @cluster, 'aurora_t_misrouted', 'eu-west', 'standard', now())",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value),
            ("cluster", cluster.Id.Value)));

        refused.SqlState.ShouldBe(ForeignKeyViolation);
        refused.ConstraintName.ShouldBe("fk_tenant_cluster_in_region");
    }

    [Fact]
    public async Task A_tenant_that_is_not_deleted_must_have_its_routing_columns()
    {
        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.tenant (id, key, state, created_at) VALUES (@id, @key, 'Active', now())",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value)));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_tenant_routing_present_unless_deleted");
    }

    [Fact]
    public async Task A_deleted_tenant_may_be_a_tombstone_of_id_key_and_dates_and_only_a_deleted_one_has_deleted_at()
    {
        // The tombstone shape of ADR-0007 11.4 is writable...
        await ExecuteAsOwnerAsync(
            "INSERT INTO catalog.tenant (id, key, state, created_at, deleted_at) VALUES (@id, @key, 'Deleted', now(), now())",
            ("id", Guid.CreateVersion7()),
            ("key", Unique.TenantKey().Value));

        // ...and deleted_at on a live tenant, or a Deleted tenant without it, is not.
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "UPDATE catalog.tenant SET deleted_at = now() WHERE id = @id",
            ("id", tenant.Id.Value)));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_tenant_deleted_at_matches_state");
    }

    [Fact]
    public async Task A_state_outside_the_lifecycle_is_refused()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsAppAsync(
            "UPDATE catalog.tenant SET state = 'Archived' WHERE id = @id",
            ("id", tenant.Id.Value)));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_tenant_state");
    }

    [Fact]
    public async Task A_key_the_TenantKey_type_would_refuse_cannot_be_stored_either()
    {
        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.tenant (id, key, state, created_at, deleted_at) VALUES (@id, 'Acme_Trading', 'Deleted', now(), now())",
            ("id", Guid.CreateVersion7())));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_tenant_key_well_formed");
    }

    [Fact]
    public async Task A_tenant_has_at_most_one_primary_host()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        PostgresException refused = await ShouldBeRefusedAsync(() => _catalog.SeedAsync(owner =>
        {
            owner.TenantHosts.Add(TenantHost.Register(Unique.Host(), tenant.Id, isPrimary: true, verifiedAt: Unique.Now));
            owner.TenantHosts.Add(TenantHost.Register(Unique.Host(), tenant.Id, isPrimary: true, verifiedAt: Unique.Now));
        }));
        refused.SqlState.ShouldBe(UniqueViolation);
        refused.ConstraintName.ShouldBe("ux_tenant_host_primary");
    }

    [Fact]
    public async Task A_host_in_any_spelling_but_lower_case_is_refused()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.tenant_host (host, tenant_id, is_primary) VALUES ('Acme.Aurora.Test', @tenant, false)",
            ("tenant", tenant.Id.Value)));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_tenant_host_lower_case");
    }

    [Fact]
    public async Task A_tenant_cannot_hold_two_subscriptions_on_the_same_day()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);
        DateOnly from = new(2026, 10, 1);

        await _catalog.SeedAsync(owner => owner.Subscriptions.Add(Subscription.Start(SubscriptionId.Create(), tenant.Id, "standard", 10, from, from.AddMonths(6))));

        PostgresException refused = await ShouldBeRefusedAsync(() => _catalog.SeedAsync(owner =>
            owner.Subscriptions.Add(Subscription.Start(SubscriptionId.Create(), tenant.Id, "premium", 10, from.AddMonths(3), null))));
        refused.SqlState.ShouldBe(ExclusionViolation);
        refused.ConstraintName.ShouldBe("ex_subscription_no_overlap");
    }

    [Fact]
    public async Task Consecutive_subscriptions_tile_because_validity_is_half_open()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);
        DateOnly from = new(2026, 10, 1);

        await _catalog.SeedAsync(owner =>
        {
            owner.Subscriptions.Add(Subscription.Start(SubscriptionId.Create(), tenant.Id, "standard", 10, from, from.AddMonths(6)));
            owner.Subscriptions.Add(Subscription.Start(SubscriptionId.Create(), tenant.Id, "premium", 10, from.AddMonths(6), null));
        });
    }

    [Fact]
    public async Task A_port_that_is_not_a_TCP_port_is_refused()
    {
        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) " +
            "VALUES (@id, 'nz', 'pg.internal', 70000, 'postgres', 'ref:a', 'ref:m', 'ref:p', 10, 'Accepting')",
            ("id", Unique.ClusterId().Value)));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_database_cluster_port_is_tcp_port");
    }

    [Fact]
    public async Task A_secret_reference_shaped_like_a_credential_is_refused_by_the_database_too()
    {
        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) " +
            "VALUES (@id, 'nz', 'pg.internal', 5432, 'postgres', 'Password=hunter2', 'ref:m', 'ref:p', 10, 'Accepting')",
            ("id", Unique.ClusterId().Value)));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_database_cluster_secret_refs_are_references");
    }

    [Fact]
    public async Task A_cluster_with_tenants_on_it_cannot_be_deleted()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await SaveAsync(cluster, tenant);

        // As the owner: the request path holds no DELETE at all, and 42501 would prove the grant,
        // not the foreign key.
        PostgresException refused = await ShouldBeRefusedAsync(() => ExecuteAsOwnerAsync(
            "DELETE FROM catalog.database_cluster WHERE id = @id",
            ("id", cluster.Id.Value)));

        refused.SqlState.ShouldBe(ForeignKeyViolation);
        refused.ConstraintName.ShouldBe("fk_tenant_cluster_in_region");
    }

    /// <summary>The cluster and the tenant, if any, as the owner: the request path reads both and creates neither.</summary>
    private Task SaveAsync(DatabaseCluster cluster, Tenant? tenant = null) =>
        _catalog.SeedAsync(owner =>
        {
            owner.DatabaseClusters.Add(cluster);

            if (tenant is not null)
            {
                owner.Tenants.Add(tenant);
            }
        });

    private Task ExecuteAsAppAsync(string sql, params (string Name, object Value)[] parameters) =>
        ExecuteAsync(_catalog.OpenAppConnectionAsync, sql, parameters);

    private Task ExecuteAsOwnerAsync(string sql, params (string Name, object Value)[] parameters) =>
        ExecuteAsync(_catalog.OpenMigratorConnectionAsync, sql, parameters);

    private static async Task ExecuteAsync(Func<Task<NpgsqlConnection>> open, string sql, (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = await open();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PostgresException> ShouldBeRefusedAsync(Func<Task> write)
    {
        Exception thrown = await Should.ThrowAsync<Exception>(write);

        return thrown switch
        {
            PostgresException postgres => postgres,
            DbUpdateException { InnerException: PostgresException postgres } => postgres,
            _ => throw new ShouldAssertException($"expected PostgreSQL to refuse the write, got {thrown.GetType().Name}: {thrown.Message}"),
        };
    }
}
