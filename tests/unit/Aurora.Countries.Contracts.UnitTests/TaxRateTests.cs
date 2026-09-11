using System;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// The one number in this contract that comes from a package and is then multiplied by money.
/// </summary>
public sealed class TaxRateTests
{
    /// <summary>
    /// Theory data arrives as text and is parsed here, so a literal in an attribute is the exact
    /// decimal it reads as - an attribute cannot hold a decimal constant, and letting one arrive as
    /// a double would make the test's inputs approximations of themselves.
    /// </summary>
    private static decimal Dec(string value) =>
        decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    [InlineData("19.6")]
    [InlineData("0.000001")]
    [InlineData("999.999999")]
    public void A_published_rate_is_read(string percent) =>
        ContractTestValues.Rate(Dec(percent)).AsPercentage.AsPercent.ShouldBe(Dec(percent));

    /// <summary>
    /// <c>somePercentage / 100m</c> carries 28 decimal places, and that is the shape of the value a
    /// caller hands over without thinking about it. A rate that precise is a division result, not a
    /// published rate, and multiplying money by one is where <c>decimal</c> starts rounding without
    /// saying so.
    /// </summary>
    [Fact]
    public void A_rate_more_precise_than_any_jurisdiction_publishes_is_refused()
    {
        Percentage divided = Percentage.FromFraction(1m / 3m);

        Result<TaxRate> rate = TaxRate.Create(divided);

        rate.IsFailure.ShouldBeTrue();
        rate.Error.Description.ShouldContain("decimal places");
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-15")]
    public void A_negative_rate_is_refused_because_sign_belongs_to_the_amount(string percent) =>
        TaxRate.Create(Percentage.FromPercent(Dec(percent))).IsFailure.ShouldBeTrue();

    [Theory]
    [InlineData("1000")]
    [InlineData("1000.000001")]
    [InlineData("100000")]
    public void A_rate_too_large_for_the_column_it_is_stored_in_is_refused(string percent) =>
        TaxRate.Create(Percentage.FromPercent(Dec(percent))).IsFailure.ShouldBeTrue();

    /// <summary>Trailing zeros carry no information: 15.0000000% is 15%.</summary>
    [Fact]
    public void Trailing_zeros_past_the_limit_are_not_extra_precision() =>
        ContractTestValues.Rate(15.00000000m).ShouldBe(ContractTestValues.Rate(15m));

    /// <summary>
    /// A rate nobody set and a rate somebody set to zero are different facts, and the difference is
    /// a tax return.
    /// </summary>
    [Fact]
    public void The_default_rate_is_not_zero_percent_it_is_no_rate_at_all()
    {
        TaxRate unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.AsPercentage);
        Should.Throw<InvalidOperationException>(() => unassigned.IsZero);
        Should.Throw<InvalidOperationException>(() =>
            unassigned.ApplyTo(ContractTestValues.Nz(100m), ContractTestValues.TwoPlacesAwayFromZero));

        TaxRate.Zero.IsSpecified.ShouldBeTrue();
        TaxRate.Zero.IsZero.ShouldBeTrue();
        unassigned.ShouldNotBe(TaxRate.Zero);
    }

    [Theory]
    [InlineData("100", "15", "15.00")]
    [InlineData("19.99", "15", "3.00")]
    [InlineData("0.03", "15", "0.00")]
    [InlineData("0.04", "15", "0.01")]
    [InlineData("1234.56", "12.5", "154.32")]
    [InlineData("0", "15", "0")]
    public void Tax_is_the_rate_applied_to_the_amount_rounded_once(
        string amount,
        string percent,
        string expected)
    {
        Money tax = ContractTestValues.Rate(Dec(percent))
            .ApplyTo(ContractTestValues.Nz(Dec(amount)), ContractTestValues.TwoPlacesAwayFromZero);

        tax.ShouldBe(ContractTestValues.Nz(Dec(expected)));
        tax.IsInWholeMinorUnits.ShouldBeTrue();
    }

    /// <summary>
    /// The midpoint rule is the package's, not core's: the same amount at the same rate rounds two
    /// ways depending on the jurisdiction's own rule, and core has no default to fall back on.
    /// </summary>
    [Fact]
    public void The_rounding_policy_decides_the_midpoint_and_core_has_no_default()
    {
        Money amount = ContractTestValues.Nz(0.10m);
        TaxRate rate = ContractTestValues.Rate(15m);

        rate.ApplyTo(amount, RoundingPolicy.Of(2, MidpointRounding.AwayFromZero))
            .ShouldBe(ContractTestValues.Nz(0.02m));
        rate.ApplyTo(amount, RoundingPolicy.Of(2, MidpointRounding.ToEven))
            .ShouldBe(ContractTestValues.Nz(0.02m));
        rate.ApplyTo(amount, RoundingPolicy.Of(2, MidpointRounding.ToZero))
            .ShouldBe(ContractTestValues.Nz(0.01m));

        Should.Throw<InvalidOperationException>(() => rate.ApplyTo(amount, default));
    }

