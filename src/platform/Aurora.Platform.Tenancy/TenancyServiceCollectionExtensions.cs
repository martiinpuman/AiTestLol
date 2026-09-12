using System;
using System.Linq;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aurora.Platform.Tenancy;

/// <summary>
/// Registers the tenant connection resolver for the composition root (<c>Aurora.Composition</c>).
/// </summary>
/// <remarks>
/// Needs <see cref="CatalogServiceCollectionExtensions.AddCatalogDatabase"/> as well: the resolver
/// reads through the catalog context and the routing cache that call registers. The resolver and
/// its catalog reader are scoped, like the context they read through; the pool settings and the
/// secret store are process-wide.
/// </remarks>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Adds <c>ITenantConnectionResolver</c> under the ADR-0007 §5.2 pool settings for
    /// <paramref name="profile"/>, with the environment-variable secret store of ADR-0011 tier 2.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="profile"/> is not a value of the enum.</exception>
    /// <exception cref="InvalidOperationException">
    /// A resolver is already registered. One process runs as one host, under one column of the
    /// §5.2 table; a second registration would silently keep whichever came first.
    /// </exception>
    public static IServiceCollection AddTenantConnectionResolver(this IServiceCollection services, TenantPoolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        TenantPoolSettings settings = TenantPoolSettings.For(profile);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(TenantPoolSettings)))
        {
            throw new InvalidOperationException(
                "AddTenantConnectionResolver was already called on this service collection; a process runs under " +
                "exactly one pool profile (ADR-0007 §5.2).");
        }

        services.AddSingleton(settings);
        services.TryAddSingleton<ISecretStore, EnvironmentSecretStore>();
        services.TryAddScoped<ITenantRoutingReader, CatalogTenantRoutingReader>();
        services.TryAddScoped<ITenantConnectionResolver, TenantConnectionResolver>();
        return services;
    }
}
