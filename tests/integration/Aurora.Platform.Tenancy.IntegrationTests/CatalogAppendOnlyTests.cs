using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Tests;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The catalog's two append-only trails — <c>catalog.operator_audit_event</c> and
/// <c>catalog.erasure_replay_log</c> — held to <c>solution-layout.md</c> §6.4 item 5 on the
/// migrated database: they exist with the columns the ADRs name, the request-path role can add
/// to them and nothing else, no default privilege reaches schema <c>catalog</c>, and every probe
/// says how much it examined.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why every assertion here is against PostgreSQL and not the EF model.</b> Both tables are
/// created by raw SQL — a table that carries a guard trigger is nothing the model can express —
/// and the B-05 security review (M-1) landed exactly such a table with a blanket grant while the
/// model-agreement test and the gate both reported a pass. So the columns are read from
/// <c>information_schema</c>, the grants from the ACL (<c>CatalogPrivilegeTests</c>), and the
/// writes are tried as the role.
/// </para>
/// <para>
/// <b>Why the request-path probes connect as <c>aurora_app</c>.</b> Criterion 2 is about the
/// privilege: <c>INSERT</c> must succeed, because the provisioning saga and the erasure path
/// write these trails as this role, and <c>UPDATE</c>/<c>DELETE</c> must be refused with
/// SQLSTATE <c>42501</c> before a row is ever looked at. The guard trigger is the other half, and
/// it is probed as the owner in <c>CatalogAppendOnlyGuardTests</c>, because as <c>aurora_app</c> a
/// <c>42501</c> proves the grant and says nothing about the trigger.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogAppendOnlyTests
{
    private const string InsufficientPrivilege = "42501";

    /// <summary>
    /// What criterion 1 asserts, column by column, against <c>information_schema.columns</c>: the
    /// name, the store type as <c>information_schema</c> spells it, and whether a null is
    /// admitted. Derived from ADR-0007 §9.2 and §11.5, ADR-0018 §1 and §6, ADR-0010 rule 8 and
    /// SPEC-001 BR-7 as <c>CatalogSchemaAllowlist.AppendOnlyColumns</c>' remarks set out; kept as
    /// its own list here rather than read from the allowlist, so that the two are compared
    /// through the database rather than with each other.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Column, string Type, bool Nullable)[]> Trails =
        new Dictionary<string, (string, string, bool)[]>(StringComparer.Ordinal)
        {
            ["operator_audit_event"] =
            [
                ("id", "uuid", false),
                ("occurred_at", "timestamp with time zone", false),
                ("tenant_id", "uuid", true),
                ("actor_type", "character varying", false),
                ("actor_id", "character varying", false),
                ("action", "character varying", false),
                ("reason_code", "character varying", true),
                ("correlation_id", "uuid", true),
                ("detail", "jsonb", true),
            ],
            ["erasure_replay_log"] =
            [
                ("id", "uuid", false),
                ("tenant_id", "uuid", false),
                ("subject_ref", "character varying", false),
                ("requested_by", "character varying", false),
                ("executed_at", "timestamp with time zone", false),
                ("affected", "jsonb", false),
                ("correlation_id", "uuid", true),
            ],
        };

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogAppendOnlyTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task Both_append_only_tables_exist_in_the_migrated_catalog_with_the_columns_the_ADRs_name()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name, column_name, data_type, is_nullable = 'YES' "
            + "FROM information_schema.columns WHERE table_schema = 'catalog' AND table_name = ANY(@tables)",
            connection);
        command.Parameters.AddWithValue("tables", Trails.Keys.ToArray());

        var observed = new List<(string Table, string Column, string Type, bool Nullable)>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                observed.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
            }
        }

        // The count is printed and floored so that a query returning nothing - the tables absent,
        // the schema name mistyped - cannot look like a pass (CLAUDE.md self-check 2).
        _output.WriteLine(
            $"Read {observed.Count} columns of {observed.Select(c => c.Table).Distinct(StringComparer.Ordinal).Count()} append-only tables from information_schema.columns.");

        foreach ((string table, (string Column, string Type, bool Nullable)[] expected) in Trails)
        {
            observed.Where(c => c.Table == table).Select(c => (c.Column, c.Type, c.Nullable))
                .OrderBy(c => c.Column, StringComparer.Ordinal)
                .ShouldBe(expected.OrderBy(c => c.Column, StringComparer.Ordinal), $"catalog.{table}");
        }

        observed.Count.ShouldBe(Trails.Sum(table => table.Value.Length));
        Trails.Keys.Order(StringComparer.Ordinal).ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task As_the_app_role_INSERT_succeeds_on_each_append_only_table_and_UPDATE_and_DELETE_are_refused()
    {
        // Criterion 2's executed half. The INSERT is not optional: a probe that only checked
        // "UPDATE is refused" would pass against a table nobody granted anything on, which is
        // what a new catalog table is by default. Committed rather than rolled back, because the
        // point is to write as aurora_app from its own connection and read the row back as the
        // same role afterwards; an append-only row can never be removed, and the ids are unique.
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await _catalog.SeedAsync(catalog =>
        {
            catalog.DatabaseClusters.Add(cluster);
            catalog.Tenants.Add(tenant);
        });

        IReadOnlyList<AppendOnlyRow> rows = [.. AppendOnlyRow.OneForEachTable(tenant.Id.Value)];
        rows.Count.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count, "one row per append-only table");

        int refusals = 0;
        await using NpgsqlConnection app = await _catalog.OpenAppConnectionAsync();
        foreach (AppendOnlyRow row in rows)
        {
            await using (NpgsqlCommand insert = row.Insert(app))
            {
                (await insert.ExecuteNonQueryAsync()).ShouldBe(1, $"INSERT into catalog.{row.Table} as {CatalogDatabaseFixture.AppRole}");
            }

            foreach (string sql in new[]
                     {
                         $"UPDATE catalog.{row.Table} SET correlation_id = gen_random_uuid() WHERE id = @id",
                         $"DELETE FROM catalog.{row.Table} WHERE id = @id",
                     })
            {
                await using var write = new NpgsqlCommand(sql, app);
                write.Parameters.AddWithValue("id", row.Id);

                PostgresException refused = await Should.ThrowAsync<PostgresException>(() => write.ExecuteNonQueryAsync(), sql);

                refused.SqlState.ShouldBe(InsufficientPrivilege, sql);
                refused.MessageText.ShouldBe($"permission denied for table {row.Table}", "the privilege check, not the guard: aurora_app never reaches the trigger");
                refusals++;
            }

            await using var readBack = new NpgsqlCommand(
                $"SELECT count(*) FROM catalog.{row.Table} WHERE id = @id AND correlation_id IS NULL", app);
            readBack.Parameters.AddWithValue("id", row.Id);
            ((long)(await readBack.ExecuteScalarAsync())!).ShouldBe(1, $"the row aurora_app inserted into catalog.{row.Table} is there and unchanged");
        }

        _output.WriteLine(
            $"As {CatalogDatabaseFixture.AppRole}: {rows.Count} INSERTs succeeded and {refusals} UPDATE/DELETE statements were refused "
            + $"with {InsufficientPrivilege} across {rows.Count} append-only tables ({string.Join(", ", rows.Select(r => r.Table))}).");
        refusals.ShouldBe(rows.Count * 2);
    }

    [Fact]
    public async Task No_default_privilege_reaches_schema_catalog_and_a_copied_GRANT_default_would_be_caught()
    {
        // Criterion 5. audit's schema-wide default grant (solution-layout.md 6.4 item 1 criterion
        // 3) must not be copied to catalog, whose tables legitimately need UPDATE; this reads the
        // rule back as a count of pg_default_acl rows scoped to the schema, which the ACL oracle
        // cannot see because a default has no object until one is created. Then the fault, inside
        // a transaction that is rolled back: the GRANT direction - B-05 M-1's blanket-grant shape,
        // and what copying criterion 3 across produces - takes the count from 0 to 1 and is
        // caught. The REVOKE direction is executed too, and leaves the count at 0, because no
        // role holds a default privilege on a new table for a REVOKE to remove: the no-op that
        // ADR-0028 Amendment 1 withdrew is invisible here, and the row says so. It is survivable
        // because CatalogPrivilegeTests asserts the resulting ACL of every catalog relation, never
        // which statement produced it.
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();

        (await DefaultPrivilegeRowsForSchemaCatalogAsync(connection, null)).ShouldBe(0);

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (var revoke = new NpgsqlCommand(
                         $"ALTER DEFAULT PRIVILEGES FOR ROLE {CatalogDatabaseFixture.MigratorRole} IN SCHEMA catalog "
                         + $"REVOKE UPDATE, DELETE ON TABLES FROM {CatalogDatabaseFixture.AppRole}", connection, transaction))
        {
            await revoke.ExecuteNonQueryAsync();
        }

        int afterRevoke = await DefaultPrivilegeRowsForSchemaCatalogAsync(connection, transaction);

        await using (var grant = new NpgsqlCommand(
                         $"ALTER DEFAULT PRIVILEGES FOR ROLE {CatalogDatabaseFixture.MigratorRole} IN SCHEMA catalog "
                         + $"GRANT SELECT, INSERT ON TABLES TO {CatalogDatabaseFixture.AppRole}", connection, transaction))
        {
            await grant.ExecuteNonQueryAsync();
        }

        int afterGrant = await DefaultPrivilegeRowsForSchemaCatalogAsync(connection, transaction);
        await transaction.RollbackAsync();

        _output.WriteLine(
            $"pg_default_acl rows for schema catalog: {afterRevoke} after the no-op REVOKE (not caught, as the row records), "
            + $"{afterGrant} after the copied GRANT (caught), 0 after rollback.");
        afterRevoke.ShouldBe(0, "the REVOKE form writes no row on PostgreSQL 17 - it is the statement the criterion cannot see");
        afterGrant.ShouldBe(1, "the GRANT form is what the criterion catches");
        (await DefaultPrivilegeRowsForSchemaCatalogAsync(connection, null)).ShouldBe(0, "rolled back");
    }

    private static async Task<int> DefaultPrivilegeRowsForSchemaCatalogAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::int FROM pg_default_acl d JOIN pg_namespace n ON n.oid = d.defaclnamespace WHERE n.nspname = 'catalog'",
            connection, transaction);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}

