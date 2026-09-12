using System;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests;

public sealed class ClusterIdTests
{
    [Theory]
    [InlineData("nz")]
    [InlineData("nz-1")]
    [InlineData("eu-west-2b")]
    public void A_well_formed_id_parses_and_keeps_its_text(string text)
    {
        ClusterId id = ClusterId.Parse(text, null);

        id.IsSpecified.ShouldBeTrue();
        id.Value.ShouldBe(text);
        id.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("NZ-1")]
    [InlineData("nz_1")]
    [InlineData("1nz")]
    [InlineData("nz-")]
    [InlineData("nz--1")]
    [InlineData("")]
    public void A_malformed_id_is_refused(string text)
    {
        ClusterId.TryParse(text, null, out ClusterId result).ShouldBeFalse();
        result.IsSpecified.ShouldBeFalse();
        Should.Throw<FormatException>(() => ClusterId.Parse(text, null));
    }

    [Fact]
    public void Null_text_is_an_argument_error_not_a_format_error()
    {
        Should.Throw<ArgumentNullException>(() => ClusterId.Parse(null!, null));
        ClusterId.TryParse(null, null, out _).ShouldBeFalse();
    }

    [Fact]
    public void An_id_nobody_assigned_says_so_rather_than_passing_for_a_real_one()
    {
        ClusterId unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        unassigned.ToString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Two_ids_with_the_same_text_are_the_same_cluster()
    {
        ClusterId.Parse("nz-1", null).ShouldBe(ClusterId.Parse("nz-1", null));
        ClusterId.Parse("nz-1", null).ShouldNotBe(ClusterId.Parse("nz-2", null));
    }
}
