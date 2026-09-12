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
/// <para>
/// The cache the resolver sees is the real one behind <see cref="CountingHybridCache"/>, which
/// counts what is stored: with the reader's count alone, a row that was never stored and a row
/// that was stored and then evicted look the same (PR #14 n-1). The real cache is built in a
/// provider of its own, so that the decorator can be registered ahead of <c>AddCatalogDatabase</c>,
/// whose <c>TryAdd</c> then keeps it.
/// </para>
/// <para>
/// Time is a <see cref="FakeTimeProvider"/>, handed to both clocks the cache reads: the
/// <c>TimeProvider</c> HybridCache resolves from its container, and the <c>ISystemClock</c> the
/// underlying <c>IMemoryCache</c> still takes through its options — the only seam that type
/// offers, obsolete but functional, and the reason for the pragma below.
/// </para>
/// </remarks>
internal sealed class ResolverHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _cacheOwner;
    private readonly ServiceProvider _provider;
    private readonly CountingHybridCache _cache;

    public ResolverHarness(TenantPoolProfile profile = TenantPoolProfile.Web)
    {
        var cacheOwner = new ServiceCollection();
        cacheOwner.AddSingleton<TimeProvider>(Clock);
        cacheOwner.AddHybridCache();
#pragma warning disable CS0618 // ISystemClock is obsolete; MemoryCacheOptions offers no other clock.
        cacheOwner.Configure<MemoryCacheOptions>(options => options.Clock = new FakeSystemClock(Clock));
#pragma warning restore CS0618
        _cacheOwner = cacheOwner.BuildServiceProvider();
        _cache = new CountingHybridCache(_cacheOwner.GetRequiredService<HybridCache>());

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<HybridCache>(_cache);
        services.AddCatalogDatabase(OfflineCatalog.ConnectionString);
        services.AddTenantConnectionResolver(profile);
        services.AddScoped<ITenantRoutingReader>(_ => Reader);
        services.AddSingleton<ISecretStore>(Secrets);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public CountingRoutingReader Reader { get; } = new();

    public InMemorySecretStore Secrets { get; } = new InMemorySecretStore().Holding(ARouting.DefaultAppSecretRef, "harness-app-secret");

    public FakeTimeProvider Clock { get; } = new(Start);

    public TenantRoutingCache Cache => _provider.GetRequiredService<TenantRoutingCache>();

    /// <summary>The cache underneath, for a test that plants an entry the way a mis-keyed backend would.</summary>
    public HybridCache HybridCache => _cache;

    /// <summary>Entries the cache was asked to store: completed read-throughs plus planted entries.</summary>
    public int CacheWrites => _cache.Writes;

    public Task<TenantConnection> ResolveAsync(TenantRouting row) => ResolveAsync(TenantId.From(row.TenantId));

    public async Task<TenantConnection> ResolveAsync(TenantId tenantId)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ResolveAsync(tenantId, CancellationToken.None);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cacheOwner.Dispose();
    }

#pragma warning disable CS0618 // ISystemClock is obsolete; see the type remarks.
    private sealed class FakeSystemClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private readonly TimeProvider _time;

        public FakeSystemClock(TimeProvider time) => _time = time;

        public DateTimeOffset UtcNow => _time.GetUtcNow();
    }
#pragma warning restore CS0618
}
