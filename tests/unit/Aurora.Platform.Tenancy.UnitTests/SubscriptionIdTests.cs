using System;
using System.Collections.Generic;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests;

/// <summary>
/// The <c>EntityIdContract</c> invariants, restated for the one identifier this module adds.
/// </summary>
/// <remarks>
/// The contract class lives in <c>Aurora.SharedKernel.UnitTests</c>, and a test project referencing
/// another test project is a dependency nobody wants. Lifting the contract into
/// <c>Aurora.TestKit</c> is recommended in the B-05 summary; until then these five facts hold the
/// identifier to the same rules by hand.
/// </remarks>
public sealed class SubscriptionIdTests
{
    [Fact]
    public void A_new_id_is_a_time_ordered_UUIDv7()
    {
        SubscriptionId.Create().Value.Version.ShouldBe(7);
    }

    [Fact]
    public void Every_new_id_is_a_different_id()
    {
        HashSet<SubscriptionId> minted = [];

        for (int mint = 0; mint < 1_000; mint++)
        {
            minted.Add(SubscriptionId.Create()).ShouldBeTrue();
        }
    }

    [Fact]
    public void An_id_rebuilt_from_its_stored_value_is_the_same_id()
    {
        SubscriptionId id = SubscriptionId.Create();

        SubscriptionId.From(id.Value).ShouldBe(id);
        SubscriptionId.Parse(id.ToString(), null).ShouldBe(id);
    }

    [Fact]
    public void An_all_zero_value_is_refused_because_it_identifies_nothing()
    {
        Should.Throw<ArgumentException>(() => SubscriptionId.From(Guid.Empty));
        Should.Throw<ArgumentException>(() => new SubscriptionId(Guid.Empty));
        SubscriptionId.TryParse(Guid.Empty.ToString(), null, out _).ShouldBeFalse();
    }

    [Fact]
    public void An_id_nobody_assigned_says_so_rather_than_passing_for_a_real_one()
    {
        default(SubscriptionId).IsEmpty.ShouldBeTrue();
        SubscriptionId.Create().IsEmpty.ShouldBeFalse();
    }
}
