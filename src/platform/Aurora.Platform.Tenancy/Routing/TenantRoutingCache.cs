using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.SharedKernel;
using Microsoft.Extensions.Caching.Hybrid;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The 60-second routing entry of ADR-0007 §3.5 / ADR-0012 §3, keyed per tenant: what the
/// resolver reads through, and what a tenant state change removes.
/// </summary>
/// <remarks>
/// <para>
/// One class knows the key, the lifetime and the read-through, so that the resolver and the
/// invalidator cannot disagree about which entry they mean. Registered once per process; the
/// reader is handed in per call because it is scoped to a unit of work and a singleton must not
/// capture one.
/// </para>
/// <para>
/// <b>A tenant that does not exist is never cached.</b> The read-through throws
/// <see cref="TenantNotFoundException"/> instead of storing a null, so a tenant provisioned a
/// moment after a mistaken lookup is visible at once, and an id that names nothing costs one
/// indexed primary-key read per call rather than a negative entry per attacker guess.
/// </para>
/// <para>
/// <b>What invalidation does and does not promise.</b> <see cref="InvalidateAsync"/> removes the
/// entry from this process's L1; with no L2 backend yet (ADR-0012), another instance learns of the
/// change when its own entry expires, within 60 s. And a read that was already in flight when the
/// entry was removed can still store the row it read a moment earlier — a cache-aside cache has no
/// version to compare against — so the guarantee is "the next resolve after a committed state
/// change re-reads the catalog", bounded by the same 60 s in the racing case. Both bounds are the
/// staleness ADR-0012 states as part of the security story.
/// </para>
/// </remarks>
internal sealed class TenantRoutingCache
{
    /// <summary>ADR-0012 §3: tenant routing / connection, 60 s.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeToLive,
        LocalCacheExpiration = TimeToLive,
    };

    private readonly HybridCache _cache;

    public TenantRoutingCache(HybridCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <summary>The key one tenant's routing entry lives under.</summary>
    public static string KeyFor(TenantId tenantId) => CatalogCacheKey.For(CatalogCacheKind.TenantRouting, tenantId.ToString());

    /// <summary>
    /// The routing row for <paramref name="tenantId"/>: from the cache while the entry is live,
    /// otherwise read through <paramref name="reader"/> and kept for <see cref="TimeToLive"/>.
    /// Concurrent misses for one tenant collapse into one read (ADR-0012, stampede protection).
    /// </summary>
    /// <exception cref="TenantNotFoundException">No <c>catalog.tenant</c> row has this id.</exception>
    public ValueTask<TenantRouting> GetOrReadAsync(TenantId tenantId, ITenantRoutingReader reader, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return _cache.GetOrCreateAsync(
            KeyFor(tenantId),
            (tenantId, reader),
            static async (state, token) =>
                await state.reader.ReadAsync(state.tenantId, token) ?? throw new TenantNotFoundException(state.tenantId),
            EntryOptions,
            tags: null,
            ct);
    }

    /// <summary>Removes the tenant's entry, so that the next resolve reads the catalog again.</summary>
    public ValueTask InvalidateAsync(TenantId tenantId, CancellationToken ct) => _cache.RemoveAsync(KeyFor(tenantId), ct);
}
