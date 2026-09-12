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
    public async Task A_stamped_database_the_app_role_can_no_longer_read_is_a_routing_violation_not_a_driver_error()
    {
        // A database provisioned by a build older than the grant, restored from a backup taken
        // before it, or hit by a REVOKE during an incident: the stamp is there and the request-path
        // role cannot read it. That is "cannot prove", and it must reach the caller as the one type
        // the alert and the SchemaBlocked reaction key on - not as SQLSTATE 42501 from the driver,
        // which fails the request closed and fires nothing else. Both grant shapes are revoked in
        // turn, because the role loses the read either way.
        TenantId tenant = TenantId.Create();
        await using TenantDatabase database = await TenantDatabase.CreateAsync(catalog, Unique.Identifier("aurora_t_stamp"));
        await database.StampAsync(tenant, Unique.TenantKey());

        // Two arms that each fail on their own: the table read is revoked and refused; then it is
        // granted back and the assertion is shown to pass again, so that the second arm's refusal
        // can only be the schema's. Without the re-grant the arms would be cumulative and the
        // schema arm could never fail independently.
        await using NpgsqlConnection owner = await database.OpenAsMigratorAsync();

        await Execute(owner, "revoke select on platform.tenant_identity from aurora_app");
        TenantRoutingViolationException tableRefused = await RefusedAsAppAsync(database, tenant, "select on the table revoked");

        await Execute(owner, "grant select on platform.tenant_identity to aurora_app");
        await using (NpgsqlConnection restored = await database.OpenAsAppAsync())
        {
            await TenantIdentityStamp.AssertAsync(restored, tenant, CancellationToken.None);
        }

        await Execute(owner, "revoke usage on schema platform from aurora_app");
        TenantRoutingViolationException schemaRefused = await RefusedAsAppAsync(database, tenant, "usage on the schema revoked");

        foreach ((TenantRoutingViolationException refused, string what) in new[]
                 {
                     (tableRefused, "select on the table revoked"), (schemaRefused, "usage on the schema revoked"),
                 })
        {
            refused.Expected.ShouldBe(tenant, what);
            refused.Found.ShouldBeNull(what);
            refused.DatabaseName.ShouldBe(database.Name, what);
            refused.Message.ShouldContain(InsufficientPrivilege, Case.Sensitive, what);
            refused.InnerException.ShouldBeOfType<PostgresException>(what).SqlState.ShouldBe(InsufficientPrivilege, what);
        }
    }

    [Fact]
    public async Task A_pre_existing_table_of_another_shape_holding_two_rows_proves_nothing()
    {
        // "create table if not exists" is right for a replayed step 4 and blind to a table that was
        // already there in another shape - no key, no check, and therefore room for two rows. The
        // single-answer property must then be the assertion's, not the table's: the first row of an
        // unordered scan is not a verdict, even when - as here - it happens to name the expected
        // tenant.
        TenantId tenant = TenantId.Create();
        await using TenantDatabase database = await TenantDatabase.CreateAsync(
            catalog, Unique.Identifier("aurora_t_stamp"), withStampTable: false);
        await using NpgsqlConnection owner = await database.OpenAsMigratorAsync();
        await Execute(owner, "create schema platform");
        await Execute(owner, "create table platform.tenant_identity (tenant_id uuid not null, tenant_key text not null)");
        await Execute(owner, TenantIdentityStamp.CreateSql);
        await Execute(
            owner,
            "insert into platform.tenant_identity (tenant_id, tenant_key) values (@first, 'first'), (@second, 'second')",
            ("first", tenant.Value), ("second", Guid.NewGuid()));
        ((long)(await Scalar(owner, "select count(*) from platform.tenant_identity"))!).ShouldBe(2L, "the DDL accepted the foreign table as it was");

        TenantRoutingViolationException refused = await Should.ThrowAsync<TenantRoutingViolationException>(
            () => TenantIdentityStamp.AssertAsync(owner, tenant, CancellationToken.None));

        refused.Expected.ShouldBe(tenant);
        refused.Found.ShouldBeNull();
        refused.DatabaseName.ShouldBe(database.Name);
        refused.Message.ShouldContain("more than one row");
    }

    [Fact]
    public async Task The_cleanup_drops_the_database_even_while_an_app_session_is_still_attached()
    {
        // The flake PR #13's second review measured (2 of 5 suite runs): DROP DATABASE ... WITH
        // (FORCE) issued as aurora_admin is refused 42501 while an aurora_app backend is attached,
        // because aurora_admin is a member of aurora_migrator but not of aurora_app and holds no
        // pg_signal_backend. The database then leaks with PUBLIC CONNECT on it, and B-05's
        // CatalogPrivilegeTests fails for a reason that has nothing to do with isolation. This holds
        // an app session open across the drop on purpose and requires the drop to succeed anyway,
        // and the session to be gone afterwards.
        TenantDatabase database = await TenantDatabase.CreateAsync(catalog, Unique.Identifier("aurora_t_stamp"));
        await using NpgsqlConnection attached = await database.OpenAsAppAsync();
        ((string?)await Scalar(attached, "select current_database()")).ShouldBe(database.Name);

        await database.DisposeAsync();

        await using NpgsqlConnection admin = await catalog.OpenAdminMaintenanceConnectionAsync();
        await using var remaining = new NpgsqlCommand("select count(*) from pg_database where datname = @name", admin);
        remaining.Parameters.AddWithValue("name", database.Name);
        ((long)(await remaining.ExecuteScalarAsync())!).ShouldBe(0L, "the database must be gone, attached session or not");

        Exception? severed = await Record.ExceptionAsync(() => Scalar(attached, "select 1"));
        severed.ShouldNotBeNull("the attached session was terminated by the drop");
        severed.ShouldBeAssignableTo<NpgsqlException>();
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

    private static async Task<TenantRoutingViolationException> RefusedAsAppAsync(TenantDatabase database, TenantId tenant, string what)
    {
        await using NpgsqlConnection asApp = await database.OpenAsAppAsync();
        return await Should.ThrowAsync<TenantRoutingViolationException>(
            () => TenantIdentityStamp.AssertAsync(asApp, tenant, CancellationToken.None), what);
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

        /// <param name="catalog">The fixture whose cluster the database is created on.</param>
        /// <param name="name">The database name, as step 2 would choose it.</param>
        /// <param name="withStampTable">
        /// Whether to run step 4's DDL. <see langword="false"/> leaves a database in the state
        /// between steps 2 and 4, for a test that wants to shape the table itself.
        /// </param>
        public static async Task<TenantDatabase> CreateAsync(CatalogDatabaseFixture catalog, string name, bool withStampTable = true)
        {
            await using (NpgsqlConnection admin = await catalog.OpenAdminMaintenanceConnectionAsync())
            {
                await CatalogDatabaseFixture.ExecuteAsync(
                    admin, $"CREATE DATABASE {name} OWNER {CatalogDatabaseFixture.MigratorRole} TEMPLATE template0 ENCODING 'UTF8'");
            }

            TenantDatabase database = new(catalog, name);
            if (withStampTable)
            {
                await using NpgsqlConnection migrator = await database.OpenAsMigratorAsync();
                await CatalogDatabaseFixture.ExecuteAsync(migrator, TenantIdentityStamp.CreateSql);
            }

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

        /// <summary>
        /// Drops the database as the container's superuser and verifies that it is gone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cleanup, not the privilege model under test. <c>DROP DATABASE ... WITH (FORCE)</c>
        /// terminates every backend still attached, and PostgreSQL lets a role terminate only its
        /// own backends, those of roles it is a member of, or any at all with
        /// <c>pg_signal_backend</c>. The fixture's <c>aurora_admin</c> is a member of
        /// <c>aurora_migrator</c>, not of <c>aurora_app</c>, and holds no <c>pg_signal_backend</c> -
        /// so a drop issued as <c>aurora_admin</c> is refused <c>42501</c> whenever a test's app
        /// session has closed its socket but its backend has not yet exited, the database leaks
        /// with <c>PUBLIC CONNECT</c> on it, and B-05's <c>CatalogPrivilegeTests</c> finds a database
        /// the app role may open. Unpooled connections do not close that race; they only keep this
        /// process from parking an idle connection in the database. Whether the provisioner role
        /// should hold <c>pg_signal_backend</c> for ADR-0007 §8's own drop is the architect's and
        /// B-07's question (routed by PR #13's second review), which is why this does not answer it
        /// by granting the role something here.
        /// </para>
        /// <para>
        /// The drop is verified, not trusted: a cleanup that fails silently is how a leaked
        /// database reaches the next test's view of the cluster.
        /// </para>
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            await using NpgsqlConnection superuser = await _catalog.OpenSuperuserConnectionAsync();
            await CatalogDatabaseFixture.ExecuteAsync(superuser, $"DROP DATABASE {Name} WITH (FORCE)");

            await using var remaining = new NpgsqlCommand("select count(*) from pg_database where datname = @name", superuser);
            remaining.Parameters.AddWithValue("name", Name);
            if ((long)(await remaining.ExecuteScalarAsync())! != 0)
            {
                throw new InvalidOperationException(
                    $"Tenant database '{Name}' was not dropped; left in place it would leak into every later "
                    + "test's view of the cluster.");
            }
        }

        private async Task<NpgsqlConnection> OpenAsync(string roleConnectionString)
        {
            // Unpooled, so a connection a test opens is closed when its `await using` ends rather
            // than parked idle in this process's pool, keeping the database busy. The backend on the
            // server side may still be exiting when the drop runs; DisposeAsync handles that.
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
