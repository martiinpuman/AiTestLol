using System;
using System.IO;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The guard that stops every other rule in this project from inspecting yesterday's build.
/// </summary>
/// <remarks>
/// Its fixture is a temporary directory rather than the repository, because proving this guard
/// fires means writing a source file after an assembly was built - and doing that to the real tree
/// would leave the repository dirty, which verify.sh stage 11 fails the run for.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class BuildFreshnessTests
{
    [Fact]
    public void A_source_file_written_after_the_build_is_reported()
    {
        using var project = new TemporaryProject();
        DateTime builtAt = DateTime.UtcNow;
        project.Write("Money.cs", "class Money { }", builtAt.AddMinutes(1));

        BuildFreshness.NewerSourceThan(project.Root, builtAt)
            .ShouldNotBeNull()
            .ShouldEndWith("Money.cs");
    }

    [Fact]
    public void A_source_file_written_before_the_build_is_not_reported()
    {
        using var project = new TemporaryProject();
        DateTime builtAt = DateTime.UtcNow;
        project.Write("Money.cs", "class Money { }", builtAt.AddMinutes(-1));

        BuildFreshness.NewerSourceThan(project.Root, builtAt).ShouldBeNull();
    }

    [Fact]
    public void Build_output_under_obj_is_not_mistaken_for_a_source_change()
    {
        using var project = new TemporaryProject();
        DateTime builtAt = DateTime.UtcNow;
        project.Write("obj/Debug/Generated.cs", "class Generated { }", builtAt.AddMinutes(1));

        BuildFreshness.NewerSourceThan(project.Root, builtAt).ShouldBeNull();
    }

    [Fact]
    public void A_file_that_is_not_source_is_not_mistaken_for_a_source_change()
    {
        using var project = new TemporaryProject();
        DateTime builtAt = DateTime.UtcNow;
        project.Write("README.md", "# notes", builtAt.AddMinutes(1));

        BuildFreshness.NewerSourceThan(project.Root, builtAt).ShouldBeNull();
    }

    private sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject() => Directory.CreateDirectory(Root);

        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "aurora-freshness-" + Guid.NewGuid().ToString("N"));

        public void Write(string relativePath, string content, DateTime writtenAtUtc)
        {
            string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, writtenAtUtc);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
