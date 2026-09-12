using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// A <see cref="HybridCache"/> that counts the entries it is asked to store, in front of the real
/// one. The instrument for PR #14 n-1: "never stored" and "stored, then evicted" read the same
/// from the reader's count alone, and only a write count tells them apart.
/// </summary>
/// <remarks>
/// A write is a factory that completed — the cache underneath stores what a factory returns and
/// nothing when it throws — or a <see cref="SetAsync{T}"/>. Reads and removals pass straight
/// through.
/// </remarks>
internal sealed class CountingHybridCache : HybridCache
{
    private readonly HybridCache _inner;
    private int _writes;

    public CountingHybridCache(HybridCache inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Entries stored so far: completed factories plus explicit sets.</summary>
    public int Writes => Volatile.Read(ref _writes);

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetOrCreateAsync(
            key,
            (state, factory, counter: this),
            static async (s, ct) =>
            {
                T value = await s.factory(s.state, ct);
                Interlocked.Increment(ref s.counter._writes);
                return value;
            },
            options,
            tags,
            cancellationToken);

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _writes);
        return _inner.SetAsync(key, value, options, tags, cancellationToken);
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.RemoveAsync(key, cancellationToken);

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        _inner.RemoveByTagAsync(tag, cancellationToken);
}
