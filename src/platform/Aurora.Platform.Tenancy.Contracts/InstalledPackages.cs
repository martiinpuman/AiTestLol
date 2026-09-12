using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The Country Packages one tenant has, as a <see cref="TenantScope"/> carries them
/// (ADR-0007 §3.4): read from <c>catalog.installed_package</c> when the scope opens, immutable
/// for the scope's life, each package at most once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutable, and a copy.</b> A scope is handed to every application-service call for a unit
/// of work (ADR-0007 §4.5); the package set it carries must be the one that was read at open, not
/// a live view that an installer running in another request can change under it.
/// </para>
/// <para>
/// <b>One entry per package, or refused.</b> The catalog holds one row per (tenant, package). Two
/// entries for one id here can only mean a reader that joined wrongly, and picking either would
/// hide that - so <see cref="Of"/> refuses the whole set, in the same spirit as
/// <c>CompanyScope.Of</c> refusing an empty one.
/// </para>
/// <para>
/// <b><see cref="None"/> is the only empty value.</b> A scope's package set is never null;
/// <see cref="TenantScope"/> refuses null, so "no packages" is always this one instance.
/// </para>
/// </remarks>
public sealed class InstalledPackages
{
    /// <summary>Keyed by package id, ordinal, so a lookup is exact and the order is stable.</summary>
    private readonly SortedList<string, InstalledPackageEntry> _byId;

    private InstalledPackages(SortedList<string, InstalledPackageEntry> byId)
    {
        _byId = byId;
        Entries = Array.AsReadOnly<InstalledPackageEntry>([.. byId.Values]);
    }

    /// <summary>A tenant with no Country Package installed.</summary>
    public static InstalledPackages None { get; } = new(new SortedList<string, InstalledPackageEntry>(0, StringComparer.Ordinal));

    /// <summary>How many packages the tenant has.</summary>
    public int Count => _byId.Count;

    /// <summary>Every package, each once, in ascending order of package id.</summary>
    public IReadOnlyList<InstalledPackageEntry> Entries { get; }

    /// <summary>
    /// The set the given entries describe, each package once.
    /// </summary>
    /// <param name="entries">
    /// Read once, so a one-shot sequence is acceptable; copied, so the caller may change the source
    /// afterwards without changing the set.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An entry is <see langword="null"/>, or two entries name the same package.
    /// </exception>
    public static InstalledPackages Of(IEnumerable<InstalledPackageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        SortedList<string, InstalledPackageEntry> byId = new(StringComparer.Ordinal);
        foreach (InstalledPackageEntry entry in entries)
        {
            if (entry is null)
            {
                throw new ArgumentException("An installed-package entry is null.", nameof(entries));
            }

            if (!byId.TryAdd(entry.PackageId, entry))
            {
                throw new ArgumentException(
                    $"Package '{entry.PackageId}' appears more than once. The catalog holds one row per " +
                    "(tenant, package), so two entries for one package are a reading error, not a set.",
                    nameof(entries));
            }
        }

        return byId.Count == 0 ? None : new InstalledPackages(byId);
    }

    /// <summary>Looks a package up by its exact id.</summary>
    public bool TryGet(string packageId, [NotNullWhen(true)] out InstalledPackageEntry? entry)
    {
        if (packageId is null)
        {
            entry = null;
            return false;
        }

        return _byId.TryGetValue(packageId, out entry);
    }
}
