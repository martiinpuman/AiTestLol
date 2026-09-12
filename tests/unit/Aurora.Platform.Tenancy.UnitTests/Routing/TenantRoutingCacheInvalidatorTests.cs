using System;
using System.Linq;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Tests;
using Aurora.Platform.Tenancy.UnitTests.Catalog;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// Which pending changes the invalidator acts on, read off a real change tracker with no database:
/// the offline catalog context tracks entities and detects changes exactly as the connected one
/// does, and only <c>SaveChanges</c> itself needs a server. The save-driven half — the interceptor
/// firing on a real save and the resolver missing afterwards — is the integration project's.
/// </summary>
public sealed class TenantRoutingCacheInvalidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_suspended_tenant_is_selected_for_invalidation()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        Tenant tenant = TrackedActive(context);

        tenant.Suspend(Now);

        TenantRoutingCacheInvalidator.TenantsWhoseRoutingChanged(context.ChangeTracker).ShouldBe([tenant.Id]);
    }

    [Fact]
    public void A_tenant_whose_only_change_is_its_activity_stamp_is_left_alone()
    {
        // last_activity_at moves at most once a minute per tenant (ADR-0007 §10.1); invalidating
        // on it would empty the cache on the very requests it exists to serve.
        using CatalogDbContext context = OfflineCatalog.Open();
        Tenant tenant = TrackedActive(context);

        tenant.RecordActivity(Now);

        context.ChangeTracker.Entries<Tenant>().Single().Property(nameof(Tenant.LastActivityAt)).IsModified.ShouldBeTrue("the change is tracked");
        TenantRoutingCacheInvalidator.TenantsWhoseRoutingChanged(context.ChangeTracker).ShouldBeEmpty();
    }

    [Fact]
    public void An_untouched_tenant_is_left_alone()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        TrackedActive(context);

        TenantRoutingCacheInvalidator.TenantsWhoseRoutingChanged(context.ChangeTracker).ShouldBeEmpty();
    }

    [Fact]
    public void A_newly_added_tenant_is_selected_too()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        Tenant tenant = Tenant.Reserve(TenantId.Create(), TenantKey.Parse("acme-trading", null), "Acme Trading Ltd", new ACluster().Build(), "standard", Now);

        context.Tenants.Add(tenant);

        TenantRoutingCacheInvalidator.TenantsWhoseRoutingChanged(context.ChangeTracker).ShouldBe([tenant.Id]);
    }

    [Fact]
    public void Only_the_changed_tenant_is_selected_when_several_are_tracked()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        Tenant suspended = TrackedActive(context);
        Tenant untouched = TrackedActive(context);
        Tenant active = TrackedActive(context);
        active.RecordActivity(Now);

        suspended.Suspend(Now);

        TenantRoutingCacheInvalidator.TenantsWhoseRoutingChanged(context.ChangeTracker).ShouldBe([suspended.Id]);
        untouched.State.ShouldBe(TenantState.Active);
    }

    [Fact]
    public void The_watched_properties_are_the_state_and_every_routing_column_of_the_tenant_row()
    {
        // The routing columns are those the privilege record already names as the ones a tenant is
        // resolved by (CatalogSchemaAllowlist.RoutingColumns), plus the state that gates routing.
        // Read from the record rather than repeated, so the two lists cannot drift apart.
        string[] routingColumns = [.. CatalogSchemaAllowlist.RoutingColumns["tenant"].OrderBy(column => column, StringComparer.Ordinal)];
        string[] watchedAsColumns = [.. TenantRoutingCacheInvalidator.RoutingProperties
            .Where(property => property != nameof(Tenant.State))
            .Select(ColumnOf)
            .OrderBy(column => column, StringComparer.Ordinal)];

        TenantRoutingCacheInvalidator.RoutingProperties.ShouldContain(nameof(Tenant.State));
        watchedAsColumns.ShouldBe(routingColumns);
        TenantRoutingCacheInvalidator.RoutingProperties.Count.ShouldBe(routingColumns.Length + 1);
    }

    private static Tenant TrackedActive(CatalogDbContext context)
    {
        Tenant tenant = Tenant.Reserve(TenantId.Create(), TenantKey.Parse("t-" + Guid.NewGuid().ToString("N")[..8], null), "Acme Trading Ltd", new ACluster().Build(), "standard", Now);
        tenant.Activate(1, Now);
        context.Attach(tenant);
        return tenant;
    }

    private static string ColumnOf(string property)
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        return context.Model.FindEntityType(typeof(Tenant))!.FindProperty(property)!.GetColumnName();
    }
}
