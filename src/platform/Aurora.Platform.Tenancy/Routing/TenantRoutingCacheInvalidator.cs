using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// Removes a tenant's routing entry when its catalog row changes state or routing
/// (ADR-0007 §3.5 "invalidated on tenant state change"; ADR-0012 rule 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>A mechanism on the context, not a convention on its callers.</b> This is a
/// <see cref="SaveChangesInterceptor"/> attached to every <c>CatalogDbContext</c> the container
/// hands out (<see cref="CatalogServiceCollectionExtensions.AddCatalogDatabase"/>), so a suspend,
/// a <c>SchemaBlocked</c> mark (B-06.2, B-08), a <c>ProvisioningFailed</c> mark (B-07) or a cluster
/// move written through the entity reaches the cache without the writer knowing the cache exists.
/// What it cannot see: a state change written around the change tracker — raw SQL,
/// <c>ExecuteUpdate</c> — which is bounded by the entry's 60 s lifetime and is the writer's to
/// invalidate through <see cref="TenantRoutingCache"/> directly.
/// </para>
/// <para>
/// The set of tenants to invalidate is taken <em>before</em> the save, because after it every entry
/// is <c>Unchanged</c> and nothing records what moved; it is acted on <em>after</em> the save, so
/// that a save that fails leaves the cache as it was — the row did not change. A synchronous
/// <c>SaveChanges</c> takes the same path and waits for the removal, which with the L1-only cache
/// of ADR-0012 completes at once; an L2 backend would make that wait a real one and is the point at
/// which the synchronous path should go.
/// </para>
/// </remarks>
internal sealed class TenantRoutingCacheInvalidator : SaveChangesInterceptor
{
    /// <summary>
    /// The columns of <c>catalog.tenant</c> that reach a resolved connection: the state that
    /// decides whether the tenant routes at all, and the four the connection string is composed
    /// from (the key names the pool in <c>pg_stat_activity</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> RoutingProperties =
    [
        nameof(Tenant.State),
        nameof(Tenant.Key),
        nameof(Tenant.ClusterId),
        nameof(Tenant.DatabaseName),
        nameof(Tenant.ResidencyRegion),
    ];

    private readonly TenantRoutingCache _cache;
    private readonly ConditionalWeakTable<DbContext, TenantId[]> _pending = new();

    public TenantRoutingCacheInvalidator(TenantRoutingCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <summary>
    /// The tenants whose routing the pending save would change: every tracked <see cref="Tenant"/>
    /// that is added, deleted, or modified in one of <see cref="RoutingProperties"/>.
    /// </summary>
    public static TenantId[] TenantsWhoseRoutingChanged(ChangeTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        return [.. tracker.Entries<Tenant>().Where(RoutingChanged).Select(static entry => entry.Entity.Id)];
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Remember(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Remember(eventData);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        InvalidateRememberedAsync(eventData, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await InvalidateRememberedAsync(eventData, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData);
        return Task.CompletedTask;
    }

    private static bool RoutingChanged(EntityEntry<Tenant> entry) => entry.State switch
    {
        EntityState.Added or EntityState.Deleted => true,
        EntityState.Modified => RoutingProperties.Any(property => entry.Property(property).IsModified),
        _ => false,
    };

    private void Remember(DbContextEventData eventData)
    {
        if (eventData.Context is null)
        {
            return;
        }

        _pending.AddOrUpdate(eventData.Context, TenantsWhoseRoutingChanged(eventData.Context.ChangeTracker));
    }

    private void Forget(DbContextEventData eventData)
    {
        if (eventData.Context is not null)
        {
            _pending.Remove(eventData.Context);
        }
    }

    private async ValueTask InvalidateRememberedAsync(DbContextEventData eventData, CancellationToken ct)
    {
        if (eventData.Context is null || !_pending.TryGetValue(eventData.Context, out TenantId[]? tenants))
        {
            return;
        }

        _pending.Remove(eventData.Context);
        foreach (TenantId tenant in tenants)
        {
            await _cache.InvalidateAsync(tenant, ct);
        }
    }
}
