using System;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// What the ADR-0007 §4.3 mis-route failure carries: enough for the alert and the incident
/// timeline, in the exception itself rather than only in its text.
/// </summary>
public sealed class TenantRoutingViolationExceptionTests
{
    private static readonly TenantId Expected = TenantId.Create();
    private static readonly TenantId Found = TenantId.Create();

    [Fact]
    public void A_mismatch_names_the_tenant_expected_the_tenant_found_and_the_database_reached()
    {
        TenantRoutingViolationException violation =
            TenantRoutingViolationException.Mismatch(Expected, Found, "aurora_t_acme");

        violation.Expected.ShouldBe(Expected);
        violation.Found.ShouldBe(Found);
        violation.DatabaseName.ShouldBe("aurora_t_acme");
        violation.Message.ShouldContain(Expected.ToString());
        violation.Message.ShouldContain(Found.ToString());
        violation.Message.ShouldContain("aurora_t_acme");
    }

    [Fact]
    public void An_unstamped_database_names_no_found_tenant_and_says_why_it_could_not_be_proven()
    {
        TenantRoutingViolationException violation =
            TenantRoutingViolationException.Unstamped(Expected, "aurora_t_acme", "platform.tenant_identity holds no row");

        violation.Expected.ShouldBe(Expected);
        violation.Found.ShouldBeNull();
        violation.DatabaseName.ShouldBe("aurora_t_acme");
        violation.Message.ShouldContain(Expected.ToString());
        violation.Message.ShouldContain("aurora_t_acme");
        violation.Message.ShouldContain("holds no row");
    }

    [Fact]
    public void A_mismatch_between_a_tenant_and_itself_cannot_be_built()
    {
        // The factory is for the case the check caught; the same id on both sides is not a
        // mismatch, and an exception saying it was would send an operator after a routing fault
        // that does not exist.
        Should.Throw<ArgumentException>(
            () => TenantRoutingViolationException.Mismatch(Expected, Expected, "aurora_t_acme"));
    }

    [Fact]
    public void An_unassigned_expected_tenant_is_refused()
    {
        Should.Throw<ArgumentException>(() => TenantRoutingViolationException.Mismatch(default, Found, "aurora_t_acme"));
        Should.Throw<ArgumentException>(() => TenantRoutingViolationException.Unstamped(default, "aurora_t_acme", "why"));
    }
}
