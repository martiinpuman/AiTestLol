using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// The shape of what is cached: serialisable, so that the L2 seam of ADR-0012 is real; immutable,
/// so that L1 keeps the instance; and free of any credential.
/// </summary>
public sealed class TenantRoutingTests
{
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
    public void The_cached_records_declare_themselves_immutable_so_the_L1_keeps_the_instance(System.Type record)
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
                property.Name.Contains("Secret", System.StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", System.StringComparison.OrdinalIgnoreCase)),
        ];

        secretShaped.Select(property => property.Name).OrderBy(name => name, System.StringComparer.Ordinal)
            .ShouldBe(["AdminSecretRef", "AppSecretRef", "MigratorSecretRef"]);
        secretShaped.Length.ShouldBe(3);
    }

    [Fact]
    public void A_tenant_connection_redacts_its_connection_string_when_rendered()
    {
        var connection = new TenantConnection("Host=h;Password=must-not-appear;Database=d", ClusterId.Parse("nz-1", null), "aurora_t_acme", Region.Parse("nz", null));

        string rendered = connection.ToString();

        rendered.ShouldNotContain("must-not-appear");
        rendered.ShouldContain("nz-1");
        rendered.ShouldContain("aurora_t_acme");
        rendered.ShouldContain("<redacted>");
    }
}
