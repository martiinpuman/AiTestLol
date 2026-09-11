using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Tests;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// Least privilege on the catalog (ADR-0004 rule 2, ADR-0007 §4.4): what the runtime role can
/// and cannot do, asserted by trying, as the role, wherever trying is possible.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what the Definition of Done's "tenant isolation test" means for a database that is
/// shared by design.</b> The catalog is the one store every tenant's request touches, so there is
/// no tenant A row for a tenant B request to be kept out of; inventing a test that partitioned it
/// would prove nothing. What is real isolation here is the blast radius of the role the request path
/// holds, and there are two halves of it:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Downwards:</b> <c>aurora_app</c> has no DDL and cannot touch the migrations history. But
/// the routing decision lives in rows, not in the schema — which tenant a host resolves to, which
/// cluster and database a tenant resolves to, which host a cluster is — so what matters is which
/// rows it can write. It holds on each catalog table exactly what
/// <c>CatalogSchemaAllowlist.AppRolePrivileges</c> records, read back from the ACL entry by entry,
/// and that record lets it insert a tenant and a host, move a tenant's lifecycle columns and write
/// <c>installed_package</c>: never write a cluster, never update a column a tenant or a host
/// resolves by, never delete a row. The takeover the security re-review ran with the grants B-05
/// first shipped is tried here, statement by statement, as the role.
/// </description></item>
/// <item><description>
/// <b>Sideways:</b> on this fixture's cluster <c>aurora_app</c> can open the catalog and no other
/// database that accepts connections. That is a property of the <em>databases</em>, not of the
/// role. ADR-0007 §4.4's "<c>CONNECT</c> on exactly one database" holds <em>under §3.5 stage 2</em>,
/// which is not what ships: stage 1 is one <c>aurora_app</c> login role per cluster, granted
/// <c>CONNECT</c> on every tenant database, and PostgreSQL grants <c>CONNECT</c> on every new
/// database to <c>PUBLIC</c>. What keeps the role out of a database today is §8 step 3 run on that
/// database (<c>REVOKE ALL … FROM PUBLIC</c>, then an explicit <c>GRANT CONNECT</c>); what catches
/// a request that reaches the wrong tenant database — which the role, by design, can — is §4.3's
/// connected-database identity check, which B-06.2 builds and proves with a deliberate mis-route.
/// That check catches a mis-routed request; it does not catch an attacker holding the shared
/// stage-1 credential, who never runs it — the accepted risk of ADR-0007 §3.5 that
/// <c>FOLLOWUP-001</c> owns (see the module README).
/// </description></item>
/// </list>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogPrivilegeTests
{
    private const string InsufficientPrivilege = "42501";

    /// <summary>
    /// Every ACL entry on every relation in <c>catalog</c> that reaches the app role: the
    /// table-level entries of <c>pg_class.relacl</c> and the column-level entries of
    /// <c>pg_attribute.attacl</c>, exploded one privilege per row, kept where the grantee is the
    /// role itself, <c>PUBLIC</c>, or a role it inherits from. Reading the ACL rather than asking
    /// <c>has_table_privilege</c> about a list of names is what closes the comparison: a column
    /// grant, a privilege a later PostgreSQL release adds and a grant that arrives through another
    /// role are all entries, and every entry the record does not name is a difference. The
    /// relation kinds are every kind a table privilege applies to, and the LEFT JOIN keeps a
    /// relation with no entry at all in the result, so a table the role cannot touch is still a
    /// row the record has to account for.
    /// </summary>
    private const string AclEntriesSql =
        "WITH relation AS (" +
        "  SELECT c.oid, c.relname, c.relacl FROM pg_class c" +
        "  JOIN pg_namespace n ON n.oid = c.relnamespace" +
        "  WHERE n.nspname = 'catalog' AND c.relkind IN ('r', 'p', 'v', 'm', 'f'))," +
        " entry AS (" +
        "  SELECT r.relname, acl.privilege_type, NULL::text AS column_name, acl.grantee, acl.is_grantable" +
        "  FROM relation r CROSS JOIN LATERAL aclexplode(r.relacl) AS acl" +
        "  UNION ALL" +
        "  SELECT r.relname, acl.privilege_type, a.attname::text, acl.grantee, acl.is_grantable" +
        "  FROM relation r" +
        "  JOIN pg_attribute a ON a.attrelid = r.oid AND a.attnum > 0 AND NOT a.attisdropped" +
        "  CROSS JOIN LATERAL aclexplode(a.attacl) AS acl)" +
        " SELECT r.relname, e.privilege_type, e.column_name, e.is_grantable," +
        "        CASE WHEN e.grantee = 0 THEN 'PUBLIC' ELSE pg_get_userbyid(e.grantee) END" +
        " FROM relation r" +
        " LEFT JOIN entry e ON e.relname = r.relname" +
        "   AND (e.grantee = 0 OR pg_has_role(@role, e.grantee, 'USAGE'))";

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogPrivilegeTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task The_app_role_holds_exactly_the_table_privileges_the_allowlist_records_and_nothing_on_any_other_catalog_table()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        HeldPrivileges held = await PrivilegesHeldAsync(connection, null);

        List<string> differences = Differences(held.ByRelation, CatalogSchemaAllowlist.AppRolePrivileges);

        // Say what was inspected, not only that it matched, so a pass is not indistinguishable
        // from a query that returned no rows (CLAUDE.md self-check 2). It cannot pass on nothing:
        // a query that returned no entries would report every recorded privilege as not held.
        _output.WriteLine(
            $"Read {held.AclEntries} ACL entries reaching {CatalogDatabaseFixture.AppRole} across {held.ByRelation.Count} catalog relations "
            + $"(pg_class.relacl and pg_attribute.attacl); it holds {held.ByRelation.Sum(relation => relation.Value.Count)} distinct privileges.");

        differences.ShouldBeEmpty(string.Join(Environment.NewLine, differences));
        held.ByRelation.Count.ShouldBe(CatalogSchemaAllowlist.AppRolePrivileges.Count);
    }

    [Fact]
    public async Task The_privilege_test_fails_the_moment_a_table_arrives_without_a_decision_or_a_grant_drifts_either_way()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        // Every shape a privilege regression takes, inside a transaction that is rolled back: a §9.2
        // table created without recording what the app role may do to it; a privilege the allowlist
        // does not name; a recorded privilege that is no longer granted; a column-level grant, which
        // has_table_privilege cannot see at all; a privilege this code had never heard of (MAINTAIN
        // arrived with PostgreSQL 17, the pinned version); a grant to PUBLIC, which reaches the role
        // without naming it; and a grant the role could pass on. The column grant, MAINTAIN and
        // PUBLIC are the security re-review's H-1: each one left the seven-privilege enumeration
        // this oracle replaced reporting a perfect match.
        string arrival = Unique.Identifier("drift");
        foreach (string sql in new[]
                 {
                     $"CREATE TABLE catalog.{arrival} (id uuid PRIMARY KEY)",
                     $"GRANT TRUNCATE ON catalog.tenant TO {CatalogDatabaseFixture.AppRole}",
                     $"REVOKE SELECT ON catalog.tenant_host FROM {CatalogDatabaseFixture.AppRole}",
                     $"GRANT UPDATE (display_name) ON catalog.tenant TO {CatalogDatabaseFixture.AppRole}",
                     $"GRANT MAINTAIN ON catalog.subscription TO {CatalogDatabaseFixture.AppRole}",
                     "GRANT SELECT ON catalog.installed_package TO PUBLIC",
                     $"GRANT TRIGGER ON catalog.installed_package TO {CatalogDatabaseFixture.AppRole} WITH GRANT OPTION",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        List<string> differences = Differences(
            (await PrivilegesHeldAsync(connection, transaction)).ByRelation, CatalogSchemaAllowlist.AppRolePrivileges);

        await transaction.RollbackAsync();

        differences.Count.ShouldBe(7, string.Join(Environment.NewLine, differences));
        differences.ShouldContain(d => d.Contains($"catalog.{arrival} exists but", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds TRUNCATE on catalog.tenant, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("records SELECT on catalog.tenant_host, which aurora_app does not hold", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds UPDATE(display_name) on catalog.tenant, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds MAINTAIN on catalog.subscription, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds SELECT through PUBLIC on catalog.installed_package, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds TRIGGER WITH GRANT OPTION on catalog.installed_package, which the allowlist does not record", StringComparison.Ordinal));

        // And once rolled back, the grants match again - the failure above was the drift, not the test.
        Differences((await PrivilegesHeldAsync(connection, null)).ByRelation, CatalogSchemaAllowlist.AppRolePrivileges).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_privilege_that_reaches_the_app_role_through_another_role_is_reported_with_the_role_it_came_through()
    {
        // GRANT REFERENCES ON catalog.tenant TO <role>; GRANT <role> TO aurora_app. The ACL names the
        // other role, and aurora_app holds the privilege all the same, by inherited membership. The
        // oracle has to follow membership, or this is the one grant that hides from it. Creating a
        // role is cluster-level DDL none of the three roles may issue, so this fault alone is
        // injected as the superuser, inside a transaction that is rolled back.
        string role = Unique.Identifier("drift_role");
        await using NpgsqlConnection connection = await _catalog.OpenSuperuserConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        foreach (string sql in new[]
                 {
                     $"CREATE ROLE {role}",
                     $"GRANT {role} TO {CatalogDatabaseFixture.AppRole}",
                     $"GRANT REFERENCES ON catalog.tenant TO {role}",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        List<string> differences = Differences(
            (await PrivilegesHeldAsync(connection, transaction)).ByRelation, CatalogSchemaAllowlist.AppRolePrivileges);

        await transaction.RollbackAsync();

        differences.Count.ShouldBe(1, string.Join(Environment.NewLine, differences));
        differences[0].ShouldContain($"holds REFERENCES through {role} on catalog.tenant, which the allowlist does not record");

        // Rolled back, neither the role nor its grant is left behind.
        Differences((await PrivilegesHeldAsync(connection, null)).ByRelation, CatalogSchemaAllowlist.AppRolePrivileges).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_table_created_without_a_grant_of_its_own_is_closed_to_the_app_role()
    {
        // The catalog sets no default privileges, on purpose: what a future table grants the app
        // role is decided in the migration that creates it, and forgetting is this 42501 at first
        // use rather than a silent inheritance of SELECT, INSERT, UPDATE, DELETE - which, on an
        // append-only table, would be exactly what ADR-0004 rule 5 forbids. The table is committed
        // rather than rolled back because the point is to try, as aurora_app, from its own
        // connection, which cannot see an uncommitted table. The collection runs one test at a
        // time, so no other test sees it either, and it is dropped whatever happens.
        string table = Unique.Identifier("probe");
        await using NpgsqlConnection migrator = await _catalog.OpenMigratorConnectionAsync();
        await CatalogDatabaseFixture.ExecuteAsync(migrator, $"CREATE TABLE catalog.{table} (id uuid PRIMARY KEY)");

        try
        {
            await using NpgsqlConnection app = await _catalog.OpenAppConnectionAsync();

            foreach (string sql in new[]
                     {
                         $"SELECT count(*) FROM catalog.{table}",
                         $"INSERT INTO catalog.{table} (id) VALUES (gen_random_uuid())",
                         $"UPDATE catalog.{table} SET id = id",
                         $"DELETE FROM catalog.{table}",
                     })
            {
                await using var command = new NpgsqlCommand(sql, app);

                PostgresException refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), sql);

                refused.SqlState.ShouldBe(InsufficientPrivilege, sql);
            }
        }
        finally
        {
            await CatalogDatabaseFixture.ExecuteAsync(migrator, $"DROP TABLE catalog.{table}");
        }
    }

    [Theory]
    [InlineData("CREATE TABLE catalog.smuggled (id uuid PRIMARY KEY)", "creating a table")]
    [InlineData("ALTER TABLE catalog.tenant ADD COLUMN smuggled text", "altering a registry table")]
    [InlineData("DROP TABLE catalog.tenant_host", "dropping a registry table")]
    [InlineData("CREATE SCHEMA smuggled", "creating a schema")]
    [InlineData("CREATE DATABASE smuggled", "creating a database")]
    [InlineData("CREATE EXTENSION IF NOT EXISTS pg_stat_statements", "installing an extension")]
    public async Task The_app_role_cannot_change_the_schema(string ddl, string what)
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();
        await using var command = new NpgsqlCommand(ddl, connection);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), what);

        refused.SqlState.ShouldBe(InsufficientPrivilege, what);
    }

    [Theory]
    [InlineData("SELECT count(*) FROM catalog.\"__EFMigrationsHistory\"")]
    [InlineData("DELETE FROM catalog.\"__EFMigrationsHistory\"")]
    public async Task The_app_role_cannot_touch_the_migrations_history(string sql)
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        refused.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Fact]
    public async Task The_app_role_cannot_repoint_where_a_tenant_or_a_host_resolves()
    {
        // The takeovers two security re-reviews ran against this migration - as aurora_app, no DDL,
        // no superuser. With the grants B-05 first shipped: unbind another tenant's hostname and
        // bind it to your own; send one tenant's requests at another tenant's database; repoint a
        // cluster at a host you control, so the resolver dials it carrying the real cluster
        // credentials. With the grants the first rework kept, using INSERT alone: a tenant of the
        // request's own whose routing columns are copied from another tenant's row, so a host of
        // the request's choosing resolves to that tenant's database; and a host row for a tenant
        // the request does not own - self-verified, or claiming the primary of a tenant that has
        // none yet - which section 4.3's identity check can never see, because the tenant is
        // genuine. Each statement is tried on rows this test owns, must be refused as 42501 - not
        // a constraint violation, which would mean the privilege was there and only the data got
        // in the way - and the rows must read back untouched.
        DatabaseCluster cluster = Unique.Cluster();
        Tenant acme = Unique.Tenant(cluster);
        Tenant globex = Unique.Tenant(cluster);
        string globexHost = Unique.Host();
        TenantKey attackerKey = Unique.TenantKey();
        await _catalog.SeedAsync(catalog =>
        {
            catalog.DatabaseClusters.Add(cluster);
            catalog.Tenants.Add(acme);
            catalog.Tenants.Add(globex);
            catalog.TenantHosts.Add(TenantHost.Register(globexHost, globex.Id, isPrimary: true, verifiedAt: Unique.Now));
        });

        (string What, string Sql, (string Name, object Value)[] Parameters)[] attempts =
        [
            ("unbind another tenant's host",
                "DELETE FROM catalog.tenant_host WHERE host = @host", [("host", globexHost)]),
            ("rebind another tenant's host to this tenant",
                "UPDATE catalog.tenant_host SET tenant_id = @acme WHERE host = @host", [("acme", acme.Id.Value), ("host", globexHost)]),
            ("send this tenant's requests at another tenant's database",
                "UPDATE catalog.tenant SET database_name = @database WHERE id = @acme", [("database", globex.DatabaseName!), ("acme", acme.Id.Value)]),
            ("move this tenant to a cluster of the request's choosing",
                "UPDATE catalog.tenant SET cluster_id = 'elsewhere', residency_region = 'elsewhere' WHERE id = @acme", [("acme", acme.Id.Value)]),
            ("rename this tenant's key",
                "UPDATE catalog.tenant SET key = 'somebody-else' WHERE id = @acme", [("acme", acme.Id.Value)]),
            ("repoint the cluster at an attacker's host",
                "UPDATE catalog.database_cluster SET host = 'attacker.example.net' WHERE id = @cluster", [("cluster", cluster.Id.Value)]),
            ("register a cluster of the request's own",
                "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) "
                + "VALUES (@id, 'nz', 'attacker.example.net', 5432, 'postgres', 'ref:a', 'ref:m', 'ref:p', 10, 'Accepting')", [("id", Unique.ClusterId().Value)]),
            ("remove the cluster",
                "DELETE FROM catalog.database_cluster WHERE id = @cluster", [("cluster", cluster.Id.Value)]),
            ("create a tenant of the request's own that resolves to another tenant's database",
                "INSERT INTO catalog.tenant (id, key, display_name, state, cluster_id, database_name, residency_region, core_schema_version, plan, created_at) "
                + "SELECT @id, @key, 'Attacker', 'Active', t.cluster_id, t.database_name, t.residency_region, t.core_schema_version, 'standard', now() "
                + "FROM catalog.tenant t WHERE t.id = @globex", [("id", TenantId.Create().Value), ("key", attackerKey.Value), ("globex", globex.Id.Value)]),
            ("register a self-verified host of the request's choosing for another tenant",
                "INSERT INTO catalog.tenant_host (host, tenant_id, is_primary, verified_at) VALUES (@host, @globex, false, now())", [("host", Unique.Host()), ("globex", globex.Id.Value)]),
            ("claim the primary host of a tenant that has none yet",
                "INSERT INTO catalog.tenant_host (host, tenant_id, is_primary, verified_at) VALUES (@host, @acme, true, now())", [("host", Unique.Host()), ("acme", acme.Id.Value)]),
        ];

        foreach ((string what, string sql, (string Name, object Value)[] parameters) in attempts)
        {
            await RefusedAsAppAsync(what, sql, parameters);
        }

        _output.WriteLine($"Tried {attempts.Length} routing writes as {CatalogDatabaseFixture.AppRole}; every one was refused with {InsufficientPrivilege}.");

        await using CatalogDbContext reader = _catalog.OpenAsApp();
        (await reader.TenantHosts.SingleAsync(h => h.Host == globexHost)).TenantId.ShouldBe(globex.Id);
        (await reader.TenantHosts.CountAsync(h => h.TenantId == globex.Id)).ShouldBe(1);
        (await reader.TenantHosts.AnyAsync(h => h.TenantId == acme.Id)).ShouldBeFalse();
        (await reader.Tenants.AnyAsync(t => t.Key == attackerKey)).ShouldBeFalse();
        Tenant acmeReadBack = await reader.Tenants.SingleAsync(t => t.Id == acme.Id);
        acmeReadBack.DatabaseName.ShouldBe(acme.DatabaseName);
        acmeReadBack.ClusterId.ShouldBe(cluster.Id);
        acmeReadBack.Key.ShouldBe(acme.Key);
        (await reader.DatabaseClusters.SingleAsync(c => c.Id == cluster.Id)).Host.ShouldBe(cluster.Host);
    }

    [Fact]
    public async Task The_app_role_cannot_delete_from_any_catalog_table()
    {
        // Every ordinary table in the schema, enumerated from pg_class rather than named, so a table
        // added later is tried too. Nothing on the request path deletes a catalog row: ADR-0007
        // §11.4 tombstones a tenant, a subscription closes with valid_to, DROP DATABASE is
        // aurora_admin's. WHERE false, because the privilege is checked before a row is looked at,
        // and a DELETE that turned out to be granted must not take the shared test database with it.
        await using NpgsqlConnection migrator = await _catalog.OpenMigratorConnectionAsync();
        List<string> tables = await OrdinaryTablesAsync(migrator);

        // The list has to be the whole catalog, or the loop below proved less than its name says.
        tables.Count.ShouldBe(CatalogSchemaAllowlist.AppRolePrivileges.Count);
        _output.WriteLine($"Tried DELETE on {tables.Count} catalog tables as {CatalogDatabaseFixture.AppRole}: {string.Join(", ", tables)}.");

        foreach (string table in tables)
        {
            await RefusedAsAppAsync($"deleting from catalog.{table}", $"DELETE FROM catalog.\"{table}\" WHERE false", []);
        }
    }

    [Fact]
    public async Task The_app_role_cannot_connect_to_any_other_database_on_the_cluster()
    {
        // Every database on the cluster that accepts connections, tried one by one. Under ADR-0007
        // §3.5 stage 1 nothing about the role keeps it out of a database - PostgreSQL grants
        // CONNECT to PUBLIC on every new one - so what this proves is that §8 step 3's REVOKE has
        // been run on each of them, template1 and the maintenance database included: what a
        // hardened cluster looks like, and therefore what the fixture has to do.
        await using NpgsqlConnection catalog = await _catalog.OpenAppConnectionAsync();
        List<string> others = await CatalogDatabaseFixture.DatabasesAcceptingConnectionsAsync(catalog);
        others.Remove(CatalogDatabaseFixture.CatalogDatabaseName);

        // The list has to hold the databases every cluster has, or the loop below proved nothing.
        others.ShouldContain("template1");
        others.ShouldContain(CatalogDatabaseFixture.MaintenanceDatabaseName);
        _output.WriteLine($"Tried to open {others.Count} databases as {CatalogDatabaseFixture.AppRole}: {string.Join(", ", others)}.");

        foreach (string database in others)
        {
            var elsewhere = new NpgsqlConnectionStringBuilder(_catalog.AppConnectionString) { Database = database }.ConnectionString;
            await using var connection = new NpgsqlConnection(elsewhere);

            PostgresException refused = await Should.ThrowAsync<PostgresException>(() => connection.OpenAsync(), database);

            refused.SqlState.ShouldBe(InsufficientPrivilege, database);
        }
    }

    [Fact]
    public async Task A_newly_created_database_is_open_to_the_app_role_until_section_8_step_3_hardens_it()
    {
        // What the test above rests on, shown on a database created the way the provisioner will
        // create a tenant's (ADR-0007 §8 step 2): until §8 step 3 runs on it, aurora_app - one
        // login role per cluster under §3.5 stage 1 - opens it, because PUBLIC holds CONNECT on
        // every new database. B-07 then grants CONNECT back to aurora_app, on purpose, on every
        // tenant database; the control that keeps a request inside its own tenant from then on is
        // §4.3's identity check, not this role. Pooling is off so the probe can be dropped.
        string database = Unique.Identifier("aurora_t_probe");
        await using NpgsqlConnection admin = await _catalog.OpenAdminMaintenanceConnectionAsync();
        await CatalogDatabaseFixture.ExecuteAsync(
            admin, $"CREATE DATABASE {database} OWNER {CatalogDatabaseFixture.MigratorRole} TEMPLATE template0 ENCODING 'UTF8'");

        try
        {
            var asApp = new NpgsqlConnectionStringBuilder(_catalog.AppConnectionString) { Database = database, Pooling = false }.ConnectionString;

            await using (var open = new NpgsqlConnection(asApp))
            {
                await open.OpenAsync();
                await using var reached = new NpgsqlCommand("SELECT current_database()", open);
                ((string?)await reached.ExecuteScalarAsync()).ShouldBe(database, "before hardening");
            }

            await CatalogDatabaseFixture.ExecuteAsync(admin, $"REVOKE ALL ON DATABASE {database} FROM PUBLIC");

            await using var closed = new NpgsqlConnection(asApp);
            PostgresException refused = await Should.ThrowAsync<PostgresException>(() => closed.OpenAsync(), "after hardening");
            refused.SqlState.ShouldBe(InsufficientPrivilege, "after hardening");
        }
        finally
        {
            await CatalogDatabaseFixture.ExecuteAsync(admin, $"DROP DATABASE {database} WITH (FORCE)");
        }
    }

    [Fact]
    public async Task The_migrator_role_owns_the_schema_and_may_alter_it()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await using var owner = new NpgsqlCommand(
            "SELECT nspowner::regrole::text FROM pg_namespace WHERE nspname = 'catalog'", connection, transaction);
        ((string)(await owner.ExecuteScalarAsync())!).ShouldBe(CatalogDatabaseFixture.MigratorRole);

        await using var alter = new NpgsqlCommand("ALTER TABLE catalog.tenant ADD COLUMN probe text", connection, transaction);
        await alter.ExecuteNonQueryAsync();

        await transaction.RollbackAsync();
    }

    private async Task RefusedAsAppAsync(string what, string sql, (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), what);

        refused.SqlState.ShouldBe(InsufficientPrivilege, what);
    }

    private static async Task<List<string>> OrdinaryTablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "WHERE n.nspname = 'catalog' AND c.relkind = 'r' ORDER BY c.relname",
            connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<string> tables = [];
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    /// <summary>
    /// What the ACL says the app role holds, relation by relation, and how many entries said so.
    /// A relation with no entry is present with an empty set: it exists, and the record has to
    /// say what was decided for it.
    /// </summary>
    private sealed record HeldPrivileges(IReadOnlyDictionary<string, IReadOnlySet<string>> ByRelation, int AclEntries);

    private static async Task<HeldPrivileges> PrivilegesHeldAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(AclEntriesSql, connection, transaction);
        command.Parameters.AddWithValue("role", CatalogDatabaseFixture.AppRole);

        var held = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int entries = 0;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string relation = reader.GetString(0);
            if (!held.TryGetValue(relation, out HashSet<string>? privileges))
            {
                privileges = new HashSet<string>(StringComparer.Ordinal);
                held[relation] = privileges;
            }

            if (reader.IsDBNull(1))
            {
                continue;
            }

            entries++;
            privileges.Add(Label(
                privilege: reader.GetString(1),
                column: reader.IsDBNull(2) ? null : reader.GetString(2),
                grantable: reader.GetBoolean(3),
                grantee: reader.GetString(4)));
        }

        return new HeldPrivileges(
            held.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal),
            entries);
    }

    /// <summary>
    /// One ACL entry, spelled the way <c>CatalogSchemaAllowlist.AppRolePrivileges</c> spells a
    /// decision: <c>SELECT</c>; <c>UPDATE(column)</c> for a column grant; then
    /// <c>WITH GRANT OPTION</c> if the role could pass it on, and <c>through PUBLIC</c> or
    /// <c>through &lt;role&gt;</c> if it reaches the app role without naming it. The allowlist
    /// records only the first two forms, so an entry in either of the others is always a
    /// difference — a privilege the request path should hold is granted to it, directly.
    /// </summary>
    private static string Label(string privilege, string? column, bool grantable, string grantee)
    {
        var label = new StringBuilder(privilege);

        if (column is not null)
        {
            label.Append('(').Append(column).Append(')');
        }

        if (grantable)
        {
            label.Append(" WITH GRANT OPTION");
        }

        if (!string.Equals(grantee, CatalogDatabaseFixture.AppRole, StringComparison.Ordinal))
        {
            label.Append(" through ").Append(grantee);
        }

        return label.ToString();
    }

    /// <summary>
    /// Every way what the app role holds can differ from what the allowlist records, each as a
    /// sentence a reviewer can act on. Empty means the two agree, table by table, both ways.
    /// </summary>
    private static List<string> Differences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> held,
        IReadOnlyDictionary<string, IReadOnlyList<AppRoleGrant>> recorded)
    {
        List<string> differences = [];

        foreach ((string table, IReadOnlySet<string> privileges) in held.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!recorded.TryGetValue(table, out IReadOnlyList<AppRoleGrant>? grants))
            {
                differences.Add(
                    $"catalog.{table} exists but CatalogSchemaAllowlist.AppRolePrivileges records no decision for it; "
                    + $"{CatalogDatabaseFixture.AppRole} holds [{string.Join(", ", privileges.Order(StringComparer.Ordinal))}]");
                continue;
            }

            HashSet<string> decided = [.. grants.Select(grant => grant.Privilege)];

            foreach (string privilege in privileges.Except(decided).Order(StringComparer.Ordinal))
            {
                differences.Add($"{CatalogDatabaseFixture.AppRole} holds {privilege} on catalog.{table}, which the allowlist does not record");
            }

            foreach (string privilege in decided.Except(privileges).Order(StringComparer.Ordinal))
            {
                differences.Add($"the allowlist records {privilege} on catalog.{table}, which {CatalogDatabaseFixture.AppRole} does not hold");
            }
        }

        foreach (string table in recorded.Keys.Except(held.Keys).Order(StringComparer.Ordinal))
        {
            differences.Add($"the allowlist records catalog.{table}, which does not exist in the migrated schema");
        }

        return differences;
    }
}
