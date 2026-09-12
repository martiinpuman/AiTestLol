using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
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
    /// <summary>The image CLAUDE.md pins for every integration test.</summary>
    public const string PostgresImage = "postgres:17-alpine";

    public const string CatalogDatabaseName = "aurora_catalog";

    /// <summary>The database <c>aurora_admin</c> issues <c>CREATE DATABASE</c> from (ADR-0007 §8 step 2).</summary>
    public const string MaintenanceDatabaseName = "postgres";
    public const string AdminRole = "aurora_admin";
    public const string MigratorRole = "aurora_migrator";
    public const string AppRole = "aurora_app";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage)
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCommand("-c", "fsync=off", "-c", "full_page_writes=off", "-c", "synchronous_commit=off", "-c", "max_connections=200")
        .Build();

    private readonly string _adminPassword = MintPassword();
    private readonly string _migratorPassword = MintPassword();
    private readonly string _appPassword = MintPassword();
    private readonly string _appSecretName = "AURORA_TEST_APP_SECRET_" + Guid.NewGuid().ToString("N");
    private string _superuserCatalogConnectionString = null!;
    private Lazy<Task<DatabaseCluster>> _thisServerAsCluster = null!;

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

            // PostgreSQL grants CONNECT on every database to PUBLIC by default, and under ADR-0007
            // 3.5 stage 1 nothing about aurora_app keeps it out of a database: it is one login role
            // per cluster, and 8 step 3's per-database REVOKE is the only thing that closes one. A
            // hardened cluster therefore runs that REVOKE on every database it ships with - the
            // maintenance database the provisioner issues CREATE DATABASE from, template1, and
            // whatever else the image created - and so does this fixture, over the list rather
            // than by name, so nothing is missed. Only an owner or a superuser can revoke it, and a
            // REVOKE issued by anyone else is a *warning*, not an error: the statement appears to
            // work and changes nothing. So this runs on the superuser connection, and
            // CatalogPrivilegeTests asserts the effect by enumerating pg_database and connecting,
            // rather than by trusting that the loop ran.
            foreach (string database in await DatabasesAcceptingConnectionsAsync(connection))
            {
                await ExecuteAsync(connection, $"REVOKE ALL ON DATABASE \"{database}\" FROM PUBLIC");
            }

            await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {MaintenanceDatabaseName} TO {AdminRole}");
        }

        AdminMaintenanceConnectionString = As(superuser, AdminRole, _adminPassword, database: MaintenanceDatabaseName);
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
        _superuserCatalogConnectionString = new NpgsqlConnectionStringBuilder(superuser) { Database = CatalogDatabaseName }.ConnectionString;

        await using CatalogDbContext migrator = CreateContext(MigratorConnectionString);
        await migrator.Database.MigrateAsync();

        _thisServerAsCluster = new Lazy<Task<DatabaseCluster>>(SeedThisServerAsClusterAsync);
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable(_appSecretName, null);
        await _container.DisposeAsync();
    }

    /// <summary>
    /// The one <c>catalog.database_cluster</c> row for this container's own endpoint, seeded on
    /// first use and shared by every test that resolves a tenant and opens what it resolved.
    /// </summary>
    /// <remarks>
    /// One row per endpoint is the rule the catalog keeps from B-20 on
    /// (<c>ux_database_cluster_host_port</c>, ADR-0034 §3.1), and ADR-0034 §3.2 says what a fixture
    /// that wants two clusters does instead: two containers, or one cluster row with two tenants.
    /// Until B-20 every routing test seeded a cluster row of its own for this endpoint, and the
    /// second such row was variant 3 of the tenant-takeover finding, admitted. The row's app secret
    /// reference names an environment variable that holds <c>aurora_app</c>'s password for the life
    /// of the fixture, which is how the resolver's <c>EnvironmentSecretStore</c> finds the credential.
    /// </remarks>
    internal Task<DatabaseCluster> ThisServerAsClusterAsync() => _thisServerAsCluster.Value;

    /// <summary>A context over <see cref="AppConnectionString"/>: what a request would get.</summary>
    /// <remarks>
    /// Internal, because <c>CatalogDbContext</c> is internal to <c>Aurora.Platform.Tenancy</c> and
    /// this assembly only sees it through <c>InternalsVisibleTo</c>. Widening the context to public
    /// so that a test helper could be public would invert the decision: see the type's remarks.
    /// </remarks>
    internal CatalogDbContext OpenAsApp() => CreateContext(AppConnectionString);

    /// <summary>A context over <see cref="MigratorConnectionString"/>: what the migration runner gets.</summary>
    internal CatalogDbContext OpenAsMigrator() => CreateContext(MigratorConnectionString);

    /// <summary>
    /// Writes rows the request path may only read — clusters are operator seed data, tenants and
    /// hosts are the provisioning saga's to create, subscriptions have no writer yet — and stamps
    /// the lifecycle columns no request-path component may touch, as <c>aurora_migrator</c>, the
    /// owner. What a test then proves about <c>aurora_app</c> is proved on rows it did not need to
    /// be able to write.
    /// </summary>
    internal async Task SeedAsync(Action<CatalogDbContext> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        await using CatalogDbContext owner = CreateContext(MigratorConnectionString);
        seed(owner);
        await owner.SaveChangesAsync();
    }

    public Task<NpgsqlConnection> OpenAppConnectionAsync() => OpenAsync(AppConnectionString);

    public Task<NpgsqlConnection> OpenMigratorConnectionAsync() => OpenAsync(MigratorConnectionString);

    /// <summary>What the provisioner holds when it creates, hardens and drops a database (ADR-0007 §8 steps 2-3).</summary>
    public Task<NpgsqlConnection> OpenAdminMaintenanceConnectionAsync() => OpenAsync(AdminMaintenanceConnectionString);

    /// <summary>
    /// The container's superuser, on the catalog database. For injecting a fault that needs
    /// cluster-level DDL none of the three roles may issue — creating a role and granting it to
    /// <c>aurora_app</c> — inside a transaction the test rolls back. Never for asserting what a role
    /// can do: a superuser bypasses every privilege check, so a probe run on this connection proves
    /// nothing about the privilege model that ships.
    /// </summary>
    public Task<NpgsqlConnection> OpenSuperuserConnectionAsync() => OpenAsync(_superuserCatalogConnectionString);

    private async Task<DatabaseCluster> SeedThisServerAsClusterAsync()
    {
        var endpoint = new NpgsqlConnectionStringBuilder(AppConnectionString);
        Environment.SetEnvironmentVariable(_appSecretName, endpoint.Password);

        DatabaseCluster cluster = DatabaseCluster.Register(
            Unique.ClusterId(),
            Region.Parse("nz", null),
            endpoint.Host!,
            endpoint.Port,
            MaintenanceDatabaseName,
            SecretReference.Of("vault://kv/aurora/test/admin"),
            SecretReference.Of("vault://kv/aurora/test/migrator"),
            SecretReference.Of("env:" + _appSecretName),
            1_000);
        await SeedAsync(owner => owner.DatabaseClusters.Add(cluster));
        return cluster;
    }

    internal static CatalogDbContext CreateContext(string connectionString)
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

    /// <summary>Every database on the cluster a login may attempt; <c>template0</c> excludes itself by its own flag.</summary>
    public static async Task<List<string>> DatabasesAcceptingConnectionsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datallowconn ORDER BY datname", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<string> databases = [];
        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
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
            // Pooling is per connection string; keep the role pools small and short-lived so the
            // container's max_connections is never the thing a test trips over. Npgsql refuses an
            // idle lifetime below its pruning interval, so both are named rather than one.
            MaxPoolSize = 8,
            ConnectionPruningInterval = 2,
            ConnectionIdleLifetime = 4,
        }.ConnectionString;

    private static string MintPassword() => Guid.NewGuid().ToString("N");
}
