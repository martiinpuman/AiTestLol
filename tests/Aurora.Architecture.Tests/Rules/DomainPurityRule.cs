using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Solution;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>L1</b> - the kernel and every module's <c>.Domain</c> depend on nothing but the
/// BCL and the kernel. No EF, no ASP.NET, no Npgsql, no DI, no logging, no JSON.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three mechanisms, because one of them has a known hole.</b>
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Declared packages</b> (<see cref="CheckDeclarations"/>): the project file's
///     <c>PackageReference</c> and <c>FrameworkReference</c> elements, plus the <c>Direct</c>
///     entries in its committed <c>packages.lock.json</c>. This is the half
///     <c>docs/reviews/B-03.md</c> m-3 found missing: a declared-but-unused package emits no
///     assembly reference, so the compiled half calls the project clean while the project file says
///     otherwise. A project in scope with no lock file at all is a violation, not a pass.
///   </description></item>
///   <item><description>
///     <b>Emitted assembly references</b> (<see cref="CheckCompiled"/>): the assembly's
///     <c>AssemblyRef</c> table, which is what the code actually binds to.
///   </description></item>
///   <item><description>
///     <b>Banned namespaces</b> (<see cref="CheckCompiled"/>): every type the assembly names, at
///     every site <see cref="TypeReferences"/> reaches. <c>System.Text.Json</c> lives in a
///     <c>System.*</c> assembly and would pass a BCL-name check, and <c>testing-strategy.md</c>
///     §5.1 bans JSON types from the domain by name - so the namespace check is not redundant with
///     the assembly check, it covers a case the assembly check cannot see.
///   </description></item>
/// </list>
/// <para>
/// <b>What it cannot see:</b> a domain type that reaches a framework through <c>object</c> or
/// through a delegate handed to it by another layer. That is not a dependency the domain declares,
/// and it is the layering rules' job to stop the layer that hands it over.
/// </para>
/// </remarks>
internal static class DomainPurityRule
{
    public const string Id = "L1";

    public const string Name = "The kernel and every .Domain depend on the BCL and the kernel only";

