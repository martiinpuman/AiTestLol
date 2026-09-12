using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>S3</b> - domain and application code reads no ambient clock. Time is
/// <see cref="TimeProvider"/>, injected (<c>modules.md</c> §3, <c>testing-strategy.md</c> §5.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>What the mechanism inspects:</b> every <c>call</c> in every method body in scope, matched
/// against the clock properties by declaring type and member name. A property read compiles to a
/// call to its getter, so <c>DateTime.UtcNow</c> is a <c>call System.DateTime::get_UtcNow</c> and
/// is visible whether it appears in an expression, a field initialiser, a lambda or a local
/// function.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a clock read performed by another assembly on this code's behalf -
/// including one in an assembly outside the population - and a read reached by reflection. The
/// first is covered by scoping the rule to assemblies rather than types; the second is not
/// detectable in IL and is left to review.
/// </para>
/// <para>
/// ADR-0020 expected this rule to need Roslyn ("reflection cannot see them"). Over compiled IL it
/// does not: the getter call is in the method body. Reading IL also means a clock read introduced
/// by a source generator is caught, which a source scan would miss.
/// </para>
/// </remarks>
internal static class AmbientTimeRule
{
    public const string Id = "S3";

    public const string Name = "No ambient clock read in domain or application code";

    /// <summary>
    /// The banned reads, as declaring type and getter name.
    /// </summary>
    /// <remarks>
    /// <c>testing-strategy.md</c> §5.6 names <c>DateTime.Now</c>, <c>DateTime.UtcNow</c> and
    /// <c>DateTimeOffset.UtcNow</c>. <c>DateTime.Today</c> and <c>DateTimeOffset.Now</c> are the
    /// same defect wearing a different name - an untestable answer that changes between two runs -
    /// so they are banned with them.
    /// </remarks>
    public static readonly ImmutableArray<(string DeclaringType, string Getter)> BannedReads =
    [
        ("System.DateTime", "get_Now"),
        ("System.DateTime", "get_UtcNow"),
        ("System.DateTime", "get_Today"),
        ("System.DateTimeOffset", "get_Now"),
        ("System.DateTimeOffset", "get_UtcNow"),
    ];

    /// <summary>
    /// The assemblies in scope: the kernel, every module's <c>.Domain</c> and every module's
    /// <c>.Application</c>.
    /// </summary>
    /// <remarks>
    /// The kernel is included although §5.6 names only <c>*.Domain</c> and <c>*.Application</c>:
    /// it is tier 0 domain code that every module depends on, so an ambient clock there would be
    /// the most widely shared version of this defect.
    /// </remarks>
    public static bool IsInScope(string assemblyName) =>
        string.Equals(assemblyName, "Aurora.SharedKernel", StringComparison.Ordinal)
        || assemblyName.EndsWith(".Domain", StringComparison.Ordinal)
        || assemblyName.EndsWith(".Application", StringComparison.Ordinal);

    public static RuleOutcome Check(IEnumerable<ScannedType> types)
    {
        ScannedType[] subjects = [.. types.Where(static type => IsInScope(type.AssemblyName))];

        return RuleOutcome.From(Id, Name, "types in domain or application assemblies", subjects.Length, Violations(subjects));
    }

    /// <summary>Runs the rule over types whose assembly scope has already been decided by the caller.</summary>
    public static RuleOutcome CheckTypes(IEnumerable<ScannedType> types)
    {
        ScannedType[] subjects = [.. types];
        return RuleOutcome.From(Id, Name, "types", subjects.Length, Violations(subjects));
    }

    private static IEnumerable<RuleViolation> Violations(IEnumerable<ScannedType> subjects) =>
        subjects
            .SelectMany(TypeReferences.InstructionsIn)
            .Where(static reference => BannedReads.Any(banned =>
                string.Equals(reference.Member.DeclaringType, banned.DeclaringType, StringComparison.Ordinal)
                && string.Equals(reference.Member.MemberName, banned.Getter, StringComparison.Ordinal)))
            .Select(static reference => new RuleViolation(
                reference.Subject,
                ViolationSite.MemberReference,
                $"reads the ambient clock: {reference.Member.OpCode} {reference.Member}"));
}
