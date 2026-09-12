using System;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Aurora.SharedKernel;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// <c>ITenantConnectionResolver</c> over a counted in-memory catalog and a real <c>HybridCache</c>:
/// what it resolves, what it refuses, and when it reads the catalog.
/// </summary>
/// <remarks>
/// Every cache assertion here is a count of reads that reached the reader. The chain from the
/// assertion to the behaviour is one link long: the resolver has no other way to learn where a
/// tenant lives, so "reads did not increase" means "served from the cache" and "reads increased"
/// means "the resolver went back to the catalog". The invalidation that a real tenant state change
/// drives — the suspend — is proven against PostgreSQL in the integration project; here the entry
/// is removed through the cache directly, which proves the resolver honours a removal.
/// </remarks>
public sealed class TenantConnectionResolverTests
{
    private readonly ITestOutputHelper _output;

    public TenantConnectionResolverTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task An_active_tenant_resolves_to_its_cluster_and_database_with_the_credential_from_the_secret_store()
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(
            new ARouting().WithKey("acme-trading").OnCluster("nz-1", "pg-nz-1.internal", 5432).InRegion("nz").Build());

        TenantConnection connection = await harness.ResolveAsync(row);

        connection.ClusterId.ShouldBe(ClusterId.Parse("nz-1", null));
        connection.DatabaseName.ShouldBe("aurora_t_acme_trading");
        connection.ResidencyRegion.ShouldBe(Region.Parse("nz", null));

        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString.Reveal());
        parsed.Host.ShouldBe("pg-nz-1.internal");
        parsed.Port.ShouldBe(5432);
        parsed.Database.ShouldBe("aurora_t_acme_trading");
        parsed.Username.ShouldBe("aurora_app");
        parsed.Password.ShouldBe("harness-app-secret");
        parsed.ApplicationName.ShouldBe("aurora-web:acme-trading");
    }

    [Fact]
    public async Task The_worker_profile_reaches_the_resolved_string()
    {
        using var harness = new ResolverHarness(TenantPoolProfile.Worker);
        TenantRouting row = harness.Reader.Holding(new ARouting().WithKey("acme-trading").Build());

        TenantConnection connection = await harness.ResolveAsync(row);

        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString.Reveal());
        parsed.MaxPoolSize.ShouldBe(5);
        parsed.CommandTimeout.ShouldBe(300);
        parsed.ApplicationName.ShouldBe("aurora-worker:acme-trading");
    }

    [Fact]
    public async Task A_suspended_tenant_still_resolves_because_suspension_is_read_only_not_unreachable()
    {
        // ADR-0007 §11.4: suspended is readable behind a banner; the application still connects.
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().InState(TenantState.Suspended).Build());

        TenantConnection connection = await harness.ResolveAsync(row);

        connection.DatabaseName.ShouldBe(row.DatabaseName);
    }

    [Theory]
    [InlineData(TenantState.Provisioning)]
    [InlineData(TenantState.ProvisioningFailed)]
    [InlineData(TenantState.SchemaBlocked)]
    [InlineData(TenantState.Exporting)]
    [InlineData(TenantState.PendingDeletion)]
    public async Task A_tenant_in_any_other_state_is_refused_with_its_state_named(TenantState state)
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().InState(state).Build());

        TenantNotRoutableException refused = await Should.ThrowAsync<TenantNotRoutableException>(() => harness.ResolveAsync(row));

        refused.TenantId.ShouldBe(TenantId.From(row.TenantId));
        refused.State.ShouldBe(state);
        harness.Secrets.Reads.ShouldBe(0, "a refused tenant never reaches the secret store");
    }

    [Fact]
    public async Task A_tombstone_is_refused_as_Deleted_rather_than_failing_on_its_missing_routing_columns()
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().Tombstoned().Build());

        TenantNotRoutableException refused = await Should.ThrowAsync<TenantNotRoutableException>(() => harness.ResolveAsync(row));

        refused.State.ShouldBe(TenantState.Deleted);
    }

    [Fact]
    public void The_routable_states_are_exactly_Active_and_Suspended_out_of_all_eight()
    {
        TenantState[] lifecycle = Enum.GetValues<TenantState>();
        lifecycle.Length.ShouldBe(8, "ADR-0007 §9.2 names eight states; a ninth must be decided here, not defaulted");

        TenantState[] routable = [.. lifecycle.Where(TenantConnectionResolver.IsRoutable)];

        _output.WriteLine($"{routable.Length} of {lifecycle.Length} states route on the application path: {string.Join(", ", routable)}");
        routable.ShouldBe([TenantState.Active, TenantState.Suspended]);
    }

    [Fact]
    public async Task An_unknown_tenant_throws_and_the_miss_is_not_cached()
    {
        using var harness = new ResolverHarness();
        TenantId unknown = TenantId.Create();

        await Should.ThrowAsync<TenantNotFoundException>(() => harness.ResolveAsync(unknown));
        await Should.ThrowAsync<TenantNotFoundException>(() => harness.ResolveAsync(unknown));

        harness.Reader.ReadsOf(unknown).ShouldBe(2, "a negative result must not be cached, or a tenant provisioned a moment later stays invisible");
    }

    [Fact]
    public async Task An_unassigned_tenant_id_is_refused_before_the_catalog_is_read()
    {
        using var harness = new ResolverHarness();

        await Should.ThrowAsync<ArgumentException>(() => harness.ResolveAsync(default(TenantId)));

        harness.Reader.TotalReads.ShouldBe(0);
    }

    [Fact]
    public async Task The_second_resolve_of_the_same_tenant_is_served_from_the_cache()
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().Build());

        await harness.ResolveAsync(row);
        int afterFirst = harness.Reader.ReadsOf(row);
        await harness.ResolveAsync(row);
        int afterSecond = harness.Reader.ReadsOf(row);

        _output.WriteLine($"catalog reads: after 1st resolve = {afterFirst}, after 2nd resolve = {afterSecond}");
        afterFirst.ShouldBe(1);
        afterSecond.ShouldBe(1, "the second resolve within 60 s must not reach the catalog");
    }

    [Fact]
    public async Task Removing_the_entry_makes_the_next_resolve_read_the_catalog_again()
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().Build());

        await harness.ResolveAsync(row);
        await harness.ResolveAsync(row);
        int beforeRemoval = harness.Reader.ReadsOf(row);

        await harness.Cache.InvalidateAsync(TenantId.From(row.TenantId), default);
        await harness.ResolveAsync(row);
        int afterRemoval = harness.Reader.ReadsOf(row);

        _output.WriteLine($"catalog reads: hit = {beforeRemoval}, after removal = {afterRemoval}");
        beforeRemoval.ShouldBe(1);
        afterRemoval.ShouldBe(2, "after the entry is removed the resolver must re-read the catalog");
    }

    [Fact]
    public async Task The_entry_expires_after_sixty_seconds_and_not_a_moment_before()
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().Build());

        await harness.ResolveAsync(row);
        harness.Clock.Advance(TimeSpan.FromSeconds(59));
        await harness.ResolveAsync(row);
        int atFiftyNine = harness.Reader.ReadsOf(row);

        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        await harness.ResolveAsync(row);
        int atSixtyOne = harness.Reader.ReadsOf(row);

        _output.WriteLine($"catalog reads: at +59 s = {atFiftyNine}, at +61 s = {atSixtyOne}");
        atFiftyNine.ShouldBe(1, "at 59 s the entry is still live (ADR-0012 §3: 60 s)");
        atSixtyOne.ShouldBe(2, "at 61 s the entry has expired and the catalog is read again");
    }

    [Fact]
    public async Task Two_tenants_never_share_an_entry()
    {
        // ADR-0012's stated danger: a cache is a place tenant data can leak without a database being
        // involved. Two tenants resolved back to back must each cost one read and get their own row.
        using var harness = new ResolverHarness();
        TenantRouting acme = harness.Reader.Holding(new ARouting().WithKey("acme-trading").Build());
        TenantRouting borealis = harness.Reader.Holding(new ARouting().WithKey("borealis-parts").Build());

        TenantConnection first = await harness.ResolveAsync(acme);
        TenantConnection second = await harness.ResolveAsync(borealis);
        TenantConnection firstAgain = await harness.ResolveAsync(acme);

        first.DatabaseName.ShouldBe("aurora_t_acme_trading");
        second.DatabaseName.ShouldBe("aurora_t_borealis_parts");
        firstAgain.DatabaseName.ShouldBe("aurora_t_acme_trading");
        harness.Reader.ReadsOf(acme).ShouldBe(1);
        harness.Reader.ReadsOf(borealis).ShouldBe(1);
    }

    [Fact]
    public async Task The_credential_is_read_on_every_resolve_and_a_rotated_secret_takes_effect_without_a_catalog_read()
    {
        // The cache holds the secret's reference, never the secret (ADR-0011, ADR-0012 rule 4).
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().Build());

        TenantConnection before = await harness.ResolveAsync(row);
        harness.Secrets.Holding(ARouting.DefaultAppSecretRef, "rotated-app-secret");
        TenantConnection after = await harness.ResolveAsync(row);

        new NpgsqlConnectionStringBuilder(before.ConnectionString.Reveal()).Password.ShouldBe("harness-app-secret");
        new NpgsqlConnectionStringBuilder(after.ConnectionString.Reveal()).Password.ShouldBe("rotated-app-secret");
        harness.Secrets.Reads.ShouldBe(2, "one secret read per resolve");
        harness.Reader.ReadsOf(row).ShouldBe(1, "the rotation needed no catalog read");
    }

    [Fact]
    public async Task A_missing_secret_fails_the_resolve_naming_the_reference_and_never_a_value()
    {
        using var harness = new ResolverHarness();
        TenantRouting row = harness.Reader.Holding(new ARouting().WithAppSecretRef("env:AURORA_UNSET_FOR_THIS_TEST").Build());

        Aurora.Platform.Tenancy.Secrets.SecretUnavailableException failed =
            await Should.ThrowAsync<Aurora.Platform.Tenancy.Secrets.SecretUnavailableException>(() => harness.ResolveAsync(row));

        failed.Message.ShouldContain("env:AURORA_UNSET_FOR_THIS_TEST");
        failed.Message.ShouldNotContain("harness-app-secret");
    }

    [Fact]
    public async Task A_reader_that_answers_another_tenants_row_is_refused_and_the_row_is_never_cached()
    {
        // PR #14 M-1, as the reviewer executed it: a row belonging to a different tenant was composed
        // into a credentialed connection to aurora_t_victim_corp under application_name
        // aurora-web:victim-corp. The cache's read-through now refuses a row whose TenantId is not
        // the one asked for, before anything is stored or composed.
        using var harness = new ResolverHarness();
        TenantRouting victim = new ARouting().WithKey("victim-corp").Build();
        TenantId attacker = TenantId.Create();
        harness.Reader.Answering(attacker, victim);

        TenantRoutingMismatchException refused = await Should.ThrowAsync<TenantRoutingMismatchException>(() => harness.ResolveAsync(attacker));
        await Should.ThrowAsync<TenantRoutingMismatchException>(() => harness.ResolveAsync(attacker));

        refused.Requested.ShouldBe(attacker);
        refused.Found.ShouldBe(victim.TenantId);
        refused.DatabaseName.ShouldBe("aurora_t_victim_corp");
        harness.Reader.ReadsOf(attacker).ShouldBe(2, "each attempt reads through again");
        // The read count alone cannot tell "never stored" from "stored, then dropped by the hand-out
        // check" (PR #14 n-1); the write count can. A refused read-through stores nothing.
        harness.CacheWrites.ShouldBe(0, "a read-through refused at the factory stores nothing");
        harness.Secrets.Reads.ShouldBe(0, "no credential is fetched for a row that is not the tenant's");
    }

    [Fact]
    public async Task A_poisoned_cache_entry_is_refused_dropped_and_read_through_again()
    {
        // The other place a wrong row can come from: the cache itself - a mis-keyed entry, or an
        // L2 backend (ADR-0012) handing back what someone else wrote. The victim's key holds the
        // attacker's row; the resolve refuses it, drops it, and the next resolve reads the catalog.
        using var harness = new ResolverHarness();
        TenantRouting victim = harness.Reader.Holding(new ARouting().WithKey("victim-corp").Build());
        TenantRouting attackerRow = new ARouting().WithKey("attacker-ltd").Build();
        await harness.HybridCache.SetAsync(TenantRoutingCache.KeyFor(TenantId.From(victim.TenantId)), attackerRow);

        TenantRoutingMismatchException refused = await Should.ThrowAsync<TenantRoutingMismatchException>(() => harness.ResolveAsync(victim));
        TenantConnection recovered = await harness.ResolveAsync(victim);

        refused.Found.ShouldBe(attackerRow.TenantId);
        recovered.DatabaseName.ShouldBe("aurora_t_victim_corp");
        harness.Reader.ReadsOf(victim).ShouldBe(1, "the poisoned entry was dropped, so the next resolve read the catalog once");
        harness.CacheWrites.ShouldBe(2, "the planted row, and the one legitimate read-through after it was dropped");
    }
}
