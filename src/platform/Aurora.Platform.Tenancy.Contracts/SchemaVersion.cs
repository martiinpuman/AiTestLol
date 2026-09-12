using System;
using System.Globalization;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The core schema version one tenant database has been migrated to (ADR-0007 §7.1): an ordinal,
/// comparable so that the §7.5 skew gate can ask "behind, level, or ahead of the code".
/// </summary>
/// <remarks>
/// <para>
/// <b>An ordinal, not a version string.</b> The core schema advances one migration set at a time
/// across every tenant (§7.3), so its position is a count, and the four comparisons §7.5 makes
/// (<c>== Current</c>, between <c>MinimumSupported</c> and <c>Current</c>, below the minimum, above
/// current) are integer comparisons. Package schema versions are a separate axis (ADR-0008 §4) and
/// are not this type.
/// </para>
/// <para>
/// <b>The struct default is unspecified, not version zero.</b> Zero is a real version - a database
/// nothing has migrated yet - and a field nobody set must not read as it: whether the §7.5 gate
/// would let such a value through depends on what <see cref="CoreSchemaVersion.Current"/> happens
/// to be that release, which is exactly the kind of silent pass this project refuses. The default
/// is therefore detectable through <see cref="IsSpecified"/>, loud through <see cref="Ordinal"/>
/// and every comparison, and refused by <see cref="TenantScope"/>'s constructor. The same rule the
/// registry's <see cref="TenantKey"/> and <see cref="Region"/> follow, for the same reason.
/// </para>
/// </remarks>
public readonly record struct SchemaVersion : IComparable<SchemaVersion>
{
    /// <summary>
    /// The largest ordinal representable. One below <see cref="int.MaxValue"/>, because the
    /// ordinal is stored shifted by one so that the struct default can mean "unspecified".
    /// </summary>
    public const int MaxOrdinal = int.MaxValue - 1;

    /// <summary>The ordinal plus one; zero only for the struct default.</summary>
    private readonly int _successor;

    private SchemaVersion(int successor) => _successor = successor;

    /// <summary>
    /// Whether this value names a version. <see langword="false"/> only for the struct default.
    /// </summary>
    public bool IsSpecified => _successor != 0;

    /// <summary>The version's ordinal, from zero.</summary>
    /// <exception cref="InvalidOperationException">The version is unspecified.</exception>
    public int Ordinal => IsSpecified ? _successor - 1 : throw Unspecified();

    /// <summary>The version with the given ordinal.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ordinal"/> is negative or above <see cref="MaxOrdinal"/>.
    /// </exception>
    public static SchemaVersion Of(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ordinal, MaxOrdinal);

        return new SchemaVersion(ordinal + 1);
    }

    /// <summary>Orders by ordinal.</summary>
    /// <exception cref="InvalidOperationException">Either version is unspecified.</exception>
    public int CompareTo(SchemaVersion other) => Ordinal.CompareTo(other.Ordinal);

    /// <summary>Whether <paramref name="left"/> is an earlier version than <paramref name="right"/>.</summary>
    /// <exception cref="InvalidOperationException">Either version is unspecified.</exception>
    public static bool operator <(SchemaVersion left, SchemaVersion right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is a later version than <paramref name="right"/>.</summary>
    /// <exception cref="InvalidOperationException">Either version is unspecified.</exception>
    public static bool operator >(SchemaVersion left, SchemaVersion right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is no later than <paramref name="right"/>.</summary>
    /// <exception cref="InvalidOperationException">Either version is unspecified.</exception>
    public static bool operator <=(SchemaVersion left, SchemaVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is no earlier than <paramref name="right"/>.</summary>
    /// <exception cref="InvalidOperationException">Either version is unspecified.</exception>
    public static bool operator >=(SchemaVersion left, SchemaVersion right) => left.CompareTo(right) >= 0;

    /// <summary>The ordinal as invariant text, or a placeholder when unspecified. Never throws.</summary>
    public override string ToString() =>
        IsSpecified ? Ordinal.ToString(CultureInfo.InvariantCulture) : "<unspecified schema version>";

    private static InvalidOperationException Unspecified() =>
        new("The schema version is unspecified. A SchemaVersion obtained from `default` names no " +
            "version; build one with SchemaVersion.Of(ordinal).");
}
