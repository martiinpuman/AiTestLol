using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// Acceptance criteria 1 and 2 of B-05: the migrated schema is <c>catalog</c> per ADR-0007 §9.2,
/// and the EF migrations that produce it apply, re-apply and are the ones the code knows about.
/// </summary>
[Collection(CatalogDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogSchemaTests
{
    /// <summary>ADR-0007 §9.2, transcribed. <c>migrator_secret_ref</c> is B-05's one addition (see the summary).</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Adr0007Section92 = new Dictionary<string, string[]>
    {
        ["tenant"] =
        [
            "id", "key", "display_name", "state", "cluster_id", "database_name", "residency_region",
            "core_schema_version", "plan", "created_at", "activated_at", "suspended_at",
            "deletion_due_at", "deleted_at", "last_activity_at",
        ],
        ["tenant_host"] = ["host", "tenant_id", "is_primary", "verified_at"],
        ["database_cluster"] =
        [
            "id", "region", "host", "port", "maintenance_database", "admin_secret_ref", "app_secret_ref",
            "max_tenants", "state",
        ],
        ["subscription"] = ["id", "tenant_id", "plan", "seats", "valid_from", "valid_to"],
        ["installed_package"] = ["tenant_id", "package_id", "version", "state", "installed_at", "installed_by"],
    };

    private readonly CatalogDatabaseFixture _catalog;

    public CatalogSchemaTests(CatalogDatabaseFixture catalog) => _catalog = catalog;

    [Fact]
    public async Task Every_table_and_column_ADR_0007_9_2_names_exists_in_schema_catalog()
    {
        IReadOnlyCollection<(string Table, string Column)> columns = await CatalogColumnsAsync();

        foreach ((string table, string[] expected) in Adr0007Section92)
        {
            foreach (string column in expected)
            {
                columns.ShouldContain((table, column), $"catalog.{table}.{column} is named in ADR-0007 9.2");
            }
        }
    }

    [Fact]
    public async Task Tenant_state_admits_the_whole_lifecycle_and_nothing_else()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_tenant_state'",
            connection);

        string? definition = (string?)await command.ExecuteScalarAsync();

        definition.ShouldNotBeNull();
        foreach (string state in new[]
                 {
                     "Provisioning", "ProvisioningFailed", "Active", "Suspended", "SchemaBlocked",
                     "Exporting", "PendingDeletion", "Deleted",
                 })
        {
            definition.ShouldContain($"'{state}'");
        }
    }

    [Fact]
    public async Task The_migrations_history_lives_in_the_catalog_schema_not_in_public()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_schema FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'",
            connection);

        List<string> schemas = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schemas.Add(reader.GetString(0));
        }

        schemas.ShouldBe([CatalogDbContext.Schema]);
    }

    [Fact]
    public async Task Nothing_is_pending_after_migrating_and_migrating_again_changes_nothing()
    {
        await using CatalogDbContext context = _catalog.OpenAsMigrator();

        IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync();
        IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();

        applied.ShouldContain(migration => migration.EndsWith("_InitialCatalog", System.StringComparison.Ordinal));
        pending.ShouldBeEmpty();

        // Idempotent: the runner (B-08) re-runs migrations on resume, and the history table is the
        // record of what already happened.
        await context.Database.MigrateAsync();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task btree_gist_is_installed_and_the_subscription_overlap_constraint_exists()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();

        await using var extension = new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'", connection);
        ((long)(await extension.ExecuteScalarAsync())!).ShouldBe(1);

        await using var constraint = new NpgsqlCommand(
            "SELECT contype FROM pg_constraint WHERE conname = 'ex_subscription_no_overlap'",
            connection);
        ((char)(await constraint.ExecuteScalarAsync())!).ShouldBe('x', "'x' is an exclusion constraint");
    }

    [Fact]
    public async Task The_context_registered_by_AddCatalogDatabase_reaches_the_database()
    {
        var services = new ServiceCollection();
        services.AddCatalogDatabase(_catalog.AppConnectionString);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        (await context.Database.CanConnectAsync()).ShouldBeTrue();
        context.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
    }

    private async Task<IReadOnlyCollection<(string Table, string Column)>> CatalogColumnsAsync()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'catalog'",
            connection);

        List<(string, string)> columns = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        return columns;
    }
}
