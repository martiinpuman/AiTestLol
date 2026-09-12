using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class TenantIdTests : EntityIdContract<TenantId>
{
    [Fact]
    public void A_tenant_id_is_built_from_a_UUID_and_reads_it_back()
    {
        Guid value = Guid.CreateVersion7();

        new TenantId(value).Value.ShouldBe(value);
    }

    [Fact]
    public void The_constructor_refuses_an_all_zero_UUID_just_as_From_does()
    {
        Should.Throw<ArgumentException>(() => new TenantId(Guid.Empty));
    }
}

public sealed class CompanyIdTests : EntityIdContract<CompanyId>
{
    [Fact]
    public void A_company_id_is_built_from_a_UUID_and_reads_it_back()
    {
        Guid value = Guid.CreateVersion7();

        new CompanyId(value).Value.ShouldBe(value);
    }

    [Fact]
    public void The_constructor_refuses_an_all_zero_UUID_just_as_From_does()
    {
        Should.Throw<ArgumentException>(() => new CompanyId(Guid.Empty));
    }
}

public sealed class IdentifierTypesDoNotMixTests
{
    [Fact]
    public void Two_ids_over_the_same_UUID_are_still_different_things()
    {
        Guid value = Guid.CreateVersion7();

        TenantId tenant = TenantId.From(value);
        CompanyId company = CompanyId.From(value);

        tenant.Value.ShouldBe(company.Value);
        tenant.Equals((object)company).ShouldBeFalse();
        company.Equals((object)tenant).ShouldBeFalse();
    }
}
