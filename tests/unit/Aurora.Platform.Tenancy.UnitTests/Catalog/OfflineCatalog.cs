using Aurora.Platform.Tenancy.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// A <see cref="CatalogDbContext"/> that can build its model but never connects: the Npgsql
/// provider is needed for the type mappings, the host named here does not exist and is never
/// contacted.
/// </summary>
internal static class OfflineCatalog
{
    public static CatalogDbContext Open()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseNpgsql("Host=unused.invalid;Database=unused;Username=unused");
        return new CatalogDbContext(options.Options);
    }
}
