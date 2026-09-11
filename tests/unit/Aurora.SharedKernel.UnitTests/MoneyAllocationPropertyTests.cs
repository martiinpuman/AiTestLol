using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// ADR-0021 §6 asks for these by name: "a property-based test asserts <c>sum(parts) == total</c>
/// for arbitrary inputs and arbitrary part counts, including negative totals and zero-weight
/// parts".
/// </summary>
/// <remarks>
/// <para>
/// The example-based tests next door pin the exact split a known input produces. These pin the law
/// that no input may break. Example-based tests systematically miss the boundaries of money
/// arithmetic (<c>testing-strategy.md</c> §2), which is why the allocation invariant is stated
/// universally here rather than sampled.
/// </para>
/// <para>
/// <b>Which dimensions are arbitrary, and which are a hand-written list.</b> Arbitrary: the total
/// (any whole number of minor units, positive, negative or zero), the part count (one to twelve),
/// and — since review B-03 — the weights, which are drawn from ratios of the
/// <c>lineAmount / documentTotal</c> shape a caller actually writes, from magnitudes spanning
/// 10⁻²⁰ to 10⁸ within one split, and from round numbers, with zero weights throughout.
/// Hand-written: the four currencies, chosen for zero, two and three minor units so that no
/// property can pass by assuming cents. The weights used to be a hand-written list too — ten
/// literals of at most two decimal places — and that is precisely why these properties reported
/// green while <c>Allocate</c> was losing cents: every such weight makes the arithmetic exact, so
/// the generator never visited the region where the law breaks.
/// </para>
/// </remarks>
public sealed class MoneyAllocationPropertyTests
{
    private const int MaxParts = 12;

