using System;
using System.Linq;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The fact that one tenant has one Country Package, at one version, in one state
/// (ADR-0008 §4, ADR-0007 §9.2).
/// </summary>
/// <remarks>
/// <para>
/// One row per (tenant, package): a package is installed in a tenant once, and upgrades change
/// the row's version rather than adding a row. The installer that moves a row through the states
/// arrives with B-13; B-05 needs only the row to exist and the first state to be
/// <see cref="InstalledPackageState.Installing"/>, so an installer that dies after inserting leaves a
/// row that says so.
/// </para>
/// <para>
/// The package id is stored as text with the spelling ADR-0008 §4.1 R1 needs for the package's
/// schema (<c>pkg_&lt;id&gt;</c>), held to <see cref="PackageIdFormat"/> here, in the check
/// constraint and in the read-path entry alike (ADR-0038 §2.2). The typed package identifier in
/// <c>Aurora.Countries.Contracts</c> is a different identifier - a dotted global id that this
/// column's rule refuses - and which of the two this row should carry is left open until the
/// installer exists (ADR-0038 §2.6). <see cref="InstalledBy"/> is an actor reference, never a
/// display name (ADR-0018).
/// </para>
/// </remarks>
internal sealed class InstalledPackage
{
    public const int MaxVersionLength = 64;
    public const int MaxInstalledByLength = 128;

    private InstalledPackage()
    {
    }

    public TenantId TenantId { get; private set; }

    public string PackageId { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public InstalledPackageState State { get; private set; }

    public DateTimeOffset InstalledAt { get; private set; }

    public string InstalledBy { get; private set; } = null!;

    /// <summary>Records that an install has begun (ADR-0008 §5.1 step 9 completes it).</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is unassigned, <paramref name="packageId"/> is not a package id,
    /// <paramref name="version"/> or <paramref name="installedBy"/> is blank or too long, or
    /// <paramref name="startedAt"/> is not UTC.
    /// </exception>
    public static InstalledPackage Begin(
        TenantId tenantId,
        string packageId,
        string version,
        string installedBy,
        DateTimeOffset startedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("The tenant id is unassigned.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedBy);

        if (!PackageIdFormat.IsWellFormed(packageId))
        {
            throw new ArgumentException(
                $"'{packageId}' is not a package id: {PackageIdFormat.Description} - the stem of the " +
                "package's pkg_<id> schema (ADR-0008 4.1).",
                nameof(packageId));
        }

        if (version.Length > MaxVersionLength || version.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"Expected 1 to {MaxVersionLength} characters with no whitespace.",
                nameof(version));
        }

        if (installedBy.Length > MaxInstalledByLength || installedBy != installedBy.Trim())
        {
            throw new ArgumentException(
                $"Expected 1 to {MaxInstalledByLength} characters with no leading or trailing whitespace.",
                nameof(installedBy));
        }

        return new InstalledPackage
        {
            TenantId = tenantId,
            PackageId = packageId,
            Version = version,
            State = InstalledPackageState.Installing,
            InstalledAt = UtcInstant.Require(startedAt, nameof(startedAt)),
            InstalledBy = installedBy,
        };
    }
}
