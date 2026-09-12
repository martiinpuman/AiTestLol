using System;
using System.Collections.Generic;
using System.Globalization;
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
/// The guards on the catalog's append-only trails (ADR-0028 §2 mechanism 3; <c>solution-layout.md</c>
/// §6.4 item 5 criterion 4, plus the truncate guard the orchestrator added to that row's scope),
/// asserted two ways that see different things: the <b>effect probe</b> and the <b>binding
/// check</b>. Every tamper shape this file knows is run against both, rolled back, and the
/// Theory states which of the two reports it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The effect probe.</b> As the owner, inside a transaction that is rolled back, an
/// <c>UPDATE</c> and a <c>DELETE</c> against a seeded row of each trail and a <c>TRUNCATE</c> of
/// it must each be refused with SQLSTATE <c>42501</c> and the guard's own message. <em>Why the
/// owner:</em> connected as <c>aurora_app</c>, an unguarded table returns the same <c>42501</c>
/// from the privilege check with no trigger involved, so a probe run as the request-path role
/// reports green over a table the guard was never attached to; <c>aurora_migrator</c> holds
/// <c>UPDATE</c>, <c>DELETE</c> and <c>TRUNCATE</c>, so from its connection a <c>42501</c> can
/// only have come from the guard, and the message pins it there. <em>Why rolled back:</em> the
/// run that finds a broken guard is the run in which the forbidden statement succeeds, and for
/// <c>TRUNCATE</c> that is the whole trail. <em>What it sees:</em> a hollowed function body
/// (<c>CREATE OR REPLACE FUNCTION</c> leaves every <c>pg_trigger</c> column byte-identical), a
/// disabled or dropped guard, a guard rebound to a function that does not raise — anything that
/// lets the statement through <em>for the probe's session</em>.
/// </para>
/// <para>
/// <b>The binding check.</b> For each guard the migration created, every <c>pg_trigger</c> column
/// that decides whether it fires is read and compared with what the migration wrote: <c>tgtype</c>
/// exactly, <c>tgenabled = 'A'</c>, <c>tgfoid</c> resolved to
/// <c>catalog.refuse_append_only_change</c> <em>by schema</em>, <c>tgqual</c> null and
/// <c>tgattr</c> empty. <em>What it sees that the probe cannot:</em> a guard whose firing was
/// made conditional. A <c>WHEN (current_setting('aurora.maintenance', true) IS DISTINCT FROM 'on')</c>
/// clause lets nothing through until a session sets that GUC, so the probe, which sets none,
/// still gets its <c>42501</c>; a column list (<c>BEFORE UPDATE OF correlation_id</c>) fires for
/// the column the probe happens to update and for no other. Both shipped through the first
/// review of this branch with every test green (PR #12, critical), because the enumeration read
/// <c>tgtype</c>, <c>tgenabled</c> and a schema-less <c>proname</c> and nothing else — a link
/// nothing was reading, which is where every break on this project so far has lived. <em>What it
/// cannot see:</em> a hollow body, by construction; that is the probe's.
/// </para>
/// <para>
/// <b>What neither sees.</b> A transient tamper — disable, act, restore — between two runs, and
/// any shape neither the probe executes nor the binding check binds. The first is what a chain
/// head outside the database is for (<c>FOLLOWUP-026</c>); against the second the only defence
/// is that the binding check reads <em>every</em> column of <c>pg_trigger</c> that changes what
/// fires, which is the list above.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogAppendOnlyGuardTests
{
    private const string InsufficientPrivilege = "42501";
    private const string GuardFunction = "refuse_append_only_change";
    private const string QualifiedGuardFunction = "catalog." + GuardFunction;

    /// <summary>The GUC the WHEN-clause tamper shapes below key on; any name would do, the point is that the probe sets none.</summary>
    private const string MaintenanceSwitch = "aurora.maintenance";

    /// <summary>
    /// The trigger type bits PostgreSQL stores in <c>pg_trigger.tgtype</c>: ROW 1, BEFORE 2,
    /// DELETE 8, UPDATE 16, TRUNCATE 32. A row-level BEFORE UPDATE OR DELETE guard is 27; a
    /// statement-level BEFORE TRUNCATE guard is 34.
    /// </summary>
    private const short RowLevelBeforeUpdateOrDelete = 1 | 2 | 8 | 16;

    private const short StatementLevelBeforeTruncate = 2 | 32;

    /// <summary>The two guards the migration creates on every append-only table, by name suffix and type.</summary>
    private static readonly IReadOnlyList<(string Suffix, short Type)> ExpectedGuards =
    [
        ("", RowLevelBeforeUpdateOrDelete),
        ("_truncate", StatementLevelBeforeTruncate),
    ];

    /// <summary>One forbidden statement per verb; <c>@id</c> names the seeded row where a row is what is forbidden.</summary>
    private static readonly IReadOnlyList<(string Verb, string SqlFor)> Forbidden =
    [
        ("UPDATE", "UPDATE catalog.{0} SET correlation_id = gen_random_uuid() WHERE id = @id"),
        ("DELETE", "DELETE FROM catalog.{0} WHERE id = @id"),
        ("TRUNCATE", "TRUNCATE catalog.{0}"),
    ];

    /// <summary>
    /// Every tamper shape, and what each side of the suite sees of it. Columns: the fault; the
    /// statements the owner issues; what the effect probe reports silent (<c>table/VERB</c>, or
    /// nothing); the binding check's finding (a substring, or nothing); a bypass statement run
    /// with <see cref="MaintenanceSwitch"/> set to <c>on</c> (or nothing); and the bypass's
    /// <em>proof</em> — a boolean <c>SELECT</c> that is true only if the bypass landed, read
    /// before the savepoint is rolled back. The proof is what is asserted, never the driver's
    /// return value: Npgsql reports <c>-1</c> for a <c>TRUNCATE</c> that succeeded, and a sentinel
    /// of <c>-1</c> for "refused" once let this very row pass while the trail was being emptied.
    /// </summary>
    public static TheoryData<string, string, string, string, string, string> Tampers => new()
    {
        {
            "hollowed for rows: CREATE OR REPLACE FUNCTION … RETURN COALESCE(NEW, OLD)",
            $"CREATE OR REPLACE FUNCTION catalog.{GuardFunction}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN COALESCE(NEW, OLD); END'",
            "operator_audit_event/UPDATE, operator_audit_event/DELETE, operator_audit_event/TRUNCATE, erasure_replay_log/UPDATE, erasure_replay_log/DELETE, erasure_replay_log/TRUNCATE",
            "",
            "",
            ""
        },
        {
            "hollowed for statements: CREATE OR REPLACE FUNCTION … RETURN NULL",
            $"CREATE OR REPLACE FUNCTION catalog.{GuardFunction}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END'",
            "operator_audit_event/UPDATE, operator_audit_event/DELETE, operator_audit_event/TRUNCATE, erasure_replay_log/UPDATE, erasure_replay_log/DELETE, erasure_replay_log/TRUNCATE",
            "",
            "",
            ""
        },
        {
            "disabled: ALTER TABLE … DISABLE TRIGGER on the row guard of one table",
            "ALTER TABLE catalog.operator_audit_event DISABLE TRIGGER trg_operator_audit_event_append_only",
            "operator_audit_event/UPDATE, operator_audit_event/DELETE",
            "catalog.operator_audit_event.trg_operator_audit_event_append_only: tgenabled 'D', expected 'A'",
            "",
            ""
        },
        {
            "dropped: DROP TRIGGER on the row guard of one table",
            "DROP TRIGGER trg_erasure_replay_log_append_only ON catalog.erasure_replay_log",
            "erasure_replay_log/UPDATE, erasure_replay_log/DELETE",
            "catalog.erasure_replay_log.trg_erasure_replay_log_append_only is missing",
            "",
            ""
        },
        {
            "disabled: ALTER TABLE … DISABLE TRIGGER on the truncate guard of one table",
            "ALTER TABLE catalog.erasure_replay_log DISABLE TRIGGER trg_erasure_replay_log_append_only_truncate",
            "erasure_replay_log/TRUNCATE",
            "catalog.erasure_replay_log.trg_erasure_replay_log_append_only_truncate: tgenabled 'D', expected 'A'",
            "",
            ""
        },
        {
            "dropped: DROP TRIGGER on the truncate guard of one table",
            "DROP TRIGGER trg_operator_audit_event_append_only_truncate ON catalog.operator_audit_event",
            "operator_audit_event/TRUNCATE",
            "catalog.operator_audit_event.trg_operator_audit_event_append_only_truncate is missing",
            "",
            ""
        },
        {
            "conditional: the row guard re-created WITH a WHEN clause keyed on a GUC the probe never sets (PR #12 critical)",
            "DROP TRIGGER trg_operator_audit_event_append_only ON catalog.operator_audit_event; "
            + "CREATE TRIGGER trg_operator_audit_event_append_only BEFORE UPDATE OR DELETE ON catalog.operator_audit_event FOR EACH ROW "
            + $"WHEN (current_setting('{MaintenanceSwitch}', true) IS DISTINCT FROM 'on') EXECUTE FUNCTION catalog.{GuardFunction}(); "
            + "ALTER TABLE catalog.operator_audit_event ENABLE ALWAYS TRIGGER trg_operator_audit_event_append_only",
            "",
            "catalog.operator_audit_event.trg_operator_audit_event_append_only has a WHEN clause",
            "DELETE FROM catalog.operator_audit_event WHERE id = @id",
            "SELECT NOT EXISTS (SELECT 1 FROM catalog.operator_audit_event WHERE id = @id)"
        },
        {
            "conditional: the truncate guard re-created WITH a WHEN clause - accepted AND evaluated by PostgreSQL 17.11 on a statement trigger",
            "DROP TRIGGER trg_erasure_replay_log_append_only_truncate ON catalog.erasure_replay_log; "
            + "CREATE TRIGGER trg_erasure_replay_log_append_only_truncate BEFORE TRUNCATE ON catalog.erasure_replay_log FOR EACH STATEMENT "
            + $"WHEN (current_setting('{MaintenanceSwitch}', true) IS DISTINCT FROM 'on') EXECUTE FUNCTION catalog.{GuardFunction}(); "
            + "ALTER TABLE catalog.erasure_replay_log ENABLE ALWAYS TRIGGER trg_erasure_replay_log_append_only_truncate",
            "",
            "catalog.erasure_replay_log.trg_erasure_replay_log_append_only_truncate has a WHEN clause",
            "TRUNCATE catalog.erasure_replay_log",
            "SELECT NOT EXISTS (SELECT 1 FROM catalog.erasure_replay_log)"
        },
        {
            "conditional: the row guard re-created for UPDATE OF the one column the probe updates",
            "DROP TRIGGER trg_erasure_replay_log_append_only ON catalog.erasure_replay_log; "
            + "CREATE TRIGGER trg_erasure_replay_log_append_only BEFORE UPDATE OF correlation_id OR DELETE ON catalog.erasure_replay_log FOR EACH ROW "
            + $"EXECUTE FUNCTION catalog.{GuardFunction}(); "
            + "ALTER TABLE catalog.erasure_replay_log ENABLE ALWAYS TRIGGER trg_erasure_replay_log_append_only",
            "",
            "catalog.erasure_replay_log.trg_erasure_replay_log_append_only fires for a column list",
            "UPDATE catalog.erasure_replay_log SET requested_by = 'rewritten' WHERE id = @id",
            "SELECT requested_by = 'rewritten' FROM catalog.erasure_replay_log WHERE id = @id"
        },
        {
            "rebound: the row guard re-created to execute a hollow function of the same name in another schema (PR #12 medium)",
            "CREATE SCHEMA shadow_b19; "
            + $"CREATE FUNCTION shadow_b19.{GuardFunction}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN COALESCE(NEW, OLD); END'; "
            + "DROP TRIGGER trg_operator_audit_event_append_only ON catalog.operator_audit_event; "
            + "CREATE TRIGGER trg_operator_audit_event_append_only BEFORE UPDATE OR DELETE ON catalog.operator_audit_event FOR EACH ROW "
            + $"EXECUTE FUNCTION shadow_b19.{GuardFunction}(); "
            + "ALTER TABLE catalog.operator_audit_event ENABLE ALWAYS TRIGGER trg_operator_audit_event_append_only",
            "operator_audit_event/UPDATE, operator_audit_event/DELETE",
            $"catalog.operator_audit_event.trg_operator_audit_event_append_only executes shadow_b19.{GuardFunction}, not {QualifiedGuardFunction}",
            "",
            ""
        },
    };

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogAppendOnlyGuardTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task As_the_owner_UPDATE_DELETE_and_TRUNCATE_are_refused_with_42501_on_every_append_only_table_and_nothing_is_changed()
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
        probe.Refused.ShouldBe(probe.Probed * Forbidden.Count, "one UPDATE, one DELETE and one TRUNCATE per table");
    }

    [Fact]
    public async Task Each_append_only_table_carries_the_two_guards_the_migration_created_bound_exactly_as_it_created_them()
    {
        // The binding check over the migrated database: every guard present, of the exact type,
        // ALWAYS, executing catalog.refuse_append_only_change by schema, unconditional, for every
        // column. tgenabled must read 'A', not merely not 'D': an 'O' trigger is suppressed under
        // session_replication_role = 'replica' with tgenabled unchanged, and ENABLE REPLICA leaves
        // 'R'. tgqual and tgattr must be empty: a WHEN clause or a column list is a guard that
        // fires for the probe and not for an attacker, and neither changes tgtype, tgenabled or
        // the function.
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        IReadOnlyList<GuardLocation> located = await LocateGuardsAsync(connection, null);
        List<string> findings = Findings(located);

        _output.WriteLine(Describe(located));

        findings.ShouldBeEmpty(string.Join(Environment.NewLine, findings));
        located.Count.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count * ExpectedGuards.Count, "two guards per append-only table");
        located.Count.ShouldBeGreaterThanOrEqualTo(4, "a binding check over fewer guards examined the wrong population");
    }

    [Theory]
    [MemberData(nameof(Tampers))]
    public async Task Each_tamper_is_reported_by_the_effect_probe_or_by_the_binding_check_and_the_report_says_which(
        string fault, string sql, string probeSilent, string finding, string bypassSql, string bypassProof)
    {
        // The deliberately-violating fixture (testing-strategy.md 1 rule 4), injected from inside
        // the system under test: each fault is what the owner may issue, executed in the same
        // transaction as the probe and the binding check and rolled back with them. Every row
        // states both verdicts, so a tamper that neither side sees cannot hide in a row that only
        // asks one of them:
        //
        // - The hollow bodies leave pg_trigger byte-identical, so the binding check reports
        //   nothing and the probe reports every statement silent. One function serves both
        //   guards, so either hollow shape silences all six; both are kept because they hollow
        //   differently - RETURN NULL cancels a row-level UPDATE or DELETE (0 rows, no error) and
        //   is ignored by a statement-level trigger, so the TRUNCATE lands; RETURN COALESCE(NEW,
        //   OLD) lets the row through and, being NULL for a statement, the TRUNCATE too.
        // - Disabling and dropping are seen by both: the probe names that guard's verbs on that
        //   table, the binding check names the guard as 'D' or missing.
        // - The conditional shapes are the ones the probe cannot see. A WHEN clause keyed on a
        //   GUC fires for every session that has not set it - the probe included - so the probe
        //   stays green while the bypass below, with the GUC set, deletes the row (PR #12
        //   critical). A column list fires for the column the probe updates and for no other, so
        //   the probe stays green while the bypass rewrites another column. Only the binding
        //   check sees these, by reading tgqual and tgattr. The truncate guard is exposed the
        //   same way: PostgreSQL 17.11 accepts a WHEN clause on a FOR EACH STATEMENT trigger and
        //   evaluates it - executed here and in psql, with the GUC set the TRUNCATE lands and the
        //   table reads back empty - so tgqual is load-bearing on both guards, not only the row
        //   guard. Each bypass is proved by a SELECT read before its savepoint rolls back.
        // - The rebound guard is seen by both: the probe, because the shadow function does not
        //   raise; the binding check, because the function is resolved by schema and not by name
        //   alone (PR #12 medium).
        Guid tenantId = await SeedTenantAsync();
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await owner.BeginTransactionAsync();

        await using (var inject = new NpgsqlCommand(sql, owner, transaction))
        {
            await inject.ExecuteNonQueryAsync();
        }

        GuardProbe probe = await ProbeAsync(owner, transaction, tenantId);
        IReadOnlyList<GuardLocation> located = await LocateGuardsAsync(owner, transaction);
        List<string> findings = Findings(located);
        string? bypass = bypassSql.Length == 0 ? null : await BypassAsync(owner, transaction, tenantId, bypassSql, bypassProof);

        await transaction.RollbackAsync();

        _output.WriteLine($"Fault '{fault}':");
        _output.WriteLine($"  effect probe:  {probe.Report()}");
        _output.WriteLine($"  binding check: {(findings.Count == 0 ? "(no finding)" : string.Join("; ", findings))}");
        _output.WriteLine($"  {Describe(located)}");
        if (bypass is not null)
        {
            _output.WriteLine($"  bypass with {MaintenanceSwitch} = on: {bypass}");
        }

        probe.Probed.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count);
        probe.Silent.Order(StringComparer.Ordinal).ShouldBe(
            probeSilent.Length == 0 ? [] : probeSilent.Split(", ").Order(StringComparer.Ordinal),
            Case.Sensitive,
            $"the fault '{fault}' silenced exactly these statements for the probe");
        if (finding.Length == 0)
        {
            findings.ShouldBeEmpty($"the fault '{fault}' leaves pg_trigger as the migration wrote it, so the binding check has nothing to report");
        }
        else
        {
            findings.ShouldHaveSingleItem($"the fault '{fault}' changes one guard's binding").ShouldContain(finding);
        }

        (probeSilent.Length > 0 || finding.Length > 0).ShouldBeTrue($"the fault '{fault}' is seen by neither side, which is the one thing this branch cannot ship");

        // Rolled back, both sides are clean again and the trail is as it was: the red above was
        // the fault, not the check, and the check cost the trail nothing.
        await using NpgsqlTransaction clean = await owner.BeginTransactionAsync();
        GuardProbe restored = await ProbeAsync(owner, clean, tenantId);
        List<string> restoredFindings = Findings(await LocateGuardsAsync(owner, clean));
        await clean.RollbackAsync();
        restored.Silent.ShouldBeEmpty(restored.Report());
        restoredFindings.ShouldBeEmpty(string.Join(Environment.NewLine, restoredFindings));
    }

    [Fact]
    public async Task The_guards_still_refuse_under_a_replica_session_role_because_they_are_enabled_ALWAYS()
    {
        // The effect behind tgenabled = 'A'. session_replication_role = 'replica' suppresses every
        // ordinary ('O') trigger without changing a byte of pg_trigger; an ALWAYS ('A') trigger
        // fires regardless. The GUC is superuser-only on PostgreSQL 17 - aurora_migrator setting it
        // is 42501 - so the fixture's superuser sets it, inside a transaction that is rolled back.
        // A superuser bypasses privilege checks, never triggers, so what refuses the statements
        // here can only be the guards: the assertion is on their message, not merely the SQLSTATE.
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
        probe.Refused.ShouldBe(probe.Probed * Forbidden.Count);
    }

    /// <summary>
    /// Seeds one row per append-only table inside <paramref name="transaction"/>, then tries each
    /// of <see cref="Forbidden"/> — an <c>UPDATE</c> and a <c>DELETE</c> of that row, and a
    /// <c>TRUNCATE</c> of the table — in a savepoint, requiring SQLSTATE <c>42501</c> and the
    /// guard's own message from every one. Reports how many tables were probed, how many
    /// statements were refused, and every statement that went through, as <c>table/VERB</c>.
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

            foreach ((string verb, string sqlFor) in Forbidden)
            {
                string sql = string.Format(CultureInfo.InvariantCulture, sqlFor, row.Table);
                await transaction.SaveAsync("probe");
                await using var write = new NpgsqlCommand(sql, connection, transaction);
                if (sql.Contains("@id", StringComparison.Ordinal))
                {
                    write.Parameters.AddWithValue("id", row.Id);
                }

                try
                {
                    await write.ExecuteNonQueryAsync();
                    silent.Add($"{row.Table}/{verb}");
                }
                catch (PostgresException raised) when (raised.SqlState == InsufficientPrivilege
                                                        && raised.MessageText == $"catalog.{row.Table} is append-only: {verb} refused")
                {
                    refused++;
                }

                await transaction.RollbackAsync("probe");
            }
        }

        return new GuardProbe(probed, refused, silent);
    }

    /// <summary>
    /// What a session that has set <see cref="MaintenanceSwitch"/> can do to a freshly seeded row
    /// of the table <paramref name="sql"/> names, in a savepoint that is rolled back. The statement
    /// is executed unguarded — a guard that still fires raises, and the test fails with that
    /// exception — and then <paramref name="proof"/>, a boolean <c>SELECT</c>, is read and must be
    /// true: the row is gone, the table is empty, the column is rewritten. Returns one line for
    /// the report.
    /// </summary>
    private static async Task<string> BypassAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, string sql, string proof)
    {
        AppendOnlyRow row = AppendOnlyRow.OneForEachTable(tenantId).Single(r => sql.Contains($"catalog.{r.Table}", StringComparison.Ordinal));
        await transaction.SaveAsync("bypass");
        await using (NpgsqlCommand insert = row.Insert(connection, transaction))
        {
            await insert.ExecuteNonQueryAsync();
        }

        await using (var arm = new NpgsqlCommand($"SET LOCAL {MaintenanceSwitch} = 'on'", connection, transaction))
        {
            await arm.ExecuteNonQueryAsync();
        }

        await using (NpgsqlCommand attempt = WithSeededRow(new NpgsqlCommand(sql, connection, transaction), row))
        {
            await attempt.ExecuteNonQueryAsync();
        }

        bool landed;
        await using (NpgsqlCommand check = WithSeededRow(new NpgsqlCommand(proof, connection, transaction), row))
        {
            landed = (bool)(await check.ExecuteScalarAsync())!;
        }

        await transaction.RollbackAsync("bypass");
        landed.ShouldBeTrue($"{sql} with {MaintenanceSwitch} = on was not refused, yet '{proof}' says it did not land");
        return $"{sql} -> landed; '{proof}' is true";
    }

    private static NpgsqlCommand WithSeededRow(NpgsqlCommand command, AppendOnlyRow row)
    {
        if (command.CommandText.Contains("@id", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("id", row.Id);
        }

        return command;
    }

    /// <summary>
    /// Every non-internal trigger on the append-only tables, with each <c>pg_trigger</c> column
    /// that decides whether it fires: type, enabled state, the function it executes resolved by
    /// schema, whether it carries a <c>WHEN</c> clause (<c>tgqual</c>) and how many columns an
    /// <c>UPDATE OF</c> list names (<c>tgattr</c>); plus PostgreSQL's own rendering, for the report.
    /// </summary>
    private static async Task<IReadOnlyList<GuardLocation>> LocateGuardsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT c.relname, t.tgname, t.tgtype, t.tgenabled, fn.nspname || '.' || p.proname, "
            + "       t.tgqual IS NOT NULL, coalesce(array_length(t.tgattr::smallint[], 1), 0), pg_get_triggerdef(t.oid) "
            + "FROM pg_trigger t "
            + "JOIN pg_class c ON c.oid = t.tgrelid "
            + "JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "JOIN pg_proc p ON p.oid = t.tgfoid "
            + "JOIN pg_namespace fn ON fn.oid = p.pronamespace "
            + "WHERE n.nspname = 'catalog' AND NOT t.tgisinternal AND c.relname = ANY(@tables) "
            + "ORDER BY c.relname, t.tgname",
            connection, transaction);
        command.Parameters.AddWithValue("tables", CatalogSchemaAllowlist.AppendOnlyTables.ToArray());

        List<GuardLocation> located = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            located.Add(new GuardLocation(
                reader.GetString(0), reader.GetString(1), reader.GetInt16(2), reader.GetChar(3), reader.GetString(4),
                reader.GetBoolean(5), reader.GetInt32(6), reader.GetString(7)));
        }

        return located;
    }

    /// <summary>
    /// Every way a located guard can differ from the one the migration created, each as a
    /// sentence naming the guard, plus every expected guard that is not there at all. Empty means
    /// every binding is as written.
    /// </summary>
    private static List<string> Findings(IReadOnlyList<GuardLocation> located)
    {
        List<string> findings = [];
        var expected = new Dictionary<(string Table, string Trigger), short>();
        foreach (string table in CatalogSchemaAllowlist.AppendOnlyTables)
        {
            foreach ((string suffix, short type) in ExpectedGuards)
            {
                expected[(table, $"trg_{table}_append_only{suffix}")] = type;
            }
        }

        foreach (GuardLocation guard in located)
        {
            string name = $"catalog.{guard.Table}.{guard.Trigger}";
            if (!expected.Remove((guard.Table, guard.Trigger), out short type))
            {
                findings.Add($"{name} is not a guard the migration created: {guard.Definition}");
                continue;
            }

            if (guard.Type != type)
            {
                findings.Add($"{name}: tgtype {guard.Type}, expected {type}");
            }

            if (guard.Enabled != 'A')
            {
                findings.Add($"{name}: tgenabled '{guard.Enabled}', expected 'A'");
            }

            if (!string.Equals(guard.Function, QualifiedGuardFunction, StringComparison.Ordinal))
            {
                findings.Add($"{name} executes {guard.Function}, not {QualifiedGuardFunction}");
            }

            if (guard.HasWhen)
            {
                findings.Add($"{name} has a WHEN clause, so it fires for some sessions and not others: {guard.Definition}");
            }

            if (guard.Columns > 0)
            {
                findings.Add($"{name} fires for a column list of {guard.Columns}, so an UPDATE of any other column goes through: {guard.Definition}");
            }
        }

        foreach ((string table, string trigger) in expected.Keys.OrderBy(key => key.Table, StringComparer.Ordinal).ThenBy(key => key.Trigger, StringComparer.Ordinal))
        {
            findings.Add($"catalog.{table}.{trigger} is missing");
        }

        return findings;
    }

    private static string Describe(IReadOnlyList<GuardLocation> located) =>
        $"Located {located.Count} guard trigger(s) on {CatalogSchemaAllowlist.AppendOnlyTables.Count} append-only tables: "
        + string.Join("; ", located.Select(g =>
            $"{g.Table}.{g.Trigger} tgtype {g.Type} tgenabled {g.Enabled} -> {g.Function}() when {(g.HasWhen ? "present" : "none")} columns {g.Columns}"));

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

    /// <summary>
    /// What one run of the effect probe saw, in the shape ADR-0028 Amendment 2 asks it to report:
    /// relations probed, statements refused, and the silent statements by name (<c>table/VERB</c>).
    /// </summary>
    private sealed record GuardProbe(int Probed, int Refused, IReadOnlyList<string> Silent)
    {
        public string Report() =>
            $"relations probed: {Probed}, statements refused with {InsufficientPrivilege}: {Refused}, "
            + $"silent: {(Silent.Count == 0 ? "(none)" : string.Join(", ", Silent))}";
    }

    /// <summary>One trigger as <c>pg_trigger</c> holds it, in the columns that decide whether it fires.</summary>
    private sealed record GuardLocation(string Table, string Trigger, short Type, char Enabled, string Function, bool HasWhen, int Columns, string Definition);
}
