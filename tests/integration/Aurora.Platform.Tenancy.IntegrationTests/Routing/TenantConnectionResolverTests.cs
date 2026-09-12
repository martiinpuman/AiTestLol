using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests.Routing;

/// <summary>
/// The resolver against a real catalog on a real PostgreSQL, as the request path holds it: the
/// catalog read runs as <c>aurora_app</c>, and the connection string it composes is opened.
/// </summary>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed partial class TenantConnectionResolverTests
{
    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public TenantConnectionResolverTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task The_reader_joins_the_tenant_to_its_cluster_in_one_statement_as_the_app_role()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        tenant.Activate(1, Unique.Now);
        await _catalog.SeedAsync(owner =>
        {
            owner.DatabaseClusters.Add(cluster);
            owner.Tenants.Add(tenant);
        });

        var counter = new CatalogStatementCounter();
        await using CatalogDbContext catalog = counter.Open(_catalog.AppConnectionString);
        TenantRouting? routing = await new CatalogTenantRoutingReader(catalog).ReadAsync(tenant.Id, default);

        routing.ShouldNotBeNull();
        routing.TenantId.ShouldBe(tenant.Id.Value);
        routing.TenantKey.ShouldBe(tenant.Key.Value);
        routing.State.ShouldBe(TenantState.Active);
        routing.DatabaseName.ShouldBe(tenant.DatabaseName);
        routing.ResidencyRegion.ShouldBe(cluster.Region.Value);
        routing.Cluster.ShouldNotBeNull();
        routing.Cluster.ClusterId.ShouldBe(cluster.Id.Value);
        routing.Cluster.Host.ShouldBe(cluster.Host);
        routing.Cluster.Port.ShouldBe(cluster.Port);
        routing.Cluster.MaintenanceDatabase.ShouldBe(cluster.MaintenanceDatabase);
        routing.Cluster.AdminSecretRef.ShouldBe(cluster.AdminSecretRef.Value);
        routing.Cluster.MigratorSecretRef.ShouldBe(cluster.MigratorSecretRef.Value);
        routing.Cluster.AppSecretRef.ShouldBe(cluster.AppSecretRef.Value);

        _output.WriteLine($"{counter.Count} statement(s):");
        foreach (string statement in counter.Statements)
        {
            _output.WriteLine(statement);
        }

        counter.Count.ShouldBe(1, "one round trip per routing read");
        string sql = counter.Statements[0];
        CatalogTable("tenant").IsMatch(sql).ShouldBeTrue("the statement reads catalog.tenant");
        CatalogTable("database_cluster").IsMatch(sql).ShouldBeTrue("the statement reads catalog.database_cluster");
    }

    [Fact]
    public async Task A_tombstone_reads_back_as_Deleted_with_no_cluster_and_the_resolver_refuses_it_as_such()
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await _catalog.SeedAsync(owner =>
        {
            owner.DatabaseClusters.Add(cluster);
            owner.Tenants.Add(tenant);
        });
        await TombstoneAsync(tenant.Id);

        await using CatalogDbContext catalog = _catalog.OpenAsApp();
        TenantRouting? routing = await new CatalogTenantRoutingReader(catalog).ReadAsync(tenant.Id, default);

        routing.ShouldNotBeNull();
        routing.State.ShouldBe(TenantState.Deleted);
        routing.Cluster.ShouldBeNull();
        routing.DatabaseName.ShouldBeNull();

        await using ServiceProvider provider = BuildRequestPath();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        TenantNotRoutableException refused = await Should.ThrowAsync<TenantNotRoutableException>(
            () => scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ResolveAsync(tenant.Id, default).AsTask());
        refused.State.ShouldBe(TenantState.Deleted);
    }

    [Fact]
    public async Task An_id_no_row_carries_reads_back_as_null()
    {
        await using CatalogDbContext catalog = _catalog.OpenAsApp();

        (await new CatalogTenantRoutingReader(catalog).ReadAsync(TenantId.Create(), default)).ShouldBeNull();
    }

    [Fact]
    public async Task A_resolved_connection_string_opens_the_tenants_database_as_aurora_app_under_the_tenants_application_name()
    {
        // The whole chain, end to end: a cluster row pointing at this container, a tenant on it, the
        // app secret reachable through the reference the row holds, and the string the resolver
        // composes actually opening the tenant's database. Nothing is asserted from the composer's
        // constants; the server reports what it was connected to and as whom.
        var endpoint = new NpgsqlConnectionStringBuilder(_catalog.AppConnectionString);
        string secretName = "AURORA_TEST_APP_SECRET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(secretName, endpoint.Password);
        DatabaseCluster cluster = DatabaseCluster.Register(
            Unique.ClusterId(),
            Region.Parse("nz", null),
            endpoint.Host!,
            endpoint.Port,
            CatalogDatabaseFixture.MaintenanceDatabaseName,
            SecretReference.Of("vault://kv/aurora/test/admin"),
            SecretReference.Of("vault://kv/aurora/test/migrator"),
            SecretReference.Of("env:" + secretName),
            1_000);
        Tenant tenant = Unique.Tenant(cluster);
        tenant.Activate(1, Unique.Now);
        string databaseName = tenant.DatabaseName!;

        await CreateHardenedTenantDatabaseAsync(databaseName);
        try
        {
            await _catalog.SeedAsync(owner =>
            {
                owner.DatabaseClusters.Add(cluster);
                owner.Tenants.Add(tenant);
            });

            await using ServiceProvider provider = BuildRequestPath();
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            TenantConnection connection = await scope.ServiceProvider
                .GetRequiredService<ITenantConnectionResolver>()
                .ResolveAsync(tenant.Id, default);

            connection.ClusterId.ShouldBe(cluster.Id);
            connection.DatabaseName.ShouldBe(databaseName);
            connection.ResidencyRegion.ShouldBe(cluster.Region);

            await using var open = new NpgsqlConnection(connection.ConnectionString);
            await open.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT current_database(), current_user, current_setting('application_name')", open);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue();

            _output.WriteLine($"connected to {reader.GetString(0)} as {reader.GetString(1)} with application_name {reader.GetString(2)}");
            reader.GetString(0).ShouldBe(databaseName);
            reader.GetString(1).ShouldBe(CatalogDatabaseFixture.AppRole);
            reader.GetString(2).ShouldBe($"aurora-web:{tenant.Key.Value}");

            // The resolved string pools (§5.2), so after disposal the physical connection sits idle
            // in a pool rather than closing; clear that pool so nothing reuses a connection to a
            // database about to be dropped.
            await reader.DisposeAsync();
            NpgsqlConnection.ClearPool(open);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretName, null);
            await DropTenantDatabaseAsync(databaseName);
        }
    }

    /// <summary>The composition root's registration over the request path's catalog connection.</summary>
    private ServiceProvider BuildRequestPath()
    {
        var services = new ServiceCollection();
        services.AddCatalogDatabase(_catalog.AppConnectionString);
        services.AddTenantConnectionResolver(TenantPoolProfile.Web);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>ADR-0007 §8 steps 2–3 by hand: a database owned by the migrator, closed to PUBLIC, opened to the app role.</summary>
    private async Task CreateHardenedTenantDatabaseAsync(string databaseName)
    {
        await using NpgsqlConnection admin = await _catalog.OpenAdminMaintenanceConnectionAsync();
        await CatalogDatabaseFixture.ExecuteAsync(
            admin,
            $"CREATE DATABASE {databaseName} OWNER {CatalogDatabaseFixture.MigratorRole} TEMPLATE template0 ENCODING 'UTF8'");
        await CatalogDatabaseFixture.ExecuteAsync(admin, $"REVOKE ALL ON DATABASE {databaseName} FROM PUBLIC");
        await CatalogDatabaseFixture.ExecuteAsync(admin, $"GRANT CONNECT ON DATABASE {databaseName} TO {CatalogDatabaseFixture.AppRole}");
    }

    /// <summary>
    /// Leaves the cluster as it was found: <c>CatalogPrivilegeTests</c> proves <c>aurora_app</c> can
    /// open no database but the catalog by trying every one on the cluster, and a hardened tenant
    /// database left behind would be one it can open. <c>WITH (FORCE)</c> ends any session still on it.
    /// </summary>
    private async Task DropTenantDatabaseAsync(string databaseName)
    {
        await using NpgsqlConnection admin = await _catalog.OpenAdminMaintenanceConnectionAsync();
        await CatalogDatabaseFixture.ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
    }

    /// <summary>ADR-0007 §11.4: the row is kept as id, key and dates; every routing column is blanked. As the owner, which B-05 made the only principal able to.</summary>
    private async Task TombstoneAsync(TenantId tenant)
    {
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE catalog.tenant SET state = 'Deleted', deleted_at = @now, display_name = NULL, cluster_id = NULL, "
            + "database_name = NULL, residency_region = NULL, plan = NULL WHERE id = @id",
            owner);
        command.Parameters.AddWithValue("now", Unique.Now);
        command.Parameters.AddWithValue("id", tenant.Value);
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    /// <summary>The table name as Npgsql's EF provider spells it, quoted or not.</summary>
    private static Regex CatalogTable(string table) => new($"catalog\"?\\.\"?{table}\\b", RegexOptions.CultureInvariant);
}
