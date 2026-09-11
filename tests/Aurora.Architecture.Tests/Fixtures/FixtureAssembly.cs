using System;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Rules;

namespace Aurora.Architecture.Tests.Fixtures;

/// <summary>
/// The deliberately-violating fixtures, read back out of this test assembly's own compiled
/// <c>.dll</c> by the same scanner the production rules use.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the fixtures live inside this assembly.</b> A rule proven only against hand-built input
/// is proven against the test author's idea of what the compiler emits. These fixtures are real
/// C#, compiled by the same compiler with the same settings as production code, and read back
/// through the same <see cref="AssemblyScanner"/>; when a rule fires on one, it fires on compiler
/// output.
/// </para>
/// <para>
/// <b>Why they cannot fail the build for everyone.</b> They are types, not tests: nothing executes
/// them. They are invisible to every production rule because the production population is built
/// from projects under <c>src/</c> (<see cref="Solution.SolutionLayout"/>) and this assembly is
/// under <c>tests/</c>. <c>ProductionPopulationTests</c> asserts that separation rather than
/// assuming it.
/// </para>
/// </remarks>
internal static class FixtureAssembly
{
    /// <summary>The namespace every deliberately-violating fixture type lives under.</summary>
    public const string ViolationsNamespace = "Aurora.Architecture.Tests.Fixtures.Violations";

    private static readonly Lazy<ScannedAssembly> LazySelf =
        new(() => AssemblyScanner.Read(typeof(FixtureAssembly).Assembly.Location));

    /// <summary>This test assembly, scanned from disk.</summary>
    public static ScannedAssembly Self => LazySelf.Value;

    /// <summary>Every deliberately-violating fixture type.</summary>
    public static ImmutableArray<ScannedType> AllViolations =>
    [
        .. Self.Types.Where(static type =>
            type.FullName.StartsWith(ViolationsNamespace + ".", StringComparison.Ordinal)),
    ];

    /// <summary>
    /// The catalog stand-in (<c>Fixtures/StandIns</c>), compiled at B-05's exact full name, as
    /// compiled: in this test assembly, which is the wrong one. <c>CatalogFixture</c> relabels it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No type at that full name: the stand-in was moved or renamed.</exception>
    public static ScannedType CatalogStandIn =>
        Self.Types.SingleOrDefault(static type =>
            string.Equals(type.FullName, TenancyNames.CatalogDbContext, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"No stand-in at {TenancyNames.CatalogDbContext} in {Self.Path}. The catalog exemption "
            + "is proven against a type compiled at that exact name, and there is none.");

    /// <summary>One fixture type and its nested types, relabelled into another assembly.</summary>
    /// <remarks>
    /// The scanner's record - members, accessibility flags, instructions - is untouched; only
    /// <c>AssemblyName</c> changes. This is how a single compiled fixture assembly exercises a rule
    /// that keys on an exact assembly name (ADR-0032 §4.4): the name is the one field the fixture
    /// cannot otherwise supply.
    /// </remarks>
    public static ImmutableArray<ScannedType> ViolationInAssembly(string typeName, string assemblyName) =>
        [.. Violation(typeName).Select(type => type with { AssemblyName = assemblyName })];

    /// <summary>
    /// One fixture type and its nested types, by unqualified name.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No such fixture. A fixture test that silently matched nothing would assert over an empty
    /// set and pass, which is the failure mode this whole rule set exists to prevent.
    /// </exception>
    public static ImmutableArray<ScannedType> Violation(string typeName)
    {
        string fullName = ViolationsNamespace + "." + typeName;
        ImmutableArray<ScannedType> matches =
        [
            .. Self.Types.Where(type =>
                string.Equals(type.FullName, fullName, StringComparison.Ordinal)
                || type.FullName.StartsWith(fullName + "+", StringComparison.Ordinal)),
        ];

        return matches.IsEmpty
            ? throw new InvalidOperationException(
                $"No fixture type '{fullName}' in {Self.Path}. The fixture was renamed or removed, "
                + "and the rule it proves is no longer proven to fail.")
            : matches;
    }
}
