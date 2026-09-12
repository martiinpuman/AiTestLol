using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Npgsql;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// ADR-0007 §4.3's connected-database identity check, proven against real PostgreSQL: the one
/// implementation of the stamp's DDL and of the assertion that every path calls (ADR-0027 §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The test that matters is the mismatch.</b> A check proven only on the matching case is the
/// shape this project has rejected repeatedly, so the mis-route is the centrepiece here: a
/// database <em>named</em> for tenant B, exactly as the provisioner would name it, stamped for
/// tenant A, asserted as B - and refused with the specific failure, naming both tenants. A check
/// that compared <c>current_database()</c> would have passed it; the assertion reads the identity
/// that travels inside the data, and that is what the test demonstrates.
/// </para>
/// <para>
/// <b>What "tenant isolation test" means here.</b> The stamp is the control that turns "the
/// request-path role can, by design, reach every tenant database on the cluster" (ADR-0007 §3.5
/// stage 1) into "and is refused by the first query on the wrong one". Two consequences are
/// proven: the role that runs the check can read the stamp, and it cannot rewrite it - a
/// re-stampable database is a database whose identity an attacker with the app credential could
/// choose.
/// </para>
/// <para>
/// Each test that needs a tenant database creates one on the fixture's cluster the way ADR-0007
/// §8 steps 2 and 4 would - as <c>aurora_admin</c> from the maintenance database, owned by
/// <c>aurora_migrator</c>, stamp table created as <c>aurora_migrator</c> - and drops it afterwards.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class TenantIdentityStampTests(CatalogDatabaseFixture catalog)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string InsufficientPrivilege = "42501";

    [Fact]
    public async Task A_matching_stamp_lets_the_assertion_return_for_both_roles_that_run_it()
    {
        TenantId tenant = TenantId.Create();
        await using TenantDatabase database = await TenantDatabase.CreateAsync(catalog, Unique.Identifier("aurora_t_stamp"));
        await database.StampAsync(tenant, Unique.TenantKey());

        // The app role runs it in the physical-connection initializer (B-06.2); the migrator role
        // runs it before any DDL (ADR-0027 §2-§3). Both must be able to read the stamp.
        await using (NpgsqlConnection asApp = await database.OpenAsAppAsync())
        {
            await TenantIdentityStamp.AssertAsync(asApp, tenant, CancellationToken.None);
        }

        await using (NpgsqlConnection asMigrator = await database.OpenAsMigratorAsync())
        {
            await TenantIdentityStamp.AssertAsync(asMigrator, tenant, CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_database_named_for_tenant_B_but_stamped_for_tenant_A_is_refused_when_B_is_expected()
    {
        TenantId tenantA = TenantId.Create();
        TenantId tenantB = TenantId.Create();
        TenantKey keyB = Unique.TenantKey();
        string namedForB = Tenant.DatabaseNameFor(keyB);
        await using TenantDatabase database = await TenantDatabase.CreateAsync(catalog, namedForB);
        await database.StampAsync(tenantA, Unique.TenantKey());

        await using NpgsqlConnection connection = await database.OpenAsAppAsync();

        // The naive check would pass: this really is the database the catalog would route B to.
        ((string?)await Scalar(connection, "select current_database()")).ShouldBe(namedForB);

        TenantRoutingViolationException refused = await Should.ThrowAsync<TenantRoutingViolationException>(
            () => TenantIdentityStamp.AssertAsync(connection, tenantB, CancellationToken.None));

        refused.Expected.ShouldBe(tenantB);
        refused.Found.ShouldBe(tenantA);
        refused.DatabaseName.ShouldBe(namedForB);
        refused.Message.ShouldContain(tenantA.ToString());
        refused.Message.ShouldContain(tenantB.ToString());
    }

    [Fact]
    public async Task A_tenant_database_with_the_table_but_no_stamp_is_refused_rather_than_trusted()
    {
        // Provisioning step 4 creates the table and inserts the row in one step, but a database
        // caught between the two - or one restored from a backup taken there - proves nothing
        // about any tenant, and the check fails closed.
        TenantId tenant = TenantId.Create();
        await using TenantDatabase database = await TenantDatabase.CreateAsync(catalog, Unique.Identifier("aurora_t_stamp"));
        await using NpgsqlConnection connection = await database.OpenAsMigratorAsync();

        TenantRoutingViolationException refused = await Should.ThrowAsync<TenantRoutingViolationException>(
            () => TenantIdentityStamp.AssertAsync(connection, tenant, CancellationToken.None));

        refused.Expected.ShouldBe(tenant);
        refused.Found.ShouldBeNull();
        refused.DatabaseName.ShouldBe(database.Name);
    }

    [Fact]
    public async Task A_database_that_is_not_a_tenant_database_at_all_is_refused()
    {
        // A connection string pointed at the catalog, or at the maintenance database, has no
        // platform.tenant_identity table. That is a mis-route too, and it is reported as one
        // rather than as a bare "relation does not exist".
        TenantId tenant = TenantId.Create();
        await using NpgsqlConnection catalogConnection = await catalog.OpenAppConnectionAsync();

        TenantRoutingViolationException refused = await Should.ThrowAsync<TenantRoutingViolationException>(
            () => TenantIdentityStamp.AssertAsync(catalogConnection, tenant, CancellationToken.None));

        refused.Expected.ShouldBe(tenant);
        refused.Found.ShouldBeNull();
        refused.DatabaseName.ShouldBe(CatalogDatabaseFixture.CatalogDatabaseName);
        refused.Message.ShouldContain("platform.tenant_identity");
    }

    [Fact]
    public async Task The_stamp_table_cannot_hold_a_second_row_whichever_way_it_is_tried()
    {
        // The only_row primary key with its check is what makes "select tenant_id from
        // platform.tenant_identity" a single answer: a second row with the default value collides
        // on the key, and a second row with the other value fails the check. Both are tried, as
        // the owner, and the count is read back afterwards.
        await using TenantDatabase database = await TenantDatabase.CreateAsync(catalog, Unique.Identifier("aurora_t_stamp"));
        await database.StampAsync(TenantId.Create(), Unique.TenantKey());
        await using NpgsqlConnection owner = await database.OpenAsMigratorAsync();

        PostgresException duplicateKey = await Should.ThrowAsync<PostgresException>(() => Execute(
            owner,
            "insert into platform.tenant_identity (tenant_id, tenant_key) values (@id, @key)",
            ("id", Guid.NewGuid()), ("key", "second")));
        duplicateKey.SqlState.ShouldBe(UniqueViolation);

        PostgresException failedCheck = await Should.ThrowAsync<PostgresException>(() => Execute(
            owner,
            "insert into platform.tenant_identity (only_row, tenant_id, tenant_key) values (false, @id, @key)",
            ("id", Guid.NewGuid()), ("key", "third")));
        failedCheck.SqlState.ShouldBe(CheckViolation);

        ((long)(await Scalar(owner, "select count(*) from platform.tenant_identity"))!).ShouldBe(1L);
    }

    [Fact]
    public async Task The_app_role_can_read_the_stamp_but_cannot_rewrite_it()
    {
        // The check runs as aurora_app on every physical connection (§4.3). If that role could
        // change the stamp, a request holding the shared stage-1 credential could make any
        // database answer to any tenant id, and the control would guard nothing.
        TenantId tenant = TenantId.Create();
        await using TenantDatabase database = await TenantDatabase.CreateAsync(catalog, Unique.Identifier("aurora_t_stamp"));
        await database.StampAsync(tenant, Unique.TenantKey());
        await using NpgsqlConnection asApp = await database.OpenAsAppAsync();

        ((Guid)(await Scalar(asApp, "select tenant_id from platform.tenant_identity"))!).ShouldBe(tenant.Value);

        (string What, string Sql)[] writes =
        [
            ("re-stamping", "update platform.tenant_identity set tenant_id = @id"),
            ("un-stamping", "delete from platform.tenant_identity"),
            ("adding a row", "insert into platform.tenant_identity (only_row, tenant_id, tenant_key) values (false, @id, 'x')"),
            ("emptying the table", "truncate platform.tenant_identity"),
        ];
        int refusals = 0;
        foreach ((string what, string sql) in writes)
        {
            PostgresException refused = await Should.ThrowAsync<PostgresException>(
                () => Execute(asApp, sql, ("id", Guid.NewGuid())), what);
            refused.SqlState.ShouldBe(InsufficientPrivilege, what);
            refusals++;
        }

        refusals.ShouldBe(writes.Length, "every write shape was tried and refused");
    }

    [Fact]
    public async Task An_already_cancelled_assertion_stops_before_it_asks_the_database()
    {
        await using NpgsqlConnection connection = await catalog.OpenAppConnectionAsync();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => TenantIdentityStamp.AssertAsync(connection, TenantId.Create(), cancelled.Token));
    }

    [Fact]
    public async Task An_unassigned_expected_tenant_is_refused_before_any_query()
    {
        // The struct default reaches here only through a field nobody set; asserting "does the
        // database belong to nobody" would be a question with a wrong answer either way.
        await using NpgsqlConnection connection = await catalog.OpenAppConnectionAsync();

        await Should.ThrowAsync<ArgumentException>(
            () => TenantIdentityStamp.AssertAsync(connection, default, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(
            () => TenantIdentityStamp.AssertAsync(null!, TenantId.Create(), CancellationToken.None));
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// One tenant database on the fixture's cluster, created and stamped the way ADR-0007 §8 steps
    /// 2 and 4 do it, and dropped when the test is done.
    /// </summary>
    private sealed class TenantDatabase : IAsyncDisposable
    {
        private readonly CatalogDatabaseFixture _catalog;

        private TenantDatabase(CatalogDatabaseFixture catalog, string name)
        {
            _catalog = catalog;
            Name = name;
        }

        public string Name { get; }

        public static async Task<TenantDatabase> CreateAsync(CatalogDatabaseFixture catalog, string name)
        {
            await using (NpgsqlConnection admin = await catalog.OpenAdminMaintenanceConnectionAsync())
            {
                await CatalogDatabaseFixture.ExecuteAsync(
                    admin, $"CREATE DATABASE {name} OWNER {CatalogDatabaseFixture.MigratorRole} TEMPLATE template0 ENCODING 'UTF8'");
            }

            TenantDatabase database = new(catalog, name);
            await using NpgsqlConnection migrator = await database.OpenAsMigratorAsync();
            await CatalogDatabaseFixture.ExecuteAsync(migrator, TenantIdentityStamp.CreateSql);
            return database;
        }

        /// <summary>Step 4's insert, as the owner. The tests write it because the saga step that owns it is B-07.2's.</summary>
        public async Task StampAsync(TenantId tenantId, TenantKey tenantKey)
        {
            await using NpgsqlConnection migrator = await OpenAsMigratorAsync();
            await Execute(
                migrator,
                "insert into platform.tenant_identity (tenant_id, tenant_key) values (@id, @key)",
                ("id", tenantId.Value), ("key", tenantKey.Value));
        }

        public Task<NpgsqlConnection> OpenAsAppAsync() => OpenAsync(_catalog.AppConnectionString);

        public Task<NpgsqlConnection> OpenAsMigratorAsync() => OpenAsync(_catalog.MigratorConnectionString);

        public async ValueTask DisposeAsync()
        {
            await using NpgsqlConnection admin = await _catalog.OpenAdminMaintenanceConnectionAsync();
            await CatalogDatabaseFixture.ExecuteAsync(admin, $"DROP DATABASE {Name} WITH (FORCE)");
        }

        private async Task<NpgsqlConnection> OpenAsync(string roleConnectionString)
        {
            // Unpooled, so DROP DATABASE ... WITH (FORCE) finds nothing lingering in a pool.
            string connectionString = new NpgsqlConnectionStringBuilder(roleConnectionString)
            {
                Database = Name,
                Pooling = false,
            }.ConnectionString;
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}
