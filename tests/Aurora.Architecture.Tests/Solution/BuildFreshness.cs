using System;
using System.IO;

namespace Aurora.Architecture.Tests.Solution;

/// <summary>
/// Answers one question: has any source file in a project been written since the project's
/// assembly was built?
/// </summary>
/// <remarks>
/// Found the hard way while proving rule F1 fails. A <c>double</c> was planted in
/// <c>Aurora.SharedKernel</c> and F1 stayed green: <c>dotnet test</c> on the architecture project
/// alone rebuilds only that project, and the rules scanned the kernel's *previous* build. A rule
/// reading stale output reports on code nobody is running, and reports it in green - the vacuity
/// this rule set exists to prevent, reached through the input rather than the logic.
/// </remarks>
internal static class BuildFreshness
{
    /// <summary>
    /// The newest source file under <paramref name="projectDirectory"/> written after
    /// <paramref name="builtAt"/>, or <c>null</c> when the build is current.
    /// </summary>
    /// <remarks>
    /// Build output is excluded: <c>Directory.Build.props</c> sends it to <c>artifacts/</c>, but a
    /// stray <c>obj/</c> or <c>bin/</c> from an IDE or an older SDK layout would otherwise always
    /// look newer than the assembly and make this check permanently red.
    /// </remarks>
    public static string? NewerSourceThan(string projectDirectory, DateTime builtAt)
    {
        string? newest = null;
        DateTime newestAt = builtAt;

        foreach (string source in Directory.EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (!IsSource(source) || IsBuildOutput(source, projectDirectory))
            {
                continue;
            }

            DateTime writtenAt = File.GetLastWriteTimeUtc(source);
            if (writtenAt > newestAt)
            {
                newest = source;
                newestAt = writtenAt;
            }
        }

        return newest;
    }

    private static bool IsSource(string path) =>
        path.EndsWith(".cs", StringComparison.Ordinal)
        || path.EndsWith(".csproj", StringComparison.Ordinal)
        || path.EndsWith(".razor", StringComparison.Ordinal);

    private static bool IsBuildOutput(string path, string projectDirectory)
    {
        string relative = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/');
        return relative.StartsWith("obj/", StringComparison.Ordinal)
            || relative.StartsWith("bin/", StringComparison.Ordinal);
    }
}
