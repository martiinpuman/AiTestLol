using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Countries.Contracts.Accounting;
using Aurora.Countries.Contracts.Identifiers;
using Aurora.Countries.Contracts.Reporting;
using Aurora.Countries.Contracts.Retention;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// The statutory-report box mapping, the identifier contract's third state, and the retention rule.
/// </summary>
public sealed class ReportingAndIdentifierTests
{
    /// <summary>
    /// ADR-0008 §11 says the box mapping is "deliberately restricted to stop it growing into a query
    /// language". A private-protected base and nested sealed cases make that literally true: nothing
    /// outside the contract can add a case, and a package that needs one needs a core change.
    /// </summary>
    [Fact]
    public void A_report_box_may_only_sum_what_core_defined()
    {
        Type[] cases =
        [
            .. typeof(CoreContract).Assembly.GetExportedTypes()
                .Where(type => type.IsSubclassOf(typeof(ReportBoxSource))),
        ];

        cases.Length.ShouldBe(3);
        cases.ShouldAllBe(source => source.IsSealed);
        cases.ShouldAllBe(source => source.DeclaringType == typeof(ReportBoxSource));

        typeof(ReportBoxSource).IsAbstract.ShouldBeTrue();
        typeof(ReportBoxSource)
            .GetConstructors(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)
            .ShouldAllBe(constructor => constructor.IsFamilyAndAssembly);
    }

    [Fact]
    public void A_report_version_refuses_a_duplicate_box_or_a_box_summing_one_that_does_not_exist()
    {
        ReportBox output = Box("5", new ReportBoxSource.TaxCategoryTotals(
            new HashSet<TaxCategory> { TaxCategory.Standard },
            TaxAmountComponent.TaxAmount));

        ReportBox input = Box("11", new ReportBoxSource.AccountRoleBalances(
            new HashSet<AccountRole> { AccountRole.InputTaxRecoverable },
            BalanceSide.Debit));

        ReportBox net = Box("15", new ReportBoxSource.SumOfBoxes(["5", "11"]));

        StatutoryReportVersion.Create(
                StatutoryReportSlot.PeriodicTaxReturn,
                [output, input, net],
                Year(2026),
                ContractTestValues.Version())
            .IsSuccess.ShouldBeTrue();

        StatutoryReportVersion.Create(
                StatutoryReportSlot.PeriodicTaxReturn,
                [output, output],
                Year(2026),
                ContractTestValues.Version())
            .IsFailure.ShouldBeTrue();

        Result<StatutoryReportVersion> dangling = StatutoryReportVersion.Create(
            StatutoryReportSlot.PeriodicTaxReturn,
            [output, Box("15", new ReportBoxSource.SumOfBoxes(["5", "99"]))],
            Year(2026),
            ContractTestValues.Version());

        dangling.IsFailure.ShouldBeTrue();
        dangling.Error.Description.ShouldContain("99");
    }

