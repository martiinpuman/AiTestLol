namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// Whether a cluster may be given new tenants (ADR-0007 §8 step 1 chooses a cluster "from
/// residency region and free capacity"; this is the operator's half of "free").
/// </summary>
/// <remarks>
/// ADR-0007 §9.2 lists <c>database_cluster.state</c> without enumerating it. These three are the
/// minimum an operator needs: open a cluster, stop placing new tenants on it while it still serves
/// the ones it has, and record that it is gone. Stored by name, like every other state in the
/// catalog.
/// </remarks>
internal enum DatabaseClusterState
{
    /// <summary>New tenants may be placed here, up to <c>max_tenants</c>.</summary>
    Accepting,

    /// <summary>Serves its existing tenants; takes no new ones.</summary>
    Closed,

    /// <summary>Holds no tenants any more. Kept so history that names it still resolves.</summary>
    Retired,
}
