using System;
using Aurora.Platform.Tenancy.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace Aurora.Platform.Tenancy;

/// <summary>
/// Registers the catalog database for the composition root (<c>Aurora.Composition</c>).
/// </summary>
/// <remarks>
/// The catalog context is the only <c>DbContext</c> in the system registered conventionally
/// (ADR-0003 rule 3, ADR-0007 §4.2). The connection string arrives from host configuration bound
/// to typed options in the host — its password is a tier-2 secret (ADR-0011) and reaches this
/// method already composed; nothing here reads configuration or logs the string.
/// </remarks>
public static class CatalogServiceCollectionExtensions
{
    /// <summary>Adds the catalog <c>DbContext</c>, scoped, over <paramref name="connectionString"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is blank.</exception>
    public static IServiceCollection AddCatalogDatabase(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CatalogDbContext>(options => CatalogDbContextOptions.Configure(options, connectionString));
        return services;
    }
}
