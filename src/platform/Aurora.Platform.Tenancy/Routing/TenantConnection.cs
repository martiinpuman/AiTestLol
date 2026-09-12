using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The result of resolving a tenant (ADR-0007 §3.5): a fully formed connection string, credential
/// included, plus the routing facts a caller may legitimately need without parsing the string.
/// </summary>
/// <param name="ConnectionString">
/// Complete, including the credential from the secret store and every ADR-0007 §5.2 pool setting.
/// It reveals itself only through <see cref="ConnectionSecret.Reveal"/> and renders redacted on
/// every other path — this record's own printing, JSON and reflection included — so the record
/// needs no rendering of its own to keep the credential out of a log.
/// </param>
/// <param name="ClusterId">The cluster the tenant is placed on.</param>
/// <param name="DatabaseName">The tenant's database on that cluster.</param>
/// <param name="ResidencyRegion">The region the tenant's data may live in (ADR-0007 §11.3).</param>
internal sealed record TenantConnection(
    ConnectionSecret ConnectionString,
    ClusterId ClusterId,
    string DatabaseName,
    Region ResidencyRegion);
