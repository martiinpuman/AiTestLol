using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Rules;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// Which rules are biting today, which are waiting for the types they govern, and the mechanism
/// that stops the second from being mistaken for the first.
/// </summary>
/// <remarks>
/// <para>
/// <b>An inert rule and a vacuous rule look identical from the outside.</b> Both report no
/// violations; both are green; both are trusted. The difference is that an inert rule has nothing
/// to bite on <i>yet</i> and a vacuous one never will, and the only way to tell them apart is to
/// write down which is which and check the writing.
/// </para>
/// <para>
/// So each rule below is recorded as <b>live</b> - a violation is expressible in today's code, and
/// the rule examined a real population - or <b>inert</b>, with the type it governs named and a
/// fixture the rule is run over to show it still fires. The inert rows also assert the
/// <i>absence</i> of that type. The day B-05 or B-06 introduces it, the assertion fails and whoever
/// added it must move the rule to the live list with a floor. Inertness expires loudly rather than
/// quietly persisting.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class RuleInventoryTests
{
    /// <summary>A rule that can be violated by code that exists today, and the population floor it must meet.</summary>
    private static readonly ImmutableArray<(string Id, string Rule, int Floor, Func<RuleOutcome> Run)> Live =
    [
        (FloatingPointRule.Id, FloatingPointRule.Name, 10,
            static () => FloatingPointRule.Check(SolutionLayout.ProductionTypes)),
        (AmbientTimeRule.Id, AmbientTimeRule.Name, 10,
            static () => AmbientTimeRule.Check(SolutionLayout.ProductionTypes)),
        (HttpContextAccessorRule.Id, HttpContextAccessorRule.Name, 10,
            static () => HttpContextAccessorRule.Check(SolutionLayout.ProductionTypes)),
        (TenantDbContextFactoryRule.Id, TenantDbContextFactoryRule.Name, 10,
            static () => TenantDbContextFactoryRule.Check(SolutionLayout.ProductionTypes)),
        (TenantDatabaseHandleRule.Id, TenantDatabaseHandleRule.Name, 10,
            static () => TenantDatabaseHandleRule.Check(SolutionLayout.ProductionTypes)),
        (TenantScopeSingletonRule.Id, TenantScopeSingletonRule.Name, 10,
            static () => TenantScopeSingletonRule.Check(TypeIndex.Of(SolutionLayout.ProductionTypes))),
        (DomainPurityRule.Id, DomainPurityRule.Name, 1,
            static () => DomainPurityRule.Check(SolutionLayout.ProductionProjects, SolutionLayout.ProductionAssemblies)),
        (ProjectLayeringRule.Id, ProjectLayeringRule.Name, 3,
            static () => ProjectLayeringRule.Check(SolutionLayout.ProductionProjects)),
    ];

    /// <summary>
    /// A rule whose subject does not exist in production yet, with the task that brings it and a
    /// fixture that violates it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// T3 and T5 appear in <see cref="Live"/> rather than here: they scan every production type
    /// today and would report a violation the moment one appeared, so the rule itself is biting.
    /// What does not exist yet is the type that could violate them, and the assertions below cover
    /// that separately.
    /// </para>
    /// <para>
    /// <c>FireAgainstFixture</c> runs the rule over a deliberately-violating fixture - the same
    /// fixtures <c>TenancyRuleTests</c> and <c>LayeringRuleTests</c> use - and
    /// <see cref="Each_inert_rule_still_fires_against_its_fixture"/> requires a violation back.
    /// </para>
    /// </remarks>
    private static readonly ImmutableArray<(
        string Id, string Rule, string Subject, string Arrives, Func<RuleOutcome> FireAgainstFixture)> Inert =
    [
        (TenantDbContextConstructorRule.Id, TenantDbContextConstructorRule.Name,
            "a tenant DbContext", "B-06 (B-05 brings CatalogDbContext, which is exempt)",
            static () => TenantDbContextConstructorRule.Check(TypeIndex.Of(FixtureAssembly.AllViolations))),
        (TenantDbContextRegistrationRule.Id, TenantDbContextRegistrationRule.Name,
            "an AddDbContext* call naming a tenant context", "B-06",
            static () => TenantDbContextRegistrationRule.Check(TypeIndex.Of(FixtureAssembly.AllViolations))),
        (ModuleDependencyRule.Id, ModuleDependencyRule.Name,
            "two business modules to reference each other", "the first module after B-15",
            static () =>
            {
                // modules.md §6 marks Tax -> Ledger 'E': events only, so this one edge violates M1.
                using var tree = new FixtureProjectTree();
                tree.Add("Aurora.Modules.Tax.Application", projectReferences: ["Aurora.Modules.Ledger.Contracts"]);
                return ModuleDependencyRule.Check(tree.All);
            }),
    ];

    [Fact]
    public void Every_rule_in_the_project_is_recorded_as_live_or_inert()
    {
        // Reflection over the rule classes, so a rule added without an inventory row fails here
        // rather than joining the set of rules nobody has said anything about.
        ImmutableHashSet<string> declared =
        [
            .. typeof(RuleAssert).Assembly.GetTypes()
                .Where(static type => type.Namespace == typeof(RuleAssert).Namespace)
                .Select(static type => type.GetField("Id", BindingFlags.Public | BindingFlags.Static))
                .Where(static field => field is { IsLiteral: true, FieldType.FullName: "System.String" })
                .Select(static field => (string)field!.GetRawConstantValue()!),
        ];

        ImmutableHashSet<string> recorded =
            [.. Live.Select(static row => row.Id), .. Inert.Select(static row => row.Id)];

        declared.Except(recorded).ShouldBeEmpty(
            "every rule class declaring an Id must have a row in this inventory, so that 'no "
            + "violations' is always accompanied by a statement of what was measured");
        recorded.Except(declared).ShouldBeEmpty("the inventory names a rule that no longer exists");
    }

    [Fact]
    public void Every_live_rule_examined_a_real_population_and_found_nothing()
    {
        List<string> report = [];

        foreach ((string id, string rule, int floor, Func<RuleOutcome> run) in Live)
        {
            RuleOutcome outcome = run();
            report.Add(FormattableString.Invariant(
                $"{id,-6} {outcome.SubjectsExamined,5} {outcome.SubjectKind} (floor {floor}) - {rule}"));

            outcome.Violations.ShouldBeEmpty(outcome.Describe());
            outcome.SubjectsExamined.ShouldBeGreaterThanOrEqualTo(
                floor,
                $"rule {id} is recorded as live but examined {outcome.SubjectsExamined} "
                + $"{outcome.SubjectKind}. It is reporting no violations because it looked at almost "
                + "nothing. Fix the population, or move the rule to the inert list and say why.");
        }

        report.Count.ShouldBe(Live.Length, string.Join(Environment.NewLine, report));
    }

    [Fact]
    public void No_tenant_DbContext_exists_yet_which_is_why_T1_and_T2_are_inert()
    {
        // Tenant contexts, not every DbContext: B-05's CatalogDbContext is exempt by design
        // (ADR-0003 rule 3), so its arrival must not fail this test. B-06.3's first tenant context
        // must.
        TypeIndex production = TypeIndex.Of(SolutionLayout.ProductionTypes);

        TenancyNames.TenantContextsIn(production)
            .Select(static type => type.FullName)
            .ShouldBeEmpty(
                "a tenant DbContext now exists in production, so T1 and T2 are no longer inert. Move "
                + "their rows from Inert to Live in this file with a floor, and raise the floors in "
                + "TenancyRuleTests from 0.");
    }

    [Fact]
    public void No_tenant_access_type_exists_yet_which_is_why_T5_and_T6_have_nothing_to_find()
    {
        SolutionLayout.ProductionTypes
            .Where(static type =>
                TenancyNames.TenantAccessSimpleNames.Contains(TypeIndex.SimpleNameOf(type.FullName)))
            .Select(static type => type.FullName)
            .ShouldBeEmpty(
                "TenantScope, TenantAccess or TenantDatabaseHandle now exists in production (B-06, "
                + "ADR-0027). T5 and T6 are already live and will now bite on the real types; delete "
                + "the stand-ins in Fixtures/Violations/TenancyViolations.cs, point the fixtures at "
                + "the real ones, and update this test.");
    }

    [Fact]
    public void No_tenant_DbContext_factory_exists_yet_which_is_why_T3_has_nothing_to_find()
    {
        SolutionLayout.ProductionTypes
            .Where(static type =>
                TenancyNames.TenantContextFactorySimpleNames.Contains(TypeIndex.SimpleNameOf(type.FullName)))
            .Select(static type => type.FullName)
            .ShouldBeEmpty(
                "a tenant DbContext factory interface now exists in production (B-06). T3 is live; "
                + "delete the stand-in in Fixtures/Violations/TenancyViolations.cs, point the "
                + "fixtures at the real interface, and update this test.");
    }

    [Fact]
    public void Fewer_than_two_business_modules_exist_which_is_why_M1_is_inert()
    {
        ImmutableArray<ProjectFile> modules =
        [
            .. SolutionLayout.ProductionProjects
                .Where(static project => ProjectIdentity.Of(project).Kind == ProjectKind.Module),
        ];

        modules.Length.ShouldBeLessThan(
            2,
            "two or more module projects now exist, so M1 has edges it could check. Move its row "
            + "from Inert to Live with a floor.");
    }

    [Fact]
    public void Each_inert_rule_still_fires_against_its_fixture()
    {
        // The point of recording a rule as inert is that it is asleep, not broken, and the only way
        // to tell those apart is to wake it. Each row carries a fixture that violates its rule; this
        // test runs the rule over that fixture and requires a violation back. A rule that stays
        // silent here is not inert, it is vacuous, and its row is what would have hidden that.
        foreach ((string id, string rule, string subject, string arrives, Func<RuleOutcome> fire) in Inert)
        {
            id.ShouldNotBeNullOrWhiteSpace();
            rule.ShouldNotBeNullOrWhiteSpace();
            subject.ShouldNotBeNullOrWhiteSpace($"rule {id} must say what it is waiting for");
            arrives.ShouldNotBeNullOrWhiteSpace($"rule {id} must say which task brings its subject");

            RuleOutcome outcome = fire();

            outcome.RuleId.ShouldBe(id, "the row's fixture must be run through the row's own rule");
            outcome.SubjectsExamined.ShouldBeGreaterThan(
                0,
                $"rule {id} examined nothing in its fixture, so it could not have fired: {outcome.Describe()}");
            outcome.Violations.ShouldNotBeEmpty(
                $"rule {id} is recorded as inert but did not fire on its own deliberately-violating "
                + $"fixture. It is not asleep, it is broken: {outcome.Describe()}");
        }

        Inert.Length.ShouldBe(3, "three rules are inert today: T1, T2 and M1");
    }
}
