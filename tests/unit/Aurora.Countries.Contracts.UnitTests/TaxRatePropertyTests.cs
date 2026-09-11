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
/// Arbitrary: the <i>amount</i> — a random mantissa of up to eighteen digits at a random scale from
/// zero to twenty, positive, negative and zero, so amounts run from sub-cent fractions to hundreds
/// of trillions and carry far more decimal places than any currency; and the <i>rate</i> — every
/// value <c>numeric(9,6)</c> can hold, drawn as a random whole number of millionths of a percent
/// from zero to 999.999999, not a list of the rates that happen to exist today.
/// </para>
/// <para>
/// A fixed list: the three currencies (zero, two and three minor units, so nothing can pass by
/// assuming cents) and the five midpoint rules, which are an enumeration and not a range.
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
/// falsifies both. But writing <see cref="TaxRate.ApplyTo"/> the obvious way — multiplying two
/// decimals — passes every property here, because randomly drawn amounts almost never land where
/// <c>decimal</c>'s twenty-eight digits run out <i>and</i> the shortfall reaches the cent. That case
/// is pinned as an example instead, in
/// <c>TaxRateTests.A_product_too_precise_for_decimal_does_not_round_a_cent_into_existence</c>, and
/// it is the only test in this suite that separates the two implementations. Saying so here rather
/// than claiming these properties cover it is the point: a property test that cannot distinguish the
/// right implementation from the wrong one is not covering that difference, however universal its
/// name sounds.
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
                    Money tax = testCase.Rate.ApplyTo(testCase.Amount, testCase.Rounding);

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
                    Money tax = testCase.Rate.ApplyTo(testCase.Amount, testCase.Rounding);

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
                    RoundingPolicy awayFromZero = RoundingPolicy.Of(
                        testCase.Amount.Currency.MinorUnits,
                        MidpointRounding.AwayFromZero);

                    Money charge = testCase.Rate.ApplyTo(testCase.Amount, awayFromZero);
                    Money credit = testCase.Rate.ApplyTo(-testCase.Amount, awayFromZero);

                    credit.ShouldBe(-charge);
                })
            .QuickCheckThrowOnFailure();
    }

    private sealed record TaxCase(Money Amount, TaxRate Rate, RoundingPolicy Rounding);

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
                    midpoints.Select(midpoint => new TaxCase(
                        amount,
                        rate,
                        RoundingPolicy.Of(currency.MinorUnits, midpoint))))));

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
