using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

public sealed class SubscriptionTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    [Fact]
    public void A_subscription_runs_from_a_day_to_a_day_or_open_ended()
    {
        SubscriptionId id = SubscriptionId.Create();
        TenantId tenantId = TenantId.Create();

        Subscription bounded = Subscription.Start(id, tenantId, "standard", 25, Start, Start.AddYears(1));
        Subscription openEnded = Subscription.Start(SubscriptionId.Create(), tenantId, "standard", 25, Start, null);

        bounded.Id.ShouldBe(id);
        bounded.TenantId.ShouldBe(tenantId);
        bounded.Plan.ShouldBe("standard");
        bounded.Seats.ShouldBe(25);
        bounded.ValidFrom.ShouldBe(Start);
        bounded.ValidTo.ShouldBe(Start.AddYears(1));
        openEnded.ValidTo.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_subscription_with_no_seats_is_refused(int seats)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Subscription.Start(SubscriptionId.Create(), TenantId.Create(), "standard", seats, Start, null));
    }

    [Fact]
    public void A_subscription_that_ends_on_or_before_the_day_it_starts_is_refused()
    {
        Should.Throw<ArgumentException>(() =>
            Subscription.Start(SubscriptionId.Create(), TenantId.Create(), "standard", 1, Start, Start));
        Should.Throw<ArgumentException>(() =>
            Subscription.Start(SubscriptionId.Create(), TenantId.Create(), "standard", 1, Start, Start.AddDays(-1)));
    }

    [Fact]
    public void A_one_day_subscription_ends_the_day_after_it_starts_because_validity_is_half_open()
    {
        Subscription.Start(SubscriptionId.Create(), TenantId.Create(), "standard", 1, Start, Start.AddDays(1))
            .ValidTo.ShouldBe(Start.AddDays(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" standard")]
    public void A_blank_or_untrimmed_plan_is_refused(string plan)
    {
        Should.Throw<ArgumentException>(() =>
            Subscription.Start(SubscriptionId.Create(), TenantId.Create(), plan, 1, Start, null));
    }

    [Fact]
    public void An_unassigned_identifier_is_refused()
    {
        Should.Throw<ArgumentException>(() => Subscription.Start(default, TenantId.Create(), "standard", 1, Start, null));
        Should.Throw<ArgumentException>(() => Subscription.Start(SubscriptionId.Create(), default, "standard", 1, Start, null));
    }
}
