using System;

namespace Aurora.Architecture.Tests.Solution;

/// <summary>Which part of the solution a project belongs to (<c>solution-layout.md</c> §1).</summary>
public enum ProjectKind
{
    /// <summary>A name that fits none of the conventions below.</summary>
    Unknown,

    /// <summary>Tier 0: <c>Aurora.SharedKernel</c>, <c>Aurora.Documents.Canonical</c>, <c>Aurora.Countries.Contracts</c>.</summary>
    Kernel,

    /// <summary>A tier-1 platform capability: <c>Aurora.Platform.&lt;Name&gt;</c>.</summary>
    Platform,

    /// <summary>A business module: <c>Aurora.Modules.&lt;Module&gt;.&lt;Layer&gt;</c>.</summary>
    Module,

    /// <summary>A Country Package: <c>Aurora.Countries.&lt;Country&gt;</c>.</summary>
    CountryPackage,

    /// <summary><c>Aurora.Web</c> or <c>Aurora.Worker</c>.</summary>
    Host,

    /// <summary><c>Aurora.Composition</c>, the only project allowed to see every Infrastructure.</summary>
    Composition,

    /// <summary>Anything under <c>tests/</c>.</summary>
    Test,
}

/// <summary>Which of a module's four projects this is (<c>solution-layout.md</c> §2).</summary>
public enum ModuleLayer
{
    /// <summary>Not a four-project module: a kernel, platform or host project.</summary>
    Whole,

    /// <summary>DTOs, service interfaces, integration events. The only thing another module may reference.</summary>
    Contracts,

    /// <summary>Aggregates and invariants. Kernel and BCL only.</summary>
    Domain,

    /// <summary>Handlers, the module <c>DbContext</c>, EF configuration.</summary>
    Application,

    /// <summary>Npgsql specifics, migrations, the DI extension.</summary>
    Infrastructure,
}

/// <summary>
/// What a project is, read from its name.
/// </summary>
/// <remarks>
/// <c>solution-layout.md</c> §1 says it outright: "the <c>Modules.</c> and <c>Platform.</c>
/// namespace segments are load-bearing - every architecture fitness test is expressed as a rule
/// over these prefixes". This type is where that dependency lives, and
/// <c>ProjectIdentityTests</c> pins every case so a renamed convention fails here, loudly, rather
/// than quietly reclassifying half the solution as <see cref="ProjectKind.Unknown"/> and exempting
/// it from every rule.
/// </remarks>
internal sealed record ProjectIdentity(ProjectKind Kind, string Module, ModuleLayer Layer)
{
    private const string ModulePrefix = "Aurora.Modules.";
    private const string PlatformPrefix = "Aurora.Platform.";
    private const string CountriesPrefix = "Aurora.Countries.";

    public static ProjectIdentity Of(ProjectFile project) =>
        project.RelativePath.StartsWith("tests/", StringComparison.Ordinal)
            ? new ProjectIdentity(ProjectKind.Test, project.Name, ModuleLayer.Whole)
            : Of(project.Name);

    public static ProjectIdentity Of(string projectName)
    {
        switch (projectName)
        {
            case "Aurora.SharedKernel":
            case "Aurora.Documents.Canonical":
            case "Aurora.Countries.Contracts":
                return new ProjectIdentity(ProjectKind.Kernel, projectName, ModuleLayer.Whole);

            case "Aurora.Web":
            case "Aurora.Worker":
                return new ProjectIdentity(ProjectKind.Host, projectName, ModuleLayer.Whole);

            case "Aurora.Composition":
                return new ProjectIdentity(ProjectKind.Composition, projectName, ModuleLayer.Whole);

            case "Aurora.Countries.Hosting":
                // Manifest reading, signature verification and the package loader: a platform
                // capability that happens to sit beside the tier-0 contracts in the folder tree.
                return new ProjectIdentity(ProjectKind.Platform, "Countries.Hosting", ModuleLayer.Whole);

            default:
                break;
        }

        if (projectName.StartsWith(ModulePrefix, StringComparison.Ordinal))
        {
            string remainder = projectName[ModulePrefix.Length..];
            int lastDot = remainder.LastIndexOf('.');

            return lastDot > 0 && TryReadLayer(remainder[(lastDot + 1)..], out ModuleLayer layer)
                ? new ProjectIdentity(ProjectKind.Module, remainder[..lastDot], layer)
                : new ProjectIdentity(ProjectKind.Unknown, projectName, ModuleLayer.Whole);
        }

        if (projectName.StartsWith(PlatformPrefix, StringComparison.Ordinal))
        {
            string remainder = projectName[PlatformPrefix.Length..];
            return remainder.EndsWith(".Contracts", StringComparison.Ordinal)
                ? new ProjectIdentity(
                    ProjectKind.Platform,
                    remainder[..^".Contracts".Length],
                    ModuleLayer.Contracts)
                : new ProjectIdentity(ProjectKind.Platform, remainder, ModuleLayer.Whole);
        }

        if (projectName.StartsWith(CountriesPrefix, StringComparison.Ordinal))
        {
            return new ProjectIdentity(
                ProjectKind.CountryPackage,
                projectName[CountriesPrefix.Length..],
                ModuleLayer.Whole);
        }

        return new ProjectIdentity(ProjectKind.Unknown, projectName, ModuleLayer.Whole);
    }

    public override string ToString() =>
        Layer == ModuleLayer.Whole ? $"{Kind} {Module}" : $"{Kind} {Module}.{Layer}";

    private static bool TryReadLayer(string segment, out ModuleLayer layer)
    {
        switch (segment)
        {
            case "Contracts":
                layer = ModuleLayer.Contracts;
                return true;
            case "Domain":
                layer = ModuleLayer.Domain;
                return true;
            case "Application":
                layer = ModuleLayer.Application;
                return true;
            case "Infrastructure":
                layer = ModuleLayer.Infrastructure;
                return true;
            default:
                layer = ModuleLayer.Whole;
                return false;
        }
    }
}
