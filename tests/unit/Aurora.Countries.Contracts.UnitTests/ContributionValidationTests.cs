using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Aurora.Countries.Contracts.Accounting;
using Aurora.Countries.Contracts.Banking;
using Aurora.Countries.Contracts.Interchange;
using Aurora.Countries.Contracts.Localization;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// What core refuses to accept from a package. Every rule here would otherwise install cleanly and
/// produce a wrong number later, somewhere nobody is looking.
/// </summary>
public sealed class ContributionValidationTests
{
    [Fact]
    public void A_chart_that_fills_every_role_core_posts_to_is_accepted() =>
        AccountRoles.Validate(ContractTestValues.CompleteChart()).IsSuccess.ShouldBeTrue();

    /// <summary>
    /// Core posts to an account by role. A template that leaves a required role empty installs and
    /// then fails on the first invoice — in a tenant, in production.
    /// </summary>
    [Fact]
    public void A_chart_missing_a_role_core_cannot_post_without_is_refused_naming_the_role()
    {
        ChartOfAccounts chart = ContractTestValues.CompleteChart();
        Dictionary<AccountRole, AccountCode> incomplete = chart.MappedRoles
            .Where(role => role != AccountRole.OutputTaxPayable)
            .ToDictionary(role => role, role => Account(chart, role));

        Result validation = AccountRoles.Validate(new ChartOfAccounts(chart.Accounts, incomplete));

        validation.IsFailure.ShouldBeTrue();
        validation.Error.Description.ShouldContain(nameof(AccountRole.OutputTaxPayable));
    }

    [Fact]
    public void A_role_mapped_to_an_account_the_template_does_not_define_is_refused()
    {
        ChartOfAccounts chart = ContractTestValues.CompleteChart();
        Dictionary<AccountRole, AccountCode> mapping =
            chart.MappedRoles.ToDictionary(role => role, role => Account(chart, role));
        mapping[AccountRole.OutputTaxPayable] = ContractTestValues.AccountCodeOf("9999");

        Result validation = AccountRoles.Validate(new ChartOfAccounts(chart.Accounts, mapping));

        validation.IsFailure.ShouldBeTrue();
        validation.Error.Description.ShouldContain("9999");
    }

