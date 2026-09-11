using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class MoneyTests
{
    private static readonly Currency Nzd = TestCurrencies.Nzd;
    private static readonly Currency Aud = TestCurrencies.Aud;

    [Fact]
    public void Money_is_a_decimal_amount_and_a_currency()
    {
        Money money = new(12.34m, Nzd);

        money.Amount.ShouldBe(12.34m);
        money.Currency.ShouldBe(Nzd);
    }

    [Fact]
    public void Money_cannot_exist_without_a_currency()
    {
        Should.Throw<ArgumentException>(() => new Money(12.34m, default));
    }

    [Fact]
    public void Zero_is_nothing_in_a_named_currency()
    {
        Money zero = Money.Zero(Nzd);

        zero.Amount.ShouldBe(0m);
        zero.Currency.ShouldBe(Nzd);
        zero.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Amounts_with_the_same_value_and_currency_are_equal()
    {
        new Money(12.34m, Nzd).ShouldBe(new Money(12.34m, Nzd));
        (new Money(12.34m, Nzd) == new Money(12.34m, Nzd)).ShouldBeTrue();
    }

    [Fact]
    public void Trailing_zeros_do_not_change_what_an_amount_is_worth()
    {
        new Money(1.50m, Nzd).ShouldBe(new Money(1.5m, Nzd));
        new Money(1.50m, Nzd).GetHashCode().ShouldBe(new Money(1.5m, Nzd).GetHashCode());
    }

    [Fact]
    public void Amounts_in_different_currencies_are_never_equal()
    {
        new Money(12.34m, Nzd).ShouldNotBe(new Money(12.34m, Aud));
        Money.Zero(Nzd).ShouldNotBe(Money.Zero(Aud));
    }

    [Fact]
    public void Adding_two_amounts_in_the_same_currency_adds_the_amounts()
    {
        (new Money(12.34m, Nzd) + new Money(0.66m, Nzd)).ShouldBe(new Money(13.00m, Nzd));
    }

    [Fact]
    public void Adding_amounts_in_different_currencies_throws()
    {
        CurrencyMismatchException exception = Should.Throw<CurrencyMismatchException>(
            () => new Money(12.34m, Nzd) + new Money(0.66m, Aud));

        exception.Left.ShouldBe(Nzd);
        exception.Right.ShouldBe(Aud);
        exception.Message.ShouldContain("NZD");
        exception.Message.ShouldContain("AUD");
    }

    [Fact]
    public void Subtracting_two_amounts_in_the_same_currency_subtracts_the_amounts()
    {
        (new Money(12.34m, Nzd) - new Money(0.34m, Nzd)).ShouldBe(new Money(12.00m, Nzd));
    }

    [Fact]
    public void Subtracting_amounts_in_different_currencies_throws()
    {
        Should.Throw<CurrencyMismatchException>(() => new Money(12.34m, Nzd) - new Money(0.66m, Aud));
    }

    [Fact]
    public void An_amount_may_be_negative_because_credit_notes_and_reversals_are_normal()
    {
        Money credit = new(-12.34m, Nzd);

        credit.Amount.ShouldBe(-12.34m);
        credit.IsNegative.ShouldBeTrue();
        credit.IsPositive.ShouldBeFalse();
        (credit + new Money(12.34m, Nzd)).ShouldBe(Money.Zero(Nzd));
    }

    [Fact]
    public void Negating_an_amount_reverses_it_and_keeps_its_currency()
    {
        (-new Money(12.34m, Nzd)).ShouldBe(new Money(-12.34m, Nzd));
        (-Money.Zero(Nzd)).ShouldBe(Money.Zero(Nzd));
    }

    [Fact]
    public void The_absolute_value_of_an_amount_drops_its_sign_and_keeps_its_currency()
    {
        new Money(-12.34m, Nzd).Abs().ShouldBe(new Money(12.34m, Nzd));
        new Money(12.34m, Nzd).Abs().ShouldBe(new Money(12.34m, Nzd));
    }

    [Fact]
    public void Arithmetic_on_a_default_Money_throws_rather_than_treating_it_as_zero()
    {
        Money uninitialized = default;

        Should.Throw<InvalidOperationException>(() => uninitialized + new Money(1m, Nzd));
        Should.Throw<InvalidOperationException>(() => new Money(1m, Nzd) + uninitialized);
        Should.Throw<InvalidOperationException>(() => uninitialized + uninitialized);
        Should.Throw<InvalidOperationException>(() => -uninitialized);
    }

    [Fact]
    public void ToString_names_the_currency_and_does_not_depend_on_the_current_culture()
    {
        new Money(1234.50m, Nzd).ToString().ShouldBe("1234.50 NZD");
        new Money(-1234.5m, Nzd).ToString().ShouldBe("-1234.5 NZD");
        default(Money).ToString().ShouldNotBeNull();
    }
}
