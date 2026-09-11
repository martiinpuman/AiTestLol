using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// The invariants every strongly-typed identifier obeys, run against each one.
/// </summary>
/// <remarks>
/// Written once and inherited, so that the fiftieth identifier in the system is held to the same
/// contract as the first without anyone remembering to copy a test file.
/// </remarks>
/// <typeparam name="TId">The identifier under test.</typeparam>
public abstract class EntityIdContract<TId>
    where TId : struct, IEntityId<TId>
{
    [Fact]
    public void A_new_id_is_a_time_ordered_UUIDv7_so_that_inserts_land_at_the_end_of_the_index()
    {
        TId.Create().Value.Version.ShouldBe(7);
    }

    [Fact]
    public void Every_new_id_is_a_different_id()
    {
        HashSet<TId> minted = [];

        for (int mint = 0; mint < 1_000; mint++)
        {
            minted.Add(TId.Create()).ShouldBeTrue();
        }
    }

    [Fact]
    public void An_id_rebuilt_from_its_stored_value_is_the_same_id()
    {
        TId id = TId.Create();

        TId.From(id.Value).ShouldBe(id);
        TId.From(id.Value).GetHashCode().ShouldBe(id.GetHashCode());
    }

    [Fact]
    public void An_id_reads_back_from_the_text_form_it_writes()
    {
        TId id = TId.Create();
        string text = id.ToString().ShouldNotBeNull();

        TId.Parse(text, null).ShouldBe(id);
    }

    [Fact]
    public void An_all_zero_value_is_refused_because_it_identifies_nothing()
    {
        Should.Throw<ArgumentException>(() => TId.From(Guid.Empty));
        Should.Throw<FormatException>(() => TId.Parse(Guid.Empty.ToString(), null));
        TId.TryParse(Guid.Empty.ToString(), null, out _).ShouldBeFalse();
    }

    [Fact]
    public void An_id_nobody_assigned_says_so_rather_than_passing_for_a_real_one()
    {
        default(TId).IsEmpty.ShouldBeTrue();
        TId.Create().IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Text_that_is_not_a_UUID_does_not_parse()
    {
        TId.TryParse("not-an-id", null, out _).ShouldBeFalse();
        TId.TryParse("", null, out _).ShouldBeFalse();
        TId.TryParse(null, null, out _).ShouldBeFalse();
        Should.Throw<FormatException>(() => TId.Parse("not-an-id", null));
        Should.Throw<ArgumentNullException>(() => TId.Parse(null!, null));
    }

    [Fact]
    public void A_failed_parse_leaves_an_id_that_identifies_nothing_rather_than_a_stale_one()
    {
        TId.TryParse("not-an-id", null, out TId result).ShouldBeFalse();

        result.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Both_halves_of_an_EF_Core_value_conversion_work_as_expression_trees()
    {
        // Exactly the pair a ValueConverter<TId, Guid> is built from. The read half goes through a
        // delegate because an expression tree may not name a static abstract interface member
        // directly; proving it here is what keeps B-05 from discovering it in an ORM stack trace.
        Expression<Func<TId, Guid>> toProvider = id => id.Value;
        Func<Guid, TId> factory = TId.From;
        Expression<Func<Guid, TId>> fromProvider = stored => factory(stored);

        TId original = TId.Create();
        Guid stored = toProvider.Compile()(original);

        fromProvider.Compile()(stored).ShouldBe(original);
    }
}
