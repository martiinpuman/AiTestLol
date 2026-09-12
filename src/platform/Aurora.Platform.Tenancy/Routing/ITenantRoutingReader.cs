using System.Threading;
using System.Threading.Tasks;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The one catalog read that maps a tenant to physical storage: <c>catalog.tenant</c> joined to
/// <c>catalog.database_cluster</c> (ADR-0007 §3.5).
/// </summary>
/// <remarks>
/// Separated from the resolver so that the read can be counted, replaced with a fake, and — the
/// reason it has a name — reused by the DDL path: <c>ITenantAdminConnectionFactory</c> (B-07.1)
/// calls this rather than writing a second query over the same two tables, "so there is still
/// exactly one place that knows how a tenant maps to physical storage" (ADR-0027 §2).
/// </remarks>
internal interface ITenantRoutingReader
{
    /// <summary>
    /// Reads the routing row for <paramref name="tenantId"/>, or <see langword="null"/> when no
    /// <c>catalog.tenant</c> row has that id.
    /// </summary>
    ValueTask<TenantRouting?> ReadAsync(TenantId tenantId, CancellationToken ct);
}
