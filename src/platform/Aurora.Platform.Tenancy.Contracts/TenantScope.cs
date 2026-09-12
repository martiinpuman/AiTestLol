using System;
using System.Threading.Tasks;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The single representation of "we know which tenant we are in" on the application path
/// (ADR-0007 §3.4): the proof every application-service method takes as an explicit parameter and
/// every tenant <c>DbContext</c> factory demands (§4.1, §4.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it carries.</b> Everything a request needs to know about its tenant without another
/// catalog read: the identity and key (<see cref="TenantAccess"/>), where the data may live
/// (<see cref="ResidencyRegion"/>, §11.3), which core schema version the database is at
/// (<see cref="SchemaVersion"/>, checked against <see cref="CoreSchemaVersion"/> on open, §7.5),
/// which Country Packages the tenant has (<see cref="Packages"/>, ADR-0008 §4) and why the tenant
/// was entered (<see cref="Reason"/>). Each is checked once, here, so a scope in hand is a scope
/// whose facts were all present.
/// </para>
/// <para>
/// <b>Who builds one.</b> Nobody outside <c>Aurora.Platform.Tenancy</c>: the constructor is
/// <c>internal</c>, the type is sealed, and there is no parameterless constructor for a serializer
/// or <c>new T()</c> to reach for. The scope factory (B-06.3) is the one door, and it will be the
/// only public member anywhere that returns a scope - <c>TenantAccessConstructionTests</c> holds the
/// contracts assembly to that.
/// </para>
/// <para>
/// <b>What this type does not yet do.</b> ADR-0007 §3.4 and §10.4 make a scope <em>leased</em>:
/// <see cref="IsActive"/> turns false when the lease is released and a reused scope throws
/// <c>TenantScopeExpiredException</c>. That is the scope factory's guarantee and arrives with it
/// (B-06.3). What this row ships is the surface that guarantee attaches to - the property, and
/// <see cref="IAsyncDisposable"/> - and the one state it can produce: every scope this constructor
/// returns is active, and <see cref="DisposeAsync"/> releases nothing because nothing has been
/// leased yet. It is stated here so that it is neither built twice nor forgotten.
/// </para>
/// <para>
/// <b>Why <see cref="IsActive"/> is a plain field the constructor sets.</b> A scope that bypasses
/// the constructor (<c>RuntimeHelpers.GetUninitializedObject</c>, which no accessibility rule can
/// stop) must read as unusable. A field set to <see langword="true"/> on construction reads
/// <see langword="false"/> on such an object; a "disposed" flag would read as live.
/// </para>
/// </remarks>
public sealed class TenantScope : TenantAccess, IAsyncDisposable
{
    /// <summary>Binds a scope to every fact ADR-0007 §3.4 lists. Reachable only through <c>[InternalsVisibleTo]</c>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is unassigned, or <paramref name="tenantKey"/>,
    /// <paramref name="residencyRegion"/> or <paramref name="schemaVersion"/> is unspecified.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="packages"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a defined reason.</exception>
    internal TenantScope(
        TenantId tenantId,
        TenantKey tenantKey,
        Region residencyRegion,
        SchemaVersion schemaVersion,
        InstalledPackages packages,
        TenantAccessReason reason)
        : base(tenantId, tenantKey)
    {
        if (!residencyRegion.IsSpecified)
        {
            throw new ArgumentException(
                "The residency region is unspecified (the struct default). A scope names where the " +
                "tenant's data may live (ADR-0007 11.3).",
                nameof(residencyRegion));
        }

        if (!schemaVersion.IsSpecified)
        {
            throw new ArgumentException(
                "The schema version is unspecified (the struct default). The 7.5 skew gate compares " +
                "it, so a scope cannot carry a version nobody read.",
                nameof(schemaVersion));
        }

        ArgumentNullException.ThrowIfNull(packages);

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a TenantAccessReason.");
        }

        ResidencyRegion = residencyRegion;
        SchemaVersion = schemaVersion;
        Packages = packages;
        Reason = reason;
        IsActive = true;
    }

    /// <summary>Where the tenant's data is allowed to live (ADR-0007 §11.3).</summary>
    public Region ResidencyRegion { get; }

    /// <summary>The core schema version the tenant database is at, as the catalog recorded it (ADR-0007 §7.1).</summary>
    public SchemaVersion SchemaVersion { get; }

    /// <summary>The Country Packages the tenant has, read when the scope was opened (ADR-0008 §4).</summary>
    public InstalledPackages Packages { get; }

    /// <summary>Why the tenant was entered.</summary>
    public TenantAccessReason Reason { get; }

    /// <summary>
    /// Whether this scope may be used. <see langword="true"/> for every scope the constructor
    /// returns; the lease that clears it arrives with the scope factory (B-06.3, ADR-0007 §10.4).
    /// </summary>
    public bool IsActive { get; }

    /// <summary>
    /// Releases the scope's lease. There is no lease to release until the scope factory (B-06.3)
    /// attaches one, so this completes at once and changes nothing.
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
