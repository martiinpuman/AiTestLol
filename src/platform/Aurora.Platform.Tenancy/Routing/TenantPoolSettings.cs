using System;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The pool settings of ADR-0007 §5.2, one member per row of its table, and why each is what it is.
/// </summary>
/// <remarks>
/// <para>
/// Every tenant gets its own <c>NpgsqlDataSource</c> and therefore its own pool (§5.1), so these
/// are <em>per tenant, per process</em>. The arithmetic in §5.3 — instances × active tenants × pool
/// size — is what makes each of the values below smaller than Npgsql's default: an idle tenant
/// must cost zero backends, a busy one must not be able to exhaust the cluster, and a quiet one
/// must hand its backends back within seconds.
/// </para>
/// <para>
/// The values differ between the two hosts in exactly two places, the pool cap and the command
/// timeout, because interactive work is bounded and batch work is not; the six other settings are
/// the same everywhere. <c>Max Auto Prepare</c> stays at zero because ADR-0004 forbids
/// session-scoped features under transaction pooling.
/// </para>
/// </remarks>
internal sealed record TenantPoolSettings(int MaximumPoolSize, int CommandTimeoutSeconds, string ApplicationNamePrefix)
{
    /// <summary>An idle tenant must cost zero backends. Never raise this (§5.2).</summary>
    public const int MinimumPoolSize = 0;

    /// <summary>Returns backends quickly when a tenant goes quiet — the dominant cost driver (§5.2).</summary>
    public const int ConnectionIdleLifetimeSeconds = 30;

    /// <summary>Matches the shorter idle lifetime (§5.2).</summary>
    public const int ConnectionPruningIntervalSeconds = 5;

    /// <summary>Off: required by ADR-0004's transaction-pooling constraint (§5.2).</summary>
    public const int MaxAutoPrepare = 0;

    /// <summary>Fail fast; a saturated pool surfaces as an error, not a hang holding a circuit (§5.2).</summary>
    public const int ConnectionTimeoutSeconds = 5;

    /// <summary>The web host's column of the §5.2 table.</summary>
    public static readonly TenantPoolSettings Web = new(MaximumPoolSize: 10, CommandTimeoutSeconds: 30, ApplicationNamePrefix: "aurora-web");

    /// <summary>The worker host's column of the §5.2 table.</summary>
    public static readonly TenantPoolSettings Worker = new(MaximumPoolSize: 5, CommandTimeoutSeconds: 300, ApplicationNamePrefix: "aurora-worker");

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="profile"/> is not a value of the enum.</exception>
    public static TenantPoolSettings For(TenantPoolProfile profile) => profile switch
    {
        TenantPoolProfile.Web => Web,
        TenantPoolProfile.Worker => Worker,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Not a tenant pool profile."),
    };

    /// <summary>
    /// <c>aurora-web:{tenantKey}</c> — what <c>pg_stat_activity</c> shows for the tenant's
    /// connections (§5.2). Set in the connection string, never via <c>SET</c> (ADR-0004).
    /// </summary>
    public string ApplicationNameFor(string tenantKey) => $"{ApplicationNamePrefix}:{tenantKey}";
}
