using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// What <c>dotnet ef migrations add</c> uses to build the model. Scaffolding never opens the
/// connection, so the string names the provider and the history table and nothing secret.
/// </summary>
/// <remarks>
/// <c>dotnet ef database update</c> against a real database is deliberately not the way the
/// catalog is migrated: that is the migration runner's job (B-08), connecting as
/// <c>aurora_migrator</c> with a secret from the store. Use this factory for scaffolding only.
/// </remarks>
internal sealed class CatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContextOptions.Configure(options, "Host=localhost;Database=aurora_catalog;Username=aurora_migrator");
        return new CatalogDbContext(options.Options);
    }
}
