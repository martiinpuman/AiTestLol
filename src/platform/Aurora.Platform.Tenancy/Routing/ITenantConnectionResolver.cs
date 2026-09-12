using System.Threading;
using System.Threading.Tasks;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// Turns a <see cref="TenantId"/> into the connection the application path uses to reach that
/// tenant's database (ADR-0007 §3.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only place that knows how a tenant maps to physical storage.</b> It reads
/// <c>catalog.tenant</c> joined to <c>catalog.database_cluster</c>, keeps the routing row for 60 s
/// in <c>HybridCache</c> under a tenant-keyed <c>c:</c> entry (ADR-0012 §3), and composes the
/// connection string in memory with the credential from the host's secret store. Every caller
/// goes through it, so moving a tenant to another cluster, adding a read replica, or the
/// shared-schema escape hatch of ADR-0007 §6 is a change inside this one implementation.
/// </para>
/// <para>
/// ADR-0007 §3.5 places this interface in <c>Aurora.Platform.Tenancy.Contracts</c>. It is
/// internal to <c>Aurora.Platform.Tenancy</c> for now: its consumers — the data-source cache
/// (B-06.2), the scope factory (B-06.3) and the DDL-path factory (B-07.1, ADR-0027 §2) — all
/// live here, and nothing outside this assembly should be able to hold a connection string
/// (<c>modules.md</c> §4). Promoting it is a one-file move when a consumer outside the assembly
/// exists.
/// </para>
/// </remarks>
internal interface ITenantConnectionResolver
{
    /// <summary>Resolves the connection for <paramref name="tenantId"/>.</summary>
    /// <exception cref="TenantNotFoundException">No <c>catalog.tenant</c> row has this id.</exception>
    /// <exception cref="TenantNotRoutableException">
    /// The tenant exists but is in a state the application path may not connect to.
    /// </exception>
    ValueTask<TenantConnection> ResolveAsync(TenantId tenantId, CancellationToken ct);
}
