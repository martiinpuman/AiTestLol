using System;
using Aurora.Platform.Tenancy.Catalog;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

public sealed class DatabaseClusterTests
{
    [Fact]
    public void A_registered_cluster_is_accepting_tenants_and_remembers_where_it_is()
    {
        DatabaseCluster cluster = new ACluster().WithId("eu-west-1a").InRegion("eu-west").WithHost("pg-1.internal").WithPort(6432).Build();

        cluster.State.ShouldBe(DatabaseClusterState.Accepting);
        cluster.Id.Value.ShouldBe("eu-west-1a");
        cluster.Region.Value.ShouldBe("eu-west");
        cluster.Host.ShouldBe("pg-1.internal");
        cluster.Port.ShouldBe(6432);
        cluster.MaintenanceDatabase.ShouldBe("postgres");
        cluster.MaxTenants.ShouldBe(1_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_the_TCP_range_is_refused(int port)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ACluster().WithPort(port).Build());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_capacity_that_admits_no_tenant_is_refused(int maxTenants)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ACluster().WithMaxTenants(maxTenants).Build());
    }

    [Theory]
    [InlineData("")]
    [InlineData("pg 1.internal")]
    [InlineData("pg-1.internal:5432")]
    [InlineData("postgres://pg-1.internal")]
    [InlineData("Host=pg-1.internal;Database=x")]
    [InlineData("/var/run/postgresql")]
    [InlineData("pg-1.internal,pg-2.internal")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void A_value_that_is_not_one_host_name_or_IPv4_literal_is_refused(string host)
    {
        // CanonicalHost is a grammar, not a deny-list: the socket directory and the multi-host
        // list are the two shapes ADR-0036 §6 names as breaking the endpoint triple, and the IPv6
        // literal is outside the grammar until the architect decides it is a cluster endpoint.
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(host).Build());
    }

    [Theory]
    [InlineData("pg.über.internal")]
    [InlineData("pg.Über.internal")]
    [InlineData("PG-1.internal")]
    [InlineData(".pg-1.internal")]
    [InlineData("pg-1.internal.")]
    [InlineData("pg-1..internal")]
    [InlineData("-pg.internal")]
    [InlineData("pg-.internal")]
    public void A_host_with_a_second_spelling_the_lower_case_check_cannot_see_is_refused(string host)
    {
        // Two spellings of one name that ck_database_cluster_host_lower_case does not fold, or
        // folds under one collation and not another: executed on postgres:17-alpine
        // (CatalogHostGrammarTests), lower('pg.Über.internal') is unchanged under C and folds
        // under en_US.utf8, so the pair pg.über.internal / pg.Über.internal is storable twice on
        // one collation and once on the other. An internationalised name is stored as punycode.
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(host).Build());
    }

    [Theory]
    [InlineData("pg-1.internal")]
    [InlineData("xn--pg-bfa.internal")]
    [InlineData("10.0.0.5")]
    [InlineData("localhost")]
    [InlineData("a")]
    public void A_host_name_or_an_IPv4_literal_in_canonical_form_is_accepted(string host)
    {
        new ACluster().WithHost(host).Build().Host.ShouldBe(host);
    }

    [Fact]
    public void A_label_may_be_63_characters_and_a_host_253_and_neither_one_more()
    {
        string label63 = new string('a', 63);
        string host253 = string.Join('.', new string('b', 63), new string('c', 63), new string('d', 63), new string('e', 61));

        host253.Length.ShouldBe(253);
        new ACluster().WithHost(label63 + ".internal").Build().Host.ShouldBe(label63 + ".internal");
        new ACluster().WithHost(host253).Build().Host.ShouldBe(host253);
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(new string('a', 64) + ".internal").Build());
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(host253 + "d").Build());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Postgres")]
    [InlineData("my db")]
    [InlineData("1db")]
    public void A_maintenance_database_name_PostgreSQL_would_fold_or_reject_is_refused(string name)
    {
        Should.Throw<ArgumentException>(() => new ACluster().WithMaintenanceDatabase(name).Build());
    }

    [Fact]
    public void An_unassigned_secret_reference_is_refused()
    {
        Should.Throw<ArgumentException>(() => new ACluster().WithAdminSecretRef(default).Build());
    }

    [Fact]
    public void A_cluster_that_stops_accepting_keeps_serving_but_is_closed_to_placement()
    {
        DatabaseCluster cluster = new ACluster().Build();

        cluster.StopAccepting();

        cluster.State.ShouldBe(DatabaseClusterState.Closed);
    }
}
