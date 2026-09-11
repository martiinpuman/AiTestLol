using Microsoft.EntityFrameworkCore;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The one place the catalog's provider options are set, shared by the runtime registration and
/// the design-time factory so that <c>dotnet ef</c> and the application agree on where the
/// migrations history lives.
/// </summary>
internal static class CatalogDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(CatalogDbContext.MigrationsHistoryTable, CatalogDbContext.Schema));
}
