using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class ErrorTests
{
    [Fact]
    public void Each_factory_says_what_sort_of_no_the_failure_is()
    {
        Error.NotFound("sales.invoice.not_found", "No invoice with that id.").Kind
            .ShouldBe(ErrorKind.NotFound);
        Error.Conflict("sales.invoice.already_posted", "The invoice is already posted.").Kind
            .ShouldBe(ErrorKind.Conflict);
        Error.NotPermitted("sales.invoice.post_denied", "The user may not post invoices.").Kind
            .ShouldBe(ErrorKind.NotPermitted);
        Error.Rejected("stock.item.insufficient", "There is not enough stock.").Kind
            .ShouldBe(ErrorKind.Rejected);
    }

    [Fact]
    public void An_error_keeps_the_code_and_the_description_it_was_given()
    {
        Error error = Error.Conflict("sales.invoice.already_posted", "The invoice is already posted.");

        error.Code.ShouldBe("sales.invoice.already_posted");
        error.Description.ShouldBe("The invoice is already posted.");
    }

    [Fact]
    public void An_error_without_a_code_is_refused_because_the_code_is_its_identity()
    {
        Should.Throw<ArgumentException>(() => Error.Conflict("", "Something went wrong."));
        Should.Throw<ArgumentNullException>(() => Error.Conflict(null!, "Something went wrong."));
    }

    [Fact]
    public void An_error_without_a_description_is_refused_because_the_log_would_be_useless()
    {
        Should.Throw<ArgumentException>(() => Error.Conflict("sales.invoice.already_posted", ""));
        Should.Throw<ArgumentNullException>(() => Error.Conflict("sales.invoice.already_posted", null!));
    }

    [Fact]
    public void Two_errors_that_say_the_same_thing_are_the_same_error()
    {
        Error first = Error.NotFound("sales.invoice.not_found", "No invoice with that id.");
        Error second = Error.NotFound("sales.invoice.not_found", "No invoice with that id.");

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void The_same_code_with_a_different_kind_is_a_different_error()
    {
        Error.NotFound("x.y", "Description.").ShouldNotBe(Error.Conflict("x.y", "Description."));
    }

    [Fact]
    public void ToString_carries_the_kind_the_code_and_the_description_into_the_log()
    {
        Error.Conflict("sales.invoice.already_posted", "The invoice is already posted.")
            .ToString()
            .ShouldBe("Conflict sales.invoice.already_posted: The invoice is already posted.");
    }

    [Fact]
    public void The_unassigned_error_names_itself_so_a_default_result_can_be_traced()
    {
        Error.Unassigned.Code.ShouldBe("kernel.result.unassigned");
        Error.Unassigned.Description.ShouldNotBeEmpty();
    }
}
