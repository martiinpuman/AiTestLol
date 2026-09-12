using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Npgsql;

namespace Aurora.Platform.Tenancy;

/// <summary>
/// The identity stamp every tenant database carries (ADR-0007 §4.3), in its one place: the DDL of
/// <c>platform.tenant_identity</c> and the assertion that a connection reached the tenant it was
/// opened for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the identity is inside the data.</b> Layers 1 and 2 of the ADR-0007 §4 guarantee stop a
/// developer forgetting the tenant. They cannot stop a mis-routed connection string - a catalog
/// bug, a restore into a differently named database, a routing row edited by hand, a failover to
/// a stale replica. So each database is stamped with its tenant's id at provisioning (§8 step 4),
/// and every path that opens a connection asks the database who it is before trusting it. That is
/// stronger than comparing <c>current_database()</c>: the name is exactly what a bad restore or a
/// hand rename gets wrong, and the stamp travels with the rows.
/// </para>
/// <para>
/// <b>One implementation, by design.</b> ADR-0027 §2: "three hand-written copies of this query is
/// how they drift". The app data source's physical-connection initializer (B-06.2), the DDL-path
/// connection factory (B-07.1), provisioning step 4 (B-07.2) and the migration executor (B-08.1)
/// all call <see cref="AssertAsync"/>; each proves it for its own path with a deliberate mis-route
/// test, and none re-queries the table itself.
/// </para>
/// <para>
/// <b>What the assertion refuses.</b> A stamp for another tenant, obviously - and, just as firmly,
/// a database that cannot prove anything: no <c>platform</c> schema or no table (the connection
/// reached the catalog, the maintenance database, or something that is not ours), a table with no
/// row, or a row whose id names nobody. Every one is <see cref="TenantRoutingViolationException"/>,
/// because "cannot prove it is the right tenant" and "is the wrong tenant" call for the same
/// reaction: fail the request, alert, mark the tenant <c>SchemaBlocked</c>.
/// </para>
/// <para>
/// <b>Internal, like the rest of this assembly.</b> Its callers are the tenancy module and the
/// provisioning/migration components that ADR-0027 places beside it; a module never asserts a
/// stamp itself, because a module never holds a raw connection.
/// </para>
/// </remarks>
internal static class TenantIdentityStamp
{
    /// <summary>The table's qualified name, for messages and for the tests that probe it.</summary>
    public const string TableName = "platform.tenant_identity";

    /// <summary>
    /// The DDL of ADR-0007 §4.3, run once per tenant database at provisioning step 4 as
    /// <c>aurora_migrator</c>, the owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>only_row</c> is the primary key and may only be <see langword="true"/>: a second row with
    /// the default collides on the key, a second row with <see langword="false"/> fails the check,
    /// so "the" stamp is a single answer. <c>stamped_at</c> defaults to the database's clock rather
    /// than the caller's, because it records when this database was claimed, which is a fact about
    /// the database.
    /// </para>
    /// <para>
    /// The grants are here because the check runs as <c>aurora_app</c> on every physical connection
    /// and a stamp that role cannot read would fail every request; the role gets <c>SELECT</c> and
    /// nothing else, so it cannot re-stamp a database as another tenant. Idempotent
    /// (<c>if not exists</c>, and grants are), because step 4 may be replayed after a crash.
    /// </para>
    /// </remarks>
    public const string CreateSql =
        """
        create schema if not exists platform;

        create table if not exists platform.tenant_identity (
            only_row    boolean     primary key default true check (only_row),
            tenant_id   uuid        not null,
            tenant_key  text        not null,
            stamped_at  timestamptz not null default now()
        );

        grant usage on schema platform to aurora_app;
        grant select on platform.tenant_identity to aurora_app;
        """;

    /// <summary>The one query every path runs (ADR-0007 §4.3, verbatim).</summary>
    private const string ReadStampSql = "select tenant_id from platform.tenant_identity";

    /// <summary>
    /// Proves that <paramref name="connection"/> reached the database stamped for
    /// <paramref name="expected"/>, or throws.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="expected"/> is unassigned.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="TenantRoutingViolationException">
    /// The database is stamped for another tenant, or carries no stamp that proves anything: no
    /// <c>platform.tenant_identity</c> table, no row, or an id that names nobody.
    /// </exception>
    public static async Task AssertAsync(NpgsqlConnection connection, TenantId expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (expected.IsEmpty)
        {
            throw new ArgumentException(
                "The expected tenant id is unassigned (the struct default). A stamp cannot be asserted " +
                "against no tenant.",
                nameof(expected));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string database = connection.Database;
        object? stamped;
        try
        {
            await using var read = new NpgsqlCommand(ReadStampSql, connection);
            stamped = await read.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException notATenantDatabase)
            when (notATenantDatabase.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            throw TenantRoutingViolationException.Unstamped(
                expected, database, $"it has no {TableName} table, so it is not a tenant database at all");
        }

        switch (stamped)
        {
            case null:
                throw TenantRoutingViolationException.Unstamped(expected, database, $"{TableName} holds no row");
            case Guid id when id == Guid.Empty:
                throw TenantRoutingViolationException.Unstamped(expected, database, "the stamp names no tenant (an all-zero id)");
            case Guid id when id != expected.Value:
                throw TenantRoutingViolationException.Mismatch(expected, TenantId.From(id), database);
            case Guid:
                return;
            default:
                throw TenantRoutingViolationException.Unstamped(
                    expected, database, $"{TableName}.tenant_id is not a uuid ({stamped.GetType().Name})");
        }
    }
}
