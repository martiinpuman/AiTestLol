using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class PercentageTests
{
    private static readonly Currency Nzd = TestCurrencies.Nzd;

    [Fact]
    public void A_rate_written_as_a_percentage_and_the_same_rate_written_as_a_fraction_are_one_value()
    {
        Percentage.FromPercent(15m).ShouldBe(Percentage.FromFraction(0.15m));
    }

    [Fact]
    public void A_rate_reads_back_in_both_spellings_without_a_factor_of_a_hundred_going_missing()
    {
        Percentage rate = Percentage.FromPercent(15m);

        rate.AsPercent.ShouldBe(15m);
        rate.AsFraction.ShouldBe(0.15m);
    }

    [Fact]
    public void How_the_rate_was_written_does_not_change_what_it_is()
    {
        Percentage.FromPercent(15m).ShouldBe(Percentage.FromPercent(15.00m));
        Percentage.FromPercent(15m).GetHashCode().ShouldBe(Percentage.FromPercent(15.00m).GetHashCode());
    }

    [Fact]
    public void A_rate_nobody_set_is_no_rate_at_all_rather_than_a_surprise()
    {
        default(Percentage).ShouldBe(Percentage.Zero);
        Percentage.Zero.AsPercent.ShouldBe(0m);
    }

    [Fact]
    public void Applying_a_rate_to_an_amount_gives_an_amount_in_the_same_currency()
    {
        Money tax = new Money(100.00m, Nzd) * Percentage.FromPercent(15m);

        tax.ShouldBe(new Money(15.0000m, Nzd));
        tax.Currency.ShouldBe(Nzd);
    }

    [Fact]
    public void A_rate_applies_the_same_way_round_whichever_side_it_is_written_on()
    {
        Money amount = new(100.00m, Nzd);
        Percentage rate = Percentage.FromPercent(15m);

        (rate * amount).ShouldBe(amount * rate);
    }

    [Fact]
    public void Applying_a_rate_never_rounds_because_rounding_happens_at_defined_points_only()
    {
        Money tax = new Money(0.07m, Nzd) * Percentage.FromPercent(12.5m);

        tax.Amount.ShouldBe(0.00875m);
        tax.IsInWholeMinorUnits.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_of_an_amount_is_nothing_and_all_of_it_is_the_amount()
    {
        Money amount = new(100.00m, Nzd);

        (amount * Percentage.Zero).ShouldBe(Money.Zero(Nzd));
        (amount * Percentage.OneHundred).ShouldBe(amount);
    }

    [Fact]
    public void A_rate_can_be_negative_because_a_settlement_adjustment_is()
    {
        Percentage adjustment = Percentage.FromPercent(-2m);

        adjustment.IsNegative.ShouldBeTrue();
        (new Money(100.00m, Nzd) * adjustment).ShouldBe(new Money(-2.0000m, Nzd));
    }

    [Fact]
    public void A_rate_can_exceed_a_hundred_percent_because_a_markup_does()
    {
        Percentage markup = Percentage.FromPercent(150m);

        (new Money(10.00m, Nzd) * markup).ShouldBe(new Money(15.0000m, Nzd));
    }

    [Fact]
    public void Rates_add_and_subtract_as_rates()
    {
        (Percentage.FromPercent(15m) + Percentage.FromPercent(5m)).ShouldBe(Percentage.FromPercent(20m));
        (Percentage.FromPercent(15m) - Percentage.FromPercent(5m)).ShouldBe(Percentage.FromPercent(10m));
        (-Percentage.FromPercent(15m)).ShouldBe(Percentage.FromPercent(-15m));
    }

    [Fact]
    public void Rates_order_by_size()
    {
        (Percentage.FromPercent(15m) > Percentage.FromPercent(5m)).ShouldBeTrue();
        (Percentage.FromPercent(5m) < Percentage.FromPercent(15m)).ShouldBeTrue();
        (Percentage.FromPercent(15m) >= Percentage.FromPercent(15m)).ShouldBeTrue();
        (Percentage.FromPercent(15m) <= Percentage.FromPercent(15m)).ShouldBeTrue();
        Percentage.FromPercent(15m).CompareTo(Percentage.FromPercent(5m)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Applying_a_rate_to_a_default_Money_throws()
    {
        Should.Throw<InvalidOperationException>(() => default(Money) * Percentage.FromPercent(15m));
        Should.Throw<InvalidOperationException>(() => Percentage.FromPercent(15m) * default(Money));
    }

    [Fact]
    public void ToString_states_the_rate_and_does_not_depend_on_the_current_culture()
    {
        Percentage.FromPercent(12.5m).ToString().ShouldBe("12.5%");
        Percentage.FromPercent(-2m).ToString().ShouldBe("-2%");
        default(Percentage).ToString().ShouldBe("0%");
    }
}
