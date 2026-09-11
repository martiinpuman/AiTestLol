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
/// rows it can write. It holds on every object in <c>catalog</c> exactly what
/// <c>CatalogSchemaAllowlist</c> records, read back from the ACL entry by entry — tables, columns,
/// the schema, and the sequences, functions and types that do not exist yet but whose PostgreSQL
/// defaults are open — and that record lets it read the routing decision, move a tenant's
/// lifecycle columns and write <c>installed_package</c>: never create a tenant or a host, never
/// write a cluster, never update a column a tenant or a host resolves by, never delete a row, never
/// call a function no migration opened to it. The takeovers two security re-reviews ran — with the
/// grants B-05 first shipped, then with <c>INSERT</c> alone, then through a <c>SECURITY DEFINER</c>
/// function — are tried here, statement by statement, as the role.
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
    /// Every ACL entry on every object in schema <c>catalog</c> that reaches the app role — the
    /// schema itself (<c>pg_namespace.nspacl</c>), its tables and sequences
    /// (<c>pg_class.relacl</c>) and their columns (<c>pg_attribute.attacl</c>), its functions and
    /// procedures (<c>pg_proc.proacl</c>) and its types (<c>pg_type.typacl</c>) — exploded one
    /// privilege per row, kept where the grantee is the role itself, <c>PUBLIC</c>, or a role the
    /// app role is a member of by any route: inherited, reachable by <c>SET ROLE</c>, or neither
    /// (<c>pg_has_role … 'MEMBER'</c>; <c>'USAGE'</c> alone was blind to a membership granted
    /// <c>WITH INHERIT FALSE, SET TRUE</c>, the second security re-review's M-3). A <c>NULL</c> ACL
    /// is read as what it means — the owner default for that object class, <c>acldefault()</c> —
    /// rather than as no entries, because for a function and a type that default includes
    /// <c>PUBLIC</c>. Reading the ACL rather than asking <c>has_*_privilege</c> about a list of
    /// names is what closes the comparison: a column grant, a privilege a later PostgreSQL release
    /// adds and a grant that arrives through another role are all entries, and every entry the
    /// record does not name is a difference. The LEFT JOIN keeps an object with no entry at all in
    /// the result, so an object the role cannot touch is still a row the record has to account
    /// for. Not read here: <c>pg_default_acl</c>, which has no object until one is created —
    /// <c>The_catalog_sets_exactly_one_default_privilege…</c> reads it on its own.
    /// </summary>
    private const string AclEntriesSql =
        "WITH object AS (" +
        "  SELECT CASE WHEN c.relkind = 'S' THEN 'sequence' ELSE 'table' END AS kind, c.relname::text AS name, c.oid AS reloid," +
        "         COALESCE(c.relacl, acldefault((CASE WHEN c.relkind = 'S' THEN 's' ELSE 'r' END)::\"char\", c.relowner)) AS acl" +
        "  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace" +
        "  WHERE n.nspname = 'catalog' AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')" +
        "  UNION ALL" +
        "  SELECT CASE WHEN p.prokind = 'p' THEN 'procedure' ELSE 'function' END," +
        "         p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')', NULL::oid," +
        "         COALESCE(p.proacl, acldefault('f'::\"char\", p.proowner))" +
        "  FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'catalog'" +
        "  UNION ALL" +
        "  SELECT 'type', t.typname::text, NULL::oid, COALESCE(t.typacl, acldefault('T'::\"char\", t.typowner))" +
        "  FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace" +
        "  WHERE n.nspname = 'catalog' AND t.typcategory <> 'A'" +
        "    AND (t.typrelid = 0 OR EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind = 'c'))" +
        "  UNION ALL" +
        "  SELECT 'schema', n.nspname::text, NULL::oid, COALESCE(n.nspacl, acldefault('n'::\"char\", n.nspowner))" +
        "  FROM pg_namespace n WHERE n.nspname = 'catalog')," +
        " entry AS (" +
        "  SELECT o.kind, o.name, acl.privilege_type, NULL::text AS column_name, acl.grantee, acl.is_grantable" +
        "  FROM object o CROSS JOIN LATERAL aclexplode(o.acl) AS acl" +
        "  UNION ALL" +
        "  SELECT o.kind, o.name, acl.privilege_type, a.attname::text, acl.grantee, acl.is_grantable" +
        "  FROM object o" +
        "  JOIN pg_attribute a ON a.attrelid = o.reloid AND a.attnum > 0 AND NOT a.attisdropped" +
        "  CROSS JOIN LATERAL aclexplode(a.attacl) AS acl)" +
        " SELECT o.kind, o.name, e.privilege_type, e.column_name, e.is_grantable," +
        "        CASE WHEN e.grantee = 0 THEN 'PUBLIC' ELSE pg_get_userbyid(e.grantee) END," +
        "        CASE WHEN e.grantee = 0 THEN true ELSE pg_has_role(@role, e.grantee, 'USAGE') END," +
        "        CASE WHEN e.grantee = 0 THEN true ELSE pg_has_role(@role, e.grantee, 'SET') END" +
        " FROM object o" +
        " LEFT JOIN entry e ON e.kind = o.kind AND e.name = o.name" +
        "   AND (e.grantee = 0 OR pg_has_role(@role, e.grantee, 'MEMBER'))";

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogPrivilegeTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task The_app_role_holds_exactly_the_privileges_the_allowlist_records_and_nothing_on_any_other_catalog_object()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        HeldPrivileges held = await PrivilegesHeldAsync(connection, null);

        List<string> differences = Differences(held.ByObject, CatalogSchemaAllowlist.AppRoleDecisions);

        // Say what was inspected, not only that it matched, so a pass is not indistinguishable
        // from a query that returned no rows (CLAUDE.md self-check 2). It cannot pass on nothing:
        // a query that returned no entries would report every recorded privilege as not held, and
        // every kind is counted, zeros included, so a kind the query stopped reading shows as 0.
        _output.WriteLine(
            $"Read {held.AclEntries} ACL entries reaching {CatalogDatabaseFixture.AppRole} across {held.ByObject.Count} catalog objects "
            + $"({held.CountByKind()}) from pg_namespace.nspacl, pg_class.relacl, pg_attribute.attacl, pg_proc.proacl and pg_type.typacl, "
            + $"a NULL ACL read as the owner default; it holds {held.ByObject.Sum(o => o.Value.Count)} distinct privileges.");

        differences.ShouldBeEmpty(string.Join(Environment.NewLine, differences));
        held.ByObject.Count.ShouldBe(CatalogSchemaAllowlist.AppRoleDecisions.Count);
        held.ByObject.Keys.Count(o => o.Kind == CatalogObject.Table).ShouldBe(CatalogSchemaAllowlist.AppRolePrivileges.Count);
        held.ByObject.Keys.ShouldContain(new CatalogObject(CatalogObject.Schema, "catalog"));
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
        //
        // And the object classes that are not tables, where the second security re-review found
        // the oracle blind (H-5, L-1): a function - closed, because the migration's one default
        // privilege revokes PUBLIC's EXECUTE from every function aurora_migrator creates, yet still
        // an object the record must decide; a function opened to the role by name; a sequence
        // granted to the role; a type, whose PostgreSQL default is USAGE to PUBLIC and which no
        // default here closes; and CREATE on the schema itself.
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
                     $"CREATE FUNCTION catalog.{arrival}_fn() RETURNS int LANGUAGE sql AS 'SELECT 1'",
                     $"CREATE FUNCTION catalog.{arrival}_open() RETURNS int LANGUAGE sql AS 'SELECT 1'",
                     $"GRANT EXECUTE ON FUNCTION catalog.{arrival}_open() TO {CatalogDatabaseFixture.AppRole}",
                     $"CREATE SEQUENCE catalog.{arrival}_seq",
                     $"GRANT USAGE ON SEQUENCE catalog.{arrival}_seq TO {CatalogDatabaseFixture.AppRole}",
                     $"CREATE TYPE catalog.{arrival}_t AS ENUM ('a')",
                     $"GRANT CREATE ON SCHEMA catalog TO {CatalogDatabaseFixture.AppRole}",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        List<string> differences = Differences(
            (await PrivilegesHeldAsync(connection, transaction)).ByObject, CatalogSchemaAllowlist.AppRoleDecisions);

        await transaction.RollbackAsync();

        differences.Count.ShouldBe(12, string.Join(Environment.NewLine, differences));
        differences.ShouldContain(d => d.Contains($"catalog.{arrival} exists but", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds TRUNCATE on catalog.tenant, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("records SELECT on catalog.tenant_host, which aurora_app does not hold", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds UPDATE(display_name) on catalog.tenant, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds MAINTAIN on catalog.subscription, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds SELECT through PUBLIC on catalog.installed_package, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds TRIGGER WITH GRANT OPTION on catalog.installed_package, which the allowlist does not record", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains($"function catalog.{arrival}_fn() exists but", StringComparison.Ordinal) && d.EndsWith("holds []", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains($"function catalog.{arrival}_open() exists but", StringComparison.Ordinal) && d.EndsWith("holds [EXECUTE]", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains($"sequence catalog.{arrival}_seq exists but", StringComparison.Ordinal) && d.EndsWith("holds [USAGE]", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains($"type catalog.{arrival}_t exists but", StringComparison.Ordinal) && d.EndsWith("holds [USAGE through PUBLIC]", StringComparison.Ordinal));
        differences.ShouldContain(d => d.Contains("holds CREATE on schema catalog, which the allowlist does not record", StringComparison.Ordinal));

        // And once rolled back, the grants match again - the failure above was the drift, not the test.
        Differences((await PrivilegesHeldAsync(connection, null)).ByObject, CatalogSchemaAllowlist.AppRoleDecisions).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" WITH INHERIT FALSE, SET TRUE", " (SET ROLE)")]
    [InlineData(" WITH INHERIT FALSE, SET FALSE", " (member, neither INHERIT nor SET)")]
    public async Task A_privilege_that_reaches_the_app_role_through_another_role_is_reported_with_the_role_it_came_through(string membership, string route)
    {
        // GRANT REFERENCES ON catalog.tenant TO <role>; GRANT <role> TO aurora_app. The ACL names the
        // other role, and aurora_app holds the privilege all the same - by inheritance with the
        // default membership, or one SET ROLE later with INHERIT FALSE, SET TRUE, which is the
        // shape the second security re-review used to take a tenant over while an oracle asking
        // pg_has_role(..., 'USAGE') reported a perfect match (M-3). A membership that is neither
        // inherited nor assumable grants nothing today and is reported anyway, because it is one
        // ADMIN OPTION away from either. The oracle has to follow membership by every route, or
        // this is the grant that hides from it. Creating a role is cluster-level DDL none of the
        // three roles may issue, so this fault alone is injected as the superuser, inside a
        // transaction that is rolled back.
        string role = Unique.Identifier("drift_role");
        await using NpgsqlConnection connection = await _catalog.OpenSuperuserConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        foreach (string sql in new[]
                 {
                     $"CREATE ROLE {role}",
                     $"GRANT {role} TO {CatalogDatabaseFixture.AppRole}{membership}",
                     $"GRANT REFERENCES ON catalog.tenant TO {role}",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        List<string> differences = Differences(
            (await PrivilegesHeldAsync(connection, transaction)).ByObject, CatalogSchemaAllowlist.AppRoleDecisions);

        await transaction.RollbackAsync();

        differences.Count.ShouldBe(1, string.Join(Environment.NewLine, differences));
        differences[0].ShouldContain($"holds REFERENCES through {role}{route} on catalog.tenant, which the allowlist does not record");

        // Rolled back, neither the role nor its grant is left behind.
        Differences((await PrivilegesHeldAsync(connection, null)).ByObject, CatalogSchemaAllowlist.AppRoleDecisions).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_function_whose_ACL_is_null_reads_as_EXECUTE_through_PUBLIC_not_as_nothing()
    {
        // aclexplode(NULL) yields no rows, and a NULL ACL is not an empty one: it is the owner
        // default, which for a function includes EXECUTE to PUBLIC. The migration's default
        // privileges close the functions aurora_migrator creates; one created by anyone else -
        // here the superuser, inside a transaction that is rolled back - keeps a NULL proacl, and
        // the oracle has to materialise the default rather than read it as "holds nothing"
        // (the second security re-review, H-5 and L-2).
        string function = Unique.Identifier("drift_public");
        await using NpgsqlConnection connection = await _catalog.OpenSuperuserConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await using (var create = new NpgsqlCommand($"CREATE FUNCTION catalog.{function}() RETURNS int LANGUAGE sql AS 'SELECT 1'", connection, transaction))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using (var acl = new NpgsqlCommand($"SELECT proacl IS NULL FROM pg_proc WHERE proname = '{function}'", connection, transaction))
        {
            ((bool)(await acl.ExecuteScalarAsync())!).ShouldBeTrue("the superuser's function carries no ACL of its own");
        }

        List<string> differences = Differences(
            (await PrivilegesHeldAsync(connection, transaction)).ByObject, CatalogSchemaAllowlist.AppRoleDecisions);

        await transaction.RollbackAsync();

        differences.Count.ShouldBe(1, string.Join(Environment.NewLine, differences));
        differences[0].ShouldContain($"function catalog.{function}() exists but");
        differences[0].ShouldEndWith("holds [EXECUTE through PUBLIC]");
    }

    [Fact]
    public async Task A_function_in_the_catalog_is_closed_to_the_app_role_and_is_a_decision_the_record_has_to_make()
    {
        // The second security re-review's H-5. PostgreSQL's default for a function inverts its
        // default for a table: a new function is EXECUTE to PUBLIC with no GRANT for a reviewer
        // to notice, and a SECURITY DEFINER body runs as its owner - the schema owner - so one
        // function in catalog handed aurora_app the column-level UPDATE it does not hold, while
        // the oracle, reading tables only, reported a perfect match. ADR-0028 section 2 mechanism
        // 3 already orders a function into catalog. Two things must hold from now on: a function
        // a migration creates is 42501 for the role until that migration opens it by name, and it
        // is an object the record has to decide - this one is not recorded, so the oracle says so.
        // Committed rather than rolled back because the point is to call it as aurora_app from its
        // own connection; the collection runs one test at a time, and it is dropped whatever happens.
        DatabaseCluster cluster = Unique.Cluster();
        Tenant acme = Unique.Tenant(cluster);
        await _catalog.SeedAsync(catalog =>
        {
            catalog.DatabaseClusters.Add(cluster);
            catalog.Tenants.Add(acme);
        });

        // A database of the attacker's naming rather than another tenant's, so that what refuses
        // the call is the privilege and not ux_tenant_cluster_id_database_name.
        string elsewhere = Tenant.DatabaseNameFor(Unique.TenantKey());
        string function = Unique.Identifier("touch_activity");
        await using NpgsqlConnection migrator = await _catalog.OpenMigratorConnectionAsync();
        await CatalogDatabaseFixture.ExecuteAsync(
            migrator,
            $"CREATE FUNCTION catalog.{function}(uuid, text) RETURNS void LANGUAGE sql SECURITY DEFINER AS "
            + "'UPDATE catalog.tenant SET database_name = $2 WHERE id = $1'");

        try
        {
            await RefusedAsAppAsync(
                "calling a function in catalog that no migration opened to the role",
                $"SELECT catalog.{function}(@acme, @database)",
                [("acme", acme.Id.Value), ("database", elsewhere)]);

            await using CatalogDbContext reader = _catalog.OpenAsApp();
            (await reader.Tenants.SingleAsync(t => t.Id == acme.Id)).DatabaseName.ShouldBe(acme.DatabaseName);

            List<string> differences = Differences(
                (await PrivilegesHeldAsync(migrator, null)).ByObject, CatalogSchemaAllowlist.AppRoleDecisions);
            differences.Count.ShouldBe(1, string.Join(Environment.NewLine, differences));
            differences[0].ShouldContain($"function catalog.{function}(uuid, text) exists but");
        }
        finally
        {
            await CatalogDatabaseFixture.ExecuteAsync(migrator, $"DROP FUNCTION catalog.{function}(uuid, text)");
        }
    }

    [Fact]
    public async Task The_catalog_sets_exactly_one_default_privilege_and_it_closes_new_functions_to_PUBLIC()
    {
        // The one ALTER DEFAULT PRIVILEGES in the catalog, and why it is not the kind the
        // migration refuses: it revokes. Without it every function a migration adds is open to
        // the role before anyone decided, the opposite of the table rule. A default has no object
        // until one is created, so pg_default_acl is invisible to the ACL oracle - the re-review
        // re-introduced a table default and the oracle read the same 14 rows - and is read here on
        // its own, every row of it for this database whatever its scope: exactly one, database-
        // wide (a per-schema REVOKE cannot remove a built-in default), for the owner, for
        // functions, and its ACL names nobody but the owner. A second row, whatever it grants and
        // wherever it is scoped, fails this test.
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT r.rolname, d.defaclobjtype::text, COALESCE(n.nspname, 'database-wide'), "
            + "  (SELECT string_agg(CASE WHEN a.grantee = 0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END || '=' || a.privilege_type, ', ' ORDER BY a.privilege_type) "
            + "   FROM aclexplode(d.defaclacl) a) "
            + "FROM pg_default_acl d "
            + "JOIN pg_roles r ON r.oid = d.defaclrole "
            + "LEFT JOIN pg_namespace n ON n.oid = d.defaclnamespace",
            connection);

        List<(string Role, string ObjectType, string Scope, string Entries)> defaults = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                defaults.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        _output.WriteLine($"Read {defaults.Count} default privilege(s) in the catalog database: {string.Join("; ", defaults)}.");

        defaults.ShouldBe([(CatalogDatabaseFixture.MigratorRole, "f", "database-wide", $"{CatalogDatabaseFixture.MigratorRole}=EXECUTE")]);
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
    /// What the ACL says the app role holds, object by object, and how many entries said so. An
    /// object with no entry is present with an empty set: it exists, and the record has to say
    /// what was decided for it.
    /// </summary>
    private sealed record HeldPrivileges(IReadOnlyDictionary<CatalogObject, IReadOnlySet<string>> ByObject, int AclEntries)
    {
        /// <summary>How many objects of each kind were read, every kind the oracle knows, zeros included.</summary>
        public string CountByKind() =>
            string.Join(", ", CatalogSchemaAllowlist.ObjectKinds.Prepend(CatalogObject.Table)
                .Select(kind => $"{ByObject.Keys.Count(o => o.Kind == kind)} {kind}"));
    }

    private static async Task<HeldPrivileges> PrivilegesHeldAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(AclEntriesSql, connection, transaction);
        command.Parameters.AddWithValue("role", CatalogDatabaseFixture.AppRole);

        var held = new Dictionary<CatalogObject, HashSet<string>>();
        int entries = 0;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var catalogObject = new CatalogObject(reader.GetString(0), reader.GetString(1));
            if (!held.TryGetValue(catalogObject, out HashSet<string>? privileges))
            {
                privileges = new HashSet<string>(StringComparer.Ordinal);
                held[catalogObject] = privileges;
            }

            if (reader.IsDBNull(2))
            {
                continue;
            }

            entries++;
            privileges.Add(Label(
                privilege: reader.GetString(2),
                column: reader.IsDBNull(3) ? null : reader.GetString(3),
                grantable: reader.GetBoolean(4),
                grantee: reader.GetString(5),
                inherited: reader.GetBoolean(6),
                settable: reader.GetBoolean(7)));
        }

        return new HeldPrivileges(
            held.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value),
            entries);
    }

    /// <summary>
    /// One ACL entry, spelled the way <c>CatalogSchemaAllowlist</c> spells a decision:
    /// <c>SELECT</c>; <c>UPDATE(column)</c> for a column grant; then <c>WITH GRANT OPTION</c> if
    /// the role could pass it on, and <c>through PUBLIC</c> or <c>through &lt;role&gt;</c> if it
    /// reaches the app role without naming it — with <c>(SET ROLE)</c> when the membership is not
    /// inherited but can be assumed, and <c>(member, neither INHERIT nor SET)</c> when it is
    /// neither, because a membership that grants nothing today is still one a reviewer should see.
    /// The allowlist records only the first two forms, so an entry in any other is always a
    /// difference — a privilege the request path should hold is granted to it, directly.
    /// </summary>
    private static string Label(string privilege, string? column, bool grantable, string grantee, bool inherited, bool settable)
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

            if (!inherited)
            {
                label.Append(settable ? " (SET ROLE)" : " (member, neither INHERIT nor SET)");
            }
        }

        return label.ToString();
    }

    /// <summary>
    /// Every way what the app role holds can differ from what the allowlist records, each as a
    /// sentence a reviewer can act on. Empty means the two agree, object by object, both ways.
    /// </summary>
    private static List<string> Differences(
        IReadOnlyDictionary<CatalogObject, IReadOnlySet<string>> held,
        IReadOnlyDictionary<CatalogObject, IReadOnlyList<AppRoleGrant>> recorded)
    {
        List<string> differences = [];

        foreach ((CatalogObject catalogObject, IReadOnlySet<string> privileges) in held.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            if (!recorded.TryGetValue(catalogObject, out IReadOnlyList<AppRoleGrant>? grants))
            {
                differences.Add(
                    $"{catalogObject} exists but CatalogSchemaAllowlist.{catalogObject.Record} records no decision for it; "
                    + $"{CatalogDatabaseFixture.AppRole} holds [{string.Join(", ", privileges.Order(StringComparer.Ordinal))}]");
                continue;
            }

            HashSet<string> decided = [.. grants.Select(grant => grant.Privilege)];

            foreach (string privilege in privileges.Except(decided).Order(StringComparer.Ordinal))
            {
                differences.Add($"{CatalogDatabaseFixture.AppRole} holds {privilege} on {catalogObject}, which the allowlist does not record");
            }

            foreach (string privilege in decided.Except(privileges).Order(StringComparer.Ordinal))
            {
                differences.Add($"the allowlist records {privilege} on {catalogObject}, which {CatalogDatabaseFixture.AppRole} does not hold");
            }
        }

        foreach (CatalogObject catalogObject in recorded.Keys.Except(held.Keys).OrderBy(o => o.ToString(), StringComparer.Ordinal))
        {
            differences.Add($"the allowlist records {catalogObject}, which does not exist in the migrated schema");
        }

        return differences;
    }
}
