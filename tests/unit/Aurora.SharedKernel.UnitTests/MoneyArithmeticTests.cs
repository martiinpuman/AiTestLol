using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class MoneyArithmeticTests
{
    private static readonly Currency Nzd = TestCurrencies.Nzd;
    private static readonly Currency Aud = TestCurrencies.Aud;

    [Fact]
    public void An_amount_times_a_scalar_is_an_amount_in_the_same_currency()
    {
        (new Money(10.00m, Nzd) * 3m).ShouldBe(new Money(30.00m, Nzd));
        (3m * new Money(10.00m, Nzd)).ShouldBe(new Money(30.00m, Nzd));
    }

    [Fact]
    public void Scaling_keeps_full_precision_because_rounding_is_a_separate_decision()
    {
        (new Money(10.005m, Nzd) * 3m).Amount.ShouldBe(30.015m);
        (new Money(10.00m, Nzd) / 3m).Amount.ShouldBe(3.3333333333333333333333333333m);
    }

    [Fact]
    public void An_amount_divided_by_a_scalar_is_an_amount_in_the_same_currency()
    {
        (new Money(30.00m, Nzd) / 3m).ShouldBe(new Money(10.00m, Nzd));
    }

    [Fact]
    public void Dividing_an_amount_by_zero_throws()
    {
        Should.Throw<DivideByZeroException>(() => new Money(30.00m, Nzd) / 0m);
    }

    [Fact]
    public void An_amount_divided_by_an_amount_is_a_ratio_not_money()
    {
        decimal ratio = new Money(30.00m, Nzd) / new Money(120.00m, Nzd);

        ratio.ShouldBe(0.25m);
    }

    [Fact]
    public void Dividing_amounts_in_different_currencies_throws()
    {
        Should.Throw<CurrencyMismatchException>(() => new Money(30.00m, Nzd) / new Money(120.00m, Aud));
    }

    [Fact]
    public void Dividing_by_an_amount_of_nothing_throws()
    {
        Should.Throw<DivideByZeroException>(() => new Money(30.00m, Nzd) / Money.Zero(Nzd));
    }

    [Fact]
    public void Amounts_in_the_same_currency_order_by_amount()
    {
        Money small = new(10.00m, Nzd);
        Money large = new(20.00m, Nzd);

        (small < large).ShouldBeTrue();
        (large > small).ShouldBeTrue();
        (small <= new Money(10.00m, Nzd)).ShouldBeTrue();
        (small >= new Money(10.00m, Nzd)).ShouldBeTrue();
        small.CompareTo(large).ShouldBeLessThan(0);
        large.CompareTo(small).ShouldBeGreaterThan(0);
        small.CompareTo(new Money(10.00m, Nzd)).ShouldBe(0);
    }

    [Fact]
    public void Amounts_in_different_currencies_are_not_comparable()
    {
        Should.Throw<CurrencyMismatchException>(() => new Money(10.00m, Nzd) < new Money(20.00m, Aud));
        Should.Throw<CurrencyMismatchException>(() => new Money(10.00m, Nzd).CompareTo(new Money(20.00m, Aud)));
    }

    [Fact]
    public void Summing_amounts_adds_every_one_of_them()
    {
        Money[] lines = [new Money(10.10m, Nzd), new Money(20.20m, Nzd), new Money(-0.30m, Nzd)];

        Money.Sum(lines, Nzd).ShouldBe(new Money(30.00m, Nzd));
    }

    [Fact]
    public void Summing_nothing_still_produces_an_amount_in_a_named_currency()
    {
        Money.Sum([], Nzd).ShouldBe(Money.Zero(Nzd));
    }

    [Fact]
    public void Summing_an_amount_in_another_currency_throws()
    {
        Money[] lines = [new Money(10.10m, Nzd), new Money(20.20m, Aud)];

        Should.Throw<CurrencyMismatchException>(() => Money.Sum(lines, Nzd));
    }

    [Fact]
    public void Summing_a_null_sequence_throws()
    {
        Should.Throw<ArgumentNullException>(() => Money.Sum(null!, Nzd));
    }

    [Fact]
    public void Money_times_Money_does_not_exist_so_it_cannot_compile()
    {
        IEnumerable<MethodInfo> moneyTimesMoney = typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "op_Multiply")
            .Where(method => method.GetParameters().All(parameter => parameter.ParameterType == typeof(Money)));

        moneyTimesMoney.ShouldBeEmpty();
    }

    [Fact]
    public void Money_has_no_conversion_to_a_bare_number()
    {
        IEnumerable<MethodInfo> conversions = typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit");

        conversions.ShouldBeEmpty();
    }
}
