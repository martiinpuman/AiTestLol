using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>Where in the metadata a violation was found. Which mechanism saw it, in one word.</summary>
internal enum ViolationSite
{
    /// <summary>A field's declared type.</summary>
    Field,

    /// <summary>A property's declared type.</summary>
    Property,

    /// <summary>A parameter or return type in a method signature.</summary>
    Signature,

    /// <summary>A local variable's declared type, inside a method body.</summary>
    Local,

    /// <summary>An IL instruction inside a method body.</summary>
    Instruction,

    /// <summary>A member or type an IL instruction names, inside a method body.</summary>
    MemberReference,

    /// <summary>A type's base type or implemented interface.</summary>
    TypeShape,

    /// <summary>A reference declared in a project file or its lock file.</summary>
    Declaration,

    /// <summary>The shape of the population itself: an identity a rule depends on is absent from it.</summary>
    Population,
}

/// <summary>One violation: what broke the rule, where the mechanism saw it, and the detail.</summary>
internal sealed record RuleViolation(string Subject, ViolationSite Site, string Detail)
{
    public override string ToString() =>
        FormattableString.Invariant($"{Subject} [{Site}] {Detail}");
}

/// <summary>
/// The result of running one rule over one population: what it examined, how much of it, and what
/// it found.
/// </summary>
/// <remarks>
/// <see cref="SubjectsExamined"/> is not decoration. B-02 shipped a gate stage that printed PASS
/// having executed zero tests; a rule that reports "no violations" without saying how many
/// subjects it looked at has the same defect. Every rule test prints this count, and
/// <c>RuleInventoryTests</c> asserts the expected count for each rule so that a population
/// collapsing to zero fails instead of passing.
/// </remarks>
internal sealed record RuleOutcome(
    string RuleId,
    string Rule,
    string SubjectKind,
    int SubjectsExamined,
    ImmutableArray<RuleViolation> Violations)
{
    public static RuleOutcome From(
        string ruleId,
        string rule,
        string subjectKind,
        int subjectsExamined,
        IEnumerable<RuleViolation> violations) =>
        new(ruleId, rule, subjectKind, subjectsExamined, [.. violations]);

    public string Describe() =>
        FormattableString.Invariant(
            $"{RuleId} ({Rule}): examined {SubjectsExamined} {SubjectKind}, found {Violations.Length} violation(s).")
        + (Violations.IsEmpty
            ? string.Empty
            : Environment.NewLine + "  " + string.Join(
                Environment.NewLine + "  ",
                Violations.Select(static violation => violation.ToString())));

    public override string ToString() => Describe();
}
