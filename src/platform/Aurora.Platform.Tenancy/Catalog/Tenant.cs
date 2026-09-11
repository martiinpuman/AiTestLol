using System;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The registry row for one tenant: who they are, where their database is, and where they are in
/// their life (ADR-0007 §3.1, §9.2, §11.4). The row every request reads to decide which database
/// it talks to.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is here and what is not.</b> Identity, routing, lifecycle and the dates that go with
/// them. No business data: nothing a tenant records in their own books belongs in the catalog
/// (ADR-0007 §9.3), and a test over the catalog schema fails when a column that looks like it
/// appears.
/// </para>
/// <para>
/// <b>Routing columns are nullable for exactly one reason.</b> ADR-0007 §11.4 tombstones a
/// deleted tenant to "id, key, dates only". Rather than blank those columns with sentinels, they
/// are nullable and a check constraint requires every one of them to be present in every state
/// but <see cref="TenantState.Deleted"/> — so a live tenant without a cluster is impossible and a
/// tombstone without a cluster is required.
/// </para>
/// <para>
/// <b>The database name is derived from the key</b> at reservation (<c>aurora_t_&lt;key&gt;</c>,
/// hyphens to underscores so the name needs no quoting) and stored rather than recomputed, because
/// a restore re-points it to a fresh database (ADR-0007 §11.2).
/// </para>
/// <para>
/// Only the transitions B-05 needs are here: reserve and activate, the two ends of the provisioning
/// saga, and recording activity. The rest of the lifecycle arrives with the tasks that drive it
/// (B-06 <c>SchemaBlocked</c>, B-07 <c>ProvisioningFailed</c>, offboarding) as methods on this
/// type, guarded the same way <see cref="Activate"/> is.
/// </para>
/// </remarks>
internal sealed class Tenant
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxPlanLength = 64;
    public const string DatabaseNamePrefix = "aurora_t_";

    private Tenant()
    {
    }

    public TenantId Id { get; private set; }

    public TenantKey Key { get; private set; }

    public string? DisplayName { get; private set; }

    public TenantState State { get; private set; }

    public ClusterId? ClusterId { get; private set; }

    public string? DatabaseName { get; private set; }

    public Region? ResidencyRegion { get; private set; }

    /// <summary>
    /// The core schema version the tenant database is at (ADR-0007 §7.1, §7.5), recorded when
    /// provisioning completes and by every migration run after that. Null until then.
    /// </summary>
    public int? CoreSchemaVersion { get; private set; }

    public string? Plan { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public DateTimeOffset? DeletionDueAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>
    /// When the tenant last opened a scope, at minute granularity; the outbox sweep tiers on it
    /// (ADR-0007 §10.3).
    /// </summary>
    public DateTimeOffset? LastActivityAt { get; private set; }

    /// <summary>
    /// Step 1 of the provisioning saga (ADR-0007 §8): a tenant in <see cref="TenantState.Provisioning"/>
    /// placed on <paramref name="cluster"/>, in that cluster's region, with a database name derived
    /// from its key.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An identifier is unassigned, or <paramref name="displayName"/> or <paramref name="plan"/> is
    /// blank or too long, or <paramref name="reservedAt"/> is not UTC.
    /// </exception>
    /// <exception cref="InvalidOperationException"><paramref name="cluster"/> is not accepting tenants.</exception>
    public static Tenant Reserve(
        TenantId id,
        TenantKey key,
        string displayName,
        DatabaseCluster cluster,
        string plan,
        DateTimeOffset reservedAt)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("The tenant id is unassigned.", nameof(id));
        }

        if (!key.IsSpecified)
        {
            throw new ArgumentException("The tenant key is unassigned.", nameof(key));
        }

        ArgumentNullException.ThrowIfNull(cluster);
        RequireText(displayName, MaxDisplayNameLength, nameof(displayName));
        RequireText(plan, MaxPlanLength, nameof(plan));
        UtcInstant.Require(reservedAt, nameof(reservedAt));

        if (cluster.State != DatabaseClusterState.Accepting)
        {
            throw new InvalidOperationException(
                $"Cluster '{cluster.Id}' is {cluster.State} and takes no new tenants; a tenant is " +
                "reserved only on a cluster that is accepting.");
        }

        return new Tenant
        {
            Id = id,
            Key = key,
            DisplayName = displayName,
            State = TenantState.Provisioning,
            ClusterId = cluster.Id,
            ResidencyRegion = cluster.Region,
            DatabaseName = DatabaseNameFor(key),
            Plan = plan,
            CreatedAt = reservedAt,
        };
    }

    /// <summary>
    /// Step 8 of the provisioning saga (ADR-0007 §8): the database exists, is migrated to
    /// <paramref name="coreSchemaVersion"/> and is routable.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant is not provisioning.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="coreSchemaVersion"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="activatedAt"/> is not UTC.</exception>
    public void Activate(int coreSchemaVersion, DateTimeOffset activatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coreSchemaVersion);
        UtcInstant.Require(activatedAt, nameof(activatedAt));

        if (State != TenantState.Provisioning)
        {
            throw new InvalidOperationException(
                $"Tenant '{Key}' is {State}; only a provisioning tenant can be activated.");
        }

        State = TenantState.Active;
        CoreSchemaVersion = coreSchemaVersion;
        ActivatedAt = activatedAt;
    }

    /// <summary>Notes that the tenant opened a scope (ADR-0007 §10.3).</summary>
    /// <exception cref="ArgumentException"><paramref name="at"/> is not UTC.</exception>
    public void RecordActivity(DateTimeOffset at) => LastActivityAt = UtcInstant.Require(at, nameof(at));

    /// <summary>
    /// <c>aurora_t_&lt;key&gt;</c> with hyphens as underscores: the name ADR-0007 §8 step 2 creates,
    /// spelled so it needs no quoting anywhere. Keys never contain underscores, so two keys cannot
    /// map to one name.
    /// </summary>
    public static string DatabaseNameFor(TenantKey key)
    {
        string name = DatabaseNamePrefix + key.Value.Replace('-', '_');

        return PostgresIdentifier.IsWellFormed(name)
            ? name
            : throw new InvalidOperationException(
                $"'{name}' is not a database name PostgreSQL can take unquoted, which the tenant key " +
                "rules should have made impossible.");
    }

    private static void RequireText(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > maxLength || value != value.Trim())
        {
            throw new ArgumentException(
                $"Expected 1 to {maxLength} characters with no leading or trailing whitespace.",
                parameterName);
        }
    }
}
