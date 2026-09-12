using System;
using System.Linq;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aurora.Platform.Tenancy;

/// <summary>
/// Registers the catalog database for the composition root (<c>Aurora.Composition</c>).
/// </summary>
/// <remarks>
/// <para>
/// The catalog context is the only <c>DbContext</c> in the system registered conventionally
/// (ADR-0003 rule 3, ADR-0007 §4.2). The connection string arrives from host configuration bound
/// to typed options in the host — its password is a tier-2 secret (ADR-0011) and reaches this
/// method already composed; nothing here reads configuration or logs the string.
/// </para>
/// <para>
/// The routing cache (ADR-0007 §3.5, ADR-0012 §3) is registered here rather than with the
/// resolver, because it is a property of the catalog context, not of its readers: every tenant
/// state change written through the context must reach the cache, whichever host wrote it and
/// whether or not that host also resolves connections. The invalidation is a
/// <c>SaveChangesInterceptor</c> attached to the context's options here, so a context obtained
/// from the container cannot be had without it.
/// </para>
/// </remarks>
public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// Adds the catalog <c>DbContext</c>, scoped, over <paramref name="connectionString"/>, with
    /// the routing cache the resolver reads through and the interceptor that invalidates it on a
    /// tenant state change.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// A catalog is already registered. A process has one catalog; the routing cache registered
    /// alongside it is keyed by tenant alone, so a second catalog would have two sources of truth
    /// sharing one keyspace, whichever context won resolution (PR #14 L-4).
    /// </exception>
    public static IServiceCollection AddCatalogDatabase(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(CatalogDbContext)))
        {
            throw new InvalidOperationException(
                "AddCatalogDatabase was already called on this service collection; a process has exactly one " +
                "catalog, and the routing cache registered with it is keyed by tenant alone (ADR-0007 §9, ADR-0012 §3).");
        }

        services.AddHybridCache();
        services.TryAddSingleton<TenantRoutingCache>();
        services.TryAddSingleton<TenantRoutingCacheInvalidator>();

        services.AddDbContext<CatalogDbContext>((provider, options) =>
        {
            CatalogDbContextOptions.Configure(options, connectionString);
            options.AddInterceptors(provider.GetRequiredService<TenantRoutingCacheInvalidator>());
        });
        return services;
    }
}