    /// <summary>
    /// A currency with no minor units and one with three: the result is a whole number of whatever
    /// the currency's minor unit is, not of cents.
    /// </summary>
    [Fact]
    public void The_result_is_whole_in_the_currency_it_is_in()
    {
        Money yen = new(1000m, ContractTestValues.Jpy);
        Money dinar = new(10m, ContractTestValues.Bhd);

        Money yenTax = ContractTestValues.Rate(10m)
            .ApplyTo(yen, RoundingPolicy.Of(0, MidpointRounding.AwayFromZero));
        Money dinarTax = ContractTestValues.Rate(10m)
            .ApplyTo(dinar, RoundingPolicy.Of(3, MidpointRounding.AwayFromZero));

        yenTax.ShouldBe(new Money(100m, ContractTestValues.Jpy));
        yenTax.IsInWholeMinorUnits.ShouldBeTrue();
        dinarTax.ShouldBe(new Money(1m, ContractTestValues.Bhd));
        dinarTax.IsInWholeMinorUnits.ShouldBeTrue();
    }

    /// <summary>
    /// A credit note is taxed exactly like the invoice it reverses, on the other side.
    /// </summary>
    [Fact]
    public void A_negative_amount_is_taxed_symmetrically()
    {
        TaxRate rate = ContractTestValues.Rate(15m);

        Money charge = rate.ApplyTo(ContractTestValues.Nz(19.99m), ContractTestValues.TwoPlacesAwayFromZero);
        Money credit = rate.ApplyTo(ContractTestValues.Nz(-19.99m), ContractTestValues.TwoPlacesAwayFromZero);

        credit.ShouldBe(-charge);
    }

    /// <summary>
    /// The case that separates the integer implementation from the obvious one, pinned as an example
    /// because the property tests do not reach it by chance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0.0199999999999999999999999999 NZD at 25 percent is exactly 0.004999999999999999999999999975,
    /// which rounds to nothing. Written the obvious way — <c>amount * (percent / 100m)</c> — the
    /// product needs thirty decimal places and <c>decimal</c> has twenty-eight, so it rounds
    /// <b>silently</b> to 0.0050000000000000000000000000 and the cent that was not there rounds up
    /// into existence.
    /// </para>
    /// <para>
    /// One cent, on one line, from an amount nobody would call unusual — an allocation intermediate,
    /// a converted price. <see cref="TaxRate.ApplyTo"/> never multiplies two decimals, so the
    /// twenty-eight-digit limit is not on its path at all. This is the same failure
    /// <c>Money.Allocate</c> was rewritten in integers to close (ADR-0021 §6).
    /// </para>
    /// </remarks>
    [Fact]
    public void A_product_too_precise_for_decimal_does_not_round_a_cent_into_existence()
    {
        Money amount = ContractTestValues.Nz(0.0199999999999999999999999999m);
        TaxRate rate = ContractTestValues.Rate(25m);

        decimal writtenTheObviousWay = amount.Amount * (rate.AsPercentage.AsPercent / 100m);
        writtenTheObviousWay.ShouldBe(
            0.0050000000000000000000000000m,
            "if this stops being true, decimal has changed and the case below no longer separates " +
            "the two implementations");

        rate.ApplyTo(amount, ContractTestValues.TwoPlacesAwayFromZero)
            .ShouldBe(ContractTestValues.Nz(0.00m));
    }

    /// <summary>
    /// An amount carrying more decimal places than its currency has minor units is perfectly
    /// ordinary — a unit price, a line net before rounding — and the tax on it is still payable.
    /// </summary>
    [Fact]
    public void An_amount_more_precise_than_its_currency_still_yields_payable_tax()
    {
        Money tax = ContractTestValues.Rate(15m)
            .ApplyTo(ContractTestValues.Nz(12.3456789m), ContractTestValues.TwoPlacesAwayFromZero);

        tax.IsInWholeMinorUnits.ShouldBeTrue();
        tax.ShouldBe(ContractTestValues.Nz(1.85m));
    }
}
