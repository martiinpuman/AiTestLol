using System;
using System.Globalization;

namespace Aurora.SharedKernel;

/// <summary>
/// How an amount is rounded: to how many decimal places, and which way a half goes
/// (ADR-0021 §3).
/// </summary>
/// <remarks>
/// <para>
/// ADR-0021 §3 requires the rounding mode to be <b>set explicitly at every rounding call</b>,
/// never left to a framework default — <see cref="Math.Round(decimal)"/> rounds to even while
/// most commercial invoicing rules round half up, and that difference is exactly the kind of
/// implicit behaviour that produces a one-cent discrepancy nobody can locate. Carrying the
/// decision in a value object is what makes "explicit" structural instead of a habit: there is
/// no way to round a <see cref="Money"/> without naming a policy.
/// </para>
/// <para>
/// The core default is <see cref="MidpointRounding.AwayFromZero"/> at the currency's minor
/// units (<see cref="CoreDefaultFor"/>). A Country Package may supply a different policy,
/// effective-dated as package data (ADR-0008 §6.2); core never branches on country, it asks for
/// the applicable policy as of the document date and passes it here.
/// </para>
/// <para>
/// The rounding <em>basis</em> — whether tax is rounded per line or per document — is a separate
/// decision that belongs to the tax engine (ADR-0023 §6), not to this type. A policy says how to
/// round one number, not which numbers to round.
/// </para>
/// </remarks>
public readonly record struct RoundingPolicy
{
    /// <summary>
    /// The midpoint rule the core uses unless a Country Package supplies another (ADR-0021 §3).
    /// </summary>
    public const MidpointRounding CoreDefaultMidpoint = MidpointRounding.AwayFromZero;

    /// <summary>The most decimal places <see cref="decimal.Round(decimal, int, MidpointRounding)"/> accepts.</summary>
    public const int MaxDecimalPlaces = 28;

    private readonly bool _isSpecified;
    private readonly int _decimalPlaces;
    private readonly MidpointRounding _midpoint;

    private RoundingPolicy(int decimalPlaces, MidpointRounding midpoint)
    {
        _isSpecified = true;
        _decimalPlaces = decimalPlaces;
        _midpoint = midpoint;
    }

    /// <summary>Creates a policy.</summary>
    /// <param name="decimalPlaces">Decimal places to round to, from 0 to <see cref="MaxDecimalPlaces"/>.</param>
    /// <param name="midpoint">Which way a value exactly halfway between two results goes.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="decimalPlaces"/> is outside the range a <see cref="decimal"/> can hold, or
    /// <paramref name="midpoint"/> is not a defined <see cref="MidpointRounding"/> value.
    /// </exception>
    public static RoundingPolicy Of(int decimalPlaces, MidpointRounding midpoint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimalPlaces, MaxDecimalPlaces);

        if (!Enum.IsDefined(midpoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(midpoint), midpoint, "Not a defined midpoint rounding rule.");
        }

        return new RoundingPolicy(decimalPlaces, midpoint);
    }

    /// <summary>
    /// The core default for a currency: half away from zero, at that currency's minor units.
    /// </summary>
    /// <remarks>
    /// The places come from the currency rather than an assumed two, because JPY has none and a
    /// few currencies have three. Assuming two is a bug that only shows up in one market.
    /// </remarks>
    public static RoundingPolicy CoreDefaultFor(Currency currency) =>
        Of(currency.MinorUnits, CoreDefaultMidpoint);

    /// <summary>Whether this value names a policy. <see langword="false"/> only for the struct default.</summary>
    public bool IsSpecified => _isSpecified;

    /// <summary>Decimal places to round to.</summary>
    /// <exception cref="InvalidOperationException">The policy is unspecified.</exception>
    public int DecimalPlaces => _isSpecified ? _decimalPlaces : throw Unspecified();

    /// <summary>Which way a value exactly halfway between two results goes.</summary>
    /// <exception cref="InvalidOperationException">The policy is unspecified.</exception>
    public MidpointRounding Midpoint => _isSpecified ? _midpoint : throw Unspecified();

    /// <summary>Rounds a number under this policy.</summary>
    /// <exception cref="InvalidOperationException">The policy is unspecified.</exception>
    public decimal Round(decimal value) => decimal.Round(value, DecimalPlaces, Midpoint);

    /// <summary>A culture-invariant rendering for logs and test failures.</summary>
    public override string ToString() =>
        _isSpecified
            ? string.Create(CultureInfo.InvariantCulture, $"{_decimalPlaces} dp, {_midpoint}")
            : "<unspecified rounding policy>";

    private static InvalidOperationException Unspecified() =>
        new("No rounding policy was supplied. ADR-0021 §3 requires the rounding rule to be named " +
            "at every rounding call rather than inherited from a framework default; build one with " +
            "RoundingPolicy.CoreDefaultFor(currency) or, for a Country Package rule, " +
            "RoundingPolicy.Of(decimalPlaces, midpoint).");
}
