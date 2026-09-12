using System;
using System.Buffers;

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

    /// <summary>
    /// What a discriminator may be made of: letters, digits, <c>.</c>, <c>_</c> and <c>-</c>. No
    /// separator, so two discriminators can never read as one key; no whitespace or invisible
    /// character, so two keys can never look alike. A kind keyed by text that needs escaping — a
    /// tenant key qualifies, a host header does not — is refused here rather than allowed to
    /// collide (PR #14 L-2).
    /// </summary>
    private static readonly SearchValues<char> DiscriminatorAlphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    /// <summary><c>c:{kind}:{discriminator}</c>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="discriminator"/> is empty or holds a character outside <c>[A-Za-z0-9._-]</c>.
    /// </exception>
    public static string For(CatalogCacheKind kind, string discriminator)
    {
        ArgumentException.ThrowIfNullOrEmpty(discriminator);

        if (discriminator.AsSpan().ContainsAnyExcept(DiscriminatorAlphabet))
        {
            throw new ArgumentException(
                "A catalog cache key discriminator is one or more of [A-Za-z0-9._-]; anything else - a separator, " +
                "whitespace, an invisible character - would let two keys collide or look alike.",
                nameof(discriminator));
        }

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
