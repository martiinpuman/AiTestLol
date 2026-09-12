using System;
using System.Linq;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// One Country Package as one tenant has it: which package, at which version, in which state -
/// one row of <c>catalog.installed_package</c> as a <see cref="TenantScope"/> sees it.
/// </summary>
/// <remarks>
/// <para>
/// The package id and version are carried as the catalog's text, verbatim. The id is the stem of
/// the package's <c>pkg_&lt;id&gt;</c> schema (ADR-0008 §4.1) and is held to exactly the rule the
/// catalog enforces on the row it came from - <see cref="PackageIdFormat"/>, the one statement of
/// that rule - and to nothing stricter: this type is on the read path, and a read-path rule
/// stricter than the writer's turns a legitimately stored row into a scope that cannot open, which
/// is a tenant that cannot be served (ADR-0038 §2.1). The version is the catalog's text with the
/// only check that could not have come from the row at all.
/// </para>
/// <para>
/// A composer of <c>pkg_&lt;id&gt;</c> still re-validates and quotes for itself (ADR-0038 §2.3);
/// what this type guarantees is that no instance carries an id the catalog would refuse. A typed
/// package identifier exists in <c>Aurora.Countries.Contracts</c>, which a tenancy contract may
/// not reference (fitness rule L2), and it names a different identifier in any case - a dotted
/// global id, not this schema key (ADR-0038 §2.6).
/// </para>
/// </remarks>
public sealed record InstalledPackageEntry
{
    /// <summary>Describes one installed package.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="packageId"/> is null, blank or not a package id
    /// (<see cref="PackageIdFormat"/>), or <paramref name="version"/> is null, blank or contains
    /// whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a defined state.</exception>
    public InstalledPackageEntry(string packageId, string version, InstalledPackageState state)
    {
        PackageId = RequirePackageId(packageId);
        Version = RequireToken(version, nameof(version));
        State = Enum.IsDefined(state)
            ? state
            : throw new ArgumentOutOfRangeException(nameof(state), state, "Not an InstalledPackageState.");
    }

    /// <summary>The package's id, as the catalog spells it.</summary>
    public string PackageId { get; }

    /// <summary>The installed version, as the catalog spells it.</summary>
    public string Version { get; }

    /// <summary>Where the package stands in this tenant.</summary>
    public InstalledPackageState State { get; }

    private static string RequirePackageId(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return PackageIdFormat.IsWellFormed(packageId)
            ? packageId
            : throw new ArgumentException(
                $"'{packageId}' is not a package id: {PackageIdFormat.Description} (ADR-0038 2.1).",
                nameof(packageId));
    }

    private static string RequireToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return value.Any(char.IsWhiteSpace)
            ? throw new ArgumentException("Expected text with no whitespace.", parameterName)
            : value;
    }
}
