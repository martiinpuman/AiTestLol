using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class UnitOfMeasureTests
{
    [Fact]
    public void A_unit_of_measure_is_a_UN_ECE_Recommendation_20_code()
    {
        UnitOfMeasure.Of("KGM").Code.ShouldBe("KGM");
    }

    [Fact]
    public void A_code_is_normalized_to_upper_case_so_that_casing_never_splits_a_unit()
    {
        UnitOfMeasure.Of("kgm").Code.ShouldBe("KGM");
        UnitOfMeasure.Of("kgm").ShouldBe(UnitOfMeasure.Of("KGM"));
    }

    [Fact]
    public void Units_with_the_same_code_are_equal()
    {
        UnitOfMeasure.Of("KGM").ShouldBe(UnitOfMeasure.Of("KGM"));
        UnitOfMeasure.Of("KGM").GetHashCode().ShouldBe(UnitOfMeasure.Of("KGM").GetHashCode());
    }

    [Fact]
    public void Units_with_different_codes_are_not_equal()
    {
        UnitOfMeasure.Of("KGM").ShouldNotBe(UnitOfMeasure.Of("HUR"));
    }

    [Theory]
    [InlineData("H87")]
    [InlineData("4G")]
    [InlineData("C")]
    public void A_code_of_one_to_three_letters_or_digits_is_accepted(string code)
    {
        UnitOfMeasure.Of(code).Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("KGMS")]
    [InlineData("KG-")]
    [InlineData(" KG")]
    [InlineData("KG ")]
    [InlineData("KİL")]
    public void A_code_that_is_not_one_to_three_alphanumerics_is_rejected(string code)
    {
        Should.Throw<ArgumentException>(() => UnitOfMeasure.Of(code));
    }

    [Fact]
    public void A_null_code_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => UnitOfMeasure.Of(null!));
    }

    [Fact]
    public void The_default_value_is_not_a_specified_unit()
    {
        default(UnitOfMeasure).IsSpecified.ShouldBeFalse();
        UnitOfMeasure.Of("KGM").IsSpecified.ShouldBeTrue();
    }

    [Fact]
    public void Reading_the_code_of_an_unspecified_unit_throws_rather_than_returning_a_blank()
    {
        Should.Throw<InvalidOperationException>(() => default(UnitOfMeasure).Code);
    }

    [Fact]
    public void ToString_is_the_code_and_never_throws()
    {
        UnitOfMeasure.Of("KGM").ToString().ShouldBe("KGM");
        default(UnitOfMeasure).ToString().ShouldNotBeNull();
    }
}