    [Fact]
    public void A_chart_with_a_duplicate_account_or_a_missing_parent_is_refused()
    {
        ChartOfAccounts chart = ContractTestValues.CompleteChart();
        Dictionary<AccountRole, AccountCode> mapping =
            chart.MappedRoles.ToDictionary(role => role, role => Account(chart, role));

        List<AccountTemplateEntry> duplicated = [.. chart.Accounts, chart.Accounts[0]];
        AccountRoles.Validate(new ChartOfAccounts(duplicated, mapping)).IsFailure.ShouldBeTrue();

        List<AccountTemplateEntry> orphaned =
        [
            .. chart.Accounts,
            new AccountTemplateEntry(
                ContractTestValues.AccountCodeOf("1110"),
                ContractTestValues.Text("Trade debtors"),
                AccountKind.Asset,
                ContractTestValues.AccountCodeOf("1000")),
        ];
        AccountRoles.Validate(new ChartOfAccounts(orphaned, mapping)).IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// A chart whose roll-up never reaches a top-level account cannot be totalled, and nothing
    /// downstream is guarded against walking it forever. Install-time validation is the only gate
    /// before core does, so the loop is refused here and spelled out.
    /// </summary>
    [Fact]
    public void An_account_that_is_its_own_parent_is_refused()
    {
        Result validation = AccountRoles.Validate(ChartWith(
            Entry("1000", parent: "1000")));

        validation.IsFailure.ShouldBeTrue();
        validation.Error.Description.ShouldContain("Account '1000' is its own parent");
    }

    [Fact]
    public void Two_accounts_that_roll_up_into_each_other_are_refused_naming_the_loop()
    {
        Result validation = AccountRoles.Validate(ChartWith(
            Entry("1000", parent: "1010"),
            Entry("1010", parent: "1000")));

        validation.IsFailure.ShouldBeTrue();
        validation.Error.Description.ShouldContain(
            "Account '1000' rolls up into '1010', which rolls up into '1000'");
    }

    /// <summary>
    /// The required set is smaller than the whole registry on purpose: a tenant that never holds
    /// stock does not need an inventory account, and refusing their package over one would be the
    /// contract inventing a requirement the business does not have.
    /// </summary>
    [Fact]
    public void Only_the_roles_core_cannot_post_without_are_required()
    {
        AccountRoles.RequiredForPosting.Count.ShouldBeLessThan(Enum.GetValues<AccountRole>().Length);
        AccountRoles.RequiredForPosting.ShouldContain(AccountRole.OutputTaxPayable);
        AccountRoles.RequiredForPosting.ShouldNotContain(AccountRole.InventoryAsset);
    }

    /// <summary>
    /// A parser that quietly drops a line it did not understand produces a statement that looks
    /// fine and reconciles wrong.
    /// </summary>
    [Fact]
    public void A_statement_whose_lines_do_not_carry_its_balances_is_refused()
    {
        BankAccountReference account = new("NZ0112340000000001", null, null);
        DateRange period = DateRange.FromUntil(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));

        List<BankStatementLine> lines =
        [
            Line(new DateOnly(2026, 1, 5), -250.00m),
            Line(new DateOnly(2026, 1, 9), 1_000.00m),
        ];

        BankStatement.Create(account, period, ContractTestValues.Nz(100m), ContractTestValues.Nz(850m), lines)
            .IsSuccess.ShouldBeTrue();

        Result<BankStatement> dropped = BankStatement.Create(
            account,
            period,
            ContractTestValues.Nz(100m),
            ContractTestValues.Nz(850m),
            [lines[0]]);

        dropped.IsFailure.ShouldBeTrue();
        dropped.Error.Description.ShouldContain("closing");
    }

    [Fact]
    public void A_statement_line_outside_its_own_period_or_in_another_currency_is_refused()
    {
        BankAccountReference account = new("NZ0112340000000001", null, null);
        DateRange period = DateRange.FromUntil(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));

        BankStatement.Create(
                account,
                period,
                ContractTestValues.Nz(0m),
                ContractTestValues.Nz(10m),
                [Line(new DateOnly(2026, 2, 3), 10.00m)])
            .IsFailure.ShouldBeTrue();

        BankStatement.Create(
                account,
                period,
                ContractTestValues.Nz(0m),
                ContractTestValues.Nz(10m),
                [
                    new BankStatementLine(
                        new DateOnly(2026, 1, 3),
                        new DateOnly(2026, 1, 3),
                        new Money(10m, ContractTestValues.Jpy),
                        null,
                        null,
                        null),
                ])
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// A duplicate end-to-end reference is how a supplier gets paid twice: the bank takes both, and
    /// the second reconciles against the first one's invoice.
    /// </summary>
    [Fact]
    public void A_payment_batch_with_a_repeated_reference_is_refused()
    {
        Result<PaymentInstructionBatch> batch = PaymentInstructionBatch.Create(
            CompanyId.Create(),
            new BankAccountReference("NZ0112340000000001", null, null),
            new DateOnly(2026, 3, 1),
            [Payment("E2E-1", 100m), Payment("E2E-1", 250m)]);

        batch.IsFailure.ShouldBeTrue();
        batch.Error.Description.ShouldContain("E2E-1");
    }

    [Fact]
    public void A_payment_batch_totals_its_own_instructions()
    {
        PaymentInstructionBatch batch = ContractTestValues.Ok(PaymentInstructionBatch.Create(
            CompanyId.Create(),
            new BankAccountReference("NZ0112340000000001", null, null),
            new DateOnly(2026, 3, 1),
            [Payment("E2E-1", 100m), Payment("E2E-2", 250.55m)]));

        batch.Total.ShouldBe(ContractTestValues.Nz(350.55m));
    }

