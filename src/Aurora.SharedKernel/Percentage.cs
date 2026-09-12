using System;
using System.Globalization;

namespace Aurora.SharedKernel;

/// <summary>
/// A rate expressed as a proportion of a hundred: a tax rate, a discount, a markup, a settlement
/// adjustment (ADR-0021 §1).
/// </summary>
/// <remarks>
/// <para>
/// A percentage is not a number and not an amount. Its own type exists to stop the single most
/// common arithmetic mistake in a financial system — applying 15 where 0.15 was meant, or the
/// reverse — by refusing to be built without saying which spelling is being handed over
/// (<see cref="FromPercent"/> or <see cref="FromFraction"/>) and by reading back in both
/// (<see cref="AsPercent"/>, <see cref="AsFraction"/>). It also keeps a rate from being multiplied
/// by another rate or added to an amount by accident.
/// </para>
/// <para>
/// The rate is held as written, in percent, so that a Country Package's "15" survives as 15 and a
/// log says <c>15%</c> rather than <c>0.1500</c>.
/// </para>
/// <para>
/// <b>No bounds.</b> A rate may be negative (a settlement adjustment) and may exceed a hundred
/// (a markup). What counts as a legal range is a property of the rule using the rate — a tax rate
/// cannot be negative, a discount cannot exceed a hundred percent — and belongs to that rule,
/// where the error message can say which rule was broken. A value object that guessed a range
/// would force every legitimate caller to work around it.
/// </para>
/// <para>
/// Applying a rate to a <see cref="Money"/> never rounds. Rounding happens at the four points
/// ADR-0021 §4 names and nowhere else, so line tax comes out of this operator at full precision
/// and is rounded afterwards under a named <see cref="RoundingPolicy"/>.
/// </para>
/// </remarks>
public readonly record struct Percentage : IComparable<Percentage>
{
    private const decimal PercentsInWhole = 100m;

    private readonly decimal _percent;

    private Percentage(decimal percent) => _percent = percent;

    /// <summary>No rate at all. Also what a <see langword="default"/> <see cref="Percentage"/> is.</summary>
    /// <remarks>
    /// Zero is a safe struct default here, unlike <see cref="Currency"/>, because "none of it" is
    /// a real and harmless answer: nothing times an amount is nothing.
    /// </remarks>
    public static Percentage Zero => default;

    /// <summary>The whole of something — 100%.</summary>
    public static Percentage OneHundred { get; } = new(PercentsInWhole);

    /// <summary>Creates a rate from the number in front of the percent sign: 15 means 15%.</summary>
    public static Percentage FromPercent(decimal percent) => new(percent);

    /// <summary>Creates a rate from its proportion of one: 0.15 means 15%.</summary>
    public static Percentage FromFraction(decimal fraction) => new(fraction * PercentsInWhole);

    /// <summary>The rate as the number in front of the percent sign: 15% reads back as 15.</summary>
    public decimal AsPercent => _percent;

    /// <summary>The rate as a proportion of one: 15% reads back as 0.15.</summary>
    public decimal AsFraction => _percent / PercentsInWhole;

    /// <summary>Whether the rate is none of it.</summary>
    public bool IsZero => _percent == 0m;

    /// <summary>Whether the rate takes away rather than adds, as a settlement adjustment does.</summary>
    public bool IsNegative => _percent < 0m;

    /// <summary>Adds two rates — a rate plus a surcharge rate.</summary>
    public static Percentage operator +(Percentage left, Percentage right) =>
        new(left._percent + right._percent);

    /// <summary>Subtracts one rate from another.</summary>
    public static Percentage operator -(Percentage left, Percentage right) =>
        new(left._percent - right._percent);

    /// <summary>Reverses the direction of a rate.</summary>
    public static Percentage operator -(Percentage rate) => new(-rate._percent);

    /// <summary>
    /// Takes this much of an amount, at full precision and in the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="amount"/> names no currency.</exception>
    public static Money operator *(Money amount, Percentage rate) => amount * rate.AsFraction;

    /// <summary>Takes this much of an amount. Mirror of <c>Money * Percentage</c>.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="amount"/> names no currency.</exception>
    public static Money operator *(Percentage rate, Money amount) => amount * rate.AsFraction;

    /// <summary>Whether the left rate is smaller than the right.</summary>
    public static bool operator <(Percentage left, Percentage right) => left.CompareTo(right) < 0;

    /// <summary>Whether the left rate is larger than the right.</summary>
    public static bool operator >(Percentage left, Percentage right) => left.CompareTo(right) > 0;

    /// <summary>Whether the left rate is at most the right.</summary>
    public static bool operator <=(Percentage left, Percentage right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the left rate is at least the right.</summary>
    public static bool operator >=(Percentage left, Percentage right) => left.CompareTo(right) >= 0;

    /// <summary>Orders this rate against another.</summary>
    public int CompareTo(Percentage other) => _percent.CompareTo(other._percent);

    /// <summary>
    /// A culture-invariant rendering for logs and test failures, such as <c>12.5%</c>.
    /// </summary>
    /// <remarks>
    /// Never what a user sees: presenting a rate in a user's locale is a localization concern
    /// (ADR-0022), and doing it here would make a log line change meaning with the ambient culture.
    /// </remarks>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{_percent}%");
}
