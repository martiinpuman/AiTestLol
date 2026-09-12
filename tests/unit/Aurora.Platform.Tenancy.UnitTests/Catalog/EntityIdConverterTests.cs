using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// The generic converter in isolation. That EF Core accepts it inside a model and a query is
/// shown by the integration tests, which round-trip every entity through PostgreSQL.
/// </summary>
public sealed class EntityIdConverterTests
{
    [Fact]
    public void A_tenant_id_becomes_its_UUID_on_the_way_out_and_itself_on_the_way_back()
    {
        var converter = new EntityIdConverter<TenantId>();
        TenantId id = TenantId.Create();

        object? stored = converter.ConvertToProvider(id);
        object? rebuilt = converter.ConvertFromProvider(stored);

        stored.ShouldBe(id.Value);
        rebuilt.ShouldBe(id);
    }

    [Fact]
    public void The_same_converter_type_serves_every_identifier()
    {
        var converter = new EntityIdConverter<SubscriptionId>();
        SubscriptionId id = SubscriptionId.Create();

        converter.ConvertFromProvider(converter.ConvertToProvider(id)).ShouldBe(id);
    }

    [Fact]
    public void A_stored_all_zero_UUID_does_not_materialise_as_an_identifier()
    {
        var converter = new EntityIdConverter<TenantId>();

        Should.Throw<ArgumentException>(() => converter.ConvertFromProvider(Guid.Empty));
    }

    [Fact]
    public void Both_halves_are_expression_trees_EF_Core_can_compile()
    {
        var converter = new EntityIdConverter<TenantId>();
        TenantId id = TenantId.Create();

        Func<TenantId, Guid> toProvider = converter.ConvertToProviderExpression.Compile();
        Func<Guid, TenantId> fromProvider = converter.ConvertFromProviderExpression.Compile();

        fromProvider(toProvider(id)).ShouldBe(id);
    }
}
