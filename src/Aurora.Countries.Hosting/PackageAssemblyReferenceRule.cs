using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Aurora.Countries.Contracts;

namespace Aurora.Countries.Hosting;

/// <summary>
/// Which Aurora assemblies a Country Package is allowed to reference (ADR-0008 §3.1).
/// </summary>
/// <remarks>
/// <para>
/// ADR-0008 §3.1 asks for an architecture fitness test asserting that no package assembly
/// references <c>Aurora.*.Domain</c>, <c>Aurora.*.Infrastructure</c>, <c>Aurora.Web</c> or a
/// module's internals. This is that rule, implemented where it is strictly stronger: at the host,
/// read from the package's metadata before its code runs. A build-time test can only cover the
/// packages built in this repository, and the whole point of the contract is that one day a package
/// will not be.
/// </para>
/// <para>
/// It is an allowlist, not a blocklist. A blocklist of forbidden prefixes passes every assembly
/// nobody thought of, and the module that has not been written yet is exactly the one nobody thought
/// of.
/// </para>
/// </remarks>
public static class PackageAssemblyReferenceRule
{
    /// <summary>
    /// The Aurora assemblies a package may reference: the contract, and the two tier-0 assemblies
    /// the contract itself is expressed in terms of.
    /// </summary>
    /// <remarks>
    /// <c>Aurora.SharedKernel</c> and <c>Aurora.Documents.Canonical</c> are on the list because the
    /// extension points hand packages <c>Money</c>, <c>DateRange</c> and canonical documents — a
    /// package cannot implement <c>ITaxRuleProvider</c> without naming those types. They are tier-0
    /// kernel assemblies with no behaviour that reaches a database, and changes to them are
    /// core-contract changes under the same SemVer rules (modules.md §3).
    /// </remarks>
    public static IReadOnlySet<string> AllowedAuroraAssemblies { get; } =
        new ReadOnlySet<string>(new HashSet<string>(StringComparer.Ordinal)
        {
            CoreContract.AssemblyName,
            "Aurora.SharedKernel",
            "Aurora.Documents.Canonical",
        });

    /// <summary>The prefix that marks an assembly as one of ours.</summary>
    public const string AuroraPrefix = "Aurora.";

    /// <summary>
    /// Whether a package may reference <paramref name="assemblyName"/>.
    /// </summary>
    /// <remarks>
    /// Anything that is not an Aurora assembly is allowed here: a package may use whatever
    /// third-party libraries it ships, and its own load context is what keeps its choice of versions
    /// away from ours. What it may not do is reach into core past the contract.
    /// </remarks>
    public static bool IsAllowed(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        return !assemblyName.StartsWith(AuroraPrefix, StringComparison.Ordinal)
            || AllowedAuroraAssemblies.Contains(assemblyName);
    }
}
