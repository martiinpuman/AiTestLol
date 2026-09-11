using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

public sealed class TenantHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("acme.aurora.example")]
    [InlineData("erp.acme.co.nz")]
    [InlineData("localhost")]
    [InlineData("xn--bcher-kva.example")]
    public void A_lower_case_DNS_name_is_accepted(string host)
    {
        TenantId tenantId = TenantId.Create();

        TenantHost registered = TenantHost.Register(host, tenantId, isPrimary: true, verifiedAt: Now);

        registered.Host.ShouldBe(host);
        registered.TenantId.ShouldBe(tenantId);
        registered.IsPrimary.ShouldBeTrue();
        registered.VerifiedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData("Acme.aurora.example", "upper case is a second spelling of the same host")]
    [InlineData("acme.aurora.example:443", "a port")]
    [InlineData("https://acme.aurora.example", "a scheme")]
    [InlineData("acme.aurora.example/app", "a path")]
    [InlineData("-acme.aurora.example", "a label starting with a hyphen")]
    [InlineData("acme-.aurora.example", "a label ending with a hyphen")]
    [InlineData("acme..aurora.example", "an empty label")]
    [InlineData(".acme.aurora.example", "a leading dot")]
    [InlineData("acme aurora.example", "whitespace")]
    [InlineData("acme_1.aurora.example", "an underscore")]
    [InlineData("", "empty")]
    public void A_host_that_is_not_a_canonical_DNS_name_is_refused(string host, string why)
    {
        Should.Throw<ArgumentException>(() => TenantHost.Register(host, TenantId.Create(), false, null), why);
    }

    [Fact]
    public void A_host_longer_than_DNS_allows_is_refused()
    {
        string label = new('a', 63);
        string tooLong = string.Join('.', label, label, label, label, "ab");
        tooLong.Length.ShouldBeGreaterThan(TenantHost.MaxHostLength);

        Should.Throw<ArgumentException>(() => TenantHost.Register(tooLong, TenantId.Create(), false, null));
    }

    [Fact]
    public void An_unverified_custom_domain_has_no_verification_instant()
    {
        TenantHost.Register("erp.acme.co.nz", TenantId.Create(), false, null).VerifiedAt.ShouldBeNull();
    }

    [Fact]
    public void An_unassigned_tenant_id_is_refused()
    {
        Should.Throw<ArgumentException>(() => TenantHost.Register("acme.aurora.example", default, true, Now));
    }

    [Fact]
    public void A_verification_instant_that_is_not_UTC_is_refused()
    {
        DateTimeOffset local = new(2026, 9, 11, 22, 0, 0, TimeSpan.FromHours(12));

        Should.Throw<ArgumentException>(() => TenantHost.Register("acme.aurora.example", TenantId.Create(), true, local));
    }
}
