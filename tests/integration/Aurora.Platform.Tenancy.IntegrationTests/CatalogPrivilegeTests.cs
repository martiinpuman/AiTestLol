using System.Threading.Tasks;
using Npgsql;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// Least privilege on the catalog (ADR-0004 rule 2, ADR-0007 §4.4): what the runtime role can
/// and cannot do, asserted by trying, not by reading the grant statements.
/// </summary>
/// <remarks>
/// This is what "tenant isolation" means for a database that is shared by design: the request
/// path can read and write registry rows and can change nothing about the schema that decides
/// where every tenant's data lives.
/// </remarks>
[Collection(CatalogDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogPrivilegeTests
{
    private const string InsufficientPrivilege = "42501";

    private readonly CatalogDatabaseFixture _catalog;

    public CatalogPrivilegeTests(CatalogDatabaseFixture catalog) => _catalog = catalog;

    [Fact]
    public async Task The_app_role_may_read_and_write_every_registry_table()
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();

        foreach (string table in new[] { "database_cluster", "tenant", "tenant_host", "subscription", "installed_package" })
        {
            foreach (string privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                await using var command = new NpgsqlCommand(
                    $"SELECT has_table_privilege('{CatalogDatabaseFixture.AppRole}', 'catalog.{table}', '{privilege}')",
                    connection);

                ((bool)(await command.ExecuteScalarAsync())!).ShouldBeTrue($"{privilege} on catalog.{table}");
            }
        }
    }

    [Theory]
    [InlineData("CREATE TABLE catalog.smuggled (id uuid PRIMARY KEY)", "creating a table")]
    [InlineData("ALTER TABLE catalog.tenant ADD COLUMN smuggled text", "altering a registry table")]
    [InlineData("DROP TABLE catalog.tenant_host", "dropping a registry table")]
    [InlineData("CREATE SCHEMA smuggled", "creating a schema")]
    [InlineData("CREATE DATABASE smuggled", "creating a database")]
    [InlineData("CREATE EXTENSION IF NOT EXISTS pg_stat_statements", "installing an extension")]
    public async Task The_app_role_cannot_change_the_schema(string ddl, string what)
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();
        await using var command = new NpgsqlCommand(ddl, connection);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), what);

        refused.SqlState.ShouldBe(InsufficientPrivilege, what);
    }

    [Theory]
    [InlineData("SELECT count(*) FROM catalog.\"__EFMigrationsHistory\"")]
    [InlineData("DELETE FROM catalog.\"__EFMigrationsHistory\"")]
    public async Task The_app_role_cannot_touch_the_migrations_history(string sql)
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        refused.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Fact]
    public async Task The_migrator_role_owns_the_schema_and_may_alter_it()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await using var owner = new NpgsqlCommand(
            "SELECT nspowner::regrole::text FROM pg_namespace WHERE nspname = 'catalog'", connection, transaction);
        ((string)(await owner.ExecuteScalarAsync())!).ShouldBe(CatalogDatabaseFixture.MigratorRole);

        await using var alter = new NpgsqlCommand("ALTER TABLE catalog.tenant ADD COLUMN probe text", connection, transaction);
        await alter.ExecuteNonQueryAsync();

        await transaction.RollbackAsync();
    }
}
