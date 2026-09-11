using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class MoneyRoundingTests
{
    private static readonly Currency Nzd = TestCurrencies.Nzd;
    private static readonly Currency Jpy = TestCurrencies.Jpy;

    [Fact]
    public void A_half_rounds_away_from_zero_under_the_core_default()
    {
        RoundingPolicy policy = RoundingPolicy.CoreDefaultFor(Nzd);

        new Money(2.345m, Nzd).Round(policy).ShouldBe(new Money(2.35m, Nzd));
        new Money(-2.345m, Nzd).Round(policy).ShouldBe(new Money(-2.35m, Nzd));
    }

    [Fact]
    public void A_package_that_needs_bankers_rounding_gets_bankers_rounding()
    {
        RoundingPolicy policy = RoundingPolicy.Of(2, MidpointRounding.ToEven);

        new Money(2.345m, Nzd).Round(policy).ShouldBe(new Money(2.34m, Nzd));
        new Money(2.355m, Nzd).Round(policy).ShouldBe(new Money(2.36m, Nzd));
    }

    [Fact]
    public void Rounding_a_currency_without_minor_units_produces_whole_units()
    {
        new Money(2.5m, Jpy).Round(RoundingPolicy.CoreDefaultFor(Jpy)).ShouldBe(new Money(3m, Jpy));
        new Money(2.4m, Jpy).Round(RoundingPolicy.CoreDefaultFor(Jpy)).ShouldBe(new Money(2m, Jpy));
    }

    [Fact]
    public void Rounding_keeps_the_currency()
    {
        new Money(2.345m, Nzd).Round(RoundingPolicy.CoreDefaultFor(Nzd)).Currency.ShouldBe(Nzd);
    }

    [Fact]
    public void Rounding_an_amount_that_is_already_rounded_changes_nothing()
    {
        Money amount = new(2.34m, Nzd);

        amount.Round(RoundingPolicy.CoreDefaultFor(Nzd)).ShouldBe(amount);
    }

    [Fact]
    public void Rounding_without_a_policy_throws_rather_than_falling_back_to_a_framework_default()
    {
        Should.Throw<InvalidOperationException>(() => new Money(2.345m, Nzd).Round(default));
    }

    [Fact]
    public void Rounding_a_default_Money_throws()
    {
        Should.Throw<InvalidOperationException>(
            () => default(Money).Round(RoundingPolicy.CoreDefaultFor(Nzd)));
    }

    [Fact]
    public void An_amount_knows_whether_it_is_already_expressed_in_whole_minor_units()
    {
        new Money(2.34m, Nzd).IsInWholeMinorUnits.ShouldBeTrue();
        new Money(2.3400m, Nzd).IsInWholeMinorUnits.ShouldBeTrue();
        new Money(2.345m, Nzd).IsInWholeMinorUnits.ShouldBeFalse();
        new Money(2m, Jpy).IsInWholeMinorUnits.ShouldBeTrue();
        new Money(2.5m, Jpy).IsInWholeMinorUnits.ShouldBeFalse();
    }
}
