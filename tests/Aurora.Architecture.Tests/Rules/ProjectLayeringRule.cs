using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Architecture.Tests.Solution;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rules <b>L1-L5</b> - every project reference is one <c>solution-layout.md</c> §2 permits
/// for the project declaring it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the mechanism inspects:</b> the <c>ProjectReference</c> elements <b>declared</b> in each
/// <c>.csproj</c> under <c>src/</c>. Declared, not emitted: <c>docs/reviews/B-03.md</c> m-3 showed
/// that a reference which is declared but never used emits nothing into the assembly, so a rule
/// reading the assembly's reference table would call such a project clean. The project file is
/// where a reviewer sees the reference, and it is where this rule reads it.
/// </para>
/// <para>
/// <b>Direct references only.</b> <c>Aurora.Web</c> reaching <c>Sales.Infrastructure</c> through
/// <c>Aurora.Composition</c> is the design (ADR-0005 rule 1, <c>solution-layout.md</c> §3): the
/// composition root is the one project allowed to see every Infrastructure, and the hosts reach
/// modules through it. A rule that followed the graph transitively would ban the architecture.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a reference expressed some other way - a <c>PackageReference</c> onto
/// a module published as a package, an MSBuild-generated reference, a reflection load. The first is
/// covered by the package rule in <see cref="DomainPurityRule"/> for the layer where it matters
/// most; the rest are outside anything a static rule can reach.
/// </para>
/// </remarks>
internal static class ProjectLayeringRule
{
    public const string Id = "L1-L5";

    public const string Name = "Every project reference is one solution-layout.md §2 permits";

    public static RuleOutcome Check(IEnumerable<ProjectFile> projects)
    {
        ProjectFile[] subjects = [.. projects];
        int edges = subjects.Sum(static project => project.ProjectReferences.Length);

        return RuleOutcome.From(Id, Name, "declared project references", edges, Violations(subjects));
    }

    /// <summary>
    /// Why <paramref name="consumer"/> may not reference <paramref name="provider"/>, or
    /// <c>null</c> when it may.
    /// </summary>
    public static string? WhyForbidden(ProjectIdentity consumer, ProjectIdentity provider)
    {
        if (provider.Kind == ProjectKind.Unknown)
        {
            return "the referenced project's name fits none of solution-layout.md §1's conventions, "
                + "so no rule can classify it; rename it or add the convention to ProjectIdentity";
        }

        return consumer.Kind switch
        {
            // The composition root is the one project that may see everything: that is its job.
            ProjectKind.Composition => null,

            // Test projects reference whatever they test.
            ProjectKind.Test => null,

            ProjectKind.Kernel => provider.Kind == ProjectKind.Kernel
                ? null
                : "tier 0 depends on nothing but tier 0 and the BCL (modules.md §3)",

            ProjectKind.Platform => provider.Kind switch
            {
                ProjectKind.Kernel => null,
                ProjectKind.Platform when provider.Layer is ModuleLayer.Contracts => null,
                _ => "a platform module may reference tier 0 and other platform modules' .Contracts only "
                    + "(modules.md §6)",
            },

            ProjectKind.CountryPackage => provider.Module is "Aurora.Countries.Contracts" or "Aurora.Documents.Canonical"
                ? null
                : "a Country Package may reference Aurora.Countries.Contracts and "
                    + "Aurora.Documents.Canonical only (ADR-0008 §3.1, modules.md §6)",

            ProjectKind.Host => provider.Kind switch
            {
                ProjectKind.Composition => null,
                ProjectKind.Kernel => null,
                ProjectKind.Module when provider.Layer == ModuleLayer.Contracts => null,
                ProjectKind.Platform when provider.Layer == ModuleLayer.Contracts => null,
                _ => "a host may reference Aurora.Composition, tier 0 and .Contracts assemblies only - "
                    + "never a .Domain or an .Infrastructure (ADR-0005 rule 1, fitness rule L5)",
            },

            ProjectKind.Module => WhyForbiddenForModule(consumer, provider),

            _ => "the referencing project's name fits none of solution-layout.md §1's conventions",
        };
    }

    private static string? WhyForbiddenForModule(ProjectIdentity consumer, ProjectIdentity provider)
    {
        bool sameModule = provider.Kind == ProjectKind.Module
            && string.Equals(provider.Module, consumer.Module, StringComparison.Ordinal);

        return consumer.Layer switch
        {
            // §2: DTOs and service interfaces. Kernel only, and not even all of tier 0 - a contract
            // assembly that referenced Countries.Contracts would drag package concerns into the
            // surface every other module compiles against.
            ModuleLayer.Contracts => provider.Module is "Aurora.SharedKernel" or "Aurora.Documents.Canonical"
                ? null
                : "a module's .Contracts may reference Aurora.SharedKernel and Aurora.Documents.Canonical "
                    + "only (solution-layout.md §2)",

            // §2 and CLAUDE.md: the domain layer has no references to frameworks, databases or UI.
            ModuleLayer.Domain => provider.Module == "Aurora.SharedKernel"
                ? null
                : "a module's .Domain may reference Aurora.SharedKernel only - not even its own "
                    + ".Contracts (solution-layout.md §2, fitness rule L1)",

            ModuleLayer.Application => provider.Kind switch
            {
                ProjectKind.Kernel => null,
                ProjectKind.Platform when provider.Layer == ModuleLayer.Contracts => null,
                ProjectKind.Module when sameModule && provider.Layer is ModuleLayer.Domain or ModuleLayer.Contracts
                    => null,
                ProjectKind.Module when !sameModule && provider.Layer == ModuleLayer.Contracts => null,
                _ => "a module's .Application may reference its own .Domain and .Contracts, another "
                    + "module's .Contracts, Aurora.Platform.*.Contracts and tier 0 (solution-layout.md §2)",
            },

            ModuleLayer.Infrastructure => provider.Kind switch
            {
                ProjectKind.Kernel => null,
                ProjectKind.Platform when provider.Layer == ModuleLayer.Contracts => null,
                ProjectKind.Module when sameModule && provider.Layer == ModuleLayer.Application => null,
                _ => "a module's .Infrastructure may reference its own .Application, "
                    + "Aurora.Platform.*.Contracts and tier 0 (solution-layout.md §2)",
            },

            _ => "a module project must be one of .Contracts, .Domain, .Application or .Infrastructure "
                + "(solution-layout.md §2)",
        };
    }

    private static IEnumerable<RuleViolation> Violations(IEnumerable<ProjectFile> projects) =>
        from project in projects
        let consumer = ProjectIdentity.Of(project)
        from reference in project.ProjectReferences
        let provider = ProjectIdentity.Of(reference)
        let reason = WhyForbidden(consumer, provider)
        where reason is not null
        select new RuleViolation(
            $"{project.RelativePath} -> {reference}",
            ViolationSite.Declaration,
            $"{consumer} may not reference {provider}: {reason}");
}
