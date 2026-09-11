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
/// The example-based tests next door pin the exact split a known input produces. These pin the law
/// that no input may break, over currencies with zero, two and three minor units, over positive,
/// negative and zero totals, and over weight sets that include zeros. Example-based tests
/// systematically miss the boundaries of money arithmetic (`testing-strategy.md` §2), which is why
/// the allocation invariant is stated universally here rather than sampled.
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
    /// Weights that include zero about one time in five, because a zero-weighted part is the case
    /// ADR-0021 §6 names and the one an implementation is most likely to get wrong.
    /// </summary>
    private static Gen<decimal> Weights() =>
        Gen.Elements(0m, 0m, 0.25m, 0.5m, 1m, 2m, 3m, 5m, 7m, 12.5m);

    private static Gen<EvenSplit> EvenSplits() =>
        SplittableAmounts().SelectMany(total =>
            Gen.Choose(1, MaxParts).Select(parts => new EvenSplit(total, parts)));

    private static Gen<WeightedSplit> WeightedSplits() =>
        SplittableAmounts().SelectMany(total =>
            Gen.Choose(1, MaxParts).SelectMany(count =>
                Gen.ArrayOf(Weights(), count)
                    .Where(weights => weights.Sum() > 0m)
                    .Select(weights => new WeightedSplit(total, weights))));

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
