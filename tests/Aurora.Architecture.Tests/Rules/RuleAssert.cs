using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>Assertions shared by every rule test, so every failure reads the same way.</summary>
internal static class RuleAssert
{
    /// <summary>
    /// The rule found nothing, over a population of at least <paramref name="minimumSubjects"/>.
    /// </summary>
    /// <remarks>
    /// The floor is mandatory, not optional: "no violations" over an empty population is the
    /// vacuous pass this rule set exists to make impossible. Pass <c>0</c> only for a rule that is
    /// currently inert, and record it in <c>RuleInventoryTests</c> so the inertness is a written
    /// fact rather than an accident.
    /// </remarks>
    public static void Holds(RuleOutcome outcome, int minimumSubjects)
    {
        outcome.Violations.ShouldBeEmpty(outcome.Describe());

        outcome.SubjectsExamined.ShouldBeGreaterThanOrEqualTo(
            minimumSubjects,
            FormattableString.Invariant(
                $"Rule {outcome.RuleId} ({outcome.Rule}) examined {outcome.SubjectsExamined} ")
            + FormattableString.Invariant(
                $"{outcome.SubjectKind}, fewer than the {minimumSubjects} it must see. It reported ")
            + "no violations because it looked at almost nothing, not because the code is clean.");
    }

    /// <summary>The rule found a violation on <paramref name="subject"/> at one of the given sites.</summary>
    public static RuleViolation Reports(RuleOutcome outcome, string subject, params ViolationSite[] sites)
    {
        List<RuleViolation> matches =
        [
            .. outcome.Violations.Where(violation =>
                violation.Subject.Contains(subject, StringComparison.Ordinal) && sites.Contains(violation.Site)),
        ];

        matches.ShouldNotBeEmpty(
            $"Rule {outcome.RuleId} ({outcome.Rule}) did not report '{subject}' at "
            + $"[{string.Join(", ", sites)}]. A rule that does not fire on its own deliberately-"
            + $"violating fixture is not a rule. What it did report:{Environment.NewLine}{outcome.Describe()}");

        return matches[0];
    }
}