    /// <summary>Namespaces the domain may not name, from <c>testing-strategy.md</c> §5.1 and <c>solution-layout.md</c> §2.</summary>
    public static readonly ImmutableArray<string> BannedNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
        "Microsoft.Data",
        "Npgsql",
        "System.Data",
        "System.Text.Json",
        "Newtonsoft.Json",
        "Serilog",
    ];

    /// <summary>The tier-0 assemblies. Everything in scope may reference these, subject to the rule below.</summary>
    public static readonly ImmutableHashSet<string> Tier0 = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Aurora.SharedKernel",
        "Aurora.Documents.Canonical",
        "Aurora.Countries.Contracts");

    /// <summary>Is this assembly governed by L1?</summary>
    public static bool IsInScope(string assemblyName) =>
        Tier0.Contains(assemblyName) || assemblyName.EndsWith(".Domain", StringComparison.Ordinal);

    /// <summary>
    /// Which non-BCL assemblies this one may reference.
    /// </summary>
    /// <remarks>
    /// <c>Aurora.SharedKernel</c> gets the strictest answer - nothing - because
    /// <c>testing-strategy.md</c> §5.1 says "references only the BCL" and every module depends on it.
    /// </remarks>
    public static ImmutableHashSet<string> AllowedReferencesFor(string assemblyName) => assemblyName switch
    {
        "Aurora.SharedKernel" => ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
        "Aurora.Documents.Canonical" => ImmutableHashSet.Create(StringComparer.Ordinal, "Aurora.SharedKernel"),
        "Aurora.Countries.Contracts" => Tier0,
        _ => ImmutableHashSet.Create(StringComparer.Ordinal, "Aurora.SharedKernel"),
    };

    /// <summary>
    /// Is this the name of a BCL assembly?
    /// </summary>
    /// <remarks>
    /// Name-based, because nothing is loaded. <c>System.Text.Json</c> passes this test and is caught
    /// by the banned-namespace mechanism instead - which is exactly why that mechanism exists.
    /// </remarks>
    public static bool IsBcl(string assemblyName) =>
        assemblyName is "netstandard" or "mscorlib" or "System"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);

    public static RuleOutcome Check(IEnumerable<ProjectFile> projects, IEnumerable<ScannedAssembly> assemblies)
    {
        ProjectFile[] scopedProjects = [.. projects.Where(static p => IsInScope(p.Name))];
        ScannedAssembly[] scopedAssemblies = [.. assemblies.Where(static a => IsInScope(a.Name))];

        return RuleOutcome.From(
            Id,
            Name,
            "kernel and .Domain assemblies",
            scopedAssemblies.Length,
            [.. CheckDeclarations(scopedProjects).Violations, .. CheckCompiled(scopedAssemblies).Violations]);
    }

    /// <summary>The declared half: package references and the committed lock file.</summary>
    public static RuleOutcome CheckDeclarations(IEnumerable<ProjectFile> projects)
    {
        ProjectFile[] subjects = [.. projects];

        return RuleOutcome.From(Id, Name, "project files", subjects.Length, subjects.SelectMany(DeclarationViolations));
    }

    /// <summary>The compiled half: emitted assembly references and named namespaces.</summary>
    public static RuleOutcome CheckCompiled(IEnumerable<ScannedAssembly> assemblies)
    {
        ScannedAssembly[] subjects = [.. assemblies];

        return RuleOutcome.From(Id, Name, "assemblies", subjects.Length, subjects.SelectMany(CompiledViolations));
    }

    private static IEnumerable<RuleViolation> DeclarationViolations(ProjectFile project)
    {
        foreach (string package in project.PackageReferences)
        {
            yield return new RuleViolation(
                project.RelativePath,
                ViolationSite.Declaration,
                $"declares <PackageReference Include=\"{package}\" />; the domain layer takes no "
                + "third-party dependency, used or not (solution-layout.md §2)");
        }

        foreach (string framework in project.FrameworkReferences)
        {
            yield return new RuleViolation(
                project.RelativePath,
                ViolationSite.Declaration,
                $"declares <FrameworkReference Include=\"{framework}\" />; the domain layer takes no "
                + "shared framework beyond the BCL (solution-layout.md §2)");
        }

        foreach (string direct in project.DirectLockFileDependencies)
        {
            yield return new RuleViolation(
                project.RelativePath,
                ViolationSite.Declaration,
                $"packages.lock.json records '{direct}' as a direct dependency");
        }

        if (project.DirectLockFileDependencies.IsEmpty && !HasLockFile(project))
        {
            yield return new RuleViolation(
                project.RelativePath,
                ViolationSite.Declaration,
                "has no committed packages.lock.json, so 'declares no package' could not be measured. "
                + "Directory.Build.props sets RestorePackagesWithLockFile; commit the generated file");
        }
    }

    private static bool HasLockFile(ProjectFile project) =>
        System.IO.File.Exists(
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(project.FullPath)!, "packages.lock.json"));

    private static IEnumerable<RuleViolation> CompiledViolations(ScannedAssembly assembly)
    {
        ImmutableHashSet<string> allowed = AllowedReferencesFor(assembly.Name);

        foreach (string reference in assembly.ReferencedAssemblies.Where(name =>
                     !IsBcl(name) && !allowed.Contains(name)))
        {
            yield return new RuleViolation(
                assembly.Name,
                ViolationSite.Declaration,
                $"references assembly '{reference}'; allowed here: the BCL"
                + (allowed.IsEmpty ? " and nothing else" : " and " + string.Join(", ", allowed.Order(StringComparer.Ordinal))));
        }

        foreach (TypeReference reference in assembly.Types.SelectMany(TypeReferences.In))
        {
            foreach (string banned in BannedNamespaces)
            {
                if (!reference.Use.Names.Any(name => IsInNamespace(name, banned)))
                {
                    continue;
                }

                yield return new RuleViolation(
                    reference.Subject,
                    reference.Site,
                    $"names a type in the banned namespace '{banned}' as {reference.Context}");
                break;
            }
        }
    }

    /// <summary>
    /// Is <paramref name="typeName"/> inside <paramref name="namespacePrefix"/>?
    /// </summary>
    /// <remarks>
    /// The trailing separator is load-bearing: without it, a hypothetical <c>NpgsqlSafeTypes</c>
    /// would match the <c>Npgsql</c> prefix. A rule that over-matches gets suppressed, and a
    /// suppressed rule protects nothing.
    /// </remarks>
    private static bool IsInNamespace(string typeName, string namespacePrefix) =>
        typeName.StartsWith(namespacePrefix + ".", StringComparison.Ordinal);
}
