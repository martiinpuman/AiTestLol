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
/// <b>The binding check.</b> Name the chain from what is asserted to what must be true. The ACL
/// decides who may issue a statement at all (<c>CatalogPrivilegeTests</c>' oracle);
/// <c>pg_trigger</c> decides which guard fires, when, for which rows and with what;
/// <c>pg_rewrite</c> can discard or redirect the statement before any guard fires;
/// <c>pg_class.relrowsecurity</c> and <c>pg_policy</c> decide which rows the statement reaches;
/// and <c>pg_proc</c> holds what the guard the trigger names <em>actually does</em>. This check
/// reads every link after the ACL and compares it with what the migration wrote: for each
/// guard <c>tgtype</c> exactly, <c>tgenabled = 'A'</c>, <c>tgfoid</c> resolved to
/// <c>catalog.refuse_append_only_change</c> <em>by schema</em>, <c>tgqual</c> null, <c>tgattr</c>
/// empty, <c>tgnargs</c> zero; for each table no rule of any kind, row-level security off, no
/// policy; and for the function, <c>pg_get_functiondef</c>'s rendering — body, language,
/// security and configuration in one string — equal to the one the migration wrote. <em>What it
/// sees that the probe cannot:</em> anything made <em>conditional on the session</em>. A guard
/// re-created <c>WHEN (current_setting('aurora.maintenance', true) IS DISTINCT FROM 'on')</c>
/// fires for every session that has not set that GUC — the probe included — and for no session
/// that has; a column list (<c>BEFORE UPDATE OF correlation_id</c>) fires for the column the
/// probe happens to update and for no other; a rule <c>ON INSERT … WHERE current_setting(…) =
/// 'on' DO INSTEAD NOTHING</c> discards an armed session's inserts with nothing refused, so an
/// unrecorded support-access grant or an erasure that is never replayed after a restore leaves a
/// trail that looks intact because nothing was ever refused, only silently dropped; a forced
/// row-level-security policy keyed the same way conceals every row from an armed session; and a
/// body that returns for an armed session and raises, with the migration's own message, for
/// every other, refuses the probe every time and empties both trails for the attacker. Each of
/// those shipped through a review of this branch with every test green — the <c>WHEN</c> clause
/// (first review, critical), the rule (second, high), the conditional body (third, blocker) —
/// because a check is only as good as the last link it follows, and the link stopped one column
/// short, then one catalog short, then at the function's <em>name</em> rather than its
/// definition. <c>aurora_app</c> can <em>arm</em> any of them (<c>SET aurora.maintenance = 'on'</c>
/// is any role's to run) but cannot plant one: planting needs the DDL role.
/// </para>
/// <para>
/// <b>What <c>pg_get_functiondef</c> cannot see.</b> The definition is text. What an identifier
/// in it resolves to at execution is decided by the session's <c>search_path</c>, which this
/// definition does not pin (no <c>SET search_path</c> — it would be shown if it did). The body's
/// one call is <c>format()</c>; a shadowing <c>format()</c> earlier on the path cannot stop the
/// <c>RAISE</c>, it can only change the message, which the probe compares exactly and would then
/// report as an unmatched exception rather than a refusal. Nor does the rendering show the
/// function's owner, which matters only under <c>SECURITY DEFINER</c> (shown), or the language's
/// handler, which is superuser territory. A run that read no definition, or an empty one, is a
/// finding rather than a clean comparison.
/// </para>
/// <para>
/// <b>What neither sees.</b> A transient tamper — plant, act, remove — between two runs, which is
/// what a chain head outside the database is for (<c>FOLLOWUP-026</c>). And a shape nobody
/// enumerated: the Theory requires at least one side to report every row, which is the guard
/// that would catch this class of gap — and it can only fire for a row that exists, which is the
/// general limit of a fault theory. A <c>CHECK</c> constraint keyed on a GUC is the one shape
/// that announces itself — an armed insert fails loudly rather than vanishing — and is not read.
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

    /// <summary>
    /// The guard function as the <c>AppendOnlyTrails</c> migration created it, rendered the way
    /// <c>pg_get_functiondef</c> renders a definition on PostgreSQL 17.11 — verified against the
    /// server's own output with whitespace made visible: name, then <c>" RETURNS trigger"</c> and
    /// <c>" LANGUAGE plpgsql"</c> each on a line with one leading space, then the body between
    /// <c>$function$</c> quotes exactly as <c>pg_proc.prosrc</c> holds it, then a newline. Any
    /// option the rendering would add — <c>SECURITY DEFINER</c>, a <c>SET</c>, a different
    /// language, a different body — makes the strings differ, which closes every <c>pg_proc</c>
    /// link at once.
    /// </summary>
    /// <remarks>
    /// A separate literal on purpose, not read from the migration: the review that found the
    /// conditional-body bypass proved it by committing three lines into the migration's source
    /// and running this suite, and an expectation derived from that source would have moved with
    /// them. A change to the migration's body that is not also a change here is exactly what
    /// this check must fail on.
    /// </remarks>
    private const string ExpectedGuardFunctionDefinition =
        "CREATE OR REPLACE FUNCTION catalog.refuse_append_only_change()\n"
        + " RETURNS trigger\n"
        + " LANGUAGE plpgsql\n"
        + "AS $function$\n"
        + "BEGIN\n"
        + "    RAISE EXCEPTION USING\n"
        + "        ERRCODE = '42501',\n"
        + "        MESSAGE = format('%I.%I is append-only: %s refused', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP),\n"
        + "        HINT = 'ADR-0004 rule 5: an audit trail is corrected by a new row, never by rewriting one.';\n"
        + "END\n"
        + "$function$\n";

    /// <summary>
    /// The third review's fault: the guard's body re-written to return for an armed session and
    /// raise, with the migration's own message, for every other — the probe's included.
    /// </summary>
    private const string ConditionalGuardBody =
        $"CREATE OR REPLACE FUNCTION catalog.{GuardFunction}() RETURNS trigger LANGUAGE plpgsql AS "
        + $"'BEGIN IF current_setting(''{MaintenanceSwitch}'', true) = ''on'' THEN RETURN COALESCE(NEW, OLD); END IF; "
        + "RAISE EXCEPTION USING ERRCODE = ''42501'', MESSAGE = format(''%I.%I is append-only: %s refused'', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP); END'";

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
    /// nothing); the binding check's findings (substrings separated by <c> | </c>, exactly that
    /// many, or nothing); a bypass statement run
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
            $"{QualifiedGuardFunction} is not the function the migration created",
            "",
            ""
        },
        {
            "hollowed for statements: CREATE OR REPLACE FUNCTION … RETURN NULL",
            $"CREATE OR REPLACE FUNCTION catalog.{GuardFunction}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END'",
            "operator_audit_event/UPDATE, operator_audit_event/DELETE, operator_audit_event/TRUNCATE, erasure_replay_log/UPDATE, erasure_replay_log/DELETE, erasure_replay_log/TRUNCATE",
            $"{QualifiedGuardFunction} is not the function the migration created",
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
        {
            "argued: the truncate guard re-created passing an argument its function ignores - inert today, read anyway",
            "DROP TRIGGER trg_operator_audit_event_append_only_truncate ON catalog.operator_audit_event; "
            + "CREATE TRIGGER trg_operator_audit_event_append_only_truncate BEFORE TRUNCATE ON catalog.operator_audit_event FOR EACH STATEMENT "
            + $"EXECUTE FUNCTION catalog.{GuardFunction}('maintenance'); "
            + "ALTER TABLE catalog.operator_audit_event ENABLE ALWAYS TRIGGER trg_operator_audit_event_append_only_truncate",
            "",
            "catalog.operator_audit_event.trg_operator_audit_event_append_only_truncate passes 1 argument(s) to its function",
            "",
            ""
        },
        {
            "rewritten: a rule that discards an armed session's INSERTs with nothing refused (PR #12 second review, high)",
            "CREATE RULE audit_off AS ON INSERT TO catalog.operator_audit_event "
            + $"WHERE current_setting('{MaintenanceSwitch}', true) = 'on' DO INSTEAD NOTHING",
            "",
            "catalog.operator_audit_event has rule audit_off",
            "INSERT INTO catalog.operator_audit_event (id, occurred_at, tenant_id, actor_type, actor_id, action) "
            + "VALUES (gen_random_uuid(), '2026-09-11T12:00:00Z', NULL, 'System', 'attacker:probe', 'silently.discarded')",
            "SELECT NOT EXISTS (SELECT 1 FROM catalog.operator_audit_event WHERE action = 'silently.discarded')"
        },
        {
            "conditional: the guard function's body re-written to return instead of raising for an armed session - every pg_trigger column, every rule and every policy unchanged (PR #12 third review, blocker)",
            ConditionalGuardBody,
            "",
            $"{QualifiedGuardFunction} is not the function the migration created",
            "DELETE FROM catalog.operator_audit_event WHERE id = @id",
            "SELECT NOT EXISTS (SELECT 1 FROM catalog.operator_audit_event WHERE id = @id)"
        },
        {
            "conditional: the same body, and its blast radius - one function serves both tables and all three verbs, so the other table's truncate guard falls to the same statement",
            ConditionalGuardBody,
            "",
            $"{QualifiedGuardFunction} is not the function the migration created",
            "TRUNCATE catalog.erasure_replay_log",
            "SELECT NOT EXISTS (SELECT 1 FROM catalog.erasure_replay_log)"
        },
        {
            "concealed: forced row-level security with a policy that hides every row from an armed session",
            "ALTER TABLE catalog.erasure_replay_log ENABLE ROW LEVEL SECURITY; "
            + "ALTER TABLE catalog.erasure_replay_log FORCE ROW LEVEL SECURITY; "
            + "CREATE POLICY conceal ON catalog.erasure_replay_log FOR ALL "
            + $"USING (current_setting('{MaintenanceSwitch}', true) IS DISTINCT FROM 'on') WITH CHECK (true)",
            "",
            "catalog.erasure_replay_log has row level security FORCE | catalog.erasure_replay_log has policy conceal",
            "UPDATE catalog.erasure_replay_log SET requested_by = 'rewritten' WHERE id = @id",
            "SELECT NOT EXISTS (SELECT 1 FROM catalog.erasure_replay_log WHERE id = @id)"
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
    public async Task Each_append_only_table_carries_the_two_guards_the_migration_created_bound_exactly_as_it_created_them_and_nothing_else_that_decides_a_write()
    {
        // The binding check over the migrated database: every guard present, of the exact type,
        // ALWAYS, executing catalog.refuse_append_only_change by schema, unconditional, for every
        // column, with no arguments; and on each table no rewrite rule, no row-level security,
        // no policy. tgenabled must read 'A', not merely not 'D': an 'O' trigger is suppressed
        // under session_replication_role = 'replica' with tgenabled unchanged, and ENABLE REPLICA
        // leaves 'R'. tgqual and tgattr must be empty and pg_rewrite and pg_policy must hold
        // nothing for these tables: a WHEN clause, a column list, a conditional rule or a policy
        // is a table that behaves for the probe and not for an attacker, and none of them changes
        // tgtype, tgenabled or the function.
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        IReadOnlyList<GuardLocation> guards = await LocateGuardsAsync(connection, null);
        IReadOnlyList<TableAttachment> attachments = await LocateAttachmentsAsync(connection, null);
        List<string> findings = Findings(guards, attachments);

        _output.WriteLine(Describe(guards, attachments));

        findings.ShouldBeEmpty(string.Join(Environment.NewLine, findings));
        guards.Count.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count * ExpectedGuards.Count, "two guards per append-only table");
        guards.Count.ShouldBeGreaterThanOrEqualTo(4, "a binding check over fewer guards examined the wrong population");

        // The definition comparison examined something: one function, read in full, and every
        // guard resolving to it - a run that read no definition cannot report a clean comparison.
        guards.Select(g => g.FunctionDefinition).Distinct(StringComparer.Ordinal).ShouldHaveSingleItem("one function serves every guard")
            .Length.ShouldBeGreaterThan(0, "pg_get_functiondef returned nothing");
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
        // - The hollow bodies leave pg_trigger byte-identical and are seen by both sides: by the
        //   probe, whose own six statements go through, and by the binding check, whose
        //   pg_get_functiondef comparison reports the changed definition. One function serves
        //   both guards, so either hollow shape silences all six; both are kept because they
        //   hollow differently - RETURN NULL cancels a row-level UPDATE or DELETE (0 rows, no
        //   error) and is ignored by a statement-level trigger, so the TRUNCATE lands; RETURN
        //   COALESCE(NEW, OLD) lets the row through and, being NULL for a statement, the
        //   TRUNCATE too.
        // - The conditional body is the case the probe cannot see: it raises, with the
        //   migration's own message, for every session that has not armed the GUC - the probe
        //   included - and returns for one that has. Before the definition was compared, it was
        //   seen by nothing on this branch and shipped through the suite at 75/75/0 (PR #12 third
        //   review, blocker); its blast radius is the whole mechanism, because one function
        //   serves both tables and all three verbs, so the two rows bypass DELETE on one table
        //   and TRUNCATE on the other with the same statement.
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
        // - A trigger argument is inert today only because the function ignores TG_ARGV; "inert
        //   only because" is how the previous doors were described, so it is read and reported.
        // - The rule and the policy are outside pg_trigger altogether, which is why the guard
        //   check of the second review was complete over the object it examined and still let
        //   them through (PR #12 second review, high). A rule ON INSERT … WHERE the GUC is set DO
        //   INSTEAD NOTHING leaves the probe's unarmed inserts landing and an armed session's
        //   discarded with nothing refused; a forced row-level-security policy keyed the same way
        //   leaves the probe's unarmed statements refused by the guard while an armed session
        //   sees no rows and updates none, silently. Only the binding check sees these, by
        //   reading pg_rewrite, pg_class.relrowsecurity and pg_policy.
        Guid tenantId = await SeedTenantAsync();
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await owner.BeginTransactionAsync();

        await using (var inject = new NpgsqlCommand(sql, owner, transaction))
        {
            await inject.ExecuteNonQueryAsync();
        }

        GuardProbe probe = await ProbeAsync(owner, transaction, tenantId);
        IReadOnlyList<GuardLocation> guards = await LocateGuardsAsync(owner, transaction);
        IReadOnlyList<TableAttachment> attachments = await LocateAttachmentsAsync(owner, transaction);
        List<string> findings = Findings(guards, attachments);
        string? bypass = bypassSql.Length == 0 ? null : await BypassAsync(owner, transaction, tenantId, bypassSql, bypassProof);

        await transaction.RollbackAsync();

        _output.WriteLine($"Fault '{fault}':");
        _output.WriteLine($"  effect probe:  {probe.Report()}");
        _output.WriteLine($"  binding check: {(findings.Count == 0 ? "(no finding)" : string.Join("; ", findings))}");
        _output.WriteLine($"  {Describe(guards, attachments)}");
        if (bypass is not null)
        {
            _output.WriteLine($"  bypass with {MaintenanceSwitch} = on: {bypass}");
        }

        probe.Probed.ShouldBe(CatalogSchemaAllowlist.AppendOnlyTables.Count);
        probe.Silent.Order(StringComparer.Ordinal).ShouldBe(
            probeSilent.Length == 0 ? [] : probeSilent.Split(", ").Order(StringComparer.Ordinal),
            Case.Sensitive,
            $"the fault '{fault}' silenced exactly these statements for the probe");
        string[] expectedFindings = finding.Length == 0 ? [] : finding.Split(" | ");
        findings.Count.ShouldBe(
            expectedFindings.Length,
            expectedFindings.Length == 0
                ? $"the fault '{fault}' leaves pg_trigger, pg_rewrite and pg_policy as the migration wrote them, so the binding check has nothing to report: {string.Join("; ", findings)}"
                : $"the fault '{fault}' changes exactly {expectedFindings.Length} binding(s): {string.Join("; ", findings)}");
        foreach (string expected in expectedFindings)
        {
            findings.ShouldContain(f => f.Contains(expected, StringComparison.Ordinal), $"the fault '{fault}' is reported as '{expected}'");
        }

        (probeSilent.Length > 0 || finding.Length > 0).ShouldBeTrue($"the fault '{fault}' is seen by neither side, which is the one thing this branch cannot ship");

        // Rolled back, both sides are clean again and the trail is as it was: the red above was
        // the fault, not the check, and the check cost the trail nothing.
        await using NpgsqlTransaction clean = await owner.BeginTransactionAsync();
        GuardProbe restored = await ProbeAsync(owner, clean, tenantId);
        List<string> restoredFindings = Findings(await LocateGuardsAsync(owner, clean), await LocateAttachmentsAsync(owner, clean));
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
        landed.ShouldBeTrue($"{sql} with {MaintenanceSwitch} = on was not refused, yet '{proof}' says it had no effect");
        return $"{sql} -> not refused; '{proof}' is true";
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
    /// that decides whether and how it fires: type, enabled state, the function it executes
    /// resolved by schema, whether it carries a <c>WHEN</c> clause (<c>tgqual</c>), how many
    /// columns an <c>UPDATE OF</c> list names (<c>tgattr</c>) and how many arguments it passes
    /// (<c>tgnargs</c>); plus PostgreSQL's own rendering, for the report.
    /// </summary>
    private static async Task<IReadOnlyList<GuardLocation>> LocateGuardsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT c.relname, t.tgname, t.tgtype, t.tgenabled, fn.nspname || '.' || p.proname, "
            + "       t.tgqual IS NOT NULL, coalesce(array_length(t.tgattr::smallint[], 1), 0), t.tgnargs::int, pg_get_triggerdef(t.oid), "
            + "       coalesce(pg_get_functiondef(p.oid), '') "
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
                reader.GetBoolean(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8), reader.GetString(9)));
        }

        return located;
    }

    /// <summary>
    /// Everything besides a trigger that PostgreSQL consults to decide what a write to one of the
    /// append-only tables does, from the catalogs that hold it: every rewrite rule
    /// (<c>pg_rewrite</c>, rendered by <c>pg_get_ruledef</c>), row-level security if it is enabled
    /// or forced (<c>pg_class.relrowsecurity</c>, <c>relforcerowsecurity</c>), and every policy
    /// (<c>pg_policy</c>, read through <c>pg_policies</c> for its rendered expressions). The
    /// migration creates none of these, so every row is a finding.
    /// </summary>
    private static async Task<IReadOnlyList<TableAttachment>> LocateAttachmentsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT c.relname, 'rule', r.rulename::text, pg_get_ruledef(r.oid) "
            + "FROM pg_rewrite r JOIN pg_class c ON c.oid = r.ev_class JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "WHERE n.nspname = 'catalog' AND c.relname = ANY(@tables) "
            + "UNION ALL "
            + "SELECT c.relname, 'row level security', CASE WHEN c.relforcerowsecurity THEN 'FORCE' ELSE 'ENABLE' END, '' "
            + "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "WHERE n.nspname = 'catalog' AND c.relname = ANY(@tables) AND c.relrowsecurity "
            + "UNION ALL "
            + "SELECT p.tablename::text, 'policy', p.policyname::text, "
            + "       'FOR ' || p.cmd || ' TO ' || array_to_string(p.roles, ', ') || ' USING (' || coalesce(p.qual, '') || ') WITH CHECK (' || coalesce(p.with_check, '') || ')' "
            + "FROM pg_policies p WHERE p.schemaname = 'catalog' AND p.tablename = ANY(@tables) "
            + "ORDER BY 1, 2, 3",
            connection, transaction);
        command.Parameters.AddWithValue("tables", CatalogSchemaAllowlist.AppendOnlyTables.ToArray());

        List<TableAttachment> attachments = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            attachments.Add(new TableAttachment(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return attachments;
    }

    /// <summary>
    /// Every way a located guard can differ from the one the migration created, each as a
    /// sentence naming the guard; every expected guard that is not there at all; and every rule,
    /// row-level-security setting or policy on either table, which the migration never creates.
    /// Empty means every binding is as written and nothing else decides a write.
    /// </summary>
    private static List<string> Findings(IReadOnlyList<GuardLocation> guards, IReadOnlyList<TableAttachment> attachments)
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

        foreach (GuardLocation guard in guards)
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

            if (guard.Args > 0)
            {
                findings.Add($"{name} passes {guard.Args} argument(s) to its function, which the migration does not: {guard.Definition}");
            }
        }

        foreach ((string table, string trigger) in expected.Keys.OrderBy(key => key.Table, StringComparer.Ordinal).ThenBy(key => key.Trigger, StringComparer.Ordinal))
        {
            findings.Add($"catalog.{table}.{trigger} is missing");
        }

        // The last link: what the function the guards name actually does. pg_get_functiondef
        // renders body, language, security and configuration as one string, compared with the
        // one the migration wrote; one function serves every guard, so it is compared once. A
        // definition that could not be read is a finding, never a silent pass.
        foreach (string definition in guards
                     .Where(g => string.Equals(g.Function, QualifiedGuardFunction, StringComparison.Ordinal))
                     .Select(g => g.FunctionDefinition)
                     .Distinct(StringComparer.Ordinal))
        {
            if (definition.Length == 0)
            {
                findings.Add($"{QualifiedGuardFunction}'s definition could not be read from pg_proc");
            }
            else if (!string.Equals(definition, ExpectedGuardFunctionDefinition, StringComparison.Ordinal))
            {
                findings.Add($"{QualifiedGuardFunction} is not the function the migration created: {definition}");
            }
        }

        foreach (TableAttachment attachment in attachments)
        {
            findings.Add(
                $"catalog.{attachment.Table} has {attachment.Kind} {attachment.Name}, which decides what a write does before any guard fires"
                + (attachment.Definition.Length == 0 ? string.Empty : $": {attachment.Definition}"));
        }

        return findings;
    }

    private static string Describe(IReadOnlyList<GuardLocation> guards, IReadOnlyList<TableAttachment> attachments)
    {
        List<string> definitions = [.. guards.Select(g => g.FunctionDefinition).Distinct(StringComparer.Ordinal)];
        return $"Located {guards.Count} guard trigger(s) on {CatalogSchemaAllowlist.AppendOnlyTables.Count} append-only tables: "
            + string.Join("; ", guards.Select(g =>
                $"{g.Table}.{g.Trigger} tgtype {g.Type} tgenabled {g.Enabled} -> {g.Function}() when {(g.HasWhen ? "present" : "none")} columns {g.Columns} args {g.Args}"))
            + $". Read {attachments.Count(a => a.Kind == "rule")} rule(s), {attachments.Count(a => a.Kind == "policy")} polic(ies) and "
            + $"row level security on {attachments.Count(a => a.Kind == "row level security")} table(s) from pg_rewrite, pg_policy and pg_class. "
            + $"Read {definitions.Count} distinct function definition(s) from pg_proc via pg_get_functiondef, {definitions.Sum(d => d.Length)} chars in all, "
            + $"against the migration's {ExpectedGuardFunctionDefinition.Length}:"
            + string.Concat(definitions.Select(d => Environment.NewLine + d));
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

    /// <summary>
    /// One trigger as <c>pg_trigger</c> holds it, in the columns that decide whether and how it
    /// fires, and the definition of the function it executes as <c>pg_get_functiondef</c> renders
    /// it from <c>pg_proc</c> — the last link, the thing that actually runs.
    /// </summary>
    private sealed record GuardLocation(
        string Table, string Trigger, short Type, char Enabled, string Function, bool HasWhen, int Columns, int Args, string Definition, string FunctionDefinition);

    /// <summary>
    /// One thing besides a trigger attached to an append-only table that decides what a write
    /// does — a rewrite rule, row-level security, a policy — by table, kind, name and rendering.
    /// </summary>
    private sealed record TableAttachment(string Table, string Kind, string Name, string Definition);
}
