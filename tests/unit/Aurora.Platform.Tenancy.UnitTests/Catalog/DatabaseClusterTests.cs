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
    public void A_host_that_is_not_a_bare_host_name_is_refused(string host)
    {
        // The socket directory is listed on purpose: a Unix-socket path is case-sensitive, the
        // lower-case rule below would be wrong for it (ADR-0036 §3), and the resolver must never
        // be handed one from this row.
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(host).Build());
    }

    [Theory]
    [InlineData("pg.über.internal")]
    [InlineData("pg.ÜBER.internal")]
    [InlineData(".pg-1.internal")]
    [InlineData("pg-1.internal.")]
    [InlineData("pg-1..internal")]
    public void A_host_with_a_second_spelling_the_database_cannot_see_is_refused(string host)
    {
        // Two spellings of one name that ck_database_cluster_host_lower_case does not fold: a
        // non-ASCII letter, whose lower() depends on the catalog's collation, and a trailing,
        // leading or doubled dot. An internationalised name is stored in its punycode form.
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(host).Build());
    }

    [Theory]
    [InlineData("pg-1.internal")]
    [InlineData("xn--pg-bfa.internal")]
    [InlineData("10.0.0.5")]
    [InlineData("localhost")]
    public void A_host_name_or_an_IPv4_literal_in_lower_case_ASCII_is_accepted(string host)
    {
        new ACluster().WithHost(host).Build().Host.ShouldBe(host);
    }

    [Theory]
    [InlineData("PG-1.internal")]
    [InlineData("pg-1.INTERNAL")]
    [InlineData("LOCALHOST")]
    public void A_host_in_any_spelling_but_lower_case_is_refused(string host)
    {
        // A host name is case-insensitive, so two spellings of one host would be two cluster rows
        // on one endpoint that ux_database_cluster_host_port could not tell apart (ADR-0034 §3.2).
        // The row keeps the canonical spelling, as TenantHost does; the database repeats the rule.
        Should.Throw<ArgumentException>(() => new ACluster().WithHost(host).Build());
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
