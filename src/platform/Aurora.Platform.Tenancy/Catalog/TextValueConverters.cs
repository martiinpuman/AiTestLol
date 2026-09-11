using Aurora.Platform.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The registry's text-backed value objects, each stored as its text and rebuilt through its own
/// <c>Parse</c>, so a row can never materialise a value the type would have refused to construct.
/// </summary>
internal sealed class TenantKeyConverter : ValueConverter<TenantKey, string>
{
    public TenantKeyConverter()
        : base(key => key.Value, text => TenantKey.Parse(text, null))
    {
    }
}

internal sealed class ClusterIdConverter : ValueConverter<ClusterId, string>
{
    public ClusterIdConverter()
        : base(id => id.Value, text => ClusterId.Parse(text, null))
    {
    }
}

internal sealed class RegionConverter : ValueConverter<Region, string>
{
    public RegionConverter()
        : base(region => region.Value, text => Region.Parse(text, null))
    {
    }
}

internal sealed class SecretReferenceConverter : ValueConverter<SecretReference, string>
{
    public SecretReferenceConverter()
        : base(reference => reference.Value, text => SecretReference.Of(text))
    {
    }
}
