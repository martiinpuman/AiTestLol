using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Aurora.Architecture.Tests.Solution;

namespace Aurora.Architecture.Tests.Fixtures;

/// <summary>
/// Real <c>.csproj</c> and <c>packages.lock.json</c> files, written to a temporary directory and
/// read back by the production <see cref="ProjectFile.Read"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a temporary directory rather than files in the repository.</b> These fixtures are project
/// files that declare forbidden references. Checked in, they would be found by every tool that
/// globs for <c>*.csproj</c> or <c>packages.lock.json</c> - an IDE, <c>dotnet sln add</c>, and
/// B-11's dependency gate, which reads project lock files - and a fixture that breaks somebody
/// else's build is worse than the problem it demonstrates.
/// </para>
/// <para>
/// Nothing is lost by writing them here: a <c>.csproj</c> is XML, no compiler is involved, and the
/// parse path is <see cref="ProjectFile.Read"/> either way. That is not true of the type fixtures in
/// <c>Fixtures/Violations</c>, which must be compiled by the real compiler to be worth anything, and
/// so are.
/// </para>
/// </remarks>
internal sealed class FixtureProjectTree : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "aurora-projects-" + Guid.NewGuid().ToString("N"));

    private readonly List<ProjectFile> _projects = [];

    /// <summary>Every project written into this tree, in the order they were added.</summary>
    public IReadOnlyList<ProjectFile> All => _projects;

    /// <summary>Writes one project file, and its lock file unless <paramref name="withLockFile"/> says otherwise.</summary>
    public ProjectFile Add(
        string name,
        string[]? projectReferences = null,
        string[]? packageReferences = null,
        string[]? frameworkReferences = null,
        bool withLockFile = true)
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);

        string items = string.Concat(
            string.Concat((projectReferences ?? []).Select(reference =>
                FormattableString.Invariant(
                    $"    <ProjectReference Include=\"..\\{reference}\\{reference}.csproj\" />\n"))),
            string.Concat((packageReferences ?? []).Select(static reference =>
                FormattableString.Invariant($"    <PackageReference Include=\"{reference}\" />\n"))),
            string.Concat((frameworkReferences ?? []).Select(static reference =>
                FormattableString.Invariant($"    <FrameworkReference Include=\"{reference}\" />\n"))));

        string csprojPath = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(
            csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" + items + "  </ItemGroup>\n</Project>\n");

        if (withLockFile)
        {
            File.WriteAllText(Path.Combine(directory, "packages.lock.json"), LockFileFor(packageReferences ?? []));
        }

        ProjectFile project = ProjectFile.Read(csprojPath, _root);
        _projects.Add(project);
        return project;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A lock file of the shape NuGet writes, listing each declared package as <c>Direct</c>.</summary>
    private static string LockFileFor(IEnumerable<string> packages)
    {
        string entries = string.Join(
            ",\n",
            packages.Select(static package => string.Format(
                CultureInfo.InvariantCulture,
                "      \"{0}\": {{ \"type\": \"Direct\", \"requested\": \"[1.0.0, )\", \"resolved\": \"1.0.0\" }}",
                package)));

        return "{\n  \"version\": 1,\n  \"dependencies\": {\n    \"net10.0\": {\n"
            + entries + (entries.Length == 0 ? string.Empty : "\n")
            + "    }\n  }\n}\n";
    }
}
