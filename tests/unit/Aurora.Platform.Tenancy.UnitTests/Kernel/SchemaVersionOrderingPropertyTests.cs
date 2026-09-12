using System;
using Aurora.Platform.Tenancy.Contracts;
using FsCheck;
using FsCheck.Fluent;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// The law behind <see cref="SchemaVersion"/>'s ordering, over arbitrary versions: it is the
/// ordering of the ordinals, in every operator and in <c>CompareTo</c>, with equality and hashing
/// agreeing. The ADR-0007 §7.5 gate is four comparisons; each must mean what the integer means.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which dimensions are arbitrary, and which are fixed.</b> Arbitrary: both ordinals, drawn
/// from the whole range <c>0</c> to <see cref="SchemaVersion.MaxOrdinal"/> and never from a list.
/// The second ordinal is drawn either independently or as a near neighbour of the first
/// (equal, or one apart, clipped to the range), because two independent draws from two billion
/// values almost never coincide and the equality branch would otherwise go untested. Fixed: only
/// the range bound, and the one-step neighbourhood.
/// </para>
/// <para>
/// The fault this catches that the examples next door do not: a comparison over the rendered
/// text, under which <c>"10" &lt; "9"</c>. Every hand-picked pair in <see cref="SchemaVersionTests"/>
/// is single-digit and orders the same way as text, so it passes there and fails here on the first
/// draw that crosses a digit boundary.
/// </para>
/// </remarks>
public sealed class SchemaVersionOrderingPropertyTests
{
    [Fact]
    public void Every_comparison_agrees_with_the_ordinals_whatever_two_versions_are_drawn()
    {
        Prop.ForAll(
                Pairs().ToArbitrary(),
                pair =>
                {
                    SchemaVersion left = SchemaVersion.Of(pair.Left);
                    SchemaVersion right = SchemaVersion.Of(pair.Right);

                    Math.Sign(left.CompareTo(right)).ShouldBe(Math.Sign(pair.Left.CompareTo(pair.Right)));
                    (left < right).ShouldBe(pair.Left < pair.Right);
                    (left > right).ShouldBe(pair.Left > pair.Right);
                    (left <= right).ShouldBe(pair.Left <= pair.Right);
                    (left >= right).ShouldBe(pair.Left >= pair.Right);
                    (left == right).ShouldBe(pair.Left == pair.Right);
                    left.Equals(right).ShouldBe(pair.Left == pair.Right);
                    if (pair.Left == pair.Right)
                    {
                        left.GetHashCode().ShouldBe(right.GetHashCode());
                    }
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void The_ordinal_survives_the_round_trip_whatever_it_is()
    {
        Prop.ForAll(
                Ordinals().ToArbitrary(),
                ordinal => SchemaVersion.Of(ordinal).Ordinal.ShouldBe(ordinal))
            .QuickCheckThrowOnFailure();
    }

    private static Gen<int> Ordinals() => Gen.Choose(0, SchemaVersion.MaxOrdinal);

    private static Gen<Pair> Pairs() =>
        Ordinals().SelectMany(left =>
            Gen.OneOf(
                    Ordinals(),
                    Gen.Constant(left),
                    Gen.Constant(Math.Min(left + 1, SchemaVersion.MaxOrdinal)),
                    Gen.Constant(Math.Max(left - 1, 0)))
                .Select(right => new Pair(left, right)));

    private sealed record Pair(int Left, int Right);
}
