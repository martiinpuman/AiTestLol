using System;
using System.Globalization;
using System.Numerics;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Taxation;

/// <summary>
/// A tax rate a Country Package supplies, bounded so that core can apply it to an amount and get
/// back something payable.
/// </summary>
/// <remarks>
/// <para>
/// A rate is the one number in this contract that comes from outside core and is then <b>multiplied
/// by money</b>. That combination is where <c>decimal</c> stops being exact without saying so:
/// multiplication silently rounds past 28-29 significant digits, so a rate carrying 28 decimal
/// places — which is exactly what <c>somePercentage / 100m</c> produces — can turn an amount into
/// one that is not a whole number of minor units, and therefore cannot be paid, invoiced or posted.
/// The same class of bug cost this codebase a cent out of one allocation in five before
/// <c>Money.Allocate</c> was moved into integer arithmetic (ADR-0021 §6).
/// </para>
/// <para>
/// Two mechanisms close it here, and both are checks rather than intentions:
/// </para>
/// <list type="number">
/// <item>
/// <b>A rate cannot be arbitrarily precise.</b> <see cref="Create"/> refuses anything that does not
/// fit <c>numeric(9,6)</c> — at most <see cref="MaxDecimalPlaces"/> decimal places and below
/// <see cref="ExclusiveUpperBound"/> percent — which is the column the rate is stored in anyway
/// (ADR-0008 §6.2). A rate refused here is refused at install, with the package named.
/// </item>
/// <item>
/// <b>Applying a rate never multiplies two decimals.</b> <see cref="ApplyTo"/> takes both operands
/// apart into whole numbers, does the arithmetic in <see cref="BigInteger"/> where nothing can
/// round, rounds once at the end under an explicit policy, and then checks the result it is about
/// to return is a whole number of minor units before returning it.
/// </item>
/// </list>
/// </remarks>
public readonly record struct TaxRate : IComparable<TaxRate>
{
    /// <summary>
    /// The most decimal places a rate may carry — six, matching the <c>numeric(9,6)</c> column
    /// package rates live in.
    /// </summary>
    public const int MaxDecimalPlaces = 6;

    /// <summary>
    /// One more than the largest rate expressible: <c>numeric(9,6)</c> holds nine digits, six of
    /// them after the point, so the largest rate is 999.999999 percent.
    /// </summary>
    public const decimal ExclusiveUpperBound = 1000m;

    private readonly decimal _percent;
    private readonly bool _isSpecified;

    private TaxRate(decimal percent)
    {
        _percent = percent;
        _isSpecified = true;
    }

    /// <summary>
    /// A rate of zero — a zero-rated supply, which is not the same thing as an exempt one.
    /// </summary>
    public static TaxRate Zero { get; } = new(0m);

    /// <summary>
    /// Whether this value names a rate at all, rather than being <c>default</c>.
    /// </summary>
    /// <remarks>
    /// The default value is deliberately <i>not</i> zero percent. A rate nobody set and a rate
    /// somebody set to zero are different facts, and the difference is a tax return: reading the
    /// first as the second would post no tax and report no tax, with nothing anywhere saying a rate
    /// was missing.
    /// </remarks>
    public bool IsSpecified => _isSpecified;

    /// <summary>The rate as a percentage.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public Percentage AsPercentage => _isSpecified ? Percentage.FromPercent(_percent) : throw Unspecified();

    /// <summary>Whether the rate is zero.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public bool IsZero => _isSpecified ? _percent == 0m : throw Unspecified();

    /// <summary>
    /// Reads a rate, refusing one core could not apply exactly.
    /// </summary>
    /// <remarks>
    /// Negative rates are refused too. A refund, a credit note and a reverse charge are all
    /// expressed by the sign of the <i>amount</i> or by a
    /// <see cref="TaxCategory"/>; a negative rate would be a second way to say the same thing, and
    /// two ways to say it is how a credit note ends up with tax that adds the wrong way round.
    /// </remarks>
    public static Result<TaxRate> Create(Percentage percentage)
    {
        decimal percent = percentage.AsPercent;

        if (percent < 0m)
        {
            return TaxErrors.Invalid(
                "taxRate",
                $"{percentage} is negative. Sign belongs to the amount or to the tax treatment, " +
                $"never to the rate.");
        }

        if (percent >= ExclusiveUpperBound)
        {
            return TaxErrors.Invalid(
                "taxRate",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{percentage} is at or above {ExclusiveUpperBound}%, which does not fit the " +
                    $"numeric(9,6) column a package rate is stored in."));
        }

        if (percent.Scale > MaxDecimalPlaces && percent != decimal.Round(percent, MaxDecimalPlaces))
        {
            return TaxErrors.Invalid(
                "taxRate",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{percentage} carries more than {MaxDecimalPlaces} decimal places. A rate that " +
                    $"precise is almost always a division result rather than a published rate, and " +
                    $"multiplying money by it is where decimal starts rounding without saying so."));
        }

        // Trailing zeros beyond the limit are information-free: 15.0000000% is 15%.
        return Result.Success(new TaxRate(decimal.Round(percent, MaxDecimalPlaces)));
    }

    /// <summary>
    /// The tax on <paramref name="taxableAmount"/> at this rate, rounded once under
    /// <paramref name="rounding"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rounding policy is a parameter and has no default, because how a jurisdiction rounds tax
    /// is a jurisdiction's decision: choosing one here would put that decision inside core code,
    /// which is the thing this whole contract exists to prevent.
    /// </para>
    /// <para>
    /// The computation runs in whole numbers end to end and rounds exactly once, at the currency's
    /// minor unit. It then asserts its own postcondition — that the amount it is about to return can
    /// actually be paid — and throws rather than return an amount that cannot.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="taxableAmount"/> names no currency, <paramref name="rounding"/> is
    /// unspecified, the rounded tax is too large for <see cref="decimal"/>, or — the case that
    /// should be unreachable — the result is not a whole number of minor units.
    /// </exception>
    public Money ApplyTo(Money taxableAmount, RoundingPolicy rounding)
    {
        if (!_isSpecified)
        {
            throw Unspecified();
        }

        if (!rounding.IsSpecified)
        {
            throw new InvalidOperationException(
                "Applying a tax rate needs an explicit rounding policy. How tax is rounded is a " +
                "jurisdiction's rule, so there is no default to fall back on.");
        }

        Currency currency = taxableAmount.Currency;

        (BigInteger amountUnits, int amountScale) = Parts(taxableAmount.Amount);
        (BigInteger rateUnits, int rateScale) = Parts(_percent);

        // tax = amount x percent / 100. Everything below is an integer: the product of two exact
        // mantissas, and a scale that is the sum of the two scales plus the two digits of the
        // division by a hundred. No decimal multiplication happens anywhere on this path, so there
        // is no significant-digit limit to fall off.
        BigInteger productUnits = amountUnits * rateUnits;
        int productScale = amountScale + rateScale + 2;

        BigInteger minorUnits = RescaleWithRounding(
            productUnits,
            productScale,
            currency.MinorUnits,
            rounding.Midpoint);

        decimal tax;
        try
        {
            tax = (decimal)minorUnits / MinorUnitScale(currency.MinorUnits);
        }
        catch (OverflowException overflow)
        {
            throw new InvalidOperationException(
                $"Tax of {minorUnits} minor units on {taxableAmount} at {AsPercentage} is larger than " +
                $"a decimal can hold.",
                overflow);
        }

        Money result = new(tax, currency);

        if (!result.IsInWholeMinorUnits)
        {
            throw new InvalidOperationException(
                $"Tax on {taxableAmount} at {AsPercentage} came out as {result}, which is not a whole " +
                $"number of minor units and therefore cannot be paid. This is a defect in " +
                $"{nameof(TaxRate)}.{nameof(ApplyTo)}, not in the package's rate.");
        }

        return result;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Either value is the default, unassigned one.</exception>
    public int CompareTo(TaxRate other) =>
        _isSpecified && other._isSpecified ? _percent.CompareTo(other._percent) : throw Unspecified();

    /// <summary>Whether the left rate is below the right one.</summary>
    public static bool operator <(TaxRate left, TaxRate right) => left.CompareTo(right) < 0;

    /// <summary>Whether the left rate is above the right one.</summary>
    public static bool operator >(TaxRate left, TaxRate right) => left.CompareTo(right) > 0;

    /// <summary>Whether the left rate is at or below the right one.</summary>
    public static bool operator <=(TaxRate left, TaxRate right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the left rate is at or above the right one.</summary>
    public static bool operator >=(TaxRate left, TaxRate right) => left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public override string ToString() => _isSpecified ? AsPercentage.ToString() : "<unspecified tax rate>";

    private static InvalidOperationException Unspecified() =>
        new("This TaxRate names no rate. It is the default value of the struct, which only exists " +
            "because C# gives every struct one; build rates with TaxRate.Create, and use " +
            "TaxRate.Zero when the rate really is zero.");

    private static decimal MinorUnitScale(int minorUnits)
    {
        decimal scale = 1m;
        for (int digit = 0; digit < minorUnits; digit++)
        {
            scale *= 10m;
        }

        return scale;
    }

    private static (BigInteger Units, int Scale) Parts(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        _ = decimal.GetBits(value, bits);

        int scale = (bits[3] >> 16) & 0xFF;
        bool negative = (bits[3] & unchecked((int)0x80000000)) != 0;

        BigInteger units = (new BigInteger((uint)bits[2]) << 64)
            | (new BigInteger((uint)bits[1]) << 32)
            | new BigInteger((uint)bits[0]);

        return (negative ? -units : units, scale);
    }

    private static BigInteger RescaleWithRounding(
        BigInteger units,
        int fromScale,
        int toScale,
        MidpointRounding midpoint)
    {
        if (fromScale <= toScale)
        {
            // Widening only adds zeros: nothing to round, and nothing that can be lost.
            return units * BigInteger.Pow(10, toScale - fromScale);
        }

        BigInteger divisor = BigInteger.Pow(10, fromScale - toScale);
        BigInteger quotient = BigInteger.DivRem(units, divisor, out BigInteger remainder);

        if (remainder.IsZero)
        {
            return quotient;
        }

        int sign = units.Sign;
        BigInteger twiceRemainder = BigInteger.Abs(remainder) * 2;

        return midpoint switch
        {
            MidpointRounding.AwayFromZero => twiceRemainder >= divisor ? quotient + sign : quotient,
            MidpointRounding.ToEven => twiceRemainder > divisor || (twiceRemainder == divisor && !quotient.IsEven)
                ? quotient + sign
                : quotient,
            MidpointRounding.ToZero => quotient,
            MidpointRounding.ToNegativeInfinity => sign < 0 ? quotient - 1 : quotient,
            MidpointRounding.ToPositiveInfinity => sign > 0 ? quotient + 1 : quotient,
            _ => throw new ArgumentOutOfRangeException(
                nameof(midpoint),
                midpoint,
                "Not a rounding mode this contract knows how to apply in whole numbers."),
        };
    }
}
