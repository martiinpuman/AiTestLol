using System;
using System.Reflection;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The core contract version of ADR-0008 §3.1 — the number a Country Package's
/// <c>coreContractRange</c> is matched against.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <b>not</b> the product version. It moves only when an extension point in
/// this assembly changes: MAJOR when a member is removed or its meaning changes, MINOR when an
/// extension point or an optional member is added, PATCH for documentation. Keeping it slow is the
/// point — every bump costs a compatibility decision for every package in the fleet.
/// </para>
/// <para>
/// It is read from this assembly's own version rather than declared as a constant here. A constant
/// would be a second place to change, and the failure mode of two places is that a release ships
/// with a number that no longer describes the contract it is attached to — which is exactly the
/// Odoo drift this ADR exists to prevent, arriving through the front door.
/// </para>
/// </remarks>
public static class CoreContract
{
    /// <summary>
    /// The running core contract version, in SemVer form (for example <c>1.0.0</c>).
    /// </summary>
    /// <remarks>
    /// Build metadata is stripped: a deterministic build appends <c>+&lt;commit sha&gt;</c> to the
    /// informational version, and SemVer build metadata takes no part in version comparison, so
    /// leaving it on would make every commit look like a different contract version.
    /// </remarks>
    public static string Version { get; } = ReadVersionFromAssembly();

    /// <summary>
    /// The simple name of this assembly — the one core assembly a Country Package may reference
    /// (ADR-0008 §3.1), and the one the package loader resolves from the default load context
    /// rather than from the package's own (ADR-0008 §9.2).
    /// </summary>
    public static string AssemblyName { get; } =
        typeof(CoreContract).Assembly.GetName().Name
        ?? throw new InvalidOperationException("The contracts assembly has no simple name.");

    private static string ReadVersionFromAssembly()
    {
        Assembly assembly = typeof(CoreContract).Assembly;

        string informational =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException(
                "Aurora.Countries.Contracts carries no AssemblyInformationalVersionAttribute, so the " +
                "core contract version cannot be read. It comes from <Version> in the .csproj.");

        int buildMetadata = informational.IndexOf('+', StringComparison.Ordinal);
        return buildMetadata < 0 ? informational : informational[..buildMetadata];
    }
}
