using System;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// A connection reached a database that is not the expected tenant's (ADR-0007 §4.3): stamped for
/// another tenant, or carrying no stamp that could prove anything.
/// </summary>
/// <remarks>
/// <para>
/// This is the failure the connected-database identity check raises, and every path that runs
/// that check raises this one type - the app data source's physical-connection initializer
/// (B-06.2), the DDL-path connection factory (ADR-0027 §2), provisioning's step 4 and its
/// compensation guard - so that the request pipeline, the alert and the "mark the tenant
/// <c>SchemaBlocked</c>" reaction can key on one exception rather than on a message.
/// </para>
/// <para>
/// It carries what the incident needs as data: the tenant that was expected, the tenant the
/// database turned out to be stamped for (or none), the database reached, and - when the check
/// could not be made because the database refused the read - the driver fault that said so, as
/// the inner exception. None of that is personal data; all of it is what an operator asks first.
/// </para>
/// <para>
/// The message is operator-facing - it names the other tenant and the database, which is what an
/// incident needs and what no client may learn - and must not be rendered into a client response:
/// an RFC 9457 body is composed by the problem-details mapper from the properties it chooses to
/// disclose, never from <see cref="Exception.Message"/> (ADR-0038 §3).
/// </para>
/// </remarks>
public sealed class TenantRoutingViolationException : Exception
{
    private TenantRoutingViolationException(
        string message,
        TenantId expected,
        TenantId? found,
        string databaseName,
        Exception? cause)
        : base(message, cause)
    {
        Expected = expected;
        Found = found;
        DatabaseName = databaseName;
    }

    /// <summary>The tenant the connection was opened for.</summary>
    public TenantId Expected { get; }

    /// <summary>
    /// The tenant the database is stamped for, or <see langword="null"/> when it carries no
    /// usable stamp at all.
    /// </summary>
    public TenantId? Found { get; }

    /// <summary>The database the connection reached, as PostgreSQL names it.</summary>
    public string DatabaseName { get; }

    /// <summary>The database is stamped for a different tenant.</summary>
    /// <exception cref="ArgumentException">
    /// Either id is unassigned, the two are the same tenant, or <paramref name="databaseName"/> is blank.
    /// </exception>
    public static TenantRoutingViolationException Mismatch(TenantId expected, TenantId found, string databaseName)
    {
        RequireAssigned(expected, nameof(expected));
        RequireAssigned(found, nameof(found));
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        if (found == expected)
        {
            throw new ArgumentException(
                "The stamped tenant is the expected tenant, so this is not a mismatch.", nameof(found));
        }

        return new TenantRoutingViolationException(
            $"Routing violation: the connection to database '{databaseName}' reached a database stamped " +
            $"for tenant {found}, but tenant {expected} was expected. The identity travels inside the data " +
            "(platform.tenant_identity), so a wrong connection string, a bad restore or a renamed database " +
            "cannot pass as the right tenant (ADR-0007 4.3). The request is refused.",
            expected,
            found,
            databaseName,
            cause: null);
    }

    /// <summary>The database carries no stamp that could prove which tenant it belongs to.</summary>
    /// <param name="expected">The tenant the connection was opened for.</param>
    /// <param name="databaseName">The database reached.</param>
    /// <param name="reason">Why nothing could be proven: no table, no row, an id that names nobody.</param>
    /// <param name="cause">
    /// The driver fault that made the stamp unreadable, when there was one - a missing table, a
    /// refused read - kept as the inner exception so the server's own words survive for the operator.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="expected"/> is unassigned, or <paramref name="databaseName"/> or
    /// <paramref name="reason"/> is blank.
    /// </exception>
    public static TenantRoutingViolationException Unstamped(
        TenantId expected,
        string databaseName,
        string reason,
        Exception? cause = null)
    {
        RequireAssigned(expected, nameof(expected));
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new TenantRoutingViolationException(
            $"Routing violation: the connection to database '{databaseName}' cannot be proven to belong to " +
            $"tenant {expected}: {reason}. A database without a readable identity stamp is never trusted " +
            "(ADR-0007 4.3). The request is refused.",
            expected,
            found: null,
            databaseName,
            cause);
    }

    private static void RequireAssigned(TenantId tenantId, string parameterName)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("The tenant id is unassigned (the struct default).", parameterName);
        }
    }
}
