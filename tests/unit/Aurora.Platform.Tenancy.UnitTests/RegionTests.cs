using System;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests;

public sealed class RegionTests
{
    [Theory]
    [InlineData("nz")]
    [InlineData("eu-west")]
    [InlineData("ap-southeast-2")]
    public void A_well_formed_region_parses_and_keeps_its_text(string text)
    {
        Region region = Region.Parse(text, null);

        region.IsSpecified.ShouldBeTrue();
        region.Value.ShouldBe(text);
        region.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("EU-WEST")]
    [InlineData("eu_west")]
    [InlineData("eu-west-")]
    [InlineData("")]
    [InlineData("a23456789012345678901234567890123")]
    public void A_malformed_region_is_refused(string text)
    {
        Region.TryParse(text, null, out Region result).ShouldBeFalse();
        result.IsSpecified.ShouldBeFalse();
        Should.Throw<FormatException>(() => Region.Parse(text, null));
    }

    [Fact]
    public void Null_text_is_an_argument_error_not_a_format_error()
    {
        Should.Throw<ArgumentNullException>(() => Region.Parse(null!, null));
        Region.TryParse(null, null, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_region_nobody_assigned_says_so_rather_than_passing_for_a_real_one()
    {
        Region unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        unassigned.ToString().ShouldNotBeNullOrEmpty();
    }
}
