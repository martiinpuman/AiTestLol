using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Routing;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// An <see cref="ITenantRoutingReader"/> over an in-memory catalog that counts every read per
/// tenant — the instrument every cache assertion in this project is made with. A read that
/// reached this class is a read the resolver made past its cache; a read that did not, was served
/// from it.
/// </summary>
internal sealed class CountingRoutingReader : ITenantRoutingReader
{
    private readonly ConcurrentDictionary<Guid, TenantRouting> _rows = new();
    private readonly ConcurrentDictionary<Guid, int> _reads = new();

    /// <summary>Every read so far, over every tenant.</summary>
    public int TotalReads => _reads.Values.Sum();

    /// <summary>Puts <paramref name="row"/> in the catalog, replacing any row with the same id.</summary>
    public TenantRouting Holding(TenantRouting row)
    {
        _rows[row.TenantId] = row;
        return row;
    }

    public int ReadsOf(TenantRouting row) => ReadsOf(TenantId.From(row.TenantId));

    public int ReadsOf(TenantId tenantId) => _reads.TryGetValue(tenantId.Value, out int reads) ? reads : 0;

    public ValueTask<TenantRouting?> ReadAsync(TenantId tenantId, CancellationToken ct)
    {
        _reads.AddOrUpdate(tenantId.Value, 1, static (_, reads) => reads + 1);
        return ValueTask.FromResult(_rows.TryGetValue(tenantId.Value, out TenantRouting? row) ? row : null);
    }
}
