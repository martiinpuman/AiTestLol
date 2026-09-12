using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class ResultTests
{
    private static readonly Error PeriodClosed =
        Error.Conflict("ledger.period.closed", "The accounting period is closed.");

    [Fact]
    public void A_success_is_a_success_and_has_no_error_to_read()
    {
        Result result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void A_failure_carries_the_reason_it_failed()
    {
        Result result = Result.Failure(PeriodClosed);

        result.IsFailure.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(PeriodClosed);
    }

    [Fact]
    public void A_failure_without_a_reason_cannot_be_built()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void A_result_nobody_set_is_a_failure_rather_than_a_success()
    {
        Result unassigned = default;

        unassigned.IsSuccess.ShouldBeFalse();
        unassigned.IsFailure.ShouldBeTrue();
        unassigned.Error.ShouldBe(Error.Unassigned);
    }

    [Fact]
    public void An_error_reads_as_the_failed_outcome_it_is()
    {
        Result result = PeriodClosed;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(PeriodClosed);
    }

    [Fact]
    public void ToString_says_which_way_it_went()
    {
        Result.Success().ToString().ShouldBe("Success");
        Result.Failure(PeriodClosed).ToString().ShouldContain(PeriodClosed.Code);
    }
}

public sealed class ResultOfValueTests
{
    private static readonly Error NotFound =
        Error.NotFound("sales.invoice.not_found", "No invoice with that id.");

    [Fact]
    public void A_success_carries_the_value_it_produced()
    {
        Result<string> result = Result.Success("INV-0001");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("INV-0001");
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void A_failure_has_no_value_to_read_and_says_why_when_asked()
    {
        Result<string> result = Result.Failure<string>(NotFound);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(NotFound);

        InvalidOperationException thrown =
            Should.Throw<InvalidOperationException>(() => result.Value);
        thrown.Message.ShouldContain(NotFound.Code);
    }

    [Fact]
    public void A_success_that_produced_nothing_is_refused_because_that_is_a_plain_Result()
    {
        Should.Throw<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Fact]
    public void A_failure_without_a_reason_cannot_be_built()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure<string>(null!));
    }

    [Fact]
    public void A_result_nobody_set_is_a_failure_rather_than_a_success_with_a_null_value()
    {
        Result<string> unassigned = default;

        unassigned.IsSuccess.ShouldBeFalse();
        unassigned.Error.ShouldBe(Error.Unassigned);
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
    }

    [Fact]
    public void An_error_reads_as_the_failed_outcome_it_is()
    {
        Result<string> result = NotFound;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(NotFound);
    }

    [Fact]
    public void A_value_type_value_survives_the_round_trip()
    {
        Result<Money> result = Result.Success(new Money(10.00m, TestCurrencies.Nzd));

        result.Value.ShouldBe(new Money(10.00m, TestCurrencies.Nzd));
    }

    [Fact]
    public void ToString_says_which_way_it_went()
    {
        Result.Success("INV-0001").ToString().ShouldBe("Success(INV-0001)");
        Result.Failure<string>(NotFound).ToString().ShouldContain(NotFound.Code);
    }
}
