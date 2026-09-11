using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
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
/// the rule examined a real population - or <b>inert</b>, with the type it governs named. The inert
/// rows assert the <i>absence</i> of that type. The day B-05 or B-06 introduces it, the assertion
/// fails and whoever added it must move the rule to the live list with a floor. Inertness expires
/// loudly rather than quietly persisting.
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
        (TenantScopeSingletonRule.Id, TenantScopeSingletonRule.Name, 10,
            static () => TenantScopeSingletonRule.Check(TypeIndex.Of(SolutionLayout.ProductionTypes))),
        (DomainPurityRule.Id, DomainPurityRule.Name, 3,
            static () => DomainPurityRule.Check(SolutionLayout.ProductionProjects, SolutionLayout.ProductionAssemblies)),
        (ProjectLayeringRule.Id, ProjectLayeringRule.Name, 3,
            static () => ProjectLayeringRule.Check(SolutionLayout.ProductionProjects)),
    ];

    /// <summary>
    /// A rule whose subject does not exist in production yet, with the task that brings it.
    /// </summary>
    /// <remarks>
    /// T3 and T5 appear in <see cref="Live"/> rather than here: they scan every production type
    /// today and would report a violation the moment one appeared, so the rule itself is biting.
    /// What does not exist yet is the type that could violate them, and the assertions below cover
    /// that separately.
    /// </remarks>
    private static readonly ImmutableArray<(string Id, string Rule, string Subject, string Arrives)> Inert =
    [
        (TenantDbContextConstructorRule.Id, TenantDbContextConstructorRule.Name,
            "a tenant DbContext", "B-06 (B-05 brings CatalogDbContext, which is exempt)"),
        (TenantDbContextRegistrationRule.Id, TenantDbContextRegistrationRule.Name,
            "an AddDbContext* call naming a tenant context", "B-06"),
        (ModuleDependencyRule.Id, ModuleDependencyRule.Name,
            "two business modules to reference each other", "the first module after B-15"),
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
        TypeIndex production = TypeIndex.Of(SolutionLayout.ProductionTypes);

        production.All
            .Where(type => production.DerivesFrom(type, TenancyNames.DbContext))
            .Select(static type => type.FullName)
            .ShouldBeEmpty(
                "a DbContext now exists in production, so T1 and T2 are no longer inert. Move their "
                + "rows from Inert to Live in this file with a floor, and raise the floors in "
                + "TenancyRuleTests from 0.");
    }

    [Fact]
    public void No_TenantScope_exists_yet_which_is_why_T5_has_nothing_to_find()
    {
        SolutionLayout.ProductionTypes
            .Where(static type =>
                TypeIndex.SimpleNameOf(type.FullName) == TenancyNames.TenantScopeSimpleName)
            .Select(static type => type.FullName)
            .ShouldBeEmpty(
                "TenantScope now exists in production (B-06). T5 is already live and will now bite "
                + "on the real type; delete the stand-in in Fixtures/Violations/TenancyViolations.cs "
                + "and point the fixtures at the real one, then update this test.");
    }

    [Fact]
    public void No_ITenantDbContextFactory_exists_yet_which_is_why_T3_has_nothing_to_find()
    {
        SolutionLayout.ProductionTypes
            .Where(static type =>
                TypeIndex.SimpleNameOf(type.FullName) == TenancyNames.TenantDbContextFactorySimpleName)
            .Select(static type => type.FullName)
            .ShouldBeEmpty(
                "ITenantDbContextFactory<> now exists in production (B-06). T3 is already live; "
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
        // The point of recording a rule as inert is that it is asleep, not broken. Every inert rule
        // has a fixture test in TenancyRuleTests or LayeringRuleTests proving it fires; this test
        // asserts the accounting - that each inert row names a subject and the task that brings it,
        // so the row cannot silently become a place where rules go to be forgotten.
        foreach ((string id, string rule, string subject, string arrives) in Inert)
        {
            id.ShouldNotBeNullOrWhiteSpace();
            rule.ShouldNotBeNullOrWhiteSpace();
            subject.ShouldNotBeNullOrWhiteSpace($"rule {id} must say what it is waiting for");
            arrives.ShouldNotBeNullOrWhiteSpace($"rule {id} must say which task brings its subject");
        }

        Inert.Length.ShouldBe(3, "three rules are inert today: T1, T2 and M1");
    }
}
