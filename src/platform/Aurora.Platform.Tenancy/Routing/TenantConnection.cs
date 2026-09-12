using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The result of resolving a tenant (ADR-0007 §3.5): a fully formed connection string, credential
/// included, plus the routing facts a caller may legitimately need without parsing the string.
/// </summary>
/// <param name="ConnectionString">
/// Complete, including the credential from the secret store and every ADR-0007 §5.2 pool setting.
/// Hand it to an <c>NpgsqlDataSourceBuilder</c>; never to a log.
/// </param>
/// <param name="ClusterId">The cluster the tenant is placed on.</param>
/// <param name="DatabaseName">The tenant's database on that cluster.</param>
/// <param name="ResidencyRegion">The region the tenant's data may live in (ADR-0007 §11.3).</param>
internal sealed record TenantConnection(
    string ConnectionString,
    ClusterId ClusterId,
    string DatabaseName,
    Region ResidencyRegion)
{
    /// <summary>
    /// Names the routing facts and redacts the connection string. A record's generated
    /// <c>ToString</c> prints every member, and this one's carries a password; a diagnostic that
    /// interpolated the value would put a credential in a log (ADR-0016).
    /// </summary>
    public override string ToString() =>
        $"{nameof(TenantConnection)} {{ {nameof(ClusterId)} = {ClusterId}, {nameof(DatabaseName)} = {DatabaseName}, "
        + $"{nameof(ResidencyRegion)} = {ResidencyRegion}, {nameof(ConnectionString)} = <redacted> }}";
}
