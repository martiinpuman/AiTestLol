using System;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// <see cref="ICompanyScoped"/> is the marker ADR-0029 A1.2 H-2 hangs the company boundary on
/// (solution-layout.md §6.2 B-03.1 criterion 2).
/// </summary>
public sealed class CompanyScopedTests
{
    [Fact]
    public void The_contract_is_one_readable_CompanyId_and_nothing_else()
    {
        // The whole surface, pinned. A second member — a setter, a TenantId, a Contains — widens
        // what B-06.3's constructor rule and query filter have to reason about, and a setter
        // would let an entity change company under a filter that has already run.
        Type contract = typeof(ICompanyScoped);
        contract.IsInterface.ShouldBeTrue();
        contract.GetInterfaces().ShouldBeEmpty();

        PropertyInfo companyId = contract.GetProperties().ShouldHaveSingleItem();
        companyId.Name.ShouldBe(nameof(ICompanyScoped.CompanyId));
        companyId.PropertyType.ShouldBe(typeof(CompanyId));
        companyId.CanRead.ShouldBeTrue();
        companyId.CanWrite.ShouldBeFalse();

        contract.GetMethods().ShouldHaveSingleItem().ShouldBe(companyId.GetMethod);
        contract.GetEvents().ShouldBeEmpty();
    }

    [Fact]
    public void A_company_is_scoped_to_itself_so_that_one_rule_serves_every_aggregate()
    {
        CompanyId company = CompanyId.Create();
        ICompanyScoped theCompany = new CompanyAggregate(company);
        ICompanyScoped aDocumentOfTheCompany = new SalesOrderAggregate(company, OrderNumber: 1);
        CompanyScope thatCompanyOnly = CompanyScope.Of([company]);
        CompanyScope anotherCompanyOnly = CompanyScope.Of([CompanyId.Create()]);

        theCompany.CompanyId.ShouldBe(company);
        aDocumentOfTheCompany.CompanyId.ShouldBe(company);

        IsInScope(theCompany, thatCompanyOnly).ShouldBeTrue();
        IsInScope(aDocumentOfTheCompany, thatCompanyOnly).ShouldBeTrue();
        IsInScope(theCompany, anotherCompanyOnly).ShouldBeFalse();
        IsInScope(aDocumentOfTheCompany, anotherCompanyOnly).ShouldBeFalse();
        IsInScope(theCompany, CompanyScope.AllCompaniesInTenant).ShouldBeTrue();
        IsInScope(aDocumentOfTheCompany, CompanyScope.AllCompaniesInTenant).ShouldBeTrue();
    }

    /// <summary>
    /// The predicate B-06.3 will parameterize a query filter from, written once for every
    /// <see cref="ICompanyScoped"/> and with no special case for the company itself. In memory
    /// here only to show the rule is uniform; in production it is a query filter and never a
    /// post-filter (ADR-0010 rule 6).
    /// </summary>
    private static bool IsInScope(ICompanyScoped entity, CompanyScope scope) =>
        scope.IsAllCompaniesInTenant || scope.CompanyIds.Contains(entity.CompanyId);

    /// <summary>
    /// The shape SPEC-002's Company aggregate takes: it <em>is</em> a company, so it is scoped
    /// to itself.
    /// </summary>
    private sealed record CompanyAggregate(CompanyId Id) : ICompanyScoped
    {
        CompanyId ICompanyScoped.CompanyId => Id;
    }

    /// <summary>Any other aggregate that belongs to a company.</summary>
    private sealed record SalesOrderAggregate(CompanyId CompanyId, int OrderNumber) : ICompanyScoped;
}
