using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Secrets;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// <see cref="ITenantConnectionResolver"/> for the application path: the cached routing row, a
/// state check, the credential from the secret store, and the composed connection string.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which states may be routed.</b> <c>Active</c>, and <c>Suspended</c> — ADR-0007 §11.4 keeps
/// a suspended tenant readable behind an end-of-service banner, so the application still connects
/// and the read-only restriction is enforced elsewhere. Every other state is refused here:
/// <c>Provisioning</c> and <c>ProvisioningFailed</c> have no usable database yet; <c>SchemaBlocked</c>
/// is served a maintenance page (§7.4, §7.5, §4.3); <c>PendingDeletion</c> has had <c>CONNECT</c>
/// revoked from everyone but <c>aurora_admin</c>; <c>Deleted</c> is a tombstone with no routing
/// columns at all. <c>Exporting</c> is refused too, on the fail-closed reading: the export job
/// (§11.4) has no backlog row yet, and what it needs can be granted when it is written rather than
/// assumed now. The DDL path never comes through here (ADR-0027 §2); it reads the routing row
/// through the same <see cref="ITenantRoutingReader"/> and applies its own rules.
/// </para>
/// <para>
/// <b>The credential is read on every resolve.</b> The cached row carries the secret's reference;
/// the store is consulted each time, so a rotated secret takes effect on the next resolve without
/// touching the cache (ADR-0011: rotation is an overlap window, not a cutover) and no password is
/// ever held in a cache entry.
/// </para>
/// </remarks>
internal sealed class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly TenantRoutingCache _cache;
    private readonly ITenantRoutingReader _reader;
    private readonly ISecretStore _secrets;
    private readonly TenantPoolSettings _pool;

    public TenantConnectionResolver(
        TenantRoutingCache cache,
        ITenantRoutingReader reader,
        ISecretStore secrets,
        TenantPoolSettings pool)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(pool);
        _cache = cache;
        _reader = reader;
        _secrets = secrets;
        _pool = pool;
    }

    /// <summary>Whether the application path may connect to a tenant in <paramref name="state"/>.</summary>
    public static bool IsRoutable(TenantState state) => state is TenantState.Active or TenantState.Suspended;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is unassigned.</exception>
    /// <exception cref="InvalidOperationException">
    /// The catalog row is in a routable state but lacks a routing column, which
    /// <c>ck_tenant_routing_present_unless_deleted</c> should make impossible.
    /// </exception>
    public async ValueTask<TenantConnection> ResolveAsync(TenantId tenantId, CancellationToken ct)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("The tenant id is unassigned; nothing can be resolved for it.", nameof(tenantId));
        }

        TenantRouting routing = await _cache.GetOrReadAsync(tenantId, _reader, ct);

        if (!IsRoutable(routing.State))
        {
            throw new TenantNotRoutableException(tenantId, routing.State);
        }

        if (routing.Cluster is null || routing.DatabaseName is null || routing.ResidencyRegion is null)
        {
            throw new InvalidOperationException(
                $"catalog.tenant {tenantId} is {routing.State} yet has no cluster, database name or residency region; " +
                "ck_tenant_routing_present_unless_deleted requires all three in every state but Deleted.");
        }

        string appPassword = await _secrets.ReadAsync(SecretReference.Of(routing.Cluster.AppSecretRef), ct);

        string connectionString = TenantConnectionStringComposer.Compose(
            routing.Cluster,
            routing.DatabaseName,
            routing.TenantKey,
            appPassword,
            _pool);

        return new TenantConnection(
            connectionString,
            ClusterId.Parse(routing.Cluster.ClusterId, null),
            routing.DatabaseName,
            Region.Parse(routing.ResidencyRegion, null));
    }
}
