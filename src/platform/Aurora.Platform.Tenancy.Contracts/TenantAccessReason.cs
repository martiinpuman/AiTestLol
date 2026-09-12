namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Why a unit of work is inside a tenant (ADR-0007 §3.4): the six ways a <see cref="TenantScope"/>
/// comes to be opened, and no seventh.
/// </summary>
/// <remarks>
/// <para>
/// The reason is not decoration. The scope factory (B-06.3) decides, per reason, which checks
/// apply and which path opened the scope - a request is resolved from a host or a claim, a job or
/// an outbox dispatch from a <c>TenantId</c> carried in a payload (§10), operator support from the
/// console - and an audit line that says <em>why</em> a tenant was entered is the difference
/// between an incident timeline and a guess.
/// </para>
/// <para>
/// There is no "unspecified" member. An enum has a default whether or not anyone wants one, and
/// here it is <see cref="Request"/>; a scope never infers its reason from that default, because
/// the code that opens a scope always names one. <see cref="Provisioning"/> and
/// <see cref="Migration"/> stay in the list because ADR-0007 §3.4 names them, and because a job or
/// operator action that runs <em>during</em> those phases still needs to say so; the DDL path
/// itself holds a <c>TenantDatabaseHandle</c>, not a scope (ADR-0027 §1).
/// </para>
/// </remarks>
public enum TenantAccessReason
{
    /// <summary>A web request or an interactive circuit, resolved per ADR-0007 §3.2.</summary>
    Request,

    /// <summary>A tenant job carrying its <c>TenantId</c> in its payload (ADR-0007 §10.1).</summary>
    Job,

    /// <summary>The outbox dispatcher delivering one tenant's events (ADR-0007 §10.3).</summary>
    Outbox,

    /// <summary>Work done while the tenant is being provisioned (ADR-0007 §8).</summary>
    Provisioning,

    /// <summary>Work done while the tenant is being migrated (ADR-0007 §7).</summary>
    Migration,

    /// <summary>An operator acting on the tenant's behalf from the console, always audited.</summary>
    OperatorSupport,
}
