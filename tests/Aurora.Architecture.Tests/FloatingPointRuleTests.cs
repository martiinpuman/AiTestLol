using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Rules;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// Fitness rule F1 (<c>testing-strategy.md</c> §5.4): no floating point anywhere in production
/// code. Money is exact in base ten or it is wrong (ADR-0021).
/// </summary>
[Trait("Category", "Architecture")]
public sealed class FloatingPointRuleTests
{
    [Fact]
    public void No_production_code_mentions_a_floating_point_type()
    {
        // The floor is a collapse detector, not an inventory: it catches the population falling to
        // nothing (a project gone from src/, a build that produced no assemblies), which is the way
        // this test would otherwise report PASS having inspected nothing. The exact population is
        // asserted once, in ProductionPopulationTests, rather than in every rule test.
        RuleAssert.Holds(FloatingPointRule.Check(SolutionLayout.ProductionTypes), minimumSubjects: 10);
    }

    [Fact]
    public void The_rule_fires_on_a_double_local_inside_a_method_with_a_decimal_signature()
    {
        RuleOutcome outcome = FloatingPointRule.Check(
            FixtureAssembly.Violation(nameof(Fixtures.Violations.MoneyMathHiddenInsideAMethodBody)));

        // The site matters as much as the hit. Reported at Local or MemberReference means the body
        // mechanism fired; this method's signature is (decimal, int) -> decimal, so a rule reading
        // signatures alone - the B-03 m-1 defect - would have reported nothing at all.
        RuleAssert.Reports(outcome, "DiscountedTotal", ViolationSite.Local, ViolationSite.MemberReference);
    }

    [Fact]
    public void The_rule_fires_on_a_double_cast_with_no_local_at_all()
    {
        RuleOutcome outcome = FloatingPointRule.Check(
            FixtureAssembly.Violation(nameof(Fixtures.Violations.MoneyMathHiddenInsideAMethodBody)));

        RuleAssert.Reports(outcome, "HalfOf", ViolationSite.Local, ViolationSite.Instruction, ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, "SquareRootOf", ViolationSite.MemberReference, ViolationSite.Instruction);
    }

    [Fact]
    public void The_rule_fires_on_floating_point_in_a_field_a_property_and_a_signature()
    {
        RuleOutcome outcome = FloatingPointRule.Check(
            FixtureAssembly.Violation(nameof(Fixtures.Violations.RateCarriedAsDouble)));

        RuleAssert.Reports(outcome, "_rate", ViolationSite.Field);
        RuleAssert.Reports(outcome, "Rate", ViolationSite.Property);
        RuleAssert.Reports(outcome, ".ctor", ViolationSite.Signature);
        RuleAssert.Reports(outcome, "AsSingle", ViolationSite.Signature);
    }

    [Fact]
    public void The_rule_exempts_no_assembly()
    {
        // An exemption is a hole in a solution-wide rule. Keeping the list empty here means adding
        // one is a diff in FloatingPointRule.cs that a reviewer sees, and this test turns red.
        FloatingPointRule.ExemptAssemblies.ShouldBeEmpty();
    }
}
