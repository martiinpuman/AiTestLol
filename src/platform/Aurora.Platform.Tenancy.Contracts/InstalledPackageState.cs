namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Where a Country Package stands in one tenant (ADR-0008 §4, <c>catalog.installed_package</c>).
/// </summary>
/// <remarks>
/// Declared with the tenant registry because the registry row is what a scope open reads to know
/// which packages a tenant has (ADR-0007 §3.4, <c>TenantScope.Packages</c>); the installer that
/// moves a package through these states arrives with B-13. Stored by name, like
/// <see cref="TenantState"/>, and for the same reasons.
/// </remarks>
public enum InstalledPackageState
{
    /// <summary>The install steps of ADR-0008 §5.1 are running. Not yet usable.</summary>
    Installing,

    /// <summary>Installed and in use.</summary>
    Active,

    /// <summary>
    /// Switched off but not removed: the tenant has posted under this jurisdiction, so its data is
    /// part of their statutory record (ADR-0008 §5.3).
    /// </summary>
    Deactivated,

    /// <summary>A newer version is staged and waits for the tenant's next migration wave.</summary>
    UpgradePending,

    /// <summary>An install or upgrade step failed and needs an operator.</summary>
    Failed,
}