    [Fact]
    public void An_even_split_of_any_amount_adds_back_up_to_it_exactly()
    {
        Prop.ForAll(
                EvenSplits().ToArbitrary(),
                split =>
                {
                    IReadOnlyList<Money> parts = split.Total.Allocate(split.Parts);

                    parts.Count.ShouldBe(split.Parts);
                    Money.Sum(parts, split.Total.Currency).ShouldBe(split.Total);
                    parts.ShouldAllBe(part => part.IsInWholeMinorUnits);
                    parts.ShouldAllBe(part => !TurnsAgainst(part, split.Total));
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void A_weighted_split_of_any_amount_adds_back_up_to_it_exactly()
    {
        Prop.ForAll(
                WeightedSplits().ToArbitrary(),
                split =>
                {
                    IReadOnlyList<Money> parts = split.Total.Allocate(split.Weights);

                    parts.Count.ShouldBe(split.Weights.Count);
                    Money.Sum(parts, split.Total.Currency).ShouldBe(split.Total);
                    parts.ShouldAllBe(part => part.IsInWholeMinorUnits);
                    parts.ShouldAllBe(part => !TurnsAgainst(part, split.Total));
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void A_part_weighted_zero_never_takes_a_leftover_unit()
    {
        Prop.ForAll(
                WeightedSplits().ToArbitrary(),
                split =>
                {
                    IReadOnlyList<Money> parts = split.Total.Allocate(split.Weights);

                    for (int part = 0; part < parts.Count; part++)
                    {
                        if (split.Weights[part] == 0m)
                        {
                            parts[part].ShouldBe(Money.Zero(split.Total.Currency));
                        }
                    }
                })
            .QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// One part takes the whole amount, whatever its weight says, because one part is the whole of
    /// whatever is being divided.
    /// </summary>
    /// <remarks>
    /// The narrowest property here and the one that would have caught review B-03's blocker on its
    /// own: there is no rounding left for a leftover rule to hide, so any inexactness in how a
    /// weight meets the amount shows up directly in the single part that comes back.
    /// </remarks>
    [Fact]
    public void A_single_part_takes_the_whole_amount_whatever_its_weight_says()
    {
        Prop.ForAll(
                SingleWeightSplits().ToArbitrary(),
                split =>
                {
                    IReadOnlyList<Money> parts = split.Total.Allocate(split.Weights);

                    parts.Count.ShouldBe(1);
                    parts[0].ShouldBe(split.Total);
                    parts[0].IsInWholeMinorUnits.ShouldBeTrue();
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void The_same_split_asked_for_twice_gives_the_same_answer_twice()
    {
        Prop.ForAll(
                WeightedSplits().ToArbitrary(),
                split => split.Total.Allocate(split.Weights)
                    .ShouldBe(split.Total.Allocate(split.Weights)))
            .QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Whether a part points the opposite way to the amount it came from — a positive part of a
    /// credit note, or a negative part of an invoice. Nothing in an allocation may change sign:
    /// that would turn one line of a credit note into a charge.
    /// </summary>
    private static bool TurnsAgainst(Money part, Money total) =>
        (part.IsPositive && total.IsNegative) || (part.IsNegative && total.IsPositive);

    /// <summary>
    /// Currencies with zero, two and three minor units, so that no property can pass by assuming
    /// cents.
    /// </summary>
    private static Gen<Currency> Currencies() =>
        Gen.Elements(TestCurrencies.Nzd, TestCurrencies.Aud, TestCurrencies.Jpy, TestCurrencies.Bhd);

    /// <summary>
    /// Amounts that are already whole minor units — the precondition of a split — positive,
    /// negative and zero.
    /// </summary>
    private static Gen<Money> SplittableAmounts() =>
        Currencies().SelectMany(currency =>
            Gen.Choose(-2_000_000, 2_000_000)
                .Select(minorUnits => FromMinorUnits(minorUnits, currency)));

    /// <summary>
    /// Weights of every shape a caller can hand in, zero included.
    /// </summary>
    /// <remarks>
    /// A weight is a proportion, and nothing here may assume how a caller arrived at one. The zero
    /// weight stays at about one draw in six, because a zero-weighted part is the case ADR-0021 §6
    /// names and the one an implementation is most likely to get wrong.
    /// </remarks>
    private static Gen<decimal> Weights() =>
        Gen.Frequency(
            (2, Gen.Constant(0m)),
            (3, RoundWeights()),
            (4, RatioWeights()),
            (3, WildlyScaledWeights()));

    /// <summary>
    /// Weights someone typed: whole numbers and halves, with at most two decimal places. Every
    /// product they take part in is exact, which is exactly why a generator made only of these
    /// cannot fail.
    /// </summary>
    private static Gen<decimal> RoundWeights() =>
        Gen.Elements(0.25m, 0.5m, 1m, 2m, 3m, 5m, 7m, 12.5m);

    /// <summary>
    /// Weights the way a caller actually produces them: one line's amount over the document total.
    /// </summary>
    /// <remarks>
    /// A <c>decimal</c> division carries 28 decimal places. Multiplying a five- or six-figure
    /// amount by one of these needs more significant digits than <c>decimal</c> has, so it rounds
    /// without saying so — the failure review B-03 found. This is the region the old generator
    /// never visited.
    /// </remarks>
    private static Gen<decimal> RatioWeights() =>
        Gen.Choose(1, 10_000_000).SelectMany(lineAmount =>
            Gen.Choose(1, 10_000_000).Select(documentTotal =>
                decimal.Divide(lineAmount, documentTotal)));

    /// <summary>
    /// Weights whose magnitudes are nothing like one another — 10⁻²⁰ beside 10⁸ in the same split —
    /// and whose scales run most of the way to what <c>decimal</c> can hold.
    /// </summary>
    private static Gen<decimal> WildlyScaledWeights() =>
        Gen.Choose(1, 999_999).SelectMany(digits =>
            Gen.Choose(-20, 8).Select(exponent => AtPowerOfTen(digits, exponent)));

    /// <summary>Weights that can carry a split on their own, for the single-part property.</summary>
    private static Gen<decimal> PositiveWeights() => Weights().Where(weight => weight > 0m);

    private static decimal AtPowerOfTen(int digits, int exponent)
    {
        decimal weight = digits;
        for (int step = 0; step < Math.Abs(exponent); step++)
        {
            weight = exponent > 0 ? weight * 10m : weight / 10m;
        }

        return weight;
    }

    private static Gen<EvenSplit> EvenSplits() =>
        SplittableAmounts().SelectMany(total =>
            Gen.Choose(1, MaxParts).Select(parts => new EvenSplit(total, parts)));

    private static Gen<WeightedSplit> WeightedSplits() =>
        SplittableAmounts().SelectMany(total =>
            Gen.Choose(1, MaxParts).SelectMany(count =>
                Gen.ArrayOf(Weights(), count)
                    .Where(weights => weights.Any(weight => weight > 0m))
                    .Select(weights => new WeightedSplit(total, weights))));

    private static Gen<WeightedSplit> SingleWeightSplits() =>
        SplittableAmounts().SelectMany(total =>
            PositiveWeights().Select(weight => new WeightedSplit(total, [weight])));

    private static Money FromMinorUnits(int minorUnits, Currency currency)
    {
        decimal scale = 1m;
        for (int place = 0; place < currency.MinorUnits; place++)
        {
            scale *= 10m;
        }

        return new Money(minorUnits / scale, currency);
    }

    private sealed record EvenSplit(Money Total, int Parts);

    private sealed record WeightedSplit(Money Total, IReadOnlyList<decimal> Weights)
    {
        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{Total} over [{string.Join(", ", Weights)}]");
    }
}
