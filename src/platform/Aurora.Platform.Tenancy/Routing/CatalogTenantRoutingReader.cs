using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// <see cref="ITenantRoutingReader"/> over the catalog: one statement, a left join from the
/// tenant to its cluster, run as whichever role the catalog context holds — <c>aurora_app</c> on
/// the request path, which holds <c>SELECT</c> on both tables and nothing more it needs here.
/// </summary>
/// <remarks>
/// A left join, not an inner one, because a tombstone (ADR-0007 §11.4) has no cluster and must
/// still come back as the row it is — <c>Deleted</c> — rather than as "unknown tenant", which
/// would send the caller looking for a catalog bug that is not there.
/// </remarks>
internal sealed class CatalogTenantRoutingReader : ITenantRoutingReader
{
    private readonly CatalogDbContext _catalog;

    public CatalogTenantRoutingReader(CatalogDbContext catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public async ValueTask<TenantRouting?> ReadAsync(TenantId tenantId, CancellationToken ct)
    {
        var row = await (
            from tenant in _catalog.Tenants
            where tenant.Id == tenantId
            from cluster in _catalog.DatabaseClusters
                .Where(candidate => candidate.Id == tenant.ClusterId && candidate.Region == tenant.ResidencyRegion)
                .DefaultIfEmpty()
            select new { Tenant = tenant, Cluster = cluster })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        ClusterEndpoint? endpoint = row.Cluster is null
            ? null
            : new ClusterEndpoint(
                row.Cluster.Id.Value,
                row.Cluster.Host,
                row.Cluster.Port,
                row.Cluster.MaintenanceDatabase,
                row.Cluster.AdminSecretRef.Value,
                row.Cluster.MigratorSecretRef.Value,
                row.Cluster.AppSecretRef.Value);

        return new TenantRouting(
            row.Tenant.Id.Value,
            row.Tenant.Key.Value,
            row.Tenant.State,
            endpoint,
            row.Tenant.DatabaseName,
            row.Tenant.ResidencyRegion?.Value);
    }
}
