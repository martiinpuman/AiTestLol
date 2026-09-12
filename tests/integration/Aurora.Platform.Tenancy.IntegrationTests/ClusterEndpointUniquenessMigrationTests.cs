using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The <c>ClusterEndpointUniqueness</c> migration applied to a catalog that already exists at the
/// state before it (ADR-0034 §5.1, expand/contract honesty): against two cluster rows on one
/// endpoint it fails loudly, names the duplicate and applies nothing; against rows on distinct
/// endpoints it applies, keeps every row and re-runs as a no-op.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a scratch database and not the fixture's catalog.</b> The fixture's catalog is migrated
/// to the latest migration before any test runs, so the question this class asks — what the
/// migration does to rows that were there first — cannot be asked of it. Each test creates a
/// database of its own on the fixture's container, owned by <c>aurora_migrator</c> as the real
/// catalog is, migrates it as the migrator to the migration <em>before</em> this one, seeds rows as
/// the owner, and then migrates to the latest as the runner would. The database is dropped after.
/// </para>
/// <para>
/// <b>Why the failure must be loud, and what "loud" is held to.</b> A unique index that could be
/// created over duplicates would be no index. PostgreSQL refuses it with SQLSTATE <c>23505</c>,
/// naming the index and the duplicated key, and EF runs the migration inside a transaction, so
/// nothing of it survives — not the index, not the history row, and neither duplicate is touched.
/// The worse outcome ADR-0034 §5.1 names is a fleet with the defect that migrates clean and stays
/// broken; this is the test that says it cannot. Made non-unique, the index is created over the
/// duplicates and the first test fails at <c>Should.ThrowAsync</c> — done once before this file was
/// committed, and recorded in B-20's handback.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class ClusterEndpointUniquenessMigrationTests
{
    private const string UniqueViolation = "23505";
    private const string IndexName = "ux_database_cluster_host_port";

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public ClusterEndpointUniquenessMigrationTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task Against_a_catalog_already_holding_two_cluster_rows_on_one_endpoint_the_migration_fails_naming_the_duplicate_and_applies_nothing()
    {
        await using ScratchCatalog scratch = await ScratchCatalog.CreateAsync(_catalog);
        await scratch.MigrateToTheStateBeforeAsync<ClusterEndpointUniqueness>();
        string host = Unique.Host();
        await scratch.InsertClusterRowAsync(host, 5432);
        await scratch.InsertClusterRowAsync(host, 5432);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(scratch.MigrateToLatestAsync);

        _output.WriteLine($"{refused.SqlState}: {refused.MessageText}");
        _output.WriteLine($"DETAIL: {refused.Detail}");
        refused.SqlState.ShouldBe(UniqueViolation);
        refused.ConstraintName.ShouldBe(IndexName);
        refused.TableName.ShouldBe("database_cluster");
        refused.Detail.ShouldNotBeNull("the duplicate is named");
        refused.Detail.ShouldContain(host);
        refused.Detail.ShouldContain("5432");

        IEnumerable<string> applied = await scratch.AppliedMigrationsAsync();
        IEnumerable<string> pending = await scratch.PendingMigrationsAsync();
        applied.ShouldNotContain(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal), "the history must not record a migration that did not apply");
        pending.ShouldContain(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal), "the migration is still pending, for the runner to retry once an operator has resolved the duplicate");
        (await scratch.IndexDefinitionAsync(IndexName)).ShouldBeNull("no index survives the failed transaction");
        (await scratch.ClusterRowCountAsync()).ShouldBe(2, "both rows are left for an operator to resolve; nothing is de-duplicated silently");
    }

    [Fact]
    public async Task Against_a_catalog_whose_cluster_rows_are_on_distinct_endpoints_the_migration_applies_keeps_every_row_and_re_runs_as_a_no_op()
    {
        // Three rows that the index must admit: one host on two ports, and a second host on the
        // first port. An index on host alone, or on port alone, would refuse one of them.
        await using ScratchCatalog scratch = await ScratchCatalog.CreateAsync(_catalog);
        await scratch.MigrateToTheStateBeforeAsync<ClusterEndpointUniqueness>();
        string host = Unique.Host();
        await scratch.InsertClusterRowAsync(host, 5432);
        await scratch.InsertClusterRowAsync(host, 5433);
        await scratch.InsertClusterRowAsync(Unique.Host(), 5432);
        int rowsBefore = await scratch.ClusterRowCountAsync();

        await scratch.MigrateToLatestAsync();

        string? definition = await scratch.IndexDefinitionAsync(IndexName);
        _output.WriteLine($"cluster rows before: {rowsBefore}, after: {await scratch.ClusterRowCountAsync()}; index: {definition}");
        definition.ShouldBe($"CREATE UNIQUE INDEX {IndexName} ON catalog.database_cluster USING btree (host, port)");
        (await scratch.ClusterRowCountAsync()).ShouldBe(rowsBefore, "an expand-only migration removes nothing");
        (await scratch.PendingMigrationsAsync()).ShouldBeEmpty();

        // Idempotent: the runner re-runs migrations on resume (ADR-0007 §7.4).
        await scratch.MigrateToLatestAsync();
        (await scratch.PendingMigrationsAsync()).ShouldBeEmpty();
        (await scratch.ClusterRowCountAsync()).ShouldBe(rowsBefore);
    }

    /// <summary>
    /// A catalog database of this test's own on the fixture's container: created as the provisioner
    /// creates one (ADR-0007 §8 steps 2–3), migrated as the runner migrates one, dropped after.
    /// </summary>
    private sealed class ScratchCatalog : IAsyncDisposable
    {
        private readonly CatalogDatabaseFixture _catalog;
        private readonly string _name;
        private readonly string _migratorConnectionString;

        private ScratchCatalog(CatalogDatabaseFixture catalog, string name, string migratorConnectionString)
        {
            _catalog = catalog;
            _name = name;
            _migratorConnectionString = migratorConnectionString;
        }

        public static async Task<ScratchCatalog> CreateAsync(CatalogDatabaseFixture catalog)
        {
            string name = Unique.Identifier("aurora_catalog_probe");
            await using NpgsqlConnection admin = await catalog.OpenAdminMaintenanceConnectionAsync();
            await CatalogDatabaseFixture.ExecuteAsync(
                admin,
                $"CREATE DATABASE {name} OWNER {CatalogDatabaseFixture.MigratorRole} TEMPLATE template0 ENCODING 'UTF8'");
            await CatalogDatabaseFixture.ExecuteAsync(admin, $"REVOKE ALL ON DATABASE {name} FROM PUBLIC");

            // Pooling off: the database is dropped at the end, and a pooled connection to it would
            // be one DROP DATABASE ... WITH (FORCE) has to kill.
            string migrator = new NpgsqlConnectionStringBuilder(catalog.MigratorConnectionString)
            {
                Database = name,
                Pooling = false,
            }.ConnectionString;

            return new ScratchCatalog(catalog, name, migrator);
        }

        /// <summary>
        /// Migrates to the migration that precedes <typeparamref name="TMigration"/> in the
        /// assembly's order: the catalog as it was the moment before that migration ran.
        /// </summary>
        public async Task MigrateToTheStateBeforeAsync<TMigration>()
            where TMigration : Migration
        {
            await using CatalogDbContext context = CatalogDatabaseFixture.CreateContext(_migratorConnectionString);
            List<string> ids = context.Database.GetMigrations().ToList();
            int position = ids.FindIndex(id => id.EndsWith("_" + typeof(TMigration).Name, StringComparison.Ordinal));
            position.ShouldBeGreaterThan(0, $"{typeof(TMigration).Name} must be a migration with a predecessor");

            await context.GetService<IMigrator>().MigrateAsync(ids[position - 1]);
        }

        public async Task MigrateToLatestAsync()
        {
            await using CatalogDbContext context = CatalogDatabaseFixture.CreateContext(_migratorConnectionString);
            await context.Database.MigrateAsync();
        }

        public async Task<IEnumerable<string>> AppliedMigrationsAsync()
        {
            await using CatalogDbContext context = CatalogDatabaseFixture.CreateContext(_migratorConnectionString);
            return await context.Database.GetAppliedMigrationsAsync();
        }

        public async Task<IEnumerable<string>> PendingMigrationsAsync()
        {
            await using CatalogDbContext context = CatalogDatabaseFixture.CreateContext(_migratorConnectionString);
            return await context.Database.GetPendingMigrationsAsync();
        }

        /// <summary>As the owner: the request path may not create a routing decision, and this one is the fleet's operator seed data.</summary>
        public async Task InsertClusterRowAsync(string host, int port)
        {
            await using NpgsqlConnection owner = await OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) "
                + "VALUES (@id, 'nz', @host, @port, 'postgres', 'vault://kv/aurora/test/admin', 'vault://kv/aurora/test/migrator', 'vault://kv/aurora/test/app', 1000, 'Accepting')",
                owner);
            command.Parameters.AddWithValue("id", Unique.ClusterId().Value);
            command.Parameters.AddWithValue("host", host);
            command.Parameters.AddWithValue("port", port);
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        public async Task<int> ClusterRowCountAsync()
        {
            await using NpgsqlConnection owner = await OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM catalog.database_cluster", owner);
            return (int)(long)(await command.ExecuteScalarAsync())!;
        }

        /// <summary>The index as PostgreSQL renders it, or <see langword="null"/> when there is none by that name.</summary>
        public async Task<string?> IndexDefinitionAsync(string index)
        {
            await using NpgsqlConnection owner = await OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'catalog' AND indexname = @index", owner);
            command.Parameters.AddWithValue("index", index);
            return (string?)await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using NpgsqlConnection admin = await _catalog.OpenAdminMaintenanceConnectionAsync();
            await CatalogDatabaseFixture.ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
        }

        private async Task<NpgsqlConnection> OpenAsync()
        {
            var connection = new NpgsqlConnection(_migratorConnectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}
