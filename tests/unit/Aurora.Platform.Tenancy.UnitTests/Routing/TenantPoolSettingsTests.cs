using System;
using System.Collections.Generic;
using Aurora.Platform.Tenancy.Routing;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// The pool-settings table of ADR-0007 §5.2, asserted from the connection string the resolver
/// actually composes — parsed back by Npgsql's own builder, the code that will read it in
/// production — never from the constants that produced it.
/// </summary>
/// <remarks>
/// A test that read <c>TenantPoolSettings.Web.MaximumPoolSize</c> and compared it with 10 would
/// prove only that the source file says 10. What matters is that the number reaches the string a
/// data source is built from, under the key Npgsql reads, for the host it was meant for.
/// </remarks>
public sealed class TenantPoolSettingsTests
{
    /// <summary>The rows of the §5.2 table, so the count below is the table's and not this file's.</summary>
    private const int SettingsInTheTable = 8;

    private const string Password = "unit-test-placeholder";

    private readonly ITestOutputHelper _output;

    public TenantPoolSettingsTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(TenantPoolProfile.Web, 10, 30, "aurora-web")]
    [InlineData(TenantPoolProfile.Worker, 5, 300, "aurora-worker")]
    public void Every_row_of_the_5_2_table_is_read_back_out_of_the_composed_connection_string(
        TenantPoolProfile profile,
        int maximumPoolSize,
        int commandTimeoutSeconds,
        string applicationNamePrefix)
    {
        string composed = Compose(profile, "acme-trading");

        var parsed = new NpgsqlConnectionStringBuilder(composed);

        int asserted = 0;
        Assert(ref asserted, "Minimum Pool Size", parsed.MinPoolSize, 0);
        Assert(ref asserted, "Maximum Pool Size", parsed.MaxPoolSize, maximumPoolSize);
        Assert(ref asserted, "Connection Idle Lifetime", parsed.ConnectionIdleLifetime, 30);
        Assert(ref asserted, "Connection Pruning Interval", parsed.ConnectionPruningInterval, 5);
        Assert(ref asserted, "Max Auto Prepare", parsed.MaxAutoPrepare, 0);
        Assert(ref asserted, "Timeout", parsed.Timeout, 5);
        Assert(ref asserted, "Command Timeout", parsed.CommandTimeout, commandTimeoutSeconds);
        Assert(ref asserted, "Application Name", parsed.ApplicationName, $"{applicationNamePrefix}:acme-trading");

        _output.WriteLine($"{profile}: {asserted} of {SettingsInTheTable} §5.2 settings read back from the composed string");
        asserted.ShouldBe(SettingsInTheTable);
    }

    [Fact]
    public void Where_the_table_says_a_value_differs_from_the_Npgsql_default_the_composed_string_actually_overrides_it()
    {
        // §5.2 gives each row a "Default" column. A composer that set nothing would still satisfy
        // the rows whose value equals the default (Minimum Pool Size 0, Max Auto Prepare 0), so this
        // test pins the rows that do not: each must differ from a freshly constructed builder.
        var npgsqlDefaults = new NpgsqlConnectionStringBuilder();
        var web = new NpgsqlConnectionStringBuilder(Compose(TenantPoolProfile.Web, "acme-trading"));
        var worker = new NpgsqlConnectionStringBuilder(Compose(TenantPoolProfile.Worker, "acme-trading"));

        List<string> overridden = [];
        Overrides(overridden, "Maximum Pool Size (Web)", web.MaxPoolSize, npgsqlDefaults.MaxPoolSize);
        Overrides(overridden, "Maximum Pool Size (Worker)", worker.MaxPoolSize, npgsqlDefaults.MaxPoolSize);
        Overrides(overridden, "Connection Idle Lifetime", web.ConnectionIdleLifetime, npgsqlDefaults.ConnectionIdleLifetime);
        Overrides(overridden, "Connection Pruning Interval", web.ConnectionPruningInterval, npgsqlDefaults.ConnectionPruningInterval);
        Overrides(overridden, "Timeout", web.Timeout, npgsqlDefaults.Timeout);
        Overrides(overridden, "Command Timeout (Worker)", worker.CommandTimeout, npgsqlDefaults.CommandTimeout);
        Overrides(overridden, "Application Name", web.ApplicationName, npgsqlDefaults.ApplicationName);

        _output.WriteLine($"{overridden.Count} setting(s) proven to override the Npgsql default: {string.Join(", ", overridden)}");
        overridden.Count.ShouldBe(7);

        // And the two rows whose value is the default are stated, not assumed, by the row above.
        npgsqlDefaults.MinPoolSize.ShouldBe(0);
        npgsqlDefaults.MaxAutoPrepare.ShouldBe(0);
    }

    [Fact]
    public void The_endpoint_the_database_the_app_role_and_the_credential_reach_the_string_too()
    {
        ClusterEndpoint cluster = new ARouting().OnCluster("nz-1", "pg-nz-1.internal", 5433).BuildCluster();

        string composed = TenantConnectionStringComposer.Compose(cluster, "aurora_t_acme_trading", "acme-trading", Password, TenantPoolSettings.Web);

        var parsed = new NpgsqlConnectionStringBuilder(composed);
        parsed.Host.ShouldBe("pg-nz-1.internal");
        parsed.Port.ShouldBe(5433);
        parsed.Database.ShouldBe("aurora_t_acme_trading");
        parsed.Username.ShouldBe("aurora_app");
        parsed.Password.ShouldBe(Password);
        parsed.Pooling.ShouldBeTrue();
    }

    [Fact]
    public void A_credential_with_connection_string_metacharacters_survives_the_round_trip()
    {
        // The builder quotes what needs quoting; string formatting would have produced a string
        // that parses to a different password, or does not parse at all.
        const string awkward = "p;a=s s'w\"o{r}d";
        ClusterEndpoint cluster = new ARouting().BuildCluster();

        string composed = TenantConnectionStringComposer.Compose(cluster, "aurora_t_acme", "acme", awkward, TenantPoolSettings.Web);

        new NpgsqlConnectionStringBuilder(composed).Password.ShouldBe(awkward);
    }

    [Fact]
    public void A_profile_that_is_not_one_of_the_two_hosts_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TenantPoolSettings.For((TenantPoolProfile)42));
    }

    private static string Compose(TenantPoolProfile profile, string tenantKey) =>
        TenantConnectionStringComposer.Compose(
            new ARouting().WithKey(tenantKey).BuildCluster(),
            "aurora_t_" + tenantKey.Replace('-', '_'),
            tenantKey,
            Password,
            TenantPoolSettings.For(profile));

    private void Assert<T>(ref int asserted, string setting, T actual, T expected)
    {
        actual.ShouldBe(expected, $"§5.2 row '{setting}'");
        asserted++;
        _output.WriteLine($"  {setting,-28} = {actual}");
    }

    private static void Overrides<T>(List<string> overridden, string setting, T composed, T npgsqlDefault)
    {
        composed.ShouldNotBe(npgsqlDefault, $"§5.2 row '{setting}' is meant to differ from the Npgsql default");
        overridden.Add(setting);
    }
}
