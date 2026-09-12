using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>ADR-0007 §11.4, first step: an active tenant becomes suspended, and only an active one can.</summary>
public sealed class TenantSuspensionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_active_tenant_becomes_suspended_at_the_given_instant()
    {
        Tenant tenant = Active();

        tenant.Suspend(Now);

        tenant.State.ShouldBe(TenantState.Suspended);
        tenant.SuspendedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_tenant_that_is_not_active_cannot_be_suspended()
    {
        Tenant provisioning = Reserved();

        Should.Throw<InvalidOperationException>(() => provisioning.Suspend(Now));

        provisioning.State.ShouldBe(TenantState.Provisioning);
        provisioning.SuspendedAt.ShouldBeNull();
    }

    [Fact]
    public void Suspending_twice_is_refused_because_the_second_call_finds_no_active_tenant()
    {
        Tenant tenant = Active();
        tenant.Suspend(Now);

        Should.Throw<InvalidOperationException>(() => tenant.Suspend(Now.AddDays(1)));

        tenant.SuspendedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_non_UTC_instant_is_refused_before_anything_changes()
    {
        Tenant tenant = Active();

        Should.Throw<ArgumentException>(() => tenant.Suspend(new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(12))));

        tenant.State.ShouldBe(TenantState.Active);
    }

    private static Tenant Reserved() =>
        Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme-trading", null), "Acme Trading Ltd", new ACluster().Build(), "standard", Now);

    private static Tenant Active()
    {
        Tenant tenant = Reserved();
        tenant.Activate(1, Now);
        return tenant;
    }
}
