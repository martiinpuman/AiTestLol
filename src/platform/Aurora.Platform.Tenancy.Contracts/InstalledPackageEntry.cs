using System;
using System.Linq;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// One Country Package as one tenant has it: which package, at which version, in which state -
/// one row of <c>catalog.installed_package</c> as a <see cref="TenantScope"/> sees it.
/// </summary>
/// <remarks>
/// <para>
/// The package id and version are carried as the catalog's text, verbatim. Their spelling - the
/// id is the stem of the package's <c>pkg_&lt;id&gt;</c> schema (ADR-0008 §4.1) - is the catalog
/// row's to enforce when it is written; this type refuses only what could not have come from that
/// row at all. A typed package identifier exists in <c>Aurora.Countries.Contracts</c>, which a
/// tenancy contract may not reference (fitness rule L2), so text is the honest shape here.
/// </para>
/// <para>
/// <b>This is not an identifier check.</b> "Not blank, no whitespace" admits any string PostgreSQL's
/// lexer would read as several tokens - <c>/**/</c> is a token separator, so a string with no
/// whitespace character in it can still be arbitrary SQL. Whatever composes <c>pkg_&lt;id&gt;</c>
/// into a statement - a <c>create schema</c>, a <c>search_path</c> - must quote the identifier or
/// validate it against the schema-name rule first; it must never trust this type to have done so.
/// Where that rule belongs, here or in the catalog write path, is the architect's decision (PR #13,
/// third review).
/// </para>
/// </remarks>
public sealed record InstalledPackageEntry
{
    /// <summary>Describes one installed package.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="packageId"/> or <paramref name="version"/> is null, blank or contains
    /// whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a defined state.</exception>
    public InstalledPackageEntry(string packageId, string version, InstalledPackageState state)
    {
        PackageId = RequireToken(packageId, nameof(packageId));
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

    private static string RequireToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return value.Any(char.IsWhiteSpace)
            ? throw new ArgumentException("Expected text with no whitespace.", parameterName)
            : value;
    }
}
