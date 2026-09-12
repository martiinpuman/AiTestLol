using System;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// A data-residency region — <c>nz</c>, <c>eu-west</c> — the place a tenant's data is allowed to
/// live (ADR-0007 §11.3).
/// </summary>
/// <remarks>
/// <para>
/// A tenant declares its region and a cluster belongs to one; routing never crosses the two.
/// That rule is enforced in the catalog itself: <c>catalog.tenant</c> references
/// <c>catalog.database_cluster</c> on <em>both</em> the cluster id and the region, so a tenant
/// row naming a cluster in another region cannot be written at all. Having the region be a
/// type rather than a bare string is what lets that pair be compared without ceremony.
/// </para>
/// <para>
/// The spelling is the registry's usual one (see <see cref="TenantKey"/>), because a region
/// appears in backup locations, metric labels and cluster ids.
/// </para>
/// </remarks>
public readonly record struct Region : IParsable<Region>
{
    /// <summary>The shortest region accepted.</summary>
    public const int MinLength = 2;

    /// <summary>The longest region accepted.</summary>
    public const int MaxLength = 32;

    private readonly string? _value;

    private Region(string value) => _value = value;

    /// <summary>
    /// Whether this value names a region. <see langword="false"/> only for the struct default.
    /// </summary>
    public bool IsSpecified => _value is not null;

    /// <summary>The region's text.</summary>
    /// <exception cref="InvalidOperationException">The region is unspecified.</exception>
    public string Value => _value ?? throw Unspecified();

    /// <summary>Reads a region from its text form.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> is not a well-formed region.</exception>
    public static Region Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);

        return TryParse(s, provider, out Region region)
            ? region
            : throw new FormatException(
                $"'{s}' is not a region. A region is {RegistrySlug.Describe(MinLength, MaxLength)}.");
    }

    /// <summary>Reads a region from its text form, reporting failure rather than throwing.</summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Region result)
    {
        if (RegistrySlug.IsWellFormed(s, MinLength, MaxLength))
        {
            result = new Region(s);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>The region's text, or a placeholder when unspecified. Never throws.</summary>
    public override string ToString() => _value ?? "<unspecified region>";

    private static InvalidOperationException Unspecified() =>
        new("The region is unspecified. A Region obtained from `default` names no region; " +
            "read one with Region.Parse(text).");
}
