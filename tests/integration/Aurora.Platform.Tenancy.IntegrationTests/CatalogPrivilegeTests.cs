using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Tests;
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
/// <b>Downwards:</b> <c>aurora_app</c> has no DDL, holds on each catalog table exactly what
/// <c>CatalogSchemaAllowlist.AppRolePrivileges</c> records and nothing on any other table, so it
/// cannot alter the schema that decides where every tenant's data lives, and cannot touch the
/// migrations history that records it.
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
/// connected-database identity check, which B-07 must test as <em>the</em> cross-tenant control.
/// </description></item>
/// </list>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogPrivilegeTests
{
    private const string InsufficientPrivilege = "42501";

    /// <summary>
    /// Every relation in <c>catalog</c> crossed with every table privilege, and whether the app
    /// role holds it. <c>has_table_privilege</c> reports the <em>effective</em> privilege — one
    /// that arrives through <c>PUBLIC</c> or role membership counts, which a read of
    /// <c>information_schema.role_table_grants</c> would miss — and the relation kinds are every
    /// kind a table privilege applies to, not only ordinary tables.
    /// </summary>
    private const string PrivilegesHeldSql =
        "SELECT c.relname, p.privilege, has_table_privilege(@role, c.oid, p.privilege) " +
        "FROM pg_class c " +
        "JOIN pg_namespace n ON n.oid = c.relnamespace " +
        "CROSS JOIN unnest(@privileges) AS p(privilege) " +
        "WHERE n.nspname = 'catalog' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')";

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
        IReadOnlyDictionary<string, IReadOnlySet<string>> held = await PrivilegesHeldAsync(connection, null);

        List<string> differences = Differences(held, CatalogSchemaAllowlist.AppRolePrivileges);

        // Say what was inspected, not only that it matched, so a pass is not indistinguishable
        // from a query that returned no rows (CLAUDE.md self-check 2).
        _output.WriteLine(
            $"Asked PostgreSQL about {held.Count} catalog relations x {CatalogSchemaAllowlist.PostgresTablePrivileges.Count} table privileges; "
            + $"{CatalogDatabaseFixture.AppRole} holds {held.Sum(table => table.Value.Count)}.");

        differences.ShouldBeEmpty(string.Join(Environment.NewLine, differences));
        held.Count.ShouldBe(CatalogSchemaAllowlist.AppRolePrivileges.Count);
    }

    [Fact]
    public async Task The_privilege_test_fails_the_moment_a_table_arrives_without_a_decision_or_a_grant_drifts_either_way()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        // The three shapes a privilege regression takes, inside a transaction that is rolled back:
        // a §9.2 table created without recording what the app role may do to it, a privilege the
        // allowlist does not name, and a recorded privilege that is no longer granted.
        foreach (string sql in new[]
                 {
                     "CREATE TABLE catalog.operator_audit_event (id uuid PRIMARY KEY)",
                     $"GRANT TRUNCATE ON catalog.tenant TO {CatalogDatabaseFixture.AppRole}",
                     $"REVOKE DELETE ON catalog.tenant_host FROM {CatalogDatabaseFixture.AppRole}",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        List<string> differences = Differences(
            await PrivilegesHeldAsync(connection, transaction), CatalogSchemaAllowlist.AppRolePrivileges);

        await transaction.RollbackAsync();

        differences.Count.ShouldBe(3, string.Join(Environment.NewLine, differences));
        differences.ShouldContain(d => d.Contains("catalog.operator_audit_event exists but", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds TRUNCATE on catalog.tenant, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("records DELETE on catalog.tenant_host, which aurora_app does not hold", StringComparison.Ordinal));

        // And once rolled back, the grants match again - the failure above was the drift, not the test.
        Differences(await PrivilegesHeldAsync(connection, null), CatalogSchemaAllowlist.AppRolePrivileges).ShouldBeEmpty();
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
    public async Task Only_section_8_step_3_hardening_keeps_the_app_role_out_of_a_newly_created_database()
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

    private static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> PrivilegesHeldAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(PrivilegesHeldSql, connection, transaction);
        command.Parameters.AddWithValue("role", CatalogDatabaseFixture.AppRole);
        command.Parameters.AddWithValue("privileges", CatalogSchemaAllowlist.PostgresTablePrivileges.ToArray());

        var held = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string table = reader.GetString(0);
            if (!held.TryGetValue(table, out HashSet<string>? privileges))
            {
                privileges = new HashSet<string>(StringComparer.Ordinal);
                held[table] = privileges;
            }

            if (reader.GetBoolean(2))
            {
                privileges.Add(reader.GetString(1));
            }
        }

        return held.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every way what the app role holds can differ from what the allowlist records, each as a
    /// sentence a reviewer can act on. Empty means the two agree, table by table, both ways.
    /// </summary>
    private static List<string> Differences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> held,
        IReadOnlyDictionary<string, IReadOnlySet<string>> recorded)
    {
        List<string> differences = [];

        foreach ((string table, IReadOnlySet<string> privileges) in held.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!recorded.TryGetValue(table, out IReadOnlySet<string>? decided))
            {
                differences.Add(
                    $"catalog.{table} exists but CatalogSchemaAllowlist.AppRolePrivileges records no decision for it; "
                    + $"{CatalogDatabaseFixture.AppRole} holds [{string.Join(", ", privileges.Order(StringComparer.Ordinal))}]");
                continue;
            }

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
