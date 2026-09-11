using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The one collection of this project (testing-strategy.md §6.1: at most two per project, one
/// container each).
/// </summary>
[CollectionDefinition(Name)]
public sealed class CatalogDatabaseCollection : ICollectionFixture<CatalogDatabaseFixture>
{
    public const string Name = "Catalog database";
}
