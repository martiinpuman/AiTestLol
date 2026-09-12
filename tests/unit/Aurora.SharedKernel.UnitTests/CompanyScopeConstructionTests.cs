using System;
using System.Collections;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// The two constructible forms of a <see cref="CompanyScope"/>, and every way of asking for a
/// third that must be refused (solution-layout.md §6.2 B-03.1 criteria 1 and 4; ADR-0029 §5 and
/// A1.2 H-2).
/// </summary>
/// <remarks>
/// The refusals are the point of the type. A scope over no companies is the company-level twin of
/// a cross-tenant read: an empty filter list that a query builder turns into no filter at all is
/// <c>WHERE 1=1</c>, which is every company. Each test below closes one path by which an empty set
/// could reach a query.
/// </remarks>
public sealed class CompanyScopeConstructionTests
{
    [Fact]
    public void AllCompaniesInTenant_names_no_company_because_the_tenant_database_is_the_boundary()
    {
        CompanyScope scope = CompanyScope.AllCompaniesInTenant;

        scope.IsAllCompaniesInTenant.ShouldBeTrue();
        scope.CompanyIds.ShouldBeEmpty();
    }

    [Fact]
    public void Of_one_id_is_a_scope_over_exactly_that_company()
    {
        CompanyId company = CompanyId.Create();

        CompanyScope scope = CompanyScope.Of([company]);

        scope.IsAllCompaniesInTenant.ShouldBeFalse();
        scope.CompanyIds.ShouldBe([company]);
    }

    [Fact]
    public void Of_refuses_an_empty_collection_so_that_no_scope_can_quietly_mean_every_company()
    {
        ArgumentException refused = Should.Throw<ArgumentException>(() => CompanyScope.Of([]));

        refused.Message.ShouldContain("no companies");
    }

    [Fact]
    public void Of_refuses_a_null_collection()
    {
        Should.Throw<ArgumentNullException>(() => CompanyScope.Of(null!));
    }

    [Fact]
    public void Of_refuses_an_id_nobody_assigned()
    {
        Should.Throw<ArgumentException>(() => CompanyScope.Of([default]));
    }

    [Fact]
    public void Of_refuses_a_collection_that_would_be_empty_once_unassigned_ids_were_dropped()
    {
        // Two copies of the default id deduplicate to one, and dropping that one would leave
        // nothing: the path an implementation that strips defaults before checking for emptiness,
        // or deduplicates before checking for defaults, would take straight to WHERE 1=1.
        Should.Throw<ArgumentException>(() => CompanyScope.Of([default, default]));
    }

    [Fact]
    public void Of_refuses_an_unassigned_id_hidden_among_real_ones_rather_than_dropping_it()
    {
        CompanyId first = CompanyId.Create();
        CompanyId last = CompanyId.Create();

        ArgumentException refused = Should.Throw<ArgumentException>(
            () => CompanyScope.Of([first, default, last]));

        // Refused for the right reason: a real id was present, so this is not the empty case.
        refused.Message.ShouldContain("unassigned");
    }

    [Fact]
    public void Of_reads_its_input_once_so_that_a_one_shot_sequence_is_enough()
    {
        OneShotSequence sequence = new([CompanyId.Create(), CompanyId.Create()]);

        CompanyScope scope = CompanyScope.Of(sequence);

        sequence.Enumerations.ShouldBe(1);
        scope.CompanyIds.Count.ShouldBe(2);
    }

    [Fact]
    public void A_scope_keeps_its_own_copy_so_that_the_caller_cannot_change_it_afterwards()
    {
        CompanyId kept = CompanyId.Create();
        List<CompanyId> source = [kept];

        CompanyScope scope = CompanyScope.Of(source);
        source.Add(CompanyId.Create());
        source.Clear();

        scope.CompanyIds.ShouldBe([kept]);
    }

    [Fact]
    public void The_ids_a_scope_exposes_cannot_be_changed_through_a_cast()
    {
        CompanyScope scope = CompanyScope.Of([CompanyId.Create()]);

        scope.CompanyIds.ShouldNotBeOfType<CompanyId[]>();
        ICollection<CompanyId>? mutableView = scope.CompanyIds as ICollection<CompanyId>;
        (mutableView is null || mutableView.IsReadOnly).ShouldBeTrue();
    }

    /// <summary>
    /// A sequence that can be walked once, the way a database reader or a LINQ query over one is.
    /// A second walk throws, so an implementation that enumerates its input twice fails here
    /// rather than in production against a reader.
    /// </summary>
    private sealed class OneShotSequence(IReadOnlyList<CompanyId> items) : IEnumerable<CompanyId>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<CompanyId> GetEnumerator()
        {
            Enumerations++;
            if (Enumerations > 1)
            {
                throw new InvalidOperationException("This sequence has already been enumerated.");
            }

            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