    [Fact]
    public void A_report_version_in_force_on_no_day_is_refused() =>
        StatutoryReportVersion.Create(
                StatutoryReportSlot.PeriodicTaxReturn,
                [Box("5", new ReportBoxSource.SumOfBoxes([]))],
                DateRange.FromUntil(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)),
                ContractTestValues.Version())
            .IsFailure.ShouldBeTrue();

    /// <summary>One return, one currency. Two is a number no authority can read.</summary>
    [Fact]
    public void A_report_result_in_two_currencies_is_refused()
    {
        Result<StatutoryReportResult> mixed = StatutoryReportResult.Create(
            StatutoryReportSlot.PeriodicTaxReturn,
            CompanyId.Create(),
            TaxRegistrationId.Create(),
            Year(2026),
            new Dictionary<string, Money>
            {
                ["5"] = ContractTestValues.Nz(1_000m),
                ["11"] = new Money(250m, ContractTestValues.Jpy),
            });

        mixed.IsFailure.ShouldBeTrue();
        mixed.Error.Description.ShouldContain("11");
    }

    [Fact]
    public void A_report_result_answers_for_the_boxes_it_has_and_refuses_the_rest()
    {
        StatutoryReportResult result = ContractTestValues.Ok(StatutoryReportResult.Create(
            StatutoryReportSlot.PeriodicTaxReturn,
            CompanyId.Create(),
            TaxRegistrationId.Create(),
            Year(2026),
            new Dictionary<string, Money> { ["5"] = ContractTestValues.Nz(1_000m) }));

        ContractTestValues.Ok(result.ValueOf("5")).ShouldBe(ContractTestValues.Nz(1_000m));
        result.ValueOf("11").IsFailure.ShouldBeTrue();
        result.BoxCodes.ShouldBe(["5"]);
    }

    /// <summary>
    /// Two packages may not both fill one slot for one company, so the slot is a core-defined value
    /// rather than free text a package invents.
    /// </summary>
    [Fact]
    public void Report_slots_are_core_values_and_an_unnamed_one_says_so()
    {
        StatutoryReportSlot.PeriodicTaxReturn.Value.ShouldBe("PeriodicTaxReturn");
        StatutoryReportSlot.AnnualAccounts.IsSpecified.ShouldBeTrue();

        StatutoryReportSlot unassigned = default;
        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        StatutoryReportSlot.Create(" ").IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// The state that keeps an unchecked identifier from reading as a checked one. Its being the
    /// zero value is deliberate: a default-constructed status is "nobody checked", not "fine".
    /// </summary>
    [Fact]
    public void An_identifier_nobody_could_check_is_unverified_and_not_a_pass()
    {
        IdentifierValidation none = IdentifierValidation.NoValidatorInstalled(
            IdentifierKind.CompanyRegistrationNumber,
            "ZW");

        none.Status.ShouldBe(IdentifierStatus.Unverified);
        none.NormalizedValue.ShouldBeNull();
        none.Reason!.BaseText.ShouldContain("ZW");
        default(IdentifierStatus).ShouldBe(IdentifierStatus.Unverified);
    }

    [Fact]
    public void A_verified_identifier_carries_the_form_to_store_it_in()
    {
        IdentifierValidation verified = IdentifierValidation.Verified("9429041234567");

        verified.Status.ShouldBe(IdentifierStatus.Verified);
        verified.NormalizedValue.ShouldBe("9429041234567");
        verified.Reason.ShouldBeNull();
    }

    [Fact]
    public void A_rejected_identifier_carries_a_localized_reason()
    {
        IdentifierValidation rejected =
            IdentifierValidation.Rejected(ContractTestValues.Text("check digit does not hold"));

        rejected.Status.ShouldBe(IdentifierStatus.Rejected);
        rejected.NormalizedValue.ShouldBeNull();
        rejected.Reason.ShouldNotBeNull();
    }

    [Fact]
    public void A_retention_rule_states_when_a_record_may_be_destroyed()
    {
        RetentionRule rule = new(
            RecordCategory.AccountingRecord,
            7,
            Immutable: true,
            ErasureHandling.DeferUntilRetentionLapses,
            Year(2026),
            ContractTestValues.Version());

        rule.RetainUntil(new DateOnly(2026, 3, 31)).ShouldBe(new DateOnly(2033, 3, 31));
        rule.Erasure.ShouldBe(ErasureHandling.DeferUntilRetentionLapses);
    }

    [Fact]
    public void A_retention_rule_that_is_negative_or_in_force_on_no_day_is_refused()
    {
        Should.Throw<ArgumentException>(() => new RetentionRule(
            RecordCategory.AccountingRecord,
            -1,
            Immutable: true,
            ErasureHandling.EraseOnRequest,
            Year(2026),
            ContractTestValues.Version()));

        Should.Throw<ArgumentException>(() => new RetentionRule(
            RecordCategory.AccountingRecord,
            7,
            Immutable: true,
            ErasureHandling.EraseOnRequest,
            default,
            ContractTestValues.Version()));
    }

    /// <summary>
    /// A tax rule checks itself on construction, so a package that ships one without a rate, or one
    /// in force on no day, fails at install with the member named.
    /// </summary>
    [Fact]
    public void A_tax_rule_without_a_rate_a_policy_or_a_period_is_refused()
    {
        Should.Throw<ArgumentException>(() => new TaxRuleVersion(
            ContractTestValues.TaxCodeOf("GST15"),
            TaxCategory.Standard,
            default,
            TaxBasis.LineNet,
            ContractTestValues.AwayFromZero,
            Year(2026),
            ContractTestValues.Version()));

        Should.Throw<ArgumentException>(() => new TaxRuleVersion(
            ContractTestValues.TaxCodeOf("GST15"),
            TaxCategory.Standard,
            ContractTestValues.Rate(15m),
            TaxBasis.LineNet,
            (MidpointRounding)99,
            Year(2026),
            ContractTestValues.Version()));

        Should.Throw<ArgumentException>(() => new TaxRuleVersion(
            ContractTestValues.TaxCodeOf("GST15"),
            TaxCategory.Standard,
            ContractTestValues.Rate(15m),
            TaxBasis.LineNet,
            ContractTestValues.AwayFromZero,
            default,
            ContractTestValues.Version()));
    }

    /// <summary>
    /// The rule carries the jurisdiction's rounding, so applying it is one call and core never picks
    /// a midpoint on a jurisdiction's behalf.
    /// </summary>
    [Fact]
    public void A_tax_rule_applies_its_own_rate_under_its_own_rounding()
    {
        TaxRuleVersion rule = new(
            ContractTestValues.TaxCodeOf("GST15"),
            TaxCategory.Standard,
            ContractTestValues.Rate(15m),
            TaxBasis.LineNet,
            ContractTestValues.AwayFromZero,
            Year(2026),
            ContractTestValues.Version());

        rule.TaxOn(ContractTestValues.Nz(19.99m)).ShouldBe(ContractTestValues.Nz(3.00m));
    }

    private static ReportBox Box(string code, ReportBoxSource source) =>
        new(code, ContractTestValues.Text($"Box {code}"), source);

    private static DateRange Year(int year) =>
        DateRange.FromUntil(new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1));
}
