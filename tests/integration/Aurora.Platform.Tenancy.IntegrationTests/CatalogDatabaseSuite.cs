using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The one xUnit collection of this project (testing-strategy.md §6.1: at most two per project,
/// one container each), so every test class here shares a single
/// <see cref="CatalogDatabaseFixture"/> and therefore a single PostgreSQL container.
/// </summary>
/// <remarks>
/// Named "…Suite" rather than the xUnit-idiomatic "…Collection" because CA1711 reserves that
/// suffix for types that are collections. The collection's <em>name</em> — the string xUnit keys
/// on — is unchanged.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class CatalogDatabaseSuite : ICollectionFixture<CatalogDatabaseFixture>
{
    public const string Name = "Catalog database";
}
