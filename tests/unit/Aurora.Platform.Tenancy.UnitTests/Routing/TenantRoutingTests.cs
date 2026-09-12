using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// The shape of what is cached and what is handed out: the cached row serialisable, so that the
/// L2 seam of ADR-0012 is real, immutable, so that L1 keeps the instance, and free of any
/// credential; the handed-out connection revealing its credential on exactly one path.
/// </summary>
public sealed class TenantRoutingTests
{
    private readonly ITestOutputHelper _output;

    public TenantRoutingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_routing_row_round_trips_through_System_Text_Json_unchanged()
    {
        TenantRouting row = new ARouting().InState(TenantState.Suspended).Build();

        TenantRouting? back = JsonSerializer.Deserialize<TenantRouting>(JsonSerializer.Serialize(row));

        back.ShouldBe(row);
    }

    [Fact]
    public void A_tombstone_round_trips_too()
    {
        TenantRouting row = new ARouting().Tombstoned().Build();

        TenantRouting? back = JsonSerializer.Deserialize<TenantRouting>(JsonSerializer.Serialize(row));

        back.ShouldBe(row);
        back!.Cluster.ShouldBeNull();
    }

    [Theory]
    [InlineData(typeof(TenantRouting))]
    [InlineData(typeof(ClusterEndpoint))]
    public void The_cached_records_declare_themselves_immutable_so_the_L1_keeps_the_instance(Type record)
    {
        ImmutableObjectAttribute? attribute = record.GetCustomAttribute<ImmutableObjectAttribute>();

        attribute.ShouldNotBeNull();
        attribute.Immutable.ShouldBeTrue();
    }

    [Fact]
    public void The_cached_records_carry_secret_references_and_nothing_named_like_a_credential()
    {
        // The catalog's own rule (SecretReference) applied to the cache: a reference, never a value.
        // Three references are expected - one per cluster role (ADR-0004 rule 2) - and any other
        // property whose name mentions a secret or a password is a credential-shaped addition.
        PropertyInfo[] properties = [.. typeof(TenantRouting).GetProperties(), .. typeof(ClusterEndpoint).GetProperties()];
        PropertyInfo[] secretShaped =
        [
            .. properties.Where(property =>
                property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)),
        ];

        secretShaped.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["AdminSecretRef", "AppSecretRef", "MigratorSecretRef"]);
        secretShaped.Length.ShouldBe(3);
    }

    [Fact]
    public void A_tenant_connection_reveals_its_credential_through_Reveal_and_on_no_other_path()
    {
        // PR #14 M-2: the earlier test asserted ToString() only, and System.Text.Json plus every
        // reflection-based sink emitted the credential verbatim. Redaction is now a property of
        // ConnectionSecret itself - it renders as <redacted> however it is rendered, and has no
        // public property for a reflecting sink to read - so it survives being placed in any
        // record, log event or payload.
        const string password = "must-not-appear";
        var connection = new TenantConnection(
            new ConnectionSecret($"Host=h;Password={password};Database=d"),
            ClusterId.Parse("nz-1", null),
            "aurora_t_acme",
            Region.Parse("nz", null));

        var renderings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["record ToString()"] = connection.ToString(),
            ["record interpolation"] = $"{connection}",
            ["record through System.Text.Json"] = JsonSerializer.Serialize(connection),
            ["secret through System.Text.Json"] = JsonSerializer.Serialize(connection.ConnectionString),
            ["secret ToString()"] = connection.ConnectionString.ToString(),
            ["secret's public properties, reflected"] = string.Join(
                ";",
                typeof(ConnectionSecret).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => $"{property.Name}={property.GetValue(connection.ConnectionString)}")),
        };

        foreach ((string path, string rendered) in renderings)
        {
            _output.WriteLine($"  {path,-40} {rendered}");
            rendered.ShouldNotContain(password, customMessage: path);
        }

        renderings.Count.ShouldBe(6, "every rendering path a sink takes, examined");
        renderings["record ToString()"].ShouldContain(ConnectionSecret.Redacted);
        // System.Text.Json escapes '<' and '>' on the wire, so the property is read back rather than searched for.
        JsonDocument.Parse(renderings["record through System.Text.Json"]).RootElement
            .GetProperty(nameof(TenantConnection.ConnectionString)).GetString()
            .ShouldBe(ConnectionSecret.Redacted);
        typeof(ConnectionSecret).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty("a reflection-based sink has no property to read");
        connection.ConnectionString.Reveal().ShouldContain(password);
    }

    [Fact]
    public void A_connection_secret_is_never_rehydrated_from_JSON()
    {
        Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<ConnectionSecret>("\"Host=h;Password=x\""));
    }

    [Fact]
    public void Connection_secrets_compare_by_value_and_a_blank_one_is_refused()
    {
        new ConnectionSecret("Host=h").ShouldBe(new ConnectionSecret("Host=h"));
        new ConnectionSecret("Host=h").ShouldNotBe(new ConnectionSecret("Host=i"));
        new ConnectionSecret("Host=h").GetHashCode().ShouldBe(new ConnectionSecret("Host=h").GetHashCode());
        Should.Throw<ArgumentException>(() => new ConnectionSecret(" "));
    }
}
