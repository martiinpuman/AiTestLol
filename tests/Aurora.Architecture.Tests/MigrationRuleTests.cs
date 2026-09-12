using System;
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

    /// <summary>
    /// The number of SQL statements MIG2 parsed over production on the branch that added it:
    /// 27 for InitialCatalog (one schema, one extension, five tables, eight indexes, one exclusion
    /// constraint, eight grants and one default privilege; two of them inside no body). Rounded
    /// down to the nearest ten, the same convention as verify.sh's stage-6 floor.
    /// </summary>
    private const int ProductionStatementFloor = 20;

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
    public void The_production_population_holds_the_catalog_migration_with_its_SQL_generated()
    {
        ScannedMigration catalog = Production.ShouldHaveSingleItem(
            "one production migration exists on this branch; when a second lands, raise the floors in "
            + "RuleInventoryTests and here deliberately");

        catalog.Id.ShouldBe(CatalogMigrationId);
        catalog.AssemblyName.ShouldBe("Aurora.Platform.Tenancy");
        catalog.GenerationFailure.ShouldBeNull();
        catalog.Commands.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_generated_SQL_is_what_the_provider_emits_from_operations_not_the_raw_strings_alone()
    {
        // InitialCatalog creates its tables through CreateTable(); no string in the migration says
        // CREATE TABLE. If this text is in the commands, the scan reads generated SQL and not only
        // Sql() arguments - the link a raw-string scan would have stopped short of.
        string script = string.Join(Environment.NewLine, Production.Single().Commands.Select(static command => command.CommandText));

        script.ShouldContain("CREATE TABLE catalog.tenant (");
        script.ShouldContain("CREATE INDEX ix_tenant_state_last_activity_at ON catalog.tenant (state, last_activity_at)");
        script.ShouldContain("GRANT USAGE ON SCHEMA catalog TO aurora_app;");
    }

    [Fact]
    public void MIG2_parsed_at_least_the_measured_number_of_production_statements()
    {
        MigrationScan scan = MigrationSafetyRule.Scan(Production);

        scan.StatementsParsed.ShouldBeGreaterThanOrEqualTo(
            ProductionStatementFloor,
            $"MIG2 parsed {scan.StatementsParsed} statements over {scan.Migrations} production migration(s). It "
            + "reports no violations because it read almost nothing, not because the SQL is clean.");
    }

    [Fact]
    public void Every_fixture_migration_is_in_the_fixture_population()
    {
        // A fixture that fails to load would vanish from the population and its test would assert
        // over its absence; the count is what stops that.
        Fixtures.Length.ShouldBe(22, string.Join(Environment.NewLine, Fixtures.Select(static migration => migration.Id)));
        Fixtures.Select(static migration => migration.Id).ShouldBeUnique();
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
        outcome.Violations.Length.ShouldBe(6, outcome.Describe());
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
    public void MIG2_reports_what_it_cannot_read_as_a_violation_never_as_clean(string fixture, string expectedDetail)
    {
        RuleOutcome outcome = MigrationSafetyRule.Check(Fixtures);

        RuleViolation violation = RuleAssert.Reports(outcome, fixture, ViolationSite.Statement);
        violation.Detail.ShouldContain(expectedDetail);
    }

    [Theory]
    [InlineData(nameof(DataOnlyThatCreatesATable), "CREATE TABLE")]
    [InlineData(nameof(DataOnlyWithAProceduralBody), "DO $…$")]
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
            .Where(static v => IsOneOf(v, nameof(CompliantExpand), nameof(CompliantContract), nameof(DataOnlyBackfill)))
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
