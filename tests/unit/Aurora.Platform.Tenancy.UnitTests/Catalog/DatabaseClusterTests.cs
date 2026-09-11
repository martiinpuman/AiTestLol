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
    public void A_host_that_is_not_a_bare_host_name_is_refused(string host)
    {
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
