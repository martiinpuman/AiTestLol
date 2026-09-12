using System;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Secrets;
using Aurora.Platform.Tenancy.UnitTests.Catalog;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests;

/// <summary>
/// What the two DI extensions register, resolved from a container built the way the composition
/// root will build it. No database is touched: the catalog context is registered over an offline
/// connection string and never opened.
/// </summary>
public sealed class TenancyRegistrationTests
{
    [Fact]
    public void The_resolver_and_its_collaborators_resolve_from_the_production_registration()
    {
        using ServiceProvider provider = Build(TenantPoolProfile.Worker);
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ShouldBeOfType<TenantConnectionResolver>();
        scope.ServiceProvider.GetRequiredService<ITenantRoutingReader>().ShouldBeOfType<CatalogTenantRoutingReader>();
        scope.ServiceProvider.GetRequiredService<ISecretStore>().ShouldBeOfType<EnvironmentSecretStore>();
        scope.ServiceProvider.GetRequiredService<TenantPoolSettings>().ShouldBeSameAs(TenantPoolSettings.Worker);
        scope.ServiceProvider.GetRequiredService<HybridCache>().ShouldNotBeNull();
    }

    [Fact]
    public void The_routing_cache_is_one_per_process()
    {
        using ServiceProvider provider = Build(TenantPoolProfile.Web);

        TenantRoutingCache first;
        TenantRoutingCache second;
        using (IServiceScope scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<TenantRoutingCache>();
        }

        using (IServiceScope scope = provider.CreateScope())
        {
            second = scope.ServiceProvider.GetRequiredService<TenantRoutingCache>();
        }

        second.ShouldBeSameAs(first, "an entry removed in one request must be gone for the next");
    }

    [Fact]
    public void A_second_pool_profile_is_refused_rather_than_silently_ignored()
    {
        var services = new ServiceCollection();
        services.AddCatalogDatabase(OfflineCatalog.ConnectionString);
        services.AddTenantConnectionResolver(TenantPoolProfile.Web);

        Should.Throw<InvalidOperationException>(() => services.AddTenantConnectionResolver(TenantPoolProfile.Worker));
    }

    [Fact]
    public void A_profile_outside_the_enum_is_refused_at_registration()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentOutOfRangeException>(() => services.AddTenantConnectionResolver((TenantPoolProfile)42));
    }

    private static ServiceProvider Build(TenantPoolProfile profile)
    {
        var services = new ServiceCollection();
        services.AddCatalogDatabase(OfflineCatalog.ConnectionString);
        services.AddTenantConnectionResolver(profile);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
