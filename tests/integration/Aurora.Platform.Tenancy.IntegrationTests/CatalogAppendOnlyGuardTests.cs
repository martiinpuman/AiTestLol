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
/// The guard on the catalog's append-only trails (ADR-0028 §2 mechanism 3; <c>solution-layout.md</c>
/// §6.4 item 5 criterion 4), asserted by its <em>effect</em>: as the owner, inside a transaction
/// that is rolled back, an <c>UPDATE</c> and a <c>DELETE</c> against a seeded row of each trail
/// must be refused with SQLSTATE <c>42501</c> and the guard's own message.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the owner.</b> Connected as <c>aurora_app</c>, an <em>unguarded</em> table returns the
/// same <c>42501</c> from the privilege check, with no trigger involved — a probe run as the
/// request-path role reports green over a table the guard was never attached to.
/// <c>aurora_migrator</c> holds <c>UPDATE</c> and <c>DELETE</c> as the owner, so its privilege path
/// cannot produce <c>42501</c>: from this connection a <c>42501</c> can only have come from the
/// guard, and the message text pins it there.
/// </para>
/// <para>
/// <b>Why rolled back.</b> The run that finds a broken guard is the run in which the forbidden
/// statement <em>succeeds</em>. A probe that ran for real would go red and destroy the trail
/// while doing it.
/// </para>
/// <para>
/// <b>Why effect and not presence.</b> <c>CREATE OR REPLACE FUNCTION … RETURN COALESCE(NEW, OLD)</c>
/// is one statement, touches no table and no trigger, and leaves every <c>pg_trigger</c> column
/// byte-identical while the <c>UPDATE</c> succeeds; no predicate over <c>pg_trigger</c> can see a
/// function body. So the probe executes the statement, and
/// <see cref="The_probe_goes_red_and_names_the_silent_table_when_the_guard_is_hollowed_disabled_or_dropped"/>
/// runs it against each of those faults to show it does. The <c>pg_trigger</c> enumeration stays
/// as the <em>locator</em> — when the probe names a silent table, it says whether the guard is
/// missing, disabled, or intact-but-hollow — never as the guarantee.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogAppendOnlyGuardTests
{
    private const string InsufficientPrivilege = "42501";
    private const string GuardFunction = "refuse_append_only_change";

    /// <summary>
    /// The trigger type bits PostgreSQL stores in <c>pg_trigger.tgtype</c>: a row-level BEFORE
    /// trigger on UPDATE and DELETE is 1 + 2 + 8 + 16 = 27.
    /// </summary>
    private const short RowLevelBeforeUpdateOrDelete = 1 | 2 | 8 | 16;

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogAppendOnlyGuardTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task As_the_owner_UPDATE_and_DELETE_are_refused_with_42501_on_every_append_only_table_and_nothing_is_changed()
    {
        Guid tenantId = await SeedTenantAsync();
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await owner.BeginTransactionAsync();

        GuardProbe probe = await ProbeAsync(owner, transaction, tenantId);

        await transaction.RollbackAsync();

        _output.WriteLine(probe.Report());
        probe.Silent.ShouldBeEmpty(probe.Report());
        probe.Probed.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count, "every append-only table was probed");
        probe.Probed.ShouldBeGreaterThanOrEqualTo(2, "ADR-0007 9.2 names two; a probe over fewer examined the wrong population");
        probe.Refused.ShouldBe(probe.Probed * 2, "one UPDATE and one DELETE per table");
    }

    [Theory]
    [InlineData(
        "hollowed: CREATE OR REPLACE FUNCTION with a body that lets the row through",
        $"CREATE OR REPLACE FUNCTION catalog.{GuardFunction}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN COALESCE(NEW, OLD); END'",
        "operator_audit_event, erasure_replay_log")]
    [InlineData(
        "disabled: ALTER TABLE … DISABLE TRIGGER on one table",
        "ALTER TABLE catalog.operator_audit_event DISABLE TRIGGER trg_operator_audit_event_append_only",
        "operator_audit_event")]
    [InlineData(
        "dropped: DROP TRIGGER on one table",
        "DROP TRIGGER trg_erasure_replay_log_append_only ON catalog.erasure_replay_log",
        "erasure_replay_log")]
    public async Task The_probe_goes_red_and_names_the_silent_table_when_the_guard_is_hollowed_disabled_or_dropped(string fault, string sql, string silent)
    {
        // The deliberately-violating fixture (testing-strategy.md 1 rule 4), injected from inside
        // the system under test: each fault is one statement the owner may issue, executed in the
        // same transaction as the probe and rolled back with it. The hollow body is the one that
        // matters - it leaves pg_trigger byte-identical, so only an executed statement can see it
        // - and it is spelled RETURN COALESCE(NEW, OLD) rather than RETURN NULL because a
        // row-level BEFORE trigger returning NULL skips the row silently, which the probe also
        // reports as silent; this shape is the one that lets the UPDATE actually land.
        Guid tenantId = await SeedTenantAsync();
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await owner.BeginTransactionAsync();

        await using (var inject = new NpgsqlCommand(sql, owner, transaction))
        {
            await inject.ExecuteNonQueryAsync();
        }

        GuardProbe broken = await ProbeAsync(owner, transaction, tenantId);
        await transaction.RollbackAsync();

        _output.WriteLine($"Fault '{fault}': {broken.Report()}");
        broken.Probed.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count);
        broken.Silent.Order(StringComparer.Ordinal).ShouldBe(
            silent.Split(", ").Order(StringComparer.Ordinal), Case.Sensitive, $"the fault '{fault}' silenced exactly these tables");

        // Rolled back, the guard refuses again and the trail is as it was: the red above was the
        // fault, not the probe, and the probe cost the trail nothing.
        await using NpgsqlTransaction clean = await owner.BeginTransactionAsync();
        GuardProbe restored = await ProbeAsync(owner, clean, tenantId);
        await clean.RollbackAsync();
        restored.Silent.ShouldBeEmpty(restored.Report());
    }

    [Fact]
    public async Task Each_append_only_table_carries_one_ALWAYS_enabled_row_level_BEFORE_UPDATE_OR_DELETE_guard_as_the_locator()
    {
        // The metadata enumeration, demoted to a locator (ADR-0028 Amendment 2): it says which
        // table is guarded and how, and it is blind to a hollow body by construction, which the
        // fault theory above shows. tgenabled must read 'A', not merely not 'D': an 'O' trigger is
        // suppressed under session_replication_role = 'replica' with tgenabled unchanged, and
        // ENABLE REPLICA leaves 'R'.
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT c.relname, t.tgname, t.tgtype, t.tgenabled, p.proname "
            + "FROM pg_trigger t "
            + "JOIN pg_class c ON c.oid = t.tgrelid "
            + "JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "JOIN pg_proc p ON p.oid = t.tgfoid "
            + "WHERE n.nspname = 'catalog' AND NOT t.tgisinternal AND c.relname = ANY(@tables) "
            + "ORDER BY c.relname, t.tgname",
            connection);
        command.Parameters.AddWithValue("tables", CatalogSchemaAllowlist.AppendOnlyTables.ToArray());

        var triggers = new List<(string Table, string Trigger, short Type, char Enabled, string Function)>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                triggers.Add((reader.GetString(0), reader.GetString(1), reader.GetInt16(2), reader.GetChar(3), reader.GetString(4)));
            }
        }

        _output.WriteLine(
            $"Located {triggers.Count} guard trigger(s) on {CatalogSchemaAllowlist.AppendOnlyTables.Count} append-only tables: "
            + string.Join("; ", triggers.Select(t => $"{t.Table}.{t.Trigger} tgtype {t.Type} tgenabled {t.Enabled} -> {t.Function}()")));

        triggers.Select(t => t.Table).Order(StringComparer.Ordinal)
            .ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Order(StringComparer.Ordinal), Case.Sensitive, "exactly one guard per table");
        foreach ((string table, string trigger, short type, char enabled, string function) in triggers)
        {
            trigger.ShouldBe($"trg_{table}_append_only");
            type.ShouldBe(RowLevelBeforeUpdateOrDelete, $"{table}: row-level, BEFORE, UPDATE and DELETE");
            enabled.ShouldBe('A', $"{table}: ENABLE ALWAYS, so a replica session role cannot suppress it");
            function.ShouldBe(GuardFunction);
        }
    }

    [Fact]
    public async Task The_guard_still_refuses_under_a_replica_session_role_because_it_is_enabled_ALWAYS()
    {
        // The effect behind tgenabled = 'A'. session_replication_role = 'replica' suppresses every
        // ordinary ('O') trigger without changing a byte of pg_trigger; an ALWAYS ('A') trigger
        // fires regardless. The GUC is superuser-only on PostgreSQL 17 - aurora_migrator setting it
        // is 42501 - so the fixture's superuser sets it, inside a transaction that is rolled back.
        // A superuser bypasses privilege checks, never triggers, so what refuses the UPDATE here
        // can only be the guard: the assertion is on its message, not merely on the SQLSTATE.
        Guid tenantId = await SeedTenantAsync();
        await using NpgsqlConnection superuser = await _catalog.OpenSuperuserConnectionAsync();
        await using NpgsqlTransaction transaction = await superuser.BeginTransactionAsync();

        await using (var replica = new NpgsqlCommand("SET LOCAL session_replication_role = 'replica'", superuser, transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        GuardProbe probe = await ProbeAsync(superuser, transaction, tenantId);
        await transaction.RollbackAsync();

        _output.WriteLine($"Under session_replication_role = 'replica': {probe.Report()}");
        probe.Silent.ShouldBeEmpty(probe.Report());
        probe.Probed.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count);
    }

    /// <summary>
    /// Seeds one row per append-only table inside <paramref name="transaction"/>, then tries an
    /// <c>UPDATE</c> and a <c>DELETE</c> of each in a savepoint, requiring SQLSTATE <c>42501</c>
    /// and the guard's own message from both. Reports every table probed, how many statements
    /// were refused, and which tables let a statement through.
    /// </summary>
    private static async Task<GuardProbe> ProbeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId)
    {
        int probed = 0;
        int refused = 0;
        List<string> silent = [];

        foreach (AppendOnlyRow row in AppendOnlyRow.OneForEachTable(tenantId))
        {
            probed++;
            await using (NpgsqlCommand insert = row.Insert(connection, transaction))
            {
                (await insert.ExecuteNonQueryAsync()).ShouldBe(1, $"seeding catalog.{row.Table}");
            }

            bool tableSilent = false;
            foreach ((string operation, string sql) in new[]
                     {
                         ("UPDATE", $"UPDATE catalog.{row.Table} SET correlation_id = gen_random_uuid() WHERE id = @id"),
                         ("DELETE", $"DELETE FROM catalog.{row.Table} WHERE id = @id"),
                     })
            {
                await transaction.SaveAsync("probe");
                await using var write = new NpgsqlCommand(sql, connection, transaction);
                write.Parameters.AddWithValue("id", row.Id);

                try
                {
                    await write.ExecuteNonQueryAsync();
                    tableSilent = true;
                }
                catch (PostgresException raised) when (raised.SqlState == InsufficientPrivilege
                                                        && raised.MessageText == $"catalog.{row.Table} is append-only: {operation} refused")
                {
                    refused++;
                }

                await transaction.RollbackAsync("probe");
            }

            if (tableSilent)
            {
                silent.Add(row.Table);
            }
        }

        return new GuardProbe(probed, refused, silent);
    }

    private async Task<Guid> SeedTenantAsync()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await _catalog.SeedAsync(catalog =>
        {
            catalog.DatabaseClusters.Add(cluster);
            catalog.Tenants.Add(tenant);
        });
        return tenant.Id.Value;
    }

    /// <summary>What one run of the effect probe saw, in the shape ADR-0028 Amendment 2 asks it to report.</summary>
    private sealed record GuardProbe(int Probed, int Refused, IReadOnlyList<string> Silent)
    {
        public string Report() =>
            $"relations probed: {Probed}, statements refused with {InsufficientPrivilege}: {Refused}, "
            + $"silent: {(Silent.Count == 0 ? "(none)" : string.Join(", ", Silent))}";
    }
}
