using System;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the whole collection, holding one catalog database set up the
/// way a real cluster is (ADR-0004 rule 2, ADR-0007 §3.5): three roles, a database owned by
/// <c>aurora_migrator</c>, migrations applied as <c>aurora_migrator</c>, and tests talking to it
/// as <c>aurora_app</c>.
/// </summary>
/// <remarks>
/// <para>
/// A container costs about nine seconds to start (testing-strategy.md §6), so this is a
/// collection fixture and the project has one collection. Tests share the database and keep
/// out of each other's way by minting unique keys and ids rather than by truncating.
/// </para>
/// <para>
/// Two facts about PostgreSQL shaped this fixture and will shape the provisioner (B-07):
/// <c>CREATE DATABASE</c> has to be issued from a connection to a <em>different</em> database on
/// the same server, and a role with <c>CREATEDB</c> may name another role as the new database's
/// owner only if it is a member of that role — hence <c>GRANT aurora_migrator TO aurora_admin</c>.
/// </para>
/// <para>
/// The role passwords are minted per run and never written down: a test database needs no
/// stable credential and the repository must hold no secret, however throwaway.
/// </para>
/// </remarks>
public sealed class CatalogDatabaseFixture : IAsyncLifetime
{
    public const string CatalogDatabaseName = "aurora_catalog";
    public const string AdminRole = "aurora_admin";
    public const string MigratorRole = "aurora_migrator";
    public const string AppRole = "aurora_app";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCommand("-c", "fsync=off", "-c", "full_page_writes=off", "-c", "synchronous_commit=off", "-c", "max_connections=200")
        .Build();

    private readonly string _adminPassword = MintPassword();
    private readonly string _migratorPassword = MintPassword();
    private readonly string _appPassword = MintPassword();

    /// <summary>What the application would hold: the runtime role over the catalog database.</summary>
    public string AppConnectionString { get; private set; } = null!;

    /// <summary>What the migration runner would hold (B-08).</summary>
    public string MigratorConnectionString { get; private set; } = null!;

    /// <summary>What the provisioner would hold (B-07): <c>aurora_admin</c> on the maintenance database.</summary>
    public string AdminMaintenanceConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        string superuser = _container.GetConnectionString();
        await using (NpgsqlConnection connection = await OpenAsync(superuser))
        {
            await ExecuteAsync(connection, $"CREATE ROLE {AdminRole} LOGIN CREATEDB PASSWORD '{_adminPassword}'");
            await ExecuteAsync(connection, $"CREATE ROLE {MigratorRole} LOGIN PASSWORD '{_migratorPassword}'");
            await ExecuteAsync(connection, $"CREATE ROLE {AppRole} LOGIN PASSWORD '{_appPassword}'");
            await ExecuteAsync(connection, $"GRANT {MigratorRole} TO {AdminRole}");
        }

        AdminMaintenanceConnectionString = As(superuser, AdminRole, _adminPassword, database: "postgres");
        await using (NpgsqlConnection connection = await OpenAsync(AdminMaintenanceConnectionString))
        {
            await ExecuteAsync(
                connection,
                $"CREATE DATABASE {CatalogDatabaseName} OWNER {MigratorRole} TEMPLATE template0 ENCODING 'UTF8'");
            await ExecuteAsync(connection, $"REVOKE ALL ON DATABASE {CatalogDatabaseName} FROM PUBLIC");
            await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {CatalogDatabaseName} TO {AppRole}");
        }

        MigratorConnectionString = As(superuser, MigratorRole, _migratorPassword, CatalogDatabaseName);
        AppConnectionString = As(superuser, AppRole, _appPassword, CatalogDatabaseName);

        await using CatalogDbContext migrator = CreateContext(MigratorConnectionString);
        await migrator.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A context over <see cref="AppConnectionString"/>: what a request would get.</summary>
    public CatalogDbContext OpenAsApp() => CreateContext(AppConnectionString);

    /// <summary>A context over <see cref="MigratorConnectionString"/>: what the migration runner gets.</summary>
    public CatalogDbContext OpenAsMigrator() => CreateContext(MigratorConnectionString);

    public Task<NpgsqlConnection> OpenAppConnectionAsync() => OpenAsync(AppConnectionString);

    public Task<NpgsqlConnection> OpenMigratorConnectionAsync() => OpenAsync(MigratorConnectionString);

    public static CatalogDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContextOptions.Configure(options, connectionString);
        return new CatalogDbContext(options.Options);
    }

    public static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string As(string superuserConnectionString, string role, string password, string database) =>
        new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = role,
            Password = password,
            Database = database,
            // Pooling is per connection string; keep the four role pools small and short-lived so the
            // container's max_connections is never the thing a test trips over.
            MaxPoolSize = 8,
            ConnectionIdleLifetime = 5,
        }.ConnectionString;

    private static string MintPassword() => Guid.NewGuid().ToString("N");
}
