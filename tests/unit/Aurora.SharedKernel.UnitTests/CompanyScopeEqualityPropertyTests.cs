using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// The laws behind <see cref="CompanyScope"/>'s equality, stated over arbitrary inputs
/// (solution-layout.md §6.2 B-03.1 criterion 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which dimensions are arbitrary, and which are fixed.</b> Arbitrary: the ids themselves
/// (UUIDs built from sixteen random bytes, never drawn from a list); how many distinct ids a
/// scope names (one to <see cref="MaxDistinctIds"/>); how many extra copies of which ids the
/// input carries (a random multiset over the set, independently per draw); and the order the
/// input arrives in (a random permutation of the whole multiset, drawn independently for each of
/// the two presentations compared). Fixed: only the upper bound on the count.
/// </para>
/// <para>
/// The fault that passes an example-based test and fails here: an implementation that
/// deduplicates but does not order. <c>Of([a, b])</c> and <c>Of([b, a])</c> then compare unequal
/// on about half of all draws, while the examples next door are written in whichever order the
/// author typed — and with UUIDv7 ids minted in sequence, that order is very often already sorted.
/// </para>
/// </remarks>
public sealed class CompanyScopeEqualityPropertyTests
{
    private const int MaxDistinctIds = 12;

    [Fact]
    public void A_scope_is_the_same_scope_whatever_order_and_repetition_its_ids_arrive_in()
    {
        Prop.ForAll(
                TwoPresentationsOfOneSet().ToArbitrary(),
                pair =>
                {
                    CompanyScope first = CompanyScope.Of(pair.First);
                    CompanyScope second = CompanyScope.Of(pair.Second);

                    first.ShouldBe(second);
                    (first == second).ShouldBeTrue();
                    first.Equals((object)second).ShouldBeTrue();
                    first.GetHashCode().ShouldBe(second.GetHashCode());
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void The_ids_come_back_each_once_in_one_canonical_order_whatever_the_input_looked_like()
    {
        Prop.ForAll(
                Presentations().ToArbitrary(),
                presentation =>
                {
                    IReadOnlyList<CompanyId> ids = CompanyScope.Of(presentation.Input).CompanyIds;

                    ids.Count.ShouldBe(presentation.Distinct.Count);
                    ids.ToHashSet().SetEquals(presentation.Distinct).ShouldBeTrue();
                    for (int later = 1; later < ids.Count; later++)
                    {
                        ids[later - 1].Value.CompareTo(ids[later].Value).ShouldBeLessThan(0);
                    }
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void One_more_company_is_a_different_scope_and_so_is_every_company()
    {
        // The negative direction. Without it, an Equals that always answers true satisfies every
        // property above.
        Prop.ForAll(
                SetsWithOneMore().ToArbitrary(),
                sample =>
                {
                    CompanyScope some = CompanyScope.Of(sample.Set);
                    CompanyScope more = CompanyScope.Of([.. sample.Set, sample.Extra]);

                    some.ShouldNotBe(more);
                    (some != more).ShouldBeTrue();
                    some.ShouldNotBe(CompanyScope.AllCompaniesInTenant);
                    more.ShouldNotBe(CompanyScope.AllCompaniesInTenant);
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void Equal_scopes_are_one_dictionary_key_and_one_set_member()
    {
        Prop.ForAll(
                TwoPresentationsOfOneSet().ToArbitrary(),
                pair =>
                {
                    Dictionary<CompanyScope, int> byScope = new()
                    {
                        [CompanyScope.Of(pair.First)] = 1,
                    };
                    byScope.ContainsKey(CompanyScope.Of(pair.Second)).ShouldBeTrue();

                    HashSet<CompanyScope> members =
                        [CompanyScope.Of(pair.First), CompanyScope.Of(pair.Second)];
                    members.Count.ShouldBe(1);
                })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void Distinct_scopes_do_not_all_share_one_hash_code()
    {
        // A constant hash code satisfies every law above and still turns every dictionary keyed
        // by a scope into a list. Sampled rather than universal: among a hundred scopes over
        // freshly minted ids, two random 32-bit hashes collide with probability about 1 in 10⁶,
        // so anything under 95 distinct values is an implementation, not chance.
        const int scopes = 100;
        HashSet<int> hashCodes = [];
        for (int minted = 0; minted < scopes; minted++)
        {
            hashCodes.Add(CompanyScope.Of([CompanyId.Create(), CompanyId.Create()]).GetHashCode());
        }

        hashCodes.Count.ShouldBeGreaterThanOrEqualTo(95);
    }

    /// <summary>
    /// A company id from sixteen arbitrary bytes. The all-zero UUID is the one value that is not
    /// an id, and the one draw in 2¹²⁸ that produces it is filtered rather than special-cased.
    /// </summary>
    private static Gen<CompanyId> CompanyIds() =>
        Gen.ArrayOf(Gen.Choose(0, 255), 16)
            .Select(octets => new Guid(Array.ConvertAll(octets, octet => (byte)octet)))
            .Where(uuid => uuid != Guid.Empty)
            .Select(CompanyId.From);

    /// <summary>One to <see cref="MaxDistinctIds"/> distinct ids.</summary>
    private static Gen<IReadOnlyList<CompanyId>> DistinctSets() =>
        Gen.Choose(1, MaxDistinctIds)
            .SelectMany(count => Gen.ArrayOf(CompanyIds(), count))
            .Select(ids => (IReadOnlyList<CompanyId>)ids.Distinct().ToList());

    /// <summary>
    /// One way of handing a set of ids to <see cref="CompanyScope.Of"/>: every id at least once,
    /// some of them several times, in a random order.
    /// </summary>
    private static Gen<IReadOnlyList<CompanyId>> PresentationsOf(IReadOnlyList<CompanyId> set) =>
        Gen.ListOf(Gen.Choose(0, set.Count - 1).Select(index => set[index]))
            .SelectMany(extraCopies => Gen.Shuffle(set.Concat(extraCopies)))
            .Select(shuffled => (IReadOnlyList<CompanyId>)shuffled);

    private static Gen<Presentation> Presentations() =>
        DistinctSets().SelectMany(set =>
            PresentationsOf(set).Select(input => new Presentation(set, input)));

    private static Gen<Pair> TwoPresentationsOfOneSet() =>
        DistinctSets().SelectMany(set =>
            PresentationsOf(set).SelectMany(first =>
                PresentationsOf(set).Select(second => new Pair(first, second))));

    private static Gen<SetWithOneMore> SetsWithOneMore() =>
        DistinctSets().SelectMany(set =>
            CompanyIds()
                .Where(extra => !set.Contains(extra))
                .Select(extra => new SetWithOneMore(set, extra)));

    private static string Listed(IEnumerable<CompanyId> ids) => "[" + string.Join(", ", ids) + "]";

    private sealed record Presentation(IReadOnlyList<CompanyId> Distinct, IReadOnlyList<CompanyId> Input)
    {
        public override string ToString() => $"{Listed(Distinct)} presented as {Listed(Input)}";
    }

    private sealed record Pair(IReadOnlyList<CompanyId> First, IReadOnlyList<CompanyId> Second)
    {
        public override string ToString() => $"{Listed(First)} and {Listed(Second)}";
    }

    private sealed record SetWithOneMore(IReadOnlyList<CompanyId> Set, CompanyId Extra)
    {
        public override string ToString() => $"{Listed(Set)} plus {Extra}";
    }
}
