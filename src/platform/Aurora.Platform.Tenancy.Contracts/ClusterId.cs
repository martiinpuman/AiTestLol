using System;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Names one PostgreSQL cluster in the fleet — <c>nz-1</c>, <c>eu-west-2b</c> — the unit a tenant
/// is routed to (ADR-0007 §3.5, §6) and the unit an operator adds, fills and retires.
/// </summary>
/// <remarks>
/// <para>
/// Text rather than a UUID, deliberately: a cluster id is chosen by an operator, appears in
/// runbooks, alerts and <c>pg_stat_activity</c>, and has to be readable at 03:00. There are tens of
/// clusters, not millions, so the index-friendliness that makes <see cref="Aurora.SharedKernel.TenantId"/>
/// a UUIDv7 buys nothing here.
/// </para>
/// <para>
/// It follows the same spelling as a <see cref="TenantKey"/> so that it can be embedded in the
/// same kinds of names (an <c>Application Name</c>, a metric label) without escaping.
/// </para>
/// </remarks>
public readonly record struct ClusterId : IParsable<ClusterId>
{
    /// <summary>The shortest id accepted.</summary>
    public const int MinLength = 2;

    /// <summary>The longest id accepted.</summary>
    public const int MaxLength = 40;

    private readonly string? _value;

    private ClusterId(string value) => _value = value;

    /// <summary>
    /// Whether this value names a cluster. <see langword="false"/> only for the struct default.
    /// </summary>
    public bool IsSpecified => _value is not null;

    /// <summary>The id's text.</summary>
    /// <exception cref="InvalidOperationException">The id is unspecified.</exception>
    public string Value => _value ?? throw Unspecified();

    /// <summary>Reads an id from its text form.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> is not a well-formed id.</exception>
    public static ClusterId Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);

        return TryParse(s, provider, out ClusterId id)
            ? id
            : throw new FormatException(
                $"'{s}' is not a cluster id. A cluster id is {RegistrySlug.Describe(MinLength, MaxLength)}.");
    }

    /// <summary>Reads an id from its text form, reporting failure rather than throwing.</summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out ClusterId result)
    {
        if (RegistrySlug.IsWellFormed(s, MinLength, MaxLength))
        {
            result = new ClusterId(s);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>The id's text, or a placeholder when unspecified. Never throws.</summary>
    public override string ToString() => _value ?? "<unspecified cluster id>";

    private static InvalidOperationException Unspecified() =>
        new("The cluster id is unspecified. A ClusterId obtained from `default` names no cluster; " +
            "read one with ClusterId.Parse(text).");
}
