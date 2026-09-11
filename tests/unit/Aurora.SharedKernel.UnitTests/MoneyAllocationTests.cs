using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class MoneyAllocationTests
{
    private static readonly Currency Nzd = TestCurrencies.Nzd;
    private static readonly Currency Jpy = TestCurrencies.Jpy;

    [Fact]
    public void Splitting_a_hundred_across_three_lines_neither_loses_nor_invents_a_cent()
    {
        IReadOnlyList<Money> parts = new Money(100.00m, Nzd).Allocate(3);

        parts.ShouldBe([new Money(33.34m, Nzd), new Money(33.33m, Nzd), new Money(33.33m, Nzd)]);
        Money.Sum(parts, Nzd).ShouldBe(new Money(100.00m, Nzd));
    }

    [Fact]
    public void The_leftover_cents_go_to_the_earliest_lines_so_a_split_is_repeatable()
    {
        IReadOnlyList<Money> parts = new Money(0.05m, Nzd).Allocate(3);

        parts.ShouldBe([new Money(0.02m, Nzd), new Money(0.02m, Nzd), new Money(0.01m, Nzd)]);
        new Money(0.05m, Nzd).Allocate(3).ShouldBe(parts);
    }

    [Fact]
    public void Splitting_a_negative_total_mirrors_the_positive_split()
    {
        IReadOnlyList<Money> parts = new Money(-100.00m, Nzd).Allocate(3);

        parts.ShouldBe([new Money(-33.34m, Nzd), new Money(-33.33m, Nzd), new Money(-33.33m, Nzd)]);
        Money.Sum(parts, Nzd).ShouldBe(new Money(-100.00m, Nzd));
    }

    [Fact]
    public void Splitting_into_one_part_gives_the_whole_back()
    {
        new Money(100.00m, Nzd).Allocate(1).ShouldBe([new Money(100.00m, Nzd)]);
    }

    [Fact]
    public void Splitting_nothing_gives_nothing_to_every_line()
    {
        Money.Zero(Nzd).Allocate(3).ShouldBe([Money.Zero(Nzd), Money.Zero(Nzd), Money.Zero(Nzd)]);
    }

    [Fact]
    public void A_currency_without_minor_units_is_split_into_whole_units()
    {
        IReadOnlyList<Money> parts = new Money(100m, Jpy).Allocate(3);

        parts.ShouldBe([new Money(34m, Jpy), new Money(33m, Jpy), new Money(33m, Jpy)]);
        Money.Sum(parts, Jpy).ShouldBe(new Money(100m, Jpy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Splitting_into_fewer_than_one_part_is_rejected(int parts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(100.00m, Nzd).Allocate(parts));
    }

    [Fact]
    public void Weights_give_each_line_its_share()
    {
        IReadOnlyList<Money> parts = new Money(100.00m, Nzd).Allocate([1m, 1m, 2m]);

        parts.ShouldBe([new Money(25.00m, Nzd), new Money(25.00m, Nzd), new Money(50.00m, Nzd)]);
    }

    [Fact]
    public void A_weighted_split_gives_a_leftover_cent_to_the_largest_remainder()
    {
        IReadOnlyList<Money> parts = new Money(100.00m, Nzd).Allocate([3m, 2m, 1m]);

        parts.ShouldBe([new Money(50.00m, Nzd), new Money(33.33m, Nzd), new Money(16.67m, Nzd)]);
        Money.Sum(parts, Nzd).ShouldBe(new Money(100.00m, Nzd));
    }

    [Fact]
    public void A_line_weighted_zero_gets_nothing_and_the_rest_still_add_up()
    {
        IReadOnlyList<Money> parts = new Money(100.00m, Nzd).Allocate([0m, 1m, 1m]);

        parts.ShouldBe([Money.Zero(Nzd), new Money(50.00m, Nzd), new Money(50.00m, Nzd)]);
        Money.Sum(parts, Nzd).ShouldBe(new Money(100.00m, Nzd));
    }

    [Fact]
    public void Weights_that_are_all_zero_are_rejected_because_there_is_no_share_to_compute()
    {
        Should.Throw<ArgumentException>(() => new Money(100.00m, Nzd).Allocate([0m, 0m]));
    }

    [Fact]
    public void A_negative_weight_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new Money(100.00m, Nzd).Allocate([2m, -1m]));
    }

    [Fact]
    public void An_empty_or_missing_set_of_weights_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new Money(100.00m, Nzd).Allocate([]));
        Should.Throw<ArgumentNullException>(() => new Money(100.00m, Nzd).Allocate(null!));
    }

    [Fact]
    public void An_amount_carrying_more_precision_than_the_currency_cannot_be_split()
    {
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => new Money(100.005m, Nzd).Allocate(3));

        exception.Message.ShouldContain("round");
    }

    [Fact]
    public void Splitting_a_default_Money_throws()
    {
        Should.Throw<InvalidOperationException>(() => default(Money).Allocate(3));
    }

    /// <summary>
    /// A discount apportioned over three lines by the most natural weights there are: each line's
    /// share of the document total, written as <c>lineAmount / documentTotal</c>.
    /// </summary>
    /// <remarks>
    /// A <c>decimal</c> division carries 28 decimal places, and an implementation that multiplies
    /// the amount by a weight of that shape exhausts <c>decimal</c>'s 28-29 significant digits and
    /// rounds — silently, with no exception. One cent used to vanish here (review B-03, finding
    /// B-1). Allocation now scales the weights to integers first, so no weight can be too precise
    /// for it.
    /// </remarks>
    [Fact]
    public void Ratio_weights_over_three_lines_still_add_up_to_the_discount()
    {
        Money discount = new(3914.33m, Nzd);

        IReadOnlyList<Money> parts = discount.Allocate(
            [5851.58m / 9400.65m, 2480.8m / 3508.02m, 7205.35m / 2643.21m]);

        Money.Sum(parts, Nzd).ShouldBe(discount);
        parts.ShouldAllBe(part => part.IsInWholeMinorUnits);
    }

    /// <summary>
    /// The simplest allocation there is. One part takes everything, whatever its weight says,
    /// because it is the only proportion of the whole there is.
    /// </summary>
    /// <remarks>
    /// This used to return 6252019.1900000000000000000001 NZD — not a payable amount, and not the
    /// amount it was given (review B-03, finding B-1).
    /// </remarks>
    [Fact]
    public void A_single_line_weighted_by_a_third_takes_the_whole_amount_unchanged()
    {
        Money total = new(6252019.19m, Nzd);

        IReadOnlyList<Money> parts = total.Allocate([1m / 3m]);

        parts.ShouldBe([total]);
        parts[0].IsInWholeMinorUnits.ShouldBeTrue();
    }

    /// <summary>
    /// Four ratio weights of mixed magnitude over a seven-figure total. One cent used to go
    /// missing (review B-03, finding B-1).
    /// </summary>
    [Fact]
    public void Four_ratio_weights_over_a_seven_figure_total_still_add_up()
    {
        Money total = new(7810283.36m, Nzd);

        IReadOnlyList<Money> parts = total.Allocate(
            [9649.37m / 9055.42m, 6827.95m / 6769.69m, 1774.05m / 5469.35m, 4729.04m / 9735.47m]);

        Money.Sum(parts, Nzd).ShouldBe(total);
        parts.ShouldAllBe(part => part.IsInWholeMinorUnits);
    }
}
