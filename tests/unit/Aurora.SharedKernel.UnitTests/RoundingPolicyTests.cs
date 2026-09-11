using System;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class RoundingPolicyTests
{
    private static readonly Currency Nzd = TestCurrencies.Nzd;
    private static readonly Currency Jpy = TestCurrencies.Jpy;
    private static readonly Currency Bhd = TestCurrencies.Bhd;

    [Fact]
    public void The_core_default_rounds_half_away_from_zero_to_the_currencys_minor_units()
    {
        RoundingPolicy policy = RoundingPolicy.CoreDefaultFor(Nzd);

        policy.DecimalPlaces.ShouldBe(2);
        policy.Midpoint.ShouldBe(MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void The_core_default_follows_the_currency_and_not_an_assumed_two_decimals()
    {
        RoundingPolicy.CoreDefaultFor(Jpy).DecimalPlaces.ShouldBe(0);
        RoundingPolicy.CoreDefaultFor(Bhd).DecimalPlaces.ShouldBe(3);
    }

    [Fact]
    public void A_country_package_may_choose_a_different_midpoint_rule()
    {
        RoundingPolicy policy = RoundingPolicy.Of(2, MidpointRounding.ToEven);

        policy.DecimalPlaces.ShouldBe(2);
        policy.Midpoint.ShouldBe(MidpointRounding.ToEven);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void A_number_of_decimal_places_a_decimal_cannot_hold_is_rejected(int decimalPlaces)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RoundingPolicy.Of(decimalPlaces, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void A_midpoint_rule_that_is_not_a_midpoint_rule_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RoundingPolicy.Of(2, (MidpointRounding)99));
    }

    [Fact]
    public void The_default_value_is_not_a_policy_because_the_frameworks_default_is_not_ours()
    {
        RoundingPolicy unspecified = default;

        unspecified.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unspecified.DecimalPlaces);
        Should.Throw<InvalidOperationException>(() => unspecified.Midpoint);
        RoundingPolicy.CoreDefaultFor(Nzd).IsSpecified.ShouldBeTrue();
    }

    [Fact]
    public void Policies_with_the_same_places_and_midpoint_rule_are_equal()
    {
        RoundingPolicy.Of(2, MidpointRounding.AwayFromZero)
            .ShouldBe(RoundingPolicy.CoreDefaultFor(Nzd));

        RoundingPolicy.Of(2, MidpointRounding.ToEven)
            .ShouldNotBe(RoundingPolicy.Of(2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void ToString_states_both_halves_of_the_policy_and_never_throws()
    {
        RoundingPolicy.CoreDefaultFor(Nzd).ToString().ShouldContain("2");
        RoundingPolicy.CoreDefaultFor(Nzd).ToString().ShouldContain(nameof(MidpointRounding.AwayFromZero));
        default(RoundingPolicy).ToString().ShouldNotBeNull();
    }

    [Fact]
    public void No_way_to_round_money_omits_the_policy()
    {
        MethodInfo[] roundingMethods = typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.Name.Contains("Round", StringComparison.Ordinal))
            .ToArray();

        roundingMethods.ShouldNotBeEmpty();
        roundingMethods.ShouldAllBe(method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(RoundingPolicy)));
    }
}
