using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.Platform.Tenancy.Tests;
using Aurora.Platform.Tenancy.UnitTests.Catalog;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// Which pending changes the invalidator acts on, read off a real change tracker with no database
/// (the offline catalog context tracks entities and detects changes exactly as the connected one
/// does; only <c>SaveChanges</c> itself needs a server), and why its watched list is the right one.
/// </summary>
/// <remarks>
/// <para>
/// The watched list's chain, link by link (PR #14 M-3): the invalidator watches
/// <c>RoutingProperties</c> → those are exactly the routing columns plus the state gate
/// (<see cref="TenantColumnClassification"/>) → routing, gate and outside-routing partition
/// <c>Columns["tenant"]</c> with a count → <c>Columns</c> is held to the EF model and the migrated
/// schema by <c>CatalogHoldsNoTenantBusinessDataTests</c> → and every fact <c>TenantRouting</c>
/// carries, the row the resolver composes from, names the watched column it is read from, with a
/// per-column proof that a change to it changes the resolve. Remove a column from any list and
/// one of these fails; before this, removing <c>key</c> from two lists in lockstep left 218 of 218
/// green and silently widened the privilege guard that reads the same list.
/// </para>
/// <para>
/// The save-driven half — the interceptor firing on a real save and the resolver missing
/// afterwards — is the integration project's.
/// </para>
/// </remarks>
public sealed class TenantRoutingCacheInvalidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;

    public TenantRoutingCacheInvalidatorTests(ITestOutputHelper output) => _output = output;

    /// <summary>The watched columns, each proven below to change the resolve; rows for the theory, and the set the coverage check compares.</summary>
    public static TheoryData<string> WatchedColumns => [.. TenantColumnClassification.Watched.OrderBy(column => column, StringComparer.Ordinal)];

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
    public void Every_column_of_the_tenant_row_is_classified_exactly_once()
    {
        // The completeness obligation RoutingColumns["tenant"] lacked: routing, the gate and
        // outside-routing must partition the row - no column in two lists, none in none - and the
        // row is Columns["tenant"], which is itself held to the model and the schema. A sixteenth
        // column fails here until it is classified.
        IReadOnlySet<string> row = CatalogSchemaAllowlist.Columns["tenant"];
        string[] routing = [.. TenantColumnClassification.Routing];
        string[] outside = [.. TenantColumnClassification.OutsideRouting];
        string[] classified = [.. routing, TenantColumnClassification.Gate, .. outside];

        _output.WriteLine(
            $"catalog.tenant: {row.Count} columns = {routing.Length} routing ({string.Join(", ", routing)}) + 1 gate "
            + $"({TenantColumnClassification.Gate}) + {outside.Length} outside routing");
        classified.Length.ShouldBe(classified.Distinct(StringComparer.Ordinal).Count(), "a column is classified once");
        classified.OrderBy(column => column, StringComparer.Ordinal)
            .ShouldBe(row.OrderBy(column => column, StringComparer.Ordinal), "every column of the row is classified, and nothing else is");
        row.Count.ShouldBe(15, "ADR-0007 §9.2's tenant row; a new column is classified here, not defaulted");
        routing.Length.ShouldBe(4);
        outside.Length.ShouldBe(10);
    }

    [Fact]
    public void The_watched_properties_are_exactly_the_routing_columns_and_the_gate()
    {
        string[] watchedAsColumns = [.. TenantRoutingCacheInvalidator.RoutingProperties.Select(ColumnOf).OrderBy(column => column, StringComparer.Ordinal)];

        watchedAsColumns.ShouldBe([.. TenantColumnClassification.Watched.OrderBy(column => column, StringComparer.Ordinal)]);
        TenantRoutingCacheInvalidator.RoutingProperties.Count.ShouldBe(TenantColumnClassification.Routing.Count + 1);
    }

    [Fact]
    public void Every_fact_TenantRouting_carries_is_read_from_a_watched_column()
    {
        // The last link: what the resolve consumes. Every property of TenantRouting - the row the
        // resolver composes from - names the tenant column it is read from, and each such column is
        // watched. TenantId is the lookup key (catalog.tenant.id, the immutable primary key): it is
        // compared by the routing cache, never composed, and is outside routing by classification.
        // Cluster is the join target, whose identity is the tenant's cluster_id.
        var readFrom = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(TenantRouting.TenantKey)] = "key",
            [nameof(TenantRouting.State)] = "state",
            [nameof(TenantRouting.Cluster)] = "cluster_id",
            [nameof(TenantRouting.DatabaseName)] = "database_name",
            [nameof(TenantRouting.ResidencyRegion)] = "residency_region",
        };
        string[] carried =
        [
            .. typeof(TenantRouting).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Where(name => name != nameof(TenantRouting.TenantId)),
        ];

        carried.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(readFrom.Keys.OrderBy(name => name, StringComparer.Ordinal), "every fact TenantRouting carries names the column it is read from");
        foreach ((string property, string column) in readFrom)
        {
            TenantColumnClassification.Watched.ShouldContain(column, $"{property} is read from {column}, so a change to {column} must evict");
        }

        _output.WriteLine($"{readFrom.Count} facts carried by TenantRouting, each read from a watched column");
        readFrom.Count.ShouldBe(5);
    }

    [Theory]
    [MemberData(nameof(WatchedColumns))]
    public async Task A_change_to_each_watched_column_changes_what_the_resolve_returns(string column)
    {
        // Why the column is watched, shown rather than listed: with everything else equal, a change
        // to it changes the resolved connection - or, for the gate, whether there is one.
        Guid tenantId = Guid.CreateVersion7();
        TenantConnection before = await ResolveAsync(new ARouting().WithTenantId(tenantId).Build());
        TenantRouting changed = Changed(column, new ARouting().WithTenantId(tenantId)).Build();

        if (column == TenantColumnClassification.Gate)
        {
            await Should.ThrowAsync<TenantNotRoutableException>(() => ResolveAsync(changed));
            return;
        }

        TenantConnection after = await ResolveAsync(changed);

        after.ShouldNotBe(before, $"a change to {column} must reach the resolved connection");
    }

    [Fact]
    public void The_per_column_proof_covers_every_watched_column()
    {
        // The theory's rows are the watched set by construction; this pins the count so that a
        // theory that ran zero rows, or the switch below refusing a column, cannot pass quietly.
        string[] proven = [.. TenantColumnClassification.Watched];

        proven.Length.ShouldBe(5);
        foreach (string column in proven)
        {
            Should.NotThrow(() => Changed(column, new ARouting()));
        }
    }

    private static ARouting Changed(string column, ARouting routing) => column switch
    {
        "key" => routing.WithKey("borealis-parts"),
        "database_name" => routing.WithDatabaseName("aurora_t_elsewhere"),
        "cluster_id" => routing.OnCluster("nz-2", ARouting.DefaultHost, ARouting.DefaultPort),
        "residency_region" => routing.InRegion("eu-west"),
        "state" => routing.InState(TenantState.SchemaBlocked),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "A watched column with no per-column proof."),
    };

    private static async Task<TenantConnection> ResolveAsync(TenantRouting row)
    {
        using var harness = new ResolverHarness();
        harness.Reader.Holding(row);
        return await harness.ResolveAsync(row);
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
