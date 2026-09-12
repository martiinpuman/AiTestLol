using System;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

public sealed class TenantAccessReasonTests
{
    [Fact]
    public void The_reasons_are_exactly_the_six_ADR_0007_3_4_names_in_its_order()
    {
        // An exact list, not a floor: a seventh reason would be a new way into a tenant database
        // and belongs in an ADR before it belongs here; a missing one would leave a caller with no
        // honest value to pass.
        Enum.GetNames<TenantAccessReason>().ShouldBe(
            ["Request", "Job", "Outbox", "Provisioning", "Migration", "OperatorSupport"]);
    }

    [Fact]
    public void The_default_is_Request_which_is_why_a_constructor_never_infers_a_reason()
    {
        // An enum has a default whether anyone wants one or not. Naming it here is what stops it
        // being mistaken for an "unspecified" member later: there is none, so every scope's reason
        // is passed explicitly by the code that opens it (B-06.3).
        default(TenantAccessReason).ShouldBe(TenantAccessReason.Request);
    }
}
