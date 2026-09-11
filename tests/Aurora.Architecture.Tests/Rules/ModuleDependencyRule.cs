using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Architecture.Tests.Solution;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>M1</b> - every compile-time reference between two modules is one the dependency
/// matrix in <c>modules.md</c> §6 permits.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the mechanism inspects:</b> every declared <c>ProjectReference</c> whose consumer and
/// provider are both <c>Aurora.Modules.*</c> projects of <i>different</i> modules, checked against
/// <see cref="ModuleMatrix"/>. Within one module, <see cref="ProjectLayeringRule"/> decides; this
/// rule is only about which other modules a module may see.
/// </para>
/// <para>
/// <b>An unknown module is refused, not waved through.</b> A module absent from the matrix may
/// reference no other module, so adding a module without adding its row turns this rule red. The
/// opposite default - unknown means unconstrained - is how a matrix stops covering the solution
/// without anyone noticing.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a module reading another module's tables or calling it through a
/// reflection-resolved type. Neither is a project reference; the first is M3/M4's ground
/// (<c>testing-strategy.md</c> §5.2, still to be built on a module's <c>DbContext</c> model, which
/// no module has yet).
/// </para>
/// </remarks>
internal static class ModuleDependencyRule
{
    public const string Id = "M1";

    public const string Name = "Every cross-module reference is in the modules.md §6 matrix";

    public static RuleOutcome Check(IEnumerable<ProjectFile> projects)
    {
        (ProjectFile Project, ProjectIdentity Consumer, string Reference, ProjectIdentity Provider)[] edges =
        [
            .. from project in projects
               let consumer = ProjectIdentity.Of(project)
               where consumer.Kind == ProjectKind.Module
               from reference in project.ProjectReferences
               let provider = ProjectIdentity.Of(reference)
               where provider.Kind == ProjectKind.Module
                   && !string.Equals(provider.Module, consumer.Module, StringComparison.Ordinal)
               select (project, consumer, reference, provider),
        ];

        return RuleOutcome.From(Id, Name, "cross-module project references", edges.Length, Violations(edges));
    }

    private static IEnumerable<RuleViolation> Violations(
        IEnumerable<(ProjectFile Project, ProjectIdentity Consumer, string Reference, ProjectIdentity Provider)> edges)
    {
        foreach ((ProjectFile project, ProjectIdentity consumer, string reference, ProjectIdentity provider) in edges)
        {
            string subject = $"{project.RelativePath} -> {reference}";

            if (!ModuleMatrix.Knows(consumer.Module))
            {
                yield return new RuleViolation(
                    subject,
                    ViolationSite.Declaration,
                    $"module '{consumer.Module}' has no row in the modules.md §6 matrix, so it may "
                    + "reference no other module. Add the row to ModuleMatrix and to modules.md §6 "
                    + "in the same change");
                continue;
            }

            if (!ModuleMatrix.Allows(consumer.Module, provider.Module))
            {
                yield return new RuleViolation(
                    subject,
                    ViolationSite.Declaration,
                    $"the matrix does not allow {consumer.Module} -> {provider.Module}. An empty cell "
                    + "is forbidden and an 'E' cell is events only, never a reference (modules.md §6)");
                continue;
            }

            if (provider.Layer != ModuleLayer.Contracts)
            {
                yield return new RuleViolation(
                    subject,
                    ViolationSite.Declaration,
                    $"{consumer.Module} may reference {provider.Module} only through its .Contracts, "
                    + $"never its .{provider.Layer} (modules.md §1.1)");
            }
        }
    }
}
