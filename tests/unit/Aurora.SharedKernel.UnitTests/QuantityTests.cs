using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class QuantityTests
{
    private static readonly UnitOfMeasure Each = TestUnits.Each;
    private static readonly UnitOfMeasure Kilogram = TestUnits.Kilogram;
    private static readonly Currency Nzd = TestCurrencies.Nzd;

    [Fact]
    public void A_quantity_is_an_amount_and_the_unit_it_is_counted_in()
    {
        Quantity quantity = new(12.5m, Kilogram);

        quantity.Amount.ShouldBe(12.5m);
        quantity.Unit.ShouldBe(Kilogram);
    }

    [Fact]
    public void A_quantity_without_a_unit_cannot_be_built()
    {
        Should.Throw<ArgumentException>(() => new Quantity(12.5m, default));
    }

    [Fact]
    public void Nothing_still_names_the_unit_it_is_nothing_of()
    {
        Quantity.Zero(Each).IsZero.ShouldBeTrue();
        Quantity.Zero(Each).Unit.ShouldBe(Each);
    }

    [Fact]
    public void A_negative_quantity_is_normal_because_a_return_is()
    {
        Quantity returned = new(-3m, Each);

        returned.IsNegative.ShouldBeTrue();
        returned.IsPositive.ShouldBeFalse();
    }

    [Fact]
    public void Quantities_in_the_same_unit_add_and_subtract()
    {
        (new Quantity(10m, Each) + new Quantity(2m, Each)).ShouldBe(new Quantity(12m, Each));
        (new Quantity(10m, Each) - new Quantity(2m, Each)).ShouldBe(new Quantity(8m, Each));
        (-new Quantity(10m, Each)).ShouldBe(new Quantity(-10m, Each));
    }

    [Fact]
    public void Quantities_in_different_units_never_combine()
    {
        Should.Throw<UnitOfMeasureMismatchException>(
            () => new Quantity(10m, Each) + new Quantity(2m, Kilogram));
        Should.Throw<UnitOfMeasureMismatchException>(
            () => new Quantity(10m, Each) - new Quantity(2m, Kilogram));
    }

    [Fact]
    public void Quantities_in_different_units_never_compare()
    {
        Should.Throw<UnitOfMeasureMismatchException>(
            () => new Quantity(10m, Each) < new Quantity(2m, Kilogram));
        Should.Throw<UnitOfMeasureMismatchException>(
            () => new Quantity(10m, Each).CompareTo(new Quantity(2m, Kilogram)));
    }

    [Fact]
    public void A_mismatch_names_both_units_so_a_log_says_which_two_were_mixed()
    {
        UnitOfMeasureMismatchException mismatch = Should.Throw<UnitOfMeasureMismatchException>(
            () => new Quantity(10m, Each) + new Quantity(2m, Kilogram));

        mismatch.Left.ShouldBe(Each);
        mismatch.Right.ShouldBe(Kilogram);
    }

    [Fact]
    public void A_quantity_scales_by_a_plain_number_and_keeps_its_unit()
    {
        (new Quantity(10m, Each) * 3m).ShouldBe(new Quantity(30m, Each));
        (3m * new Quantity(10m, Each)).ShouldBe(new Quantity(30m, Each));
        (new Quantity(10m, Each) / 4m).ShouldBe(new Quantity(2.5m, Each));
    }

    [Fact]
    public void One_quantity_divided_by_another_is_a_ratio_rather_than_a_quantity()
    {
        decimal ratio = new Quantity(10m, Each) / new Quantity(4m, Each);

        ratio.ShouldBe(2.5m);
    }

    [Fact]
    public void A_ratio_of_quantities_in_different_units_is_refused()
    {
        Should.Throw<UnitOfMeasureMismatchException>(
            () => new Quantity(10m, Each) / new Quantity(4m, Kilogram));
    }

    [Fact]
    public void Dividing_by_nothing_throws_rather_than_producing_an_infinity()
    {
        Should.Throw<DivideByZeroException>(() => new Quantity(10m, Each) / 0m);
        Should.Throw<DivideByZeroException>(() => new Quantity(10m, Each) / Quantity.Zero(Each));
    }

    [Fact]
    public void A_quantity_times_a_unit_price_is_an_amount_of_money()
    {
        Money lineNet = new Quantity(3m, Each) * new Money(19.95m, Nzd);

        lineNet.ShouldBe(new Money(59.85m, Nzd));
        lineNet.Currency.ShouldBe(Nzd);
    }

    [Fact]
    public void A_quantity_times_a_unit_price_reads_the_same_way_round_either_way()
    {
        Quantity quantity = new(3m, Each);
        Money unitPrice = new(19.95m, Nzd);

        (unitPrice * quantity).ShouldBe(quantity * unitPrice);
    }

    [Fact]
    public void A_line_amount_keeps_full_precision_because_rounding_happens_later()
    {
        Money lineNet = new Quantity(3m, Each) * new Money(0.333333m, Nzd);

        lineNet.Amount.ShouldBe(0.999999m);
        lineNet.IsInWholeMinorUnits.ShouldBeFalse();
    }

    [Fact]
    public void Quantities_order_by_size_within_one_unit()
    {
        (new Quantity(10m, Each) > new Quantity(2m, Each)).ShouldBeTrue();
        (new Quantity(2m, Each) < new Quantity(10m, Each)).ShouldBeTrue();
        (new Quantity(10m, Each) >= new Quantity(10m, Each)).ShouldBeTrue();
        (new Quantity(10m, Each) <= new Quantity(10m, Each)).ShouldBeTrue();
    }

    [Fact]
    public void Quantities_that_differ_only_in_trailing_zeros_are_the_same_quantity()
    {
        new Quantity(10m, Each).ShouldBe(new Quantity(10.00m, Each));
        new Quantity(10m, Each).GetHashCode().ShouldBe(new Quantity(10.00m, Each).GetHashCode());
    }

    [Fact]
    public void Summing_no_lines_is_nothing_in_a_named_unit_rather_than_a_bare_zero()
    {
        Quantity.Sum([], Each).ShouldBe(Quantity.Zero(Each));
        Quantity.Sum([new Quantity(2m, Each), new Quantity(3m, Each)], Each)
            .ShouldBe(new Quantity(5m, Each));
    }

    [Fact]
    public void Summing_a_line_in_another_unit_is_refused()
    {
        Should.Throw<UnitOfMeasureMismatchException>(
            () => Quantity.Sum([new Quantity(2m, Each), new Quantity(3m, Kilogram)], Each));
    }

    [Fact]
    public void Summing_a_missing_sequence_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Quantity.Sum(null!, Each));
    }

    [Fact]
    public void The_size_of_a_quantity_drops_its_direction()
    {
        new Quantity(-3m, Each).Abs().ShouldBe(new Quantity(3m, Each));
    }

    [Fact]
    public void Every_operation_on_a_default_Quantity_throws_rather_than_treating_it_as_zero()
    {
        Should.Throw<InvalidOperationException>(() => default(Quantity) + new Quantity(1m, Each));
        Should.Throw<InvalidOperationException>(() => -default(Quantity));
        Should.Throw<InvalidOperationException>(() => default(Quantity) * 2m);
        Should.Throw<InvalidOperationException>(() => default(Quantity) * new Money(1m, Nzd));
        Should.Throw<InvalidOperationException>(() => default(Quantity).Abs());
        Should.Throw<InvalidOperationException>(() => default(Quantity).IsZero);
        Should.Throw<InvalidOperationException>(() => default(Quantity).IsPositive);
        Should.Throw<InvalidOperationException>(() => default(Quantity).IsNegative);
    }

    [Fact]
    public void ToString_names_the_unit_and_does_not_depend_on_the_current_culture()
    {
        new Quantity(12.5m, Kilogram).ToString().ShouldBe("12.5 KGM");
        new Quantity(-3m, Each).ToString().ShouldBe("-3 H87");
        default(Quantity).ToString().ShouldNotBeNull();
    }
}
