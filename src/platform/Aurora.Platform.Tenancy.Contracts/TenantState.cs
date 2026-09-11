namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Where a tenant is in its life, from the first catalog row to the tombstone
/// (ADR-0007 §8, §7.4, §9.2, §11.4).
/// </summary>
/// <remarks>
/// <para>
/// The whole lifecycle is declared here from the first migration, not the happy path only:
/// provisioning (B-07), the migration runner (B-08) and the identity check (B-06) each stamp a
/// state this enum has to already contain, and widening a check constraint under live tenants is
/// exactly the kind of change the expand/contract rule exists to avoid.
/// </para>
/// <para>
/// Stored as its name, not its number, so that a catalog row is readable in <c>psql</c> and so
/// that reordering this enum can never silently relabel a tenant. The catalog's check constraint
/// on <c>catalog.tenant.state</c> is generated from these names.
/// </para>
/// <para>
/// The default value is <see cref="Provisioning"/> on purpose: a tenant whose state nobody set is
/// not routable, which is the safe way for a mistake to fail.
/// </para>
/// </remarks>
public enum TenantState
{
    /// <summary>
    /// The row is reserved and the provisioning saga is somewhere in its nine steps (ADR-0007 §8).
    /// Not routable.
    /// </summary>
    Provisioning,

    /// <summary>
    /// Provisioning gave up after its retry budget (ADR-0007 §8, reaper). The only state from which
    /// an operator may destroy the database, and only after re-reading its identity stamp.
    /// </summary>
    ProvisioningFailed,

    /// <summary>Routable and trading. The state every fan-out job selects on (ADR-0007 §10.2).</summary>
    Active,

    /// <summary>
    /// Read-only, with an end-of-service banner; jobs are skipped (ADR-0007 §11.4). Reversible.
    /// </summary>
    Suspended,

    /// <summary>
    /// Served a maintenance page because its schema cannot be trusted: a migration quarantined it
    /// (§7.4), its schema version fell outside what the code supports (§7.5), or a connection reached
    /// a database stamped for a different tenant (§4.3).
    /// </summary>
    SchemaBlocked,

    /// <summary>The offboarding export is being produced (ADR-0007 §11.4).</summary>
    Exporting,

    /// <summary>
    /// The database is renamed and unreachable except by <c>aurora_admin</c>; deletion is due at
    /// <c>deletion_due_at</c> and the state is reversible until then (ADR-0007 §11.4).
    /// </summary>
    PendingDeletion,

    /// <summary>
    /// The database is dropped and the catalog row is a tombstone holding id, key and dates only
    /// (ADR-0007 §11.4). Terminal.
    /// </summary>
    Deleted,
}