/// <summary>
/// One well-formed row for an append-only table, with the statement that inserts it. The shape
/// each writer will produce, minus the writer: this task builds no writer, so the tests write
/// the rows themselves (<c>solution-layout.md</c> §6.4 item 5, scope boundary).
/// </summary>
internal sealed record AppendOnlyRow(string Table, Guid Id, string Sql, IReadOnlyList<(string Name, object Value)> Parameters)
{
    public static IEnumerable<AppendOnlyRow> OneForEachTable(Guid tenantId)
    {
        Guid operatorEventId = Guid.CreateVersion7();
        yield return new AppendOnlyRow(
            "operator_audit_event",
            operatorEventId,
            "INSERT INTO catalog.operator_audit_event (id, occurred_at, tenant_id, actor_type, actor_id, action, reason_code, correlation_id, detail) "
            + "VALUES (@id, @at, @tenant, 'Operator', 'operator:probe', 'tenant.support_access.granted', 'support:ticket', NULL, '{\"valid_for_minutes\": 30}'::jsonb)",
            [("id", operatorEventId), ("at", Unique.Now), ("tenant", tenantId)]);

        Guid erasureId = Guid.CreateVersion7();
        yield return new AppendOnlyRow(
            "erasure_replay_log",
            erasureId,
            "INSERT INTO catalog.erasure_replay_log (id, tenant_id, subject_ref, requested_by, executed_at, affected, correlation_id) "
            + "VALUES (@id, @tenant, 'subject:7f3a', 'user:probe', @at, '[\"party.person.display_name\"]'::jsonb, NULL)",
            [("id", erasureId), ("tenant", tenantId), ("at", Unique.Now)]);
    }

    public NpgsqlCommand Insert(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        var command = new NpgsqlCommand(Sql, connection, transaction);
        foreach ((string name, object value) in Parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
