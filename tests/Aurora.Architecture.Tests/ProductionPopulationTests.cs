using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The population every other rule runs over. If these tests are wrong, every rule in this project
/// is reporting on the wrong code - or on no code - and saying so in green.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ProductionPopulationTests
{
    /// <summary>
    /// The assemblies that must be in the population for the rules to mean anything today. Not the
    /// whole list: a new module must be covered automatically, so this is the floor, not an
    /// inventory. It is spelled out so that a build that silently produced fewer assemblies fails
    /// here by name instead of everywhere by absence.
    /// </summary>
    private static readonly ImmutableArray<string> Anchors =
    [
        "Aurora.SharedKernel",
        "Aurora.Documents.Canonical",
        "Aurora.Countries.Contracts",
        "Aurora.Composition",
        "Aurora.Web",
        "Aurora.Worker",
    ];

    [Fact]
    public void Every_project_under_src_is_compiled_and_in_the_population()
    {
        // SolutionLayout.ProductionAssemblies throws when a project under src/ produced no
        // assembly, so this assertion is about the pairing being one-to-one.
        SolutionLayout.ProductionAssemblies.Length.ShouldBe(
            SolutionLayout.ProductionProjects.Length,
            "every project under src/ contributes exactly one assembly to the population");
    }

    [Fact]
    public void Nothing_under_src_is_filtered_out_of_the_population()
    {
        // The test above compares two numbers derived from the same discovered list, so it cannot
        // see a project the discovery never listed. This enumeration is independent of
        // SolutionLayout and applies no filter, and the two must agree exactly. B-04's re-review
        // (H-1) found that a path filter here made a production project under any directory named
        // Fixtures/ invisible to every rule, with nothing reporting it; the filter is gone, and this
        // is what stops one coming back.
        string srcRoot = Path.Combine(SolutionLayout.RepositoryRoot, "src");

        string[] everyProjectFile =
        [
            .. Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
                .Select(static path => Path.GetRelativePath(SolutionLayout.RepositoryRoot, path).Replace('\\', '/'))
                .OrderBy(static path => path, StringComparer.Ordinal),
        ];

        string[] discovered =
        [
            .. SolutionLayout.ProductionProjects
                .Select(static project => project.RelativePath)
                .OrderBy(static path => path, StringComparer.Ordinal),
        ];

        everyProjectFile.Length.ShouldBeGreaterThan(0, "src/ holds the projects the rules run over");
        discovered.ShouldBe(
            everyProjectFile,
            "every .csproj under src/ must be in the population; one that is not is invisible to "
            + "every rule and reports nothing");
    }

    [Fact]
    public void The_project_fixtures_are_written_outside_the_repository()
    {
        // The population is every .csproj under src/, unfiltered. That is only safe because the
        // deliberately-violating project fixtures FixtureProjectTree writes are never under the
        // repository root - a premise this asserts rather than assumes.
        using var tree = new FixtureProjectTree();
        ProjectFile fixture = tree.Add("Aurora.Fixture.Probe");

        Path.GetFullPath(fixture.FullPath)
            .StartsWith(Path.GetFullPath(SolutionLayout.RepositoryRoot), StringComparison.Ordinal)
            .ShouldBeFalse(
                $"the project fixture was written to {fixture.FullPath}, inside the repository, where "
                + "an unfiltered population would pick it up as production code");
    }

    [Fact]
    public void The_population_contains_the_assemblies_the_rules_are_anchored_on()
    {
        string[] present = [.. SolutionLayout.ProductionAssemblies.Select(static assembly => assembly.Name)];

        foreach (string anchor in Anchors)
        {
            present.ShouldContain(
                anchor,
                $"the rules are anchored on {anchor}; without it they would report no violations "
                + $"over a population of [{string.Join(", ", present)}]");
        }
    }

    [Fact]
    public void No_test_assembly_is_in_the_production_population()
    {
        // Test assemblies hold the deliberately-violating fixtures. If one reached the production
        // population, every rule in this project would fail permanently - which is the safe way for
        // this particular mistake to show up, but it must be impossible rather than merely unlikely.
        SolutionLayout.ProductionAssemblies
            .Select(static assembly => assembly.Name)
            .Where(static name => name.EndsWith(".Tests", StringComparison.Ordinal) || name == "Aurora.TestKit")
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_deliberately_violating_fixtures_are_invisible_to_the_production_population()
    {
        SolutionLayout.ProductionTypes
            .Where(static type =>
                type.FullName.StartsWith(FixtureAssembly.ViolationsNamespace, StringComparison.Ordinal))
            .ShouldBeEmpty("fixture violations must never be mistaken for production violations");
    }

    [Fact]
    public void The_fixture_assembly_carries_the_violations_the_rules_are_proven_against()
    {
        // The mirror of the test above: the fixtures have to exist somewhere, and this is where.
        FixtureAssembly.AllViolations.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_scanner_reads_every_method_body_in_production_code()
    {
        // The IL walker throws on an instruction it cannot decode rather than stopping quietly, so
        // this test asserts that the walk completed over real compiler output - the property that
        // makes "no floating-point instruction found" a measurement rather than an assumption.
        int bodies = SolutionLayout.ProductionTypes
            .SelectMany(static type => type.Methods)
            .Count(static method => !method.Body.OpCodes.IsEmpty);

        bodies.ShouldBeGreaterThan(
            100,
            "the kernel alone compiles to hundreds of method bodies; a number near zero means the "
            + "scanner read metadata but no IL, and every body-level rule is then vacuous");
    }

    [Fact]
    public void Scanning_is_deterministic_for_the_same_assembly()
    {
        ScannedAssembly first = AssemblyScanner.Read(SolutionLayout.ProductionAssemblies[0].Path);
        ScannedAssembly second = AssemblyScanner.Read(SolutionLayout.ProductionAssemblies[0].Path);

        first.Types.Select(static type => type.FullName)
            .ShouldBe(second.Types.Select(static type => type.FullName));
    }
}
