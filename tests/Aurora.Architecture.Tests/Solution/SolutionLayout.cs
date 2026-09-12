using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Solution;

/// <summary>
/// Finds the repository, its projects and their compiled assemblies, and hands the rules a
/// population that is either complete or absent - never quietly partial.
/// </summary>
/// <remarks>
/// <para>
/// <b>What counts as production code:</b> every <c>.csproj</c> under <c>src/</c>, with no filter
/// of any kind. Not a hand-kept list, so a module added tomorrow is covered by every rule without
/// anyone remembering to add it - and not a glob over build output either, because an assembly
/// nobody built would then simply vanish from the population and every rule would report "no
/// violations" over the smaller set. There used to be a path filter here excluding any directory
/// named <c>Fixtures/</c>; <c>docs/reviews/B-04-rereview.md</c> H-1 showed that a production
/// project placed under such a directory vanished from every rule with nothing reporting it.
/// <c>ProductionPopulationTests</c> now asserts, against an independent unfiltered enumeration,
/// that nothing is excluded.
/// </para>
/// <para>
/// <b>The anti-vacuity mechanism:</b> <see cref="ProductionAssemblies"/> throws when a project
/// under <c>src/</c> has no compiled assembly. Every rule test then fails with the same explicit
/// message instead of passing over an incomplete population. This is the B-02 lesson - a check
/// that cannot report having measured nothing is not a check - applied to the population itself.
/// </para>
/// </remarks>
internal static class SolutionLayout
{
    private const string SolutionFileName = "Aurora.sln";

    private static readonly Lazy<ImmutableArray<ScannedAssembly>> LazyProductionAssemblies =
        new(ScanProductionAssemblies);

    private static readonly Lazy<ImmutableArray<ProjectFile>> LazyProductionProjects =
        new(() => ReadProjectsUnder("src"));

    private static readonly Lazy<ImmutableArray<ProjectFile>> LazyTestProjects =
        new(() => ReadProjectsUnder("tests"));

    /// <summary>The repository root - the directory holding <c>Aurora.sln</c>.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// The build configuration this test assembly was compiled into, taken from its own output
    /// path, so a Debug run inspects Debug output and a Release run inspects Release output.
    /// </summary>
    public static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name;

    /// <summary>Every project under <c>src/</c>.</summary>
    public static ImmutableArray<ProjectFile> ProductionProjects => LazyProductionProjects.Value;

    /// <summary>Every project under <c>tests/</c>.</summary>
    public static ImmutableArray<ProjectFile> TestProjects => LazyTestProjects.Value;

    /// <summary>Every project under <c>src/</c> and <c>tests/</c>.</summary>
    public static ImmutableArray<ProjectFile> AllProjects => [.. ProductionProjects, .. TestProjects];

    /// <summary>
    /// The compiled form of every project under <c>src/</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A project under <c>src/</c> produced no assembly, so the population would be incomplete.
    /// </exception>
    public static ImmutableArray<ScannedAssembly> ProductionAssemblies => LazyProductionAssemblies.Value;

    /// <summary>Every type declared by every project under <c>src/</c>.</summary>
    public static ImmutableArray<ScannedType> ProductionTypes =>
        [.. ProductionAssemblies.SelectMany(static assembly => assembly.Types)];

    public static string ExpectedAssemblyPath(ProjectFile project) => Path.Combine(
        RepositoryRoot,
        "artifacts",
        "bin",
        project.Name,
        Configuration,
        project.Name + ".dll");

    private static ImmutableArray<ScannedAssembly> ScanProductionAssemblies()
    {
        ImmutableArray<ProjectFile> projects = ProductionProjects;

        if (projects.IsEmpty)
        {
            throw new InvalidOperationException(
                $"No project found under {RepositoryRoot}/src. Every architecture rule would have "
                + "reported 'no violations' over an empty population.");
        }

        string[] missing =
        [
            .. projects
                .Where(static project => !File.Exists(ExpectedAssemblyPath(project)))
                .Select(static project => $"{project.RelativePath} -> {ExpectedAssemblyPath(project)}"),
        ];

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "These projects under src/ produced no assembly, so the architecture rules would "
                + "have run over an incomplete population and reported no violations for the code "
                + "they could not see. Build the solution, and if a project is genuinely not part "
                + "of it, add it to Aurora.sln so the gate covers it:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        string[] stale = [.. projects.Select(StalenessOf).Where(static report => report is not null).Select(static report => report!)];

        if (stale.Length > 0)
        {
            throw new InvalidOperationException(
                "These projects have source files newer than their compiled assembly, so the rules "
                + "would have inspected the previous build and reported on code that no longer "
                + "exists. Build the solution before running the architecture tests - verify.sh "
                + "does this in stage 3, so a full gate run is never affected:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", stale));
        }

        return [.. projects.Select(static project => AssemblyScanner.Read(ExpectedAssemblyPath(project)))];
    }

    /// <summary>Reports a project whose sources are newer than its assembly, or <c>null</c> when it is current.</summary>
    private static string? StalenessOf(ProjectFile project)
    {
        string assemblyPath = ExpectedAssemblyPath(project);
        string? newest = BuildFreshness.NewerSourceThan(
            Path.GetDirectoryName(project.FullPath)!,
            File.GetLastWriteTimeUtc(assemblyPath));

        return newest is null
            ? null
            : $"{project.RelativePath}: {Path.GetRelativePath(RepositoryRoot, newest)} is newer than "
                + $"{Path.GetRelativePath(RepositoryRoot, assemblyPath)}";
    }

    private static ImmutableArray<ProjectFile> ReadProjectsUnder(string folder)
    {
        string root = Path.Combine(RepositoryRoot, folder);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(path => ProjectFile.Read(path, RepositoryRoot)),
        ];
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No {SolutionFileName} found in any directory above {AppContext.BaseDirectory}. The "
            + "architecture rules locate the code they inspect relative to the solution file.");
    }
}
