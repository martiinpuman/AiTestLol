using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Secrets;
using Aurora.Platform.Tenancy.UnitTests.Catalog;
using Aurora.SharedKernel;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// The resolver as the composition root builds it — <c>AddCatalogDatabase</c> plus
/// <c>AddTenantConnectionResolver</c>, a real <c>HybridCache</c> — with the two I/O edges
/// replaced: the catalog read by <see cref="CountingRoutingReader"/> and the secret store by
/// <see cref="InMemorySecretStore"/>. Every resolve runs in a fresh scope, as a request would.
/// </summary>
/// <remarks>
/// Time is a <see cref="FakeTimeProvider"/>, handed to both clocks the cache reads: the
/// <c>TimeProvider</c> HybridCache resolves from the container, and the <c>ISystemClock</c> the
/// underlying <c>IMemoryCache</c> still takes through its options — the only seam that type
/// offers, obsolete but functional, and the reason for the pragma below.
/// </remarks>
internal sealed class ResolverHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;

    public ResolverHarness(TenantPoolProfile profile = TenantPoolProfile.Web)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddCatalogDatabase(OfflineCatalog.ConnectionString);
        services.AddTenantConnectionResolver(profile);
        services.AddScoped<ITenantRoutingReader>(_ => Reader);
        services.AddSingleton<ISecretStore>(Secrets);
#pragma warning disable CS0618 // ISystemClock is obsolete; MemoryCacheOptions offers no other clock.
        services.Configure<MemoryCacheOptions>(options => options.Clock = new FakeSystemClock(Clock));
#pragma warning restore CS0618
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public CountingRoutingReader Reader { get; } = new();

    public InMemorySecretStore Secrets { get; } = new InMemorySecretStore().Holding(ARouting.DefaultAppSecretRef, "harness-app-secret");

    public FakeTimeProvider Clock { get; } = new(Start);

    public TenantRoutingCache Cache => _provider.GetRequiredService<TenantRoutingCache>();

    /// <summary>The cache underneath, for a test that plants an entry the way a mis-keyed backend would.</summary>
    public HybridCache HybridCache => _provider.GetRequiredService<HybridCache>();

    public Task<TenantConnection> ResolveAsync(TenantRouting row) => ResolveAsync(TenantId.From(row.TenantId));

    public async Task<TenantConnection> ResolveAsync(TenantId tenantId)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ResolveAsync(tenantId, CancellationToken.None);
    }

    public void Dispose() => _provider.Dispose();

#pragma warning disable CS0618 // ISystemClock is obsolete; see the type remarks.
    private sealed class FakeSystemClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private readonly TimeProvider _time;

        public FakeSystemClock(TimeProvider time) => _time = time;

        public DateTimeOffset UtcNow => _time.GetUtcNow();
    }
#pragma warning restore CS0618
}
