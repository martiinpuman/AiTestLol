using System;
using Aurora.Platform.Tenancy.Routing;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>ADR-0012 rule 2: a catalog-level entry is prefixed <c>c:</c> and keyed by a committed kind.</summary>
public sealed class CatalogCacheKeyTests
{
    [Fact]
    public void A_routing_key_is_the_catalog_prefix_the_kind_segment_and_the_tenant_id()
    {
        TenantId tenant = TenantId.Parse("0192a7c4-6b1e-7d3a-9f00-000000000001", null);

        TenantRoutingCache.KeyFor(tenant).ShouldBe("c:tenant-routing:0192a7c4-6b1e-7d3a-9f00-000000000001");
    }

    [Fact]
    public void Two_tenants_get_two_keys()
    {
        TenantRoutingCache.KeyFor(TenantId.Create()).ShouldNotBe(TenantRoutingCache.KeyFor(TenantId.Create()));
    }

    [Fact]
    public void Every_committed_kind_has_a_segment_and_a_kind_outside_the_enum_is_refused()
    {
        CatalogCacheKind[] kinds = Enum.GetValues<CatalogCacheKind>();
        kinds.Length.ShouldBe(1, "B-06.1 commits the routing kind only; a new kind is a reviewed edit here too");

        foreach (CatalogCacheKind kind in kinds)
        {
            CatalogCacheKey.For(kind, "x").ShouldStartWith(CatalogCacheKey.Prefix);
        }

        Should.Throw<ArgumentOutOfRangeException>(() => CatalogCacheKey.For((CatalogCacheKind)42, "x"));
    }

    [Fact]
    public void A_blank_discriminator_is_refused()
    {
        Should.Throw<ArgumentException>(() => CatalogCacheKey.For(CatalogCacheKind.TenantRouting, " "));
    }
}
