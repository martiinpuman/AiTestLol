using System;
using System.Numerics;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;
using FsCheck;
using FsCheck.Fluent;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// The law <see cref="TaxRate.ApplyTo"/> may never break: tax on any amount, at any rate a package
/// can publish, is a whole number of minor units, and is the nearest such number to the exact
/// product.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which dimensions are genuinely arbitrary, and which are a fixed list.</b>
/// </para>
/// <para>
/// Arbitrary, in the three properties over random amounts: the <i>amount</i> — a random mantissa
/// of up to eighteen digits at a random scale from zero to twenty, positive, negative and zero, so
/// amounts run from sub-cent fractions to hundreds of trillions and carry far more decimal places
/// than any currency; and the <i>rate</i> — every value <c>numeric(9,6)</c> can hold, drawn as a
/// random whole number of millionths of a percent from zero to 999.999999, not a list of the rates
/// that happen to exist today. In the boundary property the amount is not drawn at all but
/// <i>derived</i> (see <see cref="BoundaryCases"/>); its arbitrary dimensions are the rate and the
/// target whole number of minor units.
/// </para>
/// <para>
/// A fixed list: the three currencies (zero, two and three minor units, so nothing can pass by
/// assuming cents), the five midpoint rules, which are an enumeration and not a range — the two
/// nearest rules only, where a property is about nearness — and, in the boundary property, the two
/// signs. The rounding <i>scale</i> is not a dimension at all: tax is always rounded at the
/// currency's minor unit, because tax that is not a whole number of minor units cannot be paid.
/// </para>
/// <para>
/// <b>Why the second property is not the implementation restated.</b> <see cref="TaxRate.ApplyTo"/>
/// divides — it scales a product down to the currency's minor unit and rounds the remainder. The
/// check below multiplies the answer back up and bounds the difference from the exact product by
/// half a minor unit. A division checked by a multiplication is an independent oracle; re-deriving
/// the quotient here would only prove the test and the code make the same mistake.
/// </para>
/// <para>
/// <b>What these were watched to fail on, and what they were not.</b> Truncating instead of rounding
/// falsifies the nearest-multiple property on the first generated case. Rounding at the wrong scale
/// falsifies the first two. But writing <see cref="TaxRate.ApplyTo"/> the obvious way — multiplying
/// two decimals — passes all three properties over random amounts, and not by bad luck: the random
/// dimension cannot reach the region where <c>decimal</c> rounds. <see cref="Amounts"/> draws at
/// most eighteen digits at a scale of at most twenty and <see cref="Rates"/> at most nine digits at
/// scale six, so the naive product needs at most twenty-seven digits at a scale of at most
/// twenty-eight and is always exactly representable. Widening those ranges would make the product
/// round, but a random draw lands where that rounding reaches the minor unit with a probability too
/// small to count on. Only constructing the amount gets there, which is what
/// <see cref="Tax_one_ulp_short_of_a_half_minor_unit_is_not_rounded_up_to_it"/> does.
/// </para>
/// <para>
/// <b>Watched to fail.</b> With <see cref="TaxRate.ApplyTo"/> replaced by
/// <c>decimal.Round(amount * (percent / 100m), minorUnits, midpoint)</c>, the boundary property was
/// falsified on its first generated case — 75357.989136884078610908818808 BHD at 965.230057% came
/// back as 727377.962, where 727377.961 is the answer — and over 500 constructed draws the naive
/// implementation returned a minor unit that does not exist in 197 of them (39%), against 0 for the
/// shipped one, in 11 ms. The pinned example in
/// <c>TaxRateTests.A_product_too_precise_for_decimal_does_not_round_a_cent_into_existence</c> is
/// kept beside it: it shows the mechanism in one readable line, and pins the premise that the
/// obvious product really does land on the half.
/// </para>
/// </remarks>
public sealed class TaxRatePropertyTests
{
    [Fact]
    public void Tax_on_any_amount_at_any_publishable_rate_is_payable()
    {
        Prop.ForAll(
                Cases().ToArbitrary(),
                testCase =>
                {
                    Money tax = testCase.Rate.ApplyTo(testCase.Amount, testCase.Midpoint);

                    tax.Currency.ShouldBe(testCase.Amount.Currency);
                    tax.IsInWholeMinorUnits.ShouldBeTrue();
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void Tax_is_the_nearest_whole_minor_unit_to_the_exact_product()
    {
        Prop.ForAll(
                NearestCases().ToArbitrary(),
                testCase =>
                {
                    Money tax = testCase.Rate.ApplyTo(testCase.Amount, testCase.Midpoint);

                    // exact product = amountUnits * rateUnits / 10^(amountScale + rateScale + 2)
                    // answer        = taxUnits           / 10^minorUnits
                    // Bring both over the same denominator and compare as whole numbers.
                    (BigInteger amountUnits, int amountScale) = Parts(testCase.Amount.Amount);
                    (BigInteger rateUnits, int rateScale) = Parts(testCase.Rate.AsPercentage.AsPercent);
                    (BigInteger taxUnits, int taxScale) = Parts(tax.Amount);

                    int productScale = amountScale + rateScale + 2;
                    int commonScale = Math.Max(
                        Math.Max(productScale, taxScale),
                        testCase.Amount.Currency.MinorUnits);

                    BigInteger exact = amountUnits * rateUnits * Pow10(commonScale - productScale);
                    BigInteger answer = taxUnits * Pow10(commonScale - taxScale);
                    BigInteger oneMinorUnit =
                        Pow10(commonScale - testCase.Amount.Currency.MinorUnits);

                    (BigInteger.Abs(answer - exact) * 2).ShouldBeLessThanOrEqualTo(
                        oneMinorUnit,
                        $"{testCase.Rate} of {testCase.Amount} came back as {tax}, which is more than " +
                        $"half a minor unit away from the exact product.");
                })
            .QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// The region the random dimension cannot reach: an amount whose exact tax falls one ulp short of
    /// a half minor unit. The nearest whole minor unit is then the lower one under either nearest
    /// rule, and that answer is fixed by the construction — not by <see cref="TaxRate.ApplyTo"/>.
    /// An implementation that multiplies two decimals rounds the product onto the half and then
    /// over it: a minor unit that does not exist, on an amount nobody would call unusual.
    /// </summary>
    [Fact]
    public void Tax_one_ulp_short_of_a_half_minor_unit_is_not_rounded_up_to_it()
    {
        Prop.ForAll(
                BoundaryCases().ToArbitrary(),
                boundary =>
                {
                    Money tax = boundary.Rate.ApplyTo(boundary.Amount, boundary.Midpoint);

                    tax.ShouldBe(
                        boundary.NearestTax,
                        $"{boundary.Rate} of {boundary.Amount} is one ulp short of the half minor " +
                        $"unit above {boundary.NearestTax}, and came back as {tax}.");
                })
            .QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Rounding away from zero never rounds a charge down or a credit up, so an invoice and the
    /// credit note that reverses it carry the same tax.
    /// </summary>
    [Fact]
    public void A_credit_note_is_taxed_as_the_invoice_it_reverses()
    {
        Prop.ForAll(
                Cases().ToArbitrary(),
                testCase =>
                {
                    Money charge = testCase.Rate.ApplyTo(testCase.Amount, MidpointRounding.AwayFromZero);
                    Money credit = testCase.Rate.ApplyTo(-testCase.Amount, MidpointRounding.AwayFromZero);

                    credit.ShouldBe(-charge);
                })
            .QuickCheckThrowOnFailure();
    }

    private sealed record TaxCase(Money Amount, TaxRate Rate, MidpointRounding Midpoint);

    private static Gen<TaxCase> Cases() => Cases(AllMidpoints());

    /// <summary>
    /// The two midpoint rules that round to the <i>nearest</i> minor unit.
    /// </summary>
    /// <remarks>
    /// The three directed rules - toward zero, toward negative infinity, toward positive infinity -
    /// deliberately do not round to the nearest, and can land a whole minor unit away from the exact
    /// product. Asserting a half-unit bound over them would be asserting something untrue about what
    /// they are for, so the nearest-multiple law is stated over the rules it belongs to. The first
    /// property covers all five.
    /// </remarks>
    private static Gen<TaxCase> NearestCases() =>
        Cases(Gen.Elements(MidpointRounding.AwayFromZero, MidpointRounding.ToEven));

    private static Gen<TaxCase> Cases(Gen<MidpointRounding> midpoints) =>
        Currencies().SelectMany(currency =>
            Amounts(currency).SelectMany(amount =>
                Rates().SelectMany(rate =>
                    midpoints.Select(midpoint => new TaxCase(amount, rate, midpoint)))));

    private static Gen<MidpointRounding> AllMidpoints() =>
        Gen.Elements(
            MidpointRounding.AwayFromZero,
            MidpointRounding.ToEven,
            MidpointRounding.ToZero,
            MidpointRounding.ToNegativeInfinity,
            MidpointRounding.ToPositiveInfinity);

    /// <summary>Zero, two and three minor units. A fixed list, because currencies are.</summary>
    private static Gen<Currency> Currencies() =>
        Gen.Elements(ContractTestValues.Jpy, ContractTestValues.Nzd, ContractTestValues.Bhd);

    /// <summary>
    /// Amounts across eighteen digits of magnitude and twenty of scale, signed.
    /// </summary>
    /// <remarks>
    /// Deliberately including amounts far more precise than their own currency: a line net before
    /// rounding, a unit price in tenths of a cent, an allocation intermediate. Those are the amounts
    /// a rate actually meets, and an implementation that assumed whole minor units on the way in
    /// would pass a generator that only produced them.
    /// </remarks>
    private static Gen<Money> Amounts(Currency currency) =>
        Gen.Choose(0, 20).SelectMany(scale =>
            Gen.Choose(-999_999_999, 999_999_999).SelectMany(high =>
                Gen.Choose(-999_999_999, 999_999_999).Select(low =>
                    new Money(AtScale(((long)high * 1_000_000_000) + low, scale), currency))));

    /// <summary>
    /// Every rate <c>numeric(9,6)</c> can hold: a whole number of millionths of a percent, from zero
    /// to just under a thousand percent.
    /// </summary>
    private static Gen<TaxRate> Rates() =>
        Gen.Choose(0, 999).SelectMany(percent =>
            Gen.Choose(0, 999_999).Select(millionths =>
                ContractTestValues.Rate(percent + AtScale(millionths, 6))));

    /// <summary>
    /// A case built to sit one ulp below the half-minor-unit boundary, with the answer the
    /// construction guarantees: the exact tax is strictly between <see cref="NearestTax"/> and the
    /// half above it, so the nearest whole minor unit is <see cref="NearestTax"/> under either
    /// nearest-rounding rule.
    /// </summary>
    private sealed record BoundaryCase(
        Money Amount,
        TaxRate Rate,
        MidpointRounding Midpoint,
        Money NearestTax);

    /// <summary>
    /// Arbitrary rate (zero excluded: no amount taxes to half a minor unit at zero percent) and
    /// target whole number of minor units; a fixed list of two signs, three currencies and the two
    /// nearest rounding rules; and an amount <i>derived</i> from them rather than drawn.
    /// </summary>
    /// <remarks>
    /// The amount is the one whose exact tax would be exactly <c>target + ½</c> minor units, written
    /// with every decimal place a <c>decimal</c> has room for at that magnitude and truncated there
    /// — so it carries decimal's full precision, and its last place is the smallest step a decimal
    /// can take. When the truncation happens to be exact the amount is stepped down one such ulp
    /// instead. Either way the exact tax lands strictly between <c>target</c> and the half above it,
    /// and the construction checks that before handing the case over: a generator that silently
    /// produced cases outside its own region would make the property vacuous, not wrong.
    /// </remarks>
    private static Gen<BoundaryCase> BoundaryCases() =>
        Currencies().SelectMany(currency =>
            Rates().Where(rate => !rate.IsZero).SelectMany(rate =>
                Gen.Choose(0, 999_999_999).SelectMany(targetMinorUnits =>
                    Gen.Elements(1, -1).SelectMany(sign =>
                        Gen.Elements(MidpointRounding.AwayFromZero, MidpointRounding.ToEven).Select(midpoint =>
                            OneUlpShortOfTheHalf(currency, rate, targetMinorUnits, sign, midpoint))))));

    private static BoundaryCase OneUlpShortOfTheHalf(
        Currency currency,
        TaxRate rate,
        int targetMinorUnits,
        int sign,
        MidpointRounding midpoint)
    {
        (BigInteger rateUnits, int rateScale) = Parts(rate.AsPercentage.AsPercent);

        // boundary = (2·target + 1) / (2·10^minorUnits)  ÷  rateUnits / (100·10^rateScale),
        // first as a whole number of 10^-28, then with the scale reduced until it fits a decimal.
        BigInteger numerator =
            ((2 * (BigInteger)targetMinorUnits) + 1) * Pow10(rateScale + 2 + DecimalMaxScale);
        BigInteger denominator = 2 * Pow10(currency.MinorUnits) * rateUnits;
        BigInteger mantissa = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        bool exact = remainder.IsZero;

        int scale = DecimalMaxScale;
        while (mantissa >= DecimalMantissaLimit)
        {
            mantissa = BigInteger.DivRem(mantissa, 10, out BigInteger dropped);
            exact &= dropped.IsZero;
            scale--;
        }

        if (exact)
        {
            mantissa -= 1;
        }

        // tax·10^minorUnits = mantissa·rateUnits / 10^(scale + rateScale + 2 - minorUnits), and it
        // must sit strictly above target: doubled, on one denominator.
        BigInteger twiceTax = 2 * mantissa * rateUnits * Pow10(currency.MinorUnits);
        BigInteger twiceTarget = 2 * (BigInteger)targetMinorUnits * Pow10(scale + rateScale + 2);
        if (twiceTax <= twiceTarget)
        {
            throw new InvalidOperationException(
                $"The boundary construction is wrong: {mantissa}E-{scale} {currency} at {rate} taxes " +
                $"to at most {targetMinorUnits} minor units, not just under {targetMinorUnits} + ½.");
        }

        return new BoundaryCase(
            new Money(FromParts(sign * mantissa, scale), currency),
            rate,
            midpoint,
            new Money(FromParts(sign * targetMinorUnits, currency.MinorUnits), currency));
    }

    private const int DecimalMaxScale = 28;

    /// <summary>One past the largest mantissa a decimal can carry: 2^96.</summary>
    private static readonly BigInteger DecimalMantissaLimit = BigInteger.One << 96;

    private static decimal FromParts(BigInteger units, int scale)
    {
        BigInteger magnitude = BigInteger.Abs(units);
        int lo = (int)(uint)(magnitude & uint.MaxValue);
        int mid = (int)(uint)((magnitude >> 32) & uint.MaxValue);
        int hi = (int)(uint)((magnitude >> 64) & uint.MaxValue);
        return new decimal(lo, mid, hi, units.Sign < 0, (byte)scale);
    }

    private static decimal AtScale(long mantissa, int scale)
    {
        decimal value = mantissa;
        for (int step = 0; step < scale; step++)
        {
            value /= 10m;
        }

        return value;
    }

    private static BigInteger Pow10(int exponent) => BigInteger.Pow(10, exponent);

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
}
