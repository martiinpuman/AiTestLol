using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Fixtures.Violations;
using Aurora.Architecture.Tests.Rules;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// Fitness rule S3 (<c>testing-strategy.md</c> §5.6): domain and application code takes time as a
/// <c>TimeProvider</c>, never from an ambient clock.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class AmbientTimeRuleTests
{
    [Fact]
    public void No_domain_or_application_code_reads_the_ambient_clock()
    {
        RuleAssert.Holds(AmbientTimeRule.Check(SolutionLayout.ProductionTypes), minimumSubjects: 10);
    }

    [Fact]
    public void The_rule_fires_on_UtcNow_on_Now_and_on_Today()
    {
        RuleOutcome outcome = AmbientTimeRule.CheckTypes(FixtureAssembly.AllViolations);

        RuleAssert.Reports(
            outcome,
            nameof(PeriodCloseReadingTheAmbientClock.IsPastCutOff),
            ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, nameof(PeriodCloseReadingTheAmbientClock.Stamp), ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, nameof(PeriodCloseReadingTheAmbientClock.Today), ViolationSite.MemberReference);
    }

    [Fact]
    public void The_rule_stays_silent_on_time_taken_as_a_TimeProvider_parameter()
    {
        RuleOutcome outcome = AmbientTimeRule.CheckTypes(FixtureAssembly.AllViolations);

        outcome.Violations
            .Where(static violation => violation.Subject.Contains(nameof(PeriodCloseTakingTimeAsAParameter)))
            .ShouldBeEmpty(outcome.Describe());
    }

    [Theory]
    [InlineData("Aurora.SharedKernel", true)]
    [InlineData("Aurora.Sales.Domain", true)]
    [InlineData("Aurora.Sales.Application", true)]
    [InlineData("Aurora.Sales.Contracts", false)]
    [InlineData("Aurora.Sales.Infrastructure", false)]
    [InlineData("Aurora.Web", false)]
    [InlineData("Aurora.Sales.Domain.Tests", false)]
    public void The_scope_is_the_kernel_and_every_Domain_and_Application_assembly(string assembly, bool inScope)
    {
        // The scope predicate is tested separately from the check, because a rule is the
        // composition of the two and a wrong scope makes a correct check report nothing.
        AmbientTimeRule.IsInScope(assembly).ShouldBe(inScope);
    }
}
