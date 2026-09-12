using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Fixtures.Violations;
using Aurora.Architecture.Tests.MigrationSafety;
using Aurora.Architecture.Tests.Rules;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The migration safety rules MIG1 and MIG2 (ADR-0007 §7.2 rules 1 and 2), over production and
/// over the fixture migrations in <c>Fixtures/Violations/MigrationViolations.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is scanned, and what is not.</b> The rules read the SQL Npgsql's generator emits from
/// each migration's <c>UpOperations</c> - raw <c>Sql()</c> strings and generated statements alike
/// - through the tokenizing scanner <c>SqlScannerTests</c> proves. <c>Down()</c> is not read: it
/// is never run in production (ADR-0007 §7.4) and is where an Expand's own drops belong.
/// </para>
/// <para>
/// <b>What the scanner cannot see is asserted here as unscannable, not as clean:</b> dynamic SQL,
/// a call to a function whose body lives elsewhere, text it cannot tokenize, and a migration that
/// produces no SQL at all. Each has a fixture and a test below.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class MigrationRuleTests
{
    private const string CatalogMigrationId = "20260911172124_InitialCatalog";

    private const string AppendOnlyTrailsMigrationId = "20260912020620_AppendOnlyTrails";

    /// <summary>
    /// The number of SQL statements MIG2 parses from each production migration - top level and
    /// inside bodies - measured on the branch that set the entry. The floor each is held to is
    /// the count less a tenth (<see cref="FloorFor"/>): per migration and not in aggregate, so
    /// that with two migrations one that generated nothing cannot hide behind the other's count;
    /// and proportional rather than verify.sh's nearest-ten, which on a count of 18 left 44% of a
    /// migration outside the floor (the second review's n-1). A production migration with no
    /// entry fails the test that reads this, printing the number to write.
    /// </summary>
    /// <remarks>
    /// Measured by <c>MIG2_parsed_at_least_the_measured_number_of_statements_from_each_production_migration</c>
    /// on B-09 rework 2, against the scanner as it stands in that commit: InitialCatalog 31,
    /// AppendOnlyTrails 18. The first review's M-1 found an earlier claim (27) measured against
    /// an earlier scanner; the numbers here are re-measured whenever the scanner changes, and the
    /// test prints the live count on failure.
    /// </remarks>
    private static readonly ImmutableDictionary<string, int> ProductionStatementCounts =
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            [
                KeyValuePair.Create(CatalogMigrationId, 31),
                KeyValuePair.Create(AppendOnlyTrailsMigrationId, 18),
            ]);

    /// <summary>The measured count less a tenth, never below one: 31 → 28, 18 → 17.</summary>
    private static int FloorFor(int measured) => Math.Max(1, measured - (measured / 10));

    private static readonly Lazy<ImmutableArray<ScannedMigration>> LazyFixtures = new(() =>
        MigrationPopulation.Of(
            typeof(MigrationRuleTests).Assembly.GetTypes().Where(static type =>
                string.Equals(type.Namespace, FixtureAssembly.ViolationsNamespace, StringComparison.Ordinal)
                && !type.IsAbstract
                && typeof(Migration).IsAssignableFrom(type))));

    private static ImmutableArray<ScannedMigration> Production => MigrationPopulation.Production;

    private static ImmutableArray<ScannedMigration> Fixtures => LazyFixtures.Value;

    // ---- The population -----------------------------------------------------------------------

    [Fact]
    public void The_production_population_holds_both_catalog_migrations_with_their_SQL_generated()
    {
        Production.Length.ShouldBe(
            2,
            "two production migrations exist on this branch (InitialCatalog, AppendOnlyTrails); when a "
            + "third lands, add its statement floor here and raise the floors in RuleInventoryTests deliberately: "
            + string.Join(", ", Production.Select(static migration => migration.Id)));

        foreach (string id in new[] { CatalogMigrationId, AppendOnlyTrailsMigrationId })
        {
            ScannedMigration migration = Production.Single(candidate => candidate.Id == id);
            migration.AssemblyName.ShouldBe("Aurora.Platform.Tenancy");
            migration.GenerationFailure.ShouldBeNull();
            migration.Commands.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void The_generated_SQL_is_what_the_provider_emits_from_operations_not_the_raw_strings_alone()
    {
        // InitialCatalog creates its tables through CreateTable(); no string in the migration says
        // CREATE TABLE. If this text is in the commands, the scan reads generated SQL and not only
        // Sql() arguments - the link a raw-string scan would have stopped short of.
        ScannedMigration catalog = Production.Single(static migration => migration.Id == CatalogMigrationId);
        string script = string.Join(Environment.NewLine, catalog.Commands.Select(static command => command.CommandText));

        script.ShouldContain("CREATE TABLE catalog.tenant (");
        script.ShouldContain("CREATE INDEX ix_tenant_state_last_activity_at ON catalog.tenant (state, last_activity_at)");
        script.ShouldContain("GRANT USAGE ON SCHEMA catalog TO aurora_app;");
    }

    [Fact]
    public void MIG2_parsed_at_least_the_measured_number_of_statements_from_each_production_migration()
    {
        foreach (ScannedMigration migration in Production)
        {
            MigrationScan scan = MigrationSafetyRule.Scan([migration]);

            ProductionStatementCounts.TryGetValue(migration.Id, out int measured).ShouldBeTrue(
                $"{migration.Id} has no statement count; MIG2 parsed {scan.StatementsParsed} statements from it. "
                + "Add an entry with that number.");
            scan.StatementsParsed.ShouldBeGreaterThanOrEqualTo(
                FloorFor(measured),
                $"MIG2 parsed {scan.StatementsParsed} statements from {migration.Id}, below its floor of "
                + $"{FloorFor(measured)} (measured {measured} when the entry was written, less a tenth). It reports "
                + "no violations because it read almost nothing, not because the SQL is clean.");
        }

        ProductionStatementCounts.Keys.ShouldBe(
            Production.Select(static migration => migration.Id),
            ignoreOrder: true,
            "a floor names a migration that is not in production");
    }

    [Fact]
    public void MIG2_reports_the_bodies_it_read_from_each_production_migration()
    {
        // The count a passing run walks, so "no violations" comes with how much was read: the two
        // DO $EF$ blocks Npgsql emits for InitialCatalog's EnsureSchema and extension, and
        // AppendOnlyTrails' one trigger-function body. Measured on B-09 rework 3.
        foreach ((string id, int bodies) in new[] { (CatalogMigrationId, 2), (AppendOnlyTrailsMigrationId, 1) })
        {
            MigrationScan scan = MigrationSafetyRule.Scan([Production.Single(m => m.Id == id)]);
            scan.BodiesRead.ShouldBe(bodies, $"{id}: parsed {scan.StatementsParsed} statements, read {scan.BodiesRead} bodies");
        }
    }

    [Fact]
    public void Every_fixture_migration_is_in_the_fixture_population()
    {
        // A fixture that fails to load would vanish from the population and its test would assert
        // over its absence; the count is what stops that.
        Fixtures.Length.ShouldBe(47, string.Join(Environment.NewLine, Fixtures.Select(static migration => migration.Id)));

        // Unique but for the one pair that exists to share an id.
        Fixtures.Select(static migration => migration.Id)
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ShouldBe([DuplicateIdTwinA.Id]);
    }

    // ---- MIG1: every migration is annotated -----------------------------------------------------

    [Fact]
    public void MIG1_every_production_migration_declares_its_category_and_reason()
    {
        RuleAssert.Holds(MigrationAnnotationRule.Check(Production), minimumSubjects: 1);
    }

    [Theory]
    [InlineData(nameof(UnannotatedMigration), "carries no [MigrationSafety]")]
    [InlineData(nameof(ExpandWithABlankReason), "blank reason")]
    [InlineData(nameof(ContractNamingNoExpand), "names no Expand")]
    [InlineData(nameof(ContractNamingAMissingExpand), "not a migration in this population")]
    [InlineData(nameof(ContractNamingADataOnlyMigration), "is DataOnly, not an Expand")]
    [InlineData(nameof(ExpandNamingAnExpand), "only a Contract names")]
    [InlineData("DuplicateIdTwin", "shares the migration id")]
    public void MIG1_fires_on_a_missing_blank_or_misdirected_annotation(string fixture, string expectedDetail)
    {
        RuleOutcome outcome = MigrationAnnotationRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, fixture, ViolationSite.Annotation);
        violation.Detail.ShouldContain(expectedDetail);
    }

    [Fact]
    public void MIG1_stays_silent_on_a_well_formed_Expand_Contract_and_DataOnly_while_counting_every_fixture()
    {
        RuleOutcome outcome = MigrationAnnotationRule.Check(Fixtures);

        outcome.SubjectsExamined.ShouldBe(Fixtures.Length);
        outcome.Violations
            .Where(static violation => IsOneOf(violation, nameof(CompliantExpand), nameof(CompliantContract), nameof(DataOnlyBackfill)))
            .ShouldBeEmpty(outcome.Describe());
        outcome.Violations.Length.ShouldBe(7, outcome.Describe());
        outcome.Violations.Count(static v => v.Detail.Contains("shares the migration id", StringComparison.Ordinal))
            .ShouldBe(1, "one violation for a duplicate id, on the migration that lost the tie");
    }

    // ---- MIG2: destructive SQL only in a Contract -------------------------------------------------

    [Fact]
    public void MIG2_no_production_migration_runs_destructive_or_unreadable_SQL_outside_a_Contract()
    {
        RuleAssert.Holds(MigrationSafetyRule.Check(Production), minimumSubjects: 1);
    }

    [Fact]
    public void MIG2_fires_on_a_DropColumn_operation_that_has_no_raw_SQL_string_to_scan()
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, nameof(ExpandThatDropsAColumn), ViolationSite.Statement);
        violation.Detail.ShouldContain("DROP COLUMN");
        violation.Detail.ShouldContain("this one is Expand");
    }

    [Fact]
    public void MIG2_fires_on_a_DROP_TABLE_inside_a_dollar_quoted_body_and_says_it_was_inside_one()
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, nameof(ExpandHidingADropInADollarQuotedBody), ViolationSite.Statement);
        violation.Detail.ShouldContain("DROP TABLE");
        violation.Detail.ShouldContain("› body ›");
    }

    [Fact]
    public void MIG2_fires_on_a_DROP_TABLE_inside_a_single_quoted_DO_body_exactly_as_inside_a_dollar_quoted_one()
    {
        // The first review's B-1: DO '…' and DO $$…$$ are one statement, and only the second was read.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, nameof(ExpandHidingADropInAQuotedBody), ViolationSite.Statement);
        violation.Detail.ShouldContain("DROP TABLE");
        violation.Detail.ShouldContain("› body ›");
    }

    [Fact]
    public void MIG2_fires_on_a_DROP_TABLE_split_across_two_adjacent_literals_of_a_DO_body()
    {
        // The second review's N-2: PostgreSQL joins the two constants into one body.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, nameof(ExpandHidingADropInAConcatenatedBody), ViolationSite.Statement);
        violation.Detail.ShouldContain("DROP TABLE");
        violation.Detail.ShouldContain("› body ›");
    }

    [Fact]
    public void MIG2_fires_on_a_DROP_TABLE_in_a_quoted_routine_body_nested_in_a_DO_block()
    {
        // The third review's N-1, through the rule and not the scanner alone: reported clean before.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, nameof(ExpandHidingADropInANestedQuotedBody), ViolationSite.Statement);
        violation.Detail.ShouldContain("DROP TABLE");
        violation.Detail.ShouldContain("statement 1 › body › statement 1 › body › statement 1");
    }

    [Theory]
    [InlineData(nameof(ExpandDroppingACheckConstraintByRawSql), "DROP CONSTRAINT")]
    [InlineData(nameof(ExpandDroppingAPrimaryKeyByOperation), "DROP CONSTRAINT")]
    [InlineData(nameof(ExpandDroppingAUniqueConstraintByOperation), "DROP CONSTRAINT")]
    [InlineData(nameof(ExpandNarrowingAVarcharByOperation), "ALTER COLUMN … TYPE")]
    [InlineData(nameof(ExpandChangingANumericByOperation), "ALTER COLUMN … TYPE")]
    [InlineData(nameof(ExpandWideningAVarcharByRawSql), "ALTER COLUMN … TYPE")]
    [InlineData(nameof(ExpandReEnablingAGuardTrigger), "ENABLE TRIGGER (tgenabled 'O'")]
    public void MIG2_fires_where_the_operation_or_its_absence_proves_no_widening(string fixture, string expectedDetail)
    {
        // ADR-0037 §3.2 and §4: the name of a constraint is never evidence; the EF operation's
        // type, the source model and the operation's old and new store types are. Raw SQL has no
        // operation, and a primary key or a unique constraint is not a check. And §2.4's overturned
        // call: a plain ENABLE TRIGGER writes 'O', a reduction from ALWAYS.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleAssert.Reports(outcome, fixture, ViolationSite.Statement).Detail.ShouldContain(expectedDetail);
    }

    [Fact]
    public void MIG2_clears_a_CHECK_dropped_through_the_typed_operation_that_the_source_model_declares_and_counts_it()
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);
        MigrationScan scan = MigrationSafetyRule.Scan(Fixtures);

        outcome.Violations.Where(static v => IsOneOf(v, nameof(ExpandDroppingACheckConstraintByOperation))).ShouldBeEmpty(outcome.Describe());

        // Four DROP CONSTRAINT statements were judged: the check by operation (cleared), the same
        // check by raw SQL, the primary key and the unique constraint (all three destructive).
        scan.DropConstraintsExamined.ShouldBe(4);
        scan.DropConstraintsAttributedToCheck.ShouldBe(1);

        ScannedMigration byOperation = Fixtures.Single(static m => m.Id == ExpandDroppingACheckConstraintByOperation.Id);
        byOperation.SourceCheckConstraints.Select(static c => (c.TableKey, c.Name))
            .ShouldBe([("sales.invoice", DeclaresInvoiceCheckInModel.ConstraintName)], "the source model is the previous fixture's target model");
    }

    [Fact]
    public void MIG2_clears_the_two_widening_shapes_through_the_typed_operation_and_counts_them()
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);
        MigrationScan scan = MigrationSafetyRule.Scan(Fixtures);

        outcome.Violations
            .Where(static v => IsOneOf(v, nameof(ExpandWideningAVarcharByOperation), nameof(ExpandWideningToTextByOperation)))
            .ShouldBeEmpty(outcome.Describe());

        // Six type changes were judged: two widenings by operation (cleared), a narrowing, a numeric
        // change, a raw-SQL widening, and ExpandThatNarrowsAColumn's TYPE (all four destructive).
        scan.TypeChangesExamined.ShouldBe(6);
        scan.TypeChangesWidening.ShouldBe(2);
    }

    [Theory]
    [InlineData("character varying(50)", "character varying(100)", true)]
    [InlineData("varchar(50)", "varchar(100)", true)]
    [InlineData("character varying(50)", "text", true)]
    [InlineData("character varying", "text", true)]
    [InlineData("character varying(100)", "character varying(50)", false)]
    [InlineData("character varying(50)", "character varying(50)", false)]
    [InlineData("character varying", "character varying(100)", false)]
    [InlineData("text", "character varying(100)", false)]
    [InlineData("numeric(12,2)", "numeric(18,2)", false)]
    [InlineData(null, "character varying(100)", false)]
    [InlineData("character varying(50)", null, false)]
    public void The_widening_allowlist_is_exactly_ADR_0037_s_two_shapes(string? oldType, string? newType, bool widening)
    {
        MigrationSafetyRule.IsWideningTextType(oldType, newType).ShouldBe(widening);
    }

    [Theory]
    [InlineData(nameof(ExpandCreatingAGuardBornWeak), "trg_invoice_guard on sales.invoice is created and never enabled ALWAYS")]
    [InlineData(nameof(ContractRepointingGuardWithoutEnableAlways), "trg_invoice_append_only on sales.invoice is created and never enabled ALWAYS")]
    public void MIG2_fires_on_a_guard_created_and_not_enabled_ALWAYS_whatever_the_category(string fixture, string expectedDetail)
    {
        // ADR-0037 §2.6: CREATE TRIGGER produces tgenabled = 'O'; a guard born there is weaker than
        // the ENABLE ALWAYS one B-19 paid for, and no suppression statement betrays it - a Contract
        // re-creating a guard is held to it as much as an Expand creating one.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleAssert.Reports(outcome, fixture, ViolationSite.Statement).Detail.ShouldContain(expectedDetail);
    }

    [Fact]
    public void MIG2_accepts_the_v2_path_for_changing_a_guard_function_and_counts_the_triggers_it_saw()
    {
        // ADR-0037 §2.6: Expand creates _v2; Contract drops the trigger, re-creates it on _v2,
        // re-applies ENABLE ALWAYS, drops _v1. Every drop is permitted and the re-created guard is
        // enabled ALWAYS, so neither migration is reported.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        outcome.Violations
            .Where(static v => IsOneOf(v, nameof(ExpandCreatingGuardV2), nameof(ContractRepointingGuardToV2)))
            .ShouldBeEmpty(outcome.Describe());

        MigrationScan contract = MigrationSafetyRule.Scan([.. Fixtures.Where(static m => m.Id == ContractRepointingGuardToV2.Id)]);
        contract.TriggersCreated.ShouldBe(1);
        contract.DestructiveStatementsPermittedInContracts.ShouldBe(2, "DROP TRIGGER and DROP FUNCTION");
        contract.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void MIG2_fires_on_a_guard_trigger_switched_to_replica_mode()
    {
        // The second review's N-1: ENABLE REPLICA is DISABLE by another spelling for a guard.
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(ExpandThatDisarmsAGuardTrigger), ViolationSite.Statement)
            .Detail.ShouldContain("ENABLE REPLICA TRIGGER");
    }

    [Fact]
    public void MIG2_fires_exactly_once_on_a_camouflaged_DROP_TABLE_and_not_on_the_literal_or_the_comment()
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation[] violations = [.. outcome.Violations.Where(static v => IsOneOf(v, nameof(ExpandWithACamouflagedDropTable)))];

        violations.Length.ShouldBe(1, outcome.Describe());
        violations[0].Detail.ShouldContain("DROP TABLE");
        violations[0].Detail.ShouldContain("statement 2");
    }

    [Theory]
    [InlineData(nameof(ExpandThatRenamesAColumn), "RENAME")]
    [InlineData(nameof(ExpandWithTwoStatementsOnOneLine), "TRUNCATE")]
    [InlineData(nameof(ExpandAddingANotNullColumnWithoutADefault), "ADD COLUMN … NOT NULL without a DEFAULT")]
    [InlineData(nameof(ExpandThatNarrowsAColumn), "ALTER COLUMN … TYPE")]
    [InlineData(nameof(ExpandThatNarrowsAColumn), "ALTER COLUMN … SET NOT NULL")]
    [InlineData(nameof(UnannotatedMigration), "this one is unannotated")]
    public void MIG2_fires_on_each_narrowing_and_treats_an_unannotated_migration_as_the_strictest_category(
        string fixture, string expectedDetail)
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        outcome.Violations
            .Where(violation => IsOneOf(violation, fixture) && violation.Detail.Contains(expectedDetail, StringComparison.Ordinal))
            .ShouldNotBeEmpty(outcome.Describe());
    }

    [Theory]
    [InlineData(nameof(ExpandThatRunsDynamicSql), "EXECUTE runs SQL built at run time")]
    [InlineData(nameof(ExpandThatCallsAUserFunction), "calls sales.rebuild_everything(…)")]
    [InlineData(nameof(ExpandWithAnUnterminatedLiteral), "never closed")]
    [InlineData(nameof(ExpandWhoseUpThrows), "could not be generated")]
    [InlineData(nameof(ContractThatRunsDynamicSql), "EXECUTE runs SQL built at run time")]
    [InlineData(nameof(DataOnlyThatCallsAUserFunction), "calls sales.rebuild_everything(…)")]
    public void MIG2_reports_what_it_cannot_read_as_a_violation_never_as_clean_whatever_the_category(
        string fixture, string expectedDetail)
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, fixture, ViolationSite.Statement);
        violation.Detail.ShouldContain(expectedDetail);
    }

    [Fact]
    public void MIG2_does_not_count_an_unreadable_Contract_as_a_permitted_one()
    {
        // The first review's M-2: exempting Contract from the unscannable branch left every test
        // green. An unreadable Contract is a violation, and its EXECUTE is not a permitted
        // destructive statement - otherwise it is indistinguishable from a permitted one.
        MigrationScan scan = MigrationSafetyRule.Scan(
            [.. Fixtures.Where(static migration => migration.Id == ContractThatRunsDynamicSql.Id)]);

        scan.Violations.ShouldHaveSingleItem().Detail.ShouldContain("EXECUTE runs SQL built at run time");
        scan.DestructiveStatementsPermittedInContracts.ShouldBe(0);
    }

    [Theory]
    [InlineData(nameof(DataOnlyThatCreatesATable), "CREATE TABLE")]
    [InlineData(nameof(DataOnlyWithAProceduralBody), "DO $…$")]
    [InlineData(nameof(DataOnlyWithABodyUnderAnAllowedHead), "INSERT INTO sales.note")]
    public void MIG2_fires_on_DDL_or_a_procedural_body_in_a_DataOnly_migration(string fixture, string expectedExcerpt)
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, fixture, ViolationSite.Statement);
        violation.Detail.ShouldContain("not a plain data statement");
        violation.Detail.ShouldContain(expectedExcerpt);
    }

    [Fact]
    public void MIG2_stays_silent_on_the_compliant_fixtures_having_seen_their_SQL()
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);
        MigrationScan scan = MigrationSafetyRule.Scan(Fixtures);

        outcome.SubjectsExamined.ShouldBe(Fixtures.Length);
        outcome.Violations
            .Where(static v => IsOneOf(
                v, nameof(CompliantExpand), nameof(CompliantContract), nameof(DataOnlyBackfill), nameof(ExpandWithAProceduralLookAlikeInData),
                nameof(ExpandDeclaringACheckConstraintInItsModel)))
            .ShouldBeEmpty(outcome.Describe());

        // Silence is only meaningful if the SQL was read. The compliant Expand alone is twelve
        // statements plus one function body, and the Contract's three destructive statements were
        // seen and permitted rather than skipped.
        scan.StatementsParsed.ShouldBeGreaterThanOrEqualTo(30, scan.ToString());

        MigrationScan contractAlone = MigrationSafetyRule.Scan(
            [.. Fixtures.Where(static migration => migration.Id == CompliantContract.Id)]);
        contractAlone.DestructiveStatementsPermittedInContracts.ShouldBe(
            3, "DROP COLUMN, DROP TABLE and TRUNCATE in CompliantContract were seen and permitted");
    }

    private static bool IsOneOf(RuleViolation violation, params string[] fixtureNames) =>
        fixtureNames.Any(name => violation.Subject.EndsWith("." + name, StringComparison.Ordinal));
}
