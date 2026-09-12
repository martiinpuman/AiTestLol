using System;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The committed kinds of catalog-level cache entry (ADR-0012 rule 2, ADR-0029 A1.1). A new
/// cross-tenant entry is a reviewed edit to this enum, not a string literal.
/// </summary>
internal enum CatalogCacheKind
{
    /// <summary>Tenant routing / connection: 60 s, invalidated on tenant state change (ADR-0012 §3).</summary>
    TenantRouting,
}

/// <summary>
/// Builds every <c>c:</c>-prefixed <c>HybridCache</c> key — the catalog-side sibling of
/// <c>TenantCacheKey.For</c> (ADR-0012 rules 1–2 as amended by ADR-0029 A1.1; fitness rule T10,
/// not yet built). A catalog entry is cross-tenant by nature and holds no business data; the
/// prefix marks it as such so that a key with no tenant in it can never be mistaken for one that
/// should have had one.
/// </summary>
internal static class CatalogCacheKey
{
    /// <summary>ADR-0012 rule 2's marker for a catalog-level entry.</summary>
    public const string Prefix = "c:";

    /// <summary><c>c:{kind}:{discriminator}</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="discriminator"/> is blank.</exception>
    public static string For(CatalogCacheKind kind, string discriminator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);

        return $"{Prefix}{Segment(kind)}:{discriminator}";
    }

    /// <summary>
    /// The key segment for a kind, spelled here rather than taken from the enum member's name, so
    /// that renaming a member can never silently change every key an L2 backend already holds.
    /// </summary>
    private static string Segment(CatalogCacheKind kind) => kind switch
    {
        CatalogCacheKind.TenantRouting => "tenant-routing",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a committed catalog cache kind."),
    };
}