    [Fact]
    public void A_payment_that_is_negative_or_unpayable_is_refused()
    {
        CompanyId company = CompanyId.Create();
        BankAccountReference debtor = new("NZ0112340000000001", null, null);
        DateOnly execution = new(2026, 3, 1);

        PaymentInstructionBatch.Create(company, debtor, execution, [Payment("E2E-1", -100m)])
            .IsFailure.ShouldBeTrue();
        PaymentInstructionBatch.Create(company, debtor, execution, [Payment("E2E-1", 10.005m)])
            .IsFailure.ShouldBeTrue();
        PaymentInstructionBatch.Create(company, debtor, execution, [])
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>An entry that does not balance is not an entry.</summary>
    [Fact]
    public void An_unbalanced_journal_entry_is_refused()
    {
        Result<JournalEntryRecord> unbalanced = JournalEntryRecord.Create(
            "JE-1",
            new DateOnly(2026, 1, 31),
            null,
            [
                new JournalLineRecord(
                    ContractTestValues.AccountCodeOf("1100"),
                    ContractTestValues.Nz(115m),
                    ContractTestValues.Nz(0m),
                    null),
                new JournalLineRecord(
                    ContractTestValues.AccountCodeOf("4000"),
                    ContractTestValues.Nz(0m),
                    ContractTestValues.Nz(100m),
                    null),
            ]);

        unbalanced.IsFailure.ShouldBeTrue();
        unbalanced.Error.Description.ShouldContain("balance");
    }

    [Fact]
    public void A_balanced_journal_entry_reports_its_own_total()
    {
        JournalEntryRecord entry = ContractTestValues.Ok(JournalEntryRecord.Create(
            "JE-1",
            new DateOnly(2026, 1, 31),
            ContractTestValues.Text("Sales invoice 1001"),
            [
                new JournalLineRecord(
                    ContractTestValues.AccountCodeOf("1100"),
                    ContractTestValues.Nz(115m),
                    ContractTestValues.Nz(0m),
                    null),
                new JournalLineRecord(
                    ContractTestValues.AccountCodeOf("4000"),
                    ContractTestValues.Nz(0m),
                    ContractTestValues.Nz(100m),
                    null),
                new JournalLineRecord(
                    ContractTestValues.AccountCodeOf("2200"),
                    ContractTestValues.Nz(0m),
                    ContractTestValues.Nz(15m),
                    ContractTestValues.TaxCodeOf("GST15")),
            ]));

        entry.Total.ShouldBe(ContractTestValues.Nz(115m));
        entry.Lines.Count.ShouldBe(3);
    }

    [Fact]
    public void A_line_that_is_both_a_debit_and_a_credit_is_refused() =>
        JournalEntryRecord.Create(
                "JE-1",
                new DateOnly(2026, 1, 31),
                null,
                [
                    new JournalLineRecord(
                        ContractTestValues.AccountCodeOf("1100"),
                        ContractTestValues.Nz(10m),
                        ContractTestValues.Nz(10m),
                        null),
                ])
            .IsFailure.ShouldBeTrue();

    /// <summary>
    /// Counts rather than a bare success, so an export that moved nothing can say so.
    /// </summary>
    [Fact]
    public void Interchange_counts_report_an_empty_run_as_empty()
    {
        new InterchangeCounts(0, 0, 0).IsEmpty.ShouldBeTrue();
        new InterchangeCounts(5, 120, 340).IsEmpty.ShouldBeFalse();
        new InterchangeCounts(5, 120, 340).ToString().ShouldContain("120");
    }

    /// <summary>
    /// A package supplies user-facing text — account names, box labels — and it falls back the same
    /// way the resource manager does, so a package can ship one locale and add another later.
    /// </summary>
    [Fact]
    public void Localized_text_falls_back_through_a_culture_to_its_base()
    {
        LocalizedText text = ContractTestValues.Ok(LocalizedText.Create(
            "Goods and services tax",
            new Dictionary<string, string>
            {
                ["mi"] = "Tāke hokohoko",
            }));

        text.For(CultureInfo.GetCultureInfo("mi-NZ")).ShouldBe("Tāke hokohoko");
        text.For(CultureInfo.GetCultureInfo("mi")).ShouldBe("Tāke hokohoko");
        text.For(CultureInfo.GetCultureInfo("en-NZ")).ShouldBe("Goods and services tax");
        text.For(CultureInfo.InvariantCulture).ShouldBe("Goods and services tax");
    }

    [Fact]
    public void Localized_text_refuses_an_empty_rendering_or_an_unknown_culture()
    {
        LocalizedText.Invariant("  ").IsFailure.ShouldBeTrue();
        LocalizedText.Create("Tax", new Dictionary<string, string> { ["mi"] = " " }).IsFailure.ShouldBeTrue();
        LocalizedText.Create("Tax", new Dictionary<string, string> { ["not-a-locale"] = "x" })
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// Applying a locale override must not reach the shared, cached culture every other tenant in
    /// the process is formatting with.
    /// </summary>
    [Fact]
    public void A_locale_override_applies_to_a_copy_and_never_to_the_shared_culture()
    {
        CultureInfo shared = CultureInfo.GetCultureInfo("en-NZ");
        string originalPattern = shared.DateTimeFormat.ShortDatePattern;

        LocaleFormats formats = ContractTestValues.Ok(LocaleFormats.Create(
            shared,
            shortDatePattern: "yyyy-MM-dd",
            currencySymbol: "NZ$"));

        CultureInfo applied = formats.Apply();

        formats.HasOverrides.ShouldBeTrue();
        applied.DateTimeFormat.ShortDatePattern.ShouldBe("yyyy-MM-dd");
        applied.NumberFormat.CurrencySymbol.ShouldBe("NZ$");
        CultureInfo.GetCultureInfo("en-NZ").DateTimeFormat.ShortDatePattern.ShouldBe(originalPattern);
    }

    [Fact]
    public void A_locale_pack_that_states_no_override_is_still_a_locale_pack() =>
        ContractTestValues.Ok(LocaleFormats.Create(CultureInfo.GetCultureInfo("en-NZ")))
            .HasOverrides.ShouldBeFalse();

    private static AccountCode Account(ChartOfAccounts chart, AccountRole role)
    {
        chart.TryGetAccount(role, out AccountCode account).ShouldBeTrue();
        return account;
    }

    /// <summary>The complete chart, plus <paramref name="extra"/> accounts that fill no role.</summary>
    private static ChartOfAccounts ChartWith(params AccountTemplateEntry[] extra)
    {
        ChartOfAccounts chart = ContractTestValues.CompleteChart();
        Dictionary<AccountRole, AccountCode> mapping =
            chart.MappedRoles.ToDictionary(role => role, role => Account(chart, role));

        return new ChartOfAccounts([.. chart.Accounts, .. extra], mapping);
    }

    private static AccountTemplateEntry Entry(string code, string parent) =>
        new(
            ContractTestValues.AccountCodeOf(code),
            ContractTestValues.Text($"Account {code}"),
            AccountKind.Asset,
            ContractTestValues.AccountCodeOf(parent));

    private static BankStatementLine Line(DateOnly date, decimal amount) =>
        new(date, date, ContractTestValues.Nz(amount), null, null, null);

    private static PaymentInstruction Payment(string reference, decimal amount) =>
        new(reference,
            new BankAccountReference("NZ0198760000000002", null, null),
            "A supplier",
            ContractTestValues.Nz(amount),
            null);
}
