using System;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The human-readable, routable name of a tenant: <c>acme-trading</c> in
/// <c>acme-trading.aurora.example</c>, in <c>/t/acme-trading/...</c> and in the tenant's database
/// name (ADR-0007 §3.1, §3.2).
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> the tenant's identity. <see cref="Aurora.SharedKernel.TenantId"/> is; the
/// key is a display and routing concern that ADR-0007 §3.1 says may be renamed, which is why it
/// is a separate type rather than a property of the id. It is unique across the platform for as
/// long as the catalog row exists — including after deletion, when the row is tombstoned with its
/// key (ADR-0007 §11.4) — so a key is never reused for a different customer.
/// </para>
/// <para>
/// <b>Why the spelling is this strict.</b> The key is a DNS label, a URL segment, and the stem of
/// the database name <c>aurora_t_&lt;key&gt;</c>, the stage-2 role name
/// <c>aurora_app_&lt;key&gt;</c> and the offboarding rename <c>deleted_&lt;key&gt;_&lt;date&gt;</c>.
/// PostgreSQL truncates identifiers past 63 bytes silently, and a truncated database name is a
/// routing bug; <see cref="MaxLength"/> keeps the longest derived name (<c>deleted_</c> + key +
/// <c>_yyyymmdd</c>, 17 characters of decoration) inside that limit with room to spare.
/// </para>
/// <para>
/// A <see langword="default"/> <see cref="TenantKey"/> is <em>unspecified</em>: detectable through
/// <see cref="IsSpecified"/>, loud through <see cref="Value"/>, never silently an empty string.
/// </para>
/// </remarks>
public readonly record struct TenantKey : IParsable<TenantKey>
{
    /// <summary>The shortest key accepted.</summary>
    public const int MinLength = 3;

    /// <summary>The longest key accepted. See the type remarks for where the number comes from.</summary>
    public const int MaxLength = 40;

    private readonly string? _value;

    private TenantKey(string value) => _value = value;

    /// <summary>
    /// Whether this value names a tenant. <see langword="false"/> only for the struct default.
    /// </summary>
    public bool IsSpecified => _value is not null;

    /// <summary>The key's text, exactly as it appears in a host name or a URL.</summary>
    /// <exception cref="InvalidOperationException">The key is unspecified.</exception>
    public string Value => _value ?? throw Unspecified();

    /// <summary>Reads a key from its text form.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> is not a well-formed key.</exception>
    public static TenantKey Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);

        return TryParse(s, provider, out TenantKey key)
            ? key
            : throw new FormatException(
                $"'{s}' is not a tenant key. A tenant key is {RegistrySlug.Describe(MinLength, MaxLength)}.");
    }

    /// <summary>Reads a key from its text form, reporting failure rather than throwing.</summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out TenantKey result)
    {
        if (RegistrySlug.IsWellFormed(s, MinLength, MaxLength))
        {
            result = new TenantKey(s);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// The key's text, or a placeholder when unspecified. Never throws, because a diagnostic
    /// rendering that throws turns a logged failure into a second, unrelated one.
    /// </summary>
    public override string ToString() => _value ?? "<unspecified tenant key>";

    private static InvalidOperationException Unspecified() =>
        new("The tenant key is unspecified. A TenantKey obtained from `default` names no tenant; " +
            "read one with TenantKey.Parse(text).");
}
