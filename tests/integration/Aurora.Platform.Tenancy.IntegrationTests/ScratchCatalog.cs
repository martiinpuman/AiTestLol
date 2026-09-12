using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// A catalog database of one test's own on the fixture's container: created as the provisioner
/// creates one (ADR-0007 §8 steps 2–3), migrated as the runner migrates one, dropped after.
/// Optionally under a named <c>lc_ctype</c>, for a question — what <c>lower()</c> does to a
/// letter — whose answer depends on it.
/// </summary>
internal sealed class ScratchCatalog : IAsyncDisposable
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

    /// <param name="catalog">The fixture whose container the database is created on.</param>
    /// <param name="locale">
    /// The database's <c>LC_COLLATE</c> and <c>LC_CTYPE</c>; <see langword="null"/> takes the
    /// cluster's default, which on <c>postgres:17-alpine</c> is <c>en_US.utf8</c>.
    /// </param>
    public static async Task<ScratchCatalog> CreateAsync(CatalogDatabaseFixture catalog, string? locale = null)
    {
        string name = Unique.Identifier("aurora_catalog_probe");
        string localeClause = locale is null ? "" : $" LC_COLLATE '{locale}' LC_CTYPE '{locale}'";
        await using NpgsqlConnection admin = await catalog.OpenAdminMaintenanceConnectionAsync();
        await CatalogDatabaseFixture.ExecuteAsync(
            admin,
            $"CREATE DATABASE {name} OWNER {CatalogDatabaseFixture.MigratorRole} TEMPLATE template0 ENCODING 'UTF8'{localeClause}");
        await CatalogDatabaseFixture.ExecuteAsync(admin, $"REVOKE ALL ON DATABASE {name} FROM PUBLIC");

        // Pooling off: the database is dropped at the end, and a pooled connection to it would
        // be one DROP DATABASE ... WITH (FORCE) has to kill. Error detail on: PostgreSQL always
        // sends the duplicated key in a 23505's DETAIL, and Npgsql redacts it on the client
        // unless the connection asks - the runner's connection (B-08) decides what an operator
        // sees; this one asks, so a test can hold the server to naming the duplicate.
        string migrator = new NpgsqlConnectionStringBuilder(catalog.MigratorConnectionString)
        {
            Database = name,
            Pooling = false,
            IncludeErrorDetail = true,
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

    /// <summary>Every cluster host the catalog holds, as stored.</summary>
    public async Task<List<string>> ClusterHostsAsync()
    {
        await using NpgsqlConnection owner = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT host FROM catalog.database_cluster ORDER BY host", owner);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<string> hosts = [];
        while (await reader.ReadAsync())
        {
            hosts.Add(reader.GetString(0));
        }

        return hosts;
    }

    /// <summary>The check constraint as PostgreSQL renders it, or <see langword="null"/> when there is none by that name.</summary>
    public async Task<string?> CheckConstraintDefinitionAsync(string constraint)
    {
        await using NpgsqlConnection owner = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = @constraint AND contype = 'c'", owner);
        command.Parameters.AddWithValue("constraint", constraint);
        return (string?)await command.ExecuteScalarAsync();
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

    /// <summary>One scalar, as the owner, on this database.</summary>
    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection owner = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
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
