using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Platform.Tenancy.Tests;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// ADR-0036 §2.4 D1: the endpoint comparison over synthetic connection strings, one dimension
/// varied per case, so the comparator is proven able to fail — and stays able to, because nothing
/// here touches the catalog and so nothing the catalog refuses can disarm it.
/// </summary>
/// <remarks>
/// The base string carries every field the real composer sets (ADR-0007 §5.2), so each case
/// varies one field of a realistic string rather than one field of a bare one. The dimension is
/// named in the case, and each case first proves the two strings really differ, so "differ only in
/// X" is measured rather than assumed.
/// </remarks>
public sealed class ResolvedEndpointComparisonTests
{
    /// <summary>What the resolver composes for one tenant, every §5.2 field set.</summary>
    private const string Fields =
        "Host=pg-1.internal;Port=5432;Database=aurora_t_acme;Username=aurora_app;Password=pw-one;"
        + "Pooling=True;Minimum Pool Size=0;Maximum Pool Size=10;Connection Idle Lifetime=30;Connection Pruning Interval=5;"
        + "Max Auto Prepare=0;Timeout=5;Command Timeout=30;Application Name=aurora-web:acme";

    /// <summary>
    /// The base string as the builder renders it. Every varied string is rendered by the same
    /// builder, so a whole-string comparison against this is a comparison of the varied field and
    /// nothing else - against the literal above it would differ in key spelling and order, and
    /// "the strings differ" would be true for the wrong reason.
    /// </summary>
    private static readonly string Base = new NpgsqlConnectionStringBuilder(Fields).ConnectionString;

    private readonly ITestOutputHelper _output;

    public ResolvedEndpointComparisonTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, string, string, bool> OneDimensionVaried => new()
    {
        // dimension varied, key, the other tenant's value, one endpoint?
        { "Application Name (the discriminator that made ADR-0034 §3.3 vacuous)", "Application Name", "aurora-web:globex", true },
        { "Host case", "Host", "PG-1.INTERNAL", true },
        { "Password", "Password", "pw-two", true },
        { "Username", "Username", "aurora_migrator", true },
        { "Maximum Pool Size (an ADR-0007 §5.2 pool setting, Web against Worker)", "Maximum Pool Size", "5", true },
        { "Command Timeout (an ADR-0007 §5.2 pool setting, Web against Worker)", "Command Timeout", "300", true },
        { "Database case", "Database", "AURORA_T_ACME", false },
        { "Database name", "Database", "aurora_t_globex", false },
        { "Port", "Port", "5433", false },
        { "Host name", "Host", "pg-2.internal", false },
    };

    [Theory]
    [MemberData(nameof(OneDimensionVaried))]
    public void Two_strings_that_differ_in_one_dimension_are_one_endpoint_exactly_when_that_dimension_is_not_part_of_the_destination(
        string dimension, string key, string otherValue, bool oneEndpoint)
    {
        string varied = new NpgsqlConnectionStringBuilder(Base) { [key] = otherValue }.ConnectionString;
        string.Equals(varied, Base, StringComparison.Ordinal).ShouldBeFalse($"the case varies {dimension}, so the strings must differ");

        (string Label, string ConnectionString)[] tenants = [("acme", Base), ("other", varied)];
        EndpointComparison<(string Label, string ConnectionString)> comparison = EndpointCollisions.Find(
            tenants,
            item => ResolvedEndpoint.Parse(item.ConnectionString));

        _output.WriteLine($"{dimension}: pairs compared {comparison.PairsCompared}, collisions {comparison.Collisions.Count}");
        comparison.PairsCompared.ShouldBe(1);
        comparison.Collisions.Count.ShouldBe(oneEndpoint ? 1 : 0, dimension);
    }

    [Fact]
    public void Whole_string_equality_reports_no_collision_where_the_endpoint_comparison_reports_one_on_every_non_destination_dimension()
    {
        // ADR-0034 §3.3 as written, executed beside its replacement over the same input: every
        // case above that is one endpoint is two different strings, so a whole-string comparison
        // is green over all of them. That is the vacuity ADR-0036 §2.1 replaces, kept executable.
        (string Dimension, string Varied)[] sameEndpoint = OneDimensionVaried
            .Where(row => (bool)row[3])
            .Select(row => ((string)row[0], new NpgsqlConnectionStringBuilder(Base) { [(string)row[1]] = (string)row[2] }.ConnectionString))
            .ToArray();

        int wholeStringCollisions = sameEndpoint.Count(item => string.Equals(item.Varied, Base, StringComparison.Ordinal));
        int endpointCollisions = sameEndpoint.Count(item => ResolvedEndpoint.Parse(item.Varied).Equals(ResolvedEndpoint.Parse(Base)));

        // The control: the whole-string comparison can report a collision - the base against its
        // own rendering - so its zero over the varied cases is a measurement, not an inability.
        string rerendered = new NpgsqlConnectionStringBuilder(Base).ConnectionString;
        string.Equals(rerendered, Base, StringComparison.Ordinal).ShouldBeTrue("the builder renders the same fields the same way twice");

        _output.WriteLine($"cases: {sameEndpoint.Length}; whole-string collisions: {wholeStringCollisions}; endpoint collisions: {endpointCollisions}");
        sameEndpoint.Length.ShouldBeGreaterThanOrEqualTo(4);
        wholeStringCollisions.ShouldBe(0, "no two of these strings are equal, so the criterion ADR-0034 §3.3 named could never fail over them");
        endpointCollisions.ShouldBe(sameEndpoint.Length);
    }

    [Fact]
    public void Every_unordered_pair_is_compared_and_counted()
    {
        string[] strings =
        [
            Base,
            new NpgsqlConnectionStringBuilder(Base) { Host = "PG-1.internal" }.ConnectionString,
            new NpgsqlConnectionStringBuilder(Base) { Database = "aurora_t_globex" }.ConnectionString,
            new NpgsqlConnectionStringBuilder(Base) { Port = 5433 }.ConnectionString,
        ];

        EndpointComparison<string> comparison = EndpointCollisions.Find(strings, ResolvedEndpoint.Parse);

        comparison.PairsCompared.ShouldBe(6, "four items make six unordered pairs");
        comparison.Collisions.Count.ShouldBe(1);
        comparison.Collisions[0].ShouldBe((strings[0], strings[1]));
    }

    [Fact]
    public void A_string_that_names_no_host_or_no_database_addresses_nothing_and_is_refused()
    {
        Should.Throw<ArgumentException>(() => ResolvedEndpoint.Parse("Port=5432;Database=aurora_t_acme"));
        Should.Throw<ArgumentException>(() => ResolvedEndpoint.Parse("Host=pg-1.internal;Port=5432"));
    }

    [Fact]
    public void The_endpoint_renders_as_host_port_database_and_keeps_the_hosts_spelling()
    {
        ResolvedEndpoint endpoint = ResolvedEndpoint.Parse(new NpgsqlConnectionStringBuilder(Base) { Host = "PG-1.internal" }.ConnectionString);

        endpoint.ToString().ShouldBe("PG-1.internal:5432/aurora_t_acme");
        endpoint.ShouldBe(ResolvedEndpoint.Parse(Base));
        endpoint.GetHashCode().ShouldBe(ResolvedEndpoint.Parse(Base).GetHashCode(), "equal endpoints hash alike, whatever the host's case");
        new HashSet<ResolvedEndpoint> { endpoint, ResolvedEndpoint.Parse(Base) }.Count.ShouldBe(1);
    }
}
