using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class CurrencyTests
{
    [Fact]
    public void A_currency_is_an_ISO_4217_code_and_its_minor_unit_count()
    {
        Currency currency = Currency.Of("NZD", minorUnits: 2);

        currency.Code.ShouldBe("NZD");
        currency.MinorUnits.ShouldBe(2);
    }

    [Fact]
    public void A_currency_without_minor_units_is_valid()
    {
        Currency.Of("JPY", minorUnits: 0).MinorUnits.ShouldBe(0);
    }

    [Fact]
    public void A_code_is_normalized_to_upper_case_so_that_casing_never_splits_a_currency()
    {
        Currency.Of("nzd", minorUnits: 2).Code.ShouldBe("NZD");
        Currency.Of("nzd", minorUnits: 2).ShouldBe(Currency.Of("NZD", minorUnits: 2));
    }

    [Fact]
    public void Currencies_with_the_same_code_and_minor_units_are_equal()
    {
        Currency.Of("NZD", 2).ShouldBe(Currency.Of("NZD", 2));
        Currency.Of("NZD", 2).GetHashCode().ShouldBe(Currency.Of("NZD", 2).GetHashCode());
    }

    [Fact]
    public void Currencies_with_different_codes_are_not_equal()
    {
        Currency.Of("NZD", 2).ShouldNotBe(Currency.Of("AUD", 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("US1")]
    [InlineData("US$")]
    [InlineData(" US")]
    [InlineData("US ")]
    public void A_code_that_is_not_three_letters_is_rejected(string code)
    {
        Should.Throw<ArgumentException>(() => Currency.Of(code, minorUnits: 2));
    }

    [Fact]
    public void A_null_code_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Currency.Of(null!, minorUnits: 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void A_minor_unit_count_outside_the_storable_range_is_rejected(int minorUnits)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Currency.Of("NZD", minorUnits));
    }

    [Fact]
    public void The_default_value_is_not_a_specified_currency()
    {
        Currency unspecified = default;

        unspecified.IsSpecified.ShouldBeFalse();
        Currency.Of("NZD", 2).IsSpecified.ShouldBeTrue();
    }

    [Fact]
    public void Reading_the_code_of_an_unspecified_currency_throws_rather_than_returning_a_blank()
    {
        Currency unspecified = default;

        Should.Throw<InvalidOperationException>(() => unspecified.Code);
        Should.Throw<InvalidOperationException>(() => unspecified.MinorUnits);
    }

    [Fact]
    public void ToString_is_the_ISO_code_and_never_throws()
    {
        Currency.Of("NZD", 2).ToString().ShouldBe("NZD");
        default(Currency).ToString().ShouldNotBeNull();
    }
}
