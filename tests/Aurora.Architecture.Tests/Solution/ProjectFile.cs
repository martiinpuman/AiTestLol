using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace Aurora.Architecture.Tests.Solution;

/// <summary>
/// One <c>.csproj</c>, read as declared rather than as compiled.
/// </summary>
/// <remarks>
/// The distinction is the point. <c>docs/reviews/B-03.md</c> m-3 showed that a rule reading an
/// assembly's emitted references cannot see a <c>PackageReference</c> that is declared but not
/// used: the compiler emits no reference for it, so the assembly looks clean while the project
/// file says otherwise. Reference rules therefore read the project file and the committed
/// <c>packages.lock.json</c>, which verify.sh stage 1 keeps in step with it by restoring
/// <c>--locked-mode</c>.
/// </remarks>
internal sealed record ProjectFile(
    string Name,
    string FullPath,
    string RelativePath,
    ImmutableArray<string> ProjectReferences,
    ImmutableArray<string> PackageReferences,
    ImmutableArray<string> FrameworkReferences,
    ImmutableArray<string> DirectLockFileDependencies)
{
    public static ProjectFile Read(string csprojPath, string repositoryRoot)
    {
        XDocument document = XDocument.Load(csprojPath);
        string directory = Path.GetDirectoryName(csprojPath)!;

        return new ProjectFile(
            Name: Path.GetFileNameWithoutExtension(csprojPath),
            FullPath: csprojPath,
            RelativePath: Path.GetRelativePath(repositoryRoot, csprojPath).Replace('\\', '/'),
            ProjectReferences:
            [
                .. Includes(document, "ProjectReference")
                    .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
                    .OrderBy(static name => name, StringComparer.Ordinal),
            ],
            PackageReferences: [.. Includes(document, "PackageReference").OrderBy(static n => n, StringComparer.Ordinal)],
            FrameworkReferences: [.. Includes(document, "FrameworkReference").OrderBy(static n => n, StringComparer.Ordinal)],
            DirectLockFileDependencies: ReadDirectLockFileDependencies(Path.Combine(directory, "packages.lock.json")));
    }

    private static IEnumerable<string> Includes(XDocument document, string elementName) =>
        document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, elementName, StringComparison.Ordinal))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!);

    /// <summary>
    /// The packages the project itself declares, read from its committed lock file.
    /// </summary>
    /// <remarks>
    /// A missing lock file yields an empty set, which would make a "declares no package" rule pass
    /// vacuously. The rule that reads this therefore treats an absent lock file as a violation in
    /// its own right rather than as a clean project.
    /// </remarks>
    private static ImmutableArray<string> ReadDirectLockFileDependencies(string lockFilePath)
    {
        if (!File.Exists(lockFilePath))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockFilePath));
        if (!document.RootElement.TryGetProperty("dependencies", out JsonElement frameworks))
        {
            return [];
        }

        var direct = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty framework in frameworks.EnumerateObject())
        {
            foreach (JsonProperty package in framework.Value.EnumerateObject())
            {
                if (package.Value.TryGetProperty("type", out JsonElement type)
                    && string.Equals(type.GetString(), "Direct", StringComparison.Ordinal))
                {
                    direct.Add(package.Name);
                }
            }
        }

        return [.. direct];
    }

    public override string ToString() => RelativePath;
}
