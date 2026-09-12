using System;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The routing row handed back for a tenant belongs to another tenant: a reader, a cache entry
/// or a cache backend answered the wrong row, and composing it would have produced a credentialed
/// connection to another tenant's database under that tenant's application name (PR #14 M-1).
/// </summary>
/// <remarks>
/// <b>Branch-local and temporary — superseded by <c>TenantRoutingViolationException.Mismatch</c>
/// when <c>task/B-06.1a</c> merges.</b> That type is ADR-0007 §4.3's, declared in
/// <c>Aurora.Platform.Tenancy.Contracts</c> for the database-side check (the identity stamp) and
/// not on this branch; this is its catalog-side sibling and carries the same facts — the tenant
/// asked for, the tenant found, the database named — so the fold is a rename. The orchestrator owns
/// that reconciliation.
/// </remarks>
internal sealed class TenantRoutingMismatchException : Exception
{
    public TenantRoutingMismatchException(TenantId requested, Guid found, string? databaseName)
        : base(
            $"Routing violation: the routing row handed back for tenant {requested} belongs to tenant {found} " +
            $"(database '{databaseName ?? "<none>"}'). A row that is not the tenant's is never composed into a " +
            "connection; the request is refused.")
    {
        Requested = requested;
        Found = found;
        DatabaseName = databaseName;
    }

    /// <summary>The tenant the resolve was for.</summary>
    public TenantId Requested { get; }

    /// <summary>
    /// The tenant the row belongs to, as the raw value: a poisoned row may carry an unassigned id,
    /// which <see cref="TenantId"/> refuses to wrap, and the refusal must not turn into a different
    /// exception.
    /// </summary>
    public Guid Found { get; }

    /// <summary>The database the row would have routed to, or <see langword="null"/> for a tombstone.</summary>
    public string? DatabaseName { get; }
}
