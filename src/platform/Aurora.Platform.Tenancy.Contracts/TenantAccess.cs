using System;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Proof that the holder knows which tenant it is in (ADR-0027 §1): the base of the two forms that
/// proof takes, <see cref="TenantScope"/> on the application path and <c>TenantDatabaseHandle</c>
/// on the DDL path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a proof rather than an id.</b> A <see cref="SharedKernel.TenantId"/> can be typed into
/// a job payload, read from a claim, or copied from a log; anything can hold one. A
/// <see cref="TenantAccess"/> can only come from <c>Aurora.Platform.Tenancy</c>, which is what
/// makes "a tenant <c>DbContext</c> takes a <c>TenantAccess</c>" (ADR-0007 §4.1 as ADR-0027 §1
/// restates it) a guarantee and not a convention: the constructor that could produce one is
/// <c>internal</c>, and <c>internal</c> reaches exactly the assemblies the contracts project's
/// <c>[InternalsVisibleTo]</c> names. Both facts are asserted in <c>TenantAccessConstructionTests</c>,
/// as an exact set, so widening either is a red test.
/// </para>
/// <para>
/// <b>What is checked on the way in.</b> An unassigned id or an unspecified key is refused here,
/// once, so that no derived proof can carry either and no consumer has to check.
/// </para>
/// <para>
/// <b>Identity, not value.</b> Two proofs for one tenant are two proofs: a scope is leased and
/// disposed (§3.4, §10.4), a handle is one connection, and neither is interchangeable with another
/// instance by virtue of naming the same tenant. Equality is therefore reference equality.
/// </para>
/// </remarks>
public abstract class TenantAccess
{
    /// <summary>Binds the proof to a tenant. Reachable only through <c>[InternalsVisibleTo]</c>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is unassigned or <paramref name="tenantKey"/> is unspecified.
    /// </exception>
    internal TenantAccess(TenantId tenantId, TenantKey tenantKey)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException(
                "The tenant id is unassigned (the struct default). A proof of tenant identity cannot " +
                "name no tenant.",
                nameof(tenantId));
        }

        if (!tenantKey.IsSpecified)
        {
            throw new ArgumentException(
                "The tenant key is unspecified (the struct default). A proof of tenant identity " +
                "carries the key the tenant is routed and named by.",
                nameof(tenantKey));
        }

        TenantId = tenantId;
        TenantKey = tenantKey;
    }

    /// <summary>The tenant this proof is for.</summary>
    public TenantId TenantId { get; }

    /// <summary>The tenant's routable name, for database names, log lines and metric labels.</summary>
    public TenantKey TenantKey { get; }

    /// <summary>The key and the id, for a log line. Never throws.</summary>
    public override string ToString() => $"tenant {TenantKey} ({TenantId})";
}
