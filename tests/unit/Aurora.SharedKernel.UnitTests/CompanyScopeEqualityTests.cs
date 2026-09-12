using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// A <see cref="CompanyScope"/> is a value: equal by the set of companies it names, whichever
/// order and however many times they were listed (solution-layout.md §6.2 B-03.1 criterion 3).
/// </summary>
/// <remarks>
/// These pin the exact cases a reader reaches for. <see cref="CompanyScopeEqualityPropertyTests"/>
/// states the same laws over arbitrary ids, counts and permutations, which is where an
/// implementation that deduplicates but forgets to order actually gets caught.
/// </remarks>
public sealed class CompanyScopeEqualityTests
{
    [Fact]
    public void Two_scopes_over_the_same_ids_are_equal_whatever_order_the_ids_were_given_in()
    {
        CompanyId a = CompanyId.Create();
        CompanyId b = CompanyId.Create();
        CompanyId c = CompanyId.Create();

        CompanyScope first = CompanyScope.Of([a, b, c]);
        CompanyScope second = CompanyScope.Of([c, a, b]);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.CompanyIds.ShouldBe(second.CompanyIds);
    }

    [Fact]
    public void Repeating_an_id_does_not_make_a_different_scope()
    {
        CompanyId a = CompanyId.Create();
        CompanyId b = CompanyId.Create();

        CompanyScope repeated = CompanyScope.Of([a, b, b, a, b]);

        repeated.ShouldBe(CompanyScope.Of([a, b]));
        repeated.CompanyIds.Count.ShouldBe(2);
    }

    [Fact]
    public void A_scope_over_a_different_set_of_companies_is_a_different_scope()
    {
        CompanyId a = CompanyId.Create();
        CompanyId b = CompanyId.Create();
        CompanyId c = CompanyId.Create();

        CompanyScope.Of([a, b]).ShouldNotBe(CompanyScope.Of([a, c]));
        CompanyScope.Of([a]).ShouldNotBe(CompanyScope.Of([a, b]));
        CompanyScope.Of([a, b]).ShouldNotBe(CompanyScope.Of([a]));
    }

    [Fact]
    public void AllCompaniesInTenant_is_one_value_and_never_equal_to_a_scope_over_named_companies()
    {
        CompanyScope named = CompanyScope.Of([CompanyId.Create()]);

        CompanyScope.AllCompaniesInTenant.ShouldBe(CompanyScope.AllCompaniesInTenant);
        CompanyScope.AllCompaniesInTenant.ShouldNotBe(named);
        named.ShouldNotBe(CompanyScope.AllCompaniesInTenant);
        CompanyScope.AllCompaniesInTenant.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Equal_scopes_hash_alike_so_that_a_scope_finds_its_own_entry_in_a_dictionary()
    {
        CompanyId a = CompanyId.Create();
        CompanyId b = CompanyId.Create();
        Dictionary<CompanyScope, string> cachedByScope = new()
        {
            [CompanyScope.Of([a, b])] = "the entry",
        };

        cachedByScope[CompanyScope.Of([b, a, b])].ShouldBe("the entry");
        cachedByScope.ContainsKey(CompanyScope.Of([a])).ShouldBeFalse();

        HashSet<CompanyScope> distinct =
        [
            CompanyScope.Of([a, b]),
            CompanyScope.Of([b, a]),
            CompanyScope.AllCompaniesInTenant,
            CompanyScope.AllCompaniesInTenant,
        ];
        distinct.Count.ShouldBe(2);
    }

    [Fact]
    public void The_operators_say_the_same_as_Equals()
    {
        CompanyId a = CompanyId.Create();
        CompanyId b = CompanyId.Create();
        CompanyScope left = CompanyScope.Of([a, b]);
        CompanyScope same = CompanyScope.Of([b, a]);
        CompanyScope other = CompanyScope.Of([a]);
        CompanyScope? nothing = null;

        (left == same).ShouldBeTrue();
        (left != same).ShouldBeFalse();
        (left == other).ShouldBeFalse();
        (left != other).ShouldBeTrue();
        (left == nothing).ShouldBeFalse();
        (nothing == left).ShouldBeFalse();
        (nothing != left).ShouldBeTrue();
        (nothing == null).ShouldBeTrue();
    }

    [Fact]
    public void Equals_on_object_answers_the_same_as_the_typed_overload()
    {
        CompanyId a = CompanyId.Create();
        CompanyScope scope = CompanyScope.Of([a]);

        scope.Equals((object)CompanyScope.Of([a])).ShouldBeTrue();
        scope.Equals((object)CompanyScope.AllCompaniesInTenant).ShouldBeFalse();
        scope.Equals("not a scope").ShouldBeFalse();
    }

    [Fact]
    public void ToString_names_the_form_and_lists_the_companies_in_canonical_order()
    {
        CompanyId a = CompanyId.Create();
        CompanyId b = CompanyId.Create();
        CompanyScope scope = CompanyScope.Of([b, a]);

        CompanyScope.AllCompaniesInTenant.ToString().ShouldBe("every company in the tenant");
        scope.ToString().ShouldBe(
            "companies " + string.Join(", ", scope.CompanyIds));
        scope.ToString().ShouldBe(CompanyScope.Of([a, b]).ToString());
    }
}
