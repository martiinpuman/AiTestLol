using System;
using System.Collections.Generic;
using Aurora.Countries.Contracts.Accounting;
using Aurora.Countries.Contracts.Localization;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// Values the tests need and the contract deliberately makes hard to build by accident.
/// </summary>
/// <remarks>
/// Every factory in the contract returns a <see cref="Result{TValue}"/>, which is what stops a
/// package handing core a malformed value. <see cref="Ok{T}"/> is how a test says "this input is
/// known-good and is not what I am testing" without swallowing the failure if it ever stops being
/// good.
/// </remarks>
internal static class ContractTestValues
{
    /// <summary>Two minor units — the ordinary case.</summary>
    internal static Currency Nzd { get; } = Currency.Of("NZD", 2);

    /// <summary>Zero minor units, so nothing can pass by assuming cents.</summary>
    internal static Currency Jpy { get; } = Currency.Of("JPY", 0);

    /// <summary>Three minor units, the other side of the same assumption.</summary>
    internal static Currency Bhd { get; } = Currency.Of("BHD", 3);

    internal const MidpointRounding AwayFromZero = MidpointRounding.AwayFromZero;

    internal static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"A value the test treats as known-good was rejected: {result.Error}");

    internal static PackageVersion Version(string value = "1.4.0") => Ok(PackageVersion.Create(value));

    internal static TaxCode TaxCodeOf(string value) => Ok(TaxCode.Create(value));

    internal static AccountCode AccountCodeOf(string value) => Ok(AccountCode.Create(value));

    internal static LocalizedText Text(string value) => Ok(LocalizedText.Invariant(value));

    internal static TaxRate Rate(decimal percent) => Ok(TaxRate.Create(Percentage.FromPercent(percent)));

    internal static Money Nz(decimal amount) => new(amount, Nzd);

    /// <summary>A chart of accounts that fills every role core needs to post.</summary>
    internal static ChartOfAccounts CompleteChart()
    {
        Dictionary<AccountRole, AccountCode> mapping = new()
        {
            [AccountRole.AccountsReceivableControl] = AccountCodeOf("1100"),
            [AccountRole.AccountsPayableControl] = AccountCodeOf("2100"),
            [AccountRole.OutputTaxPayable] = AccountCodeOf("2200"),
            [AccountRole.InputTaxRecoverable] = AccountCodeOf("1200"),
            [AccountRole.RoundingResidual] = AccountCodeOf("8900"),
        };

        List<AccountTemplateEntry> accounts =
        [
            new(AccountCodeOf("1100"), Text("Accounts receivable"), AccountKind.Asset, null),
            new(AccountCodeOf("1200"), Text("Input tax"), AccountKind.Asset, null),
            new(AccountCodeOf("2100"), Text("Accounts payable"), AccountKind.Liability, null),
            new(AccountCodeOf("2200"), Text("Output tax"), AccountKind.Liability, null),
            new(AccountCodeOf("8900"), Text("Rounding"), AccountKind.Expense, null),
        ];

        return new ChartOfAccounts(accounts, mapping);
    }
}

/// <summary>A chart of accounts template a test controls completely.</summary>
internal sealed class ChartOfAccounts : IChartOfAccountsTemplate, IAccountRoleMapping
{
    private readonly Dictionary<AccountRole, AccountCode> _mapping;

    internal ChartOfAccounts(
        IReadOnlyList<AccountTemplateEntry> accounts,
        Dictionary<AccountRole, AccountCode> mapping)
    {
        Accounts = accounts;
        _mapping = mapping;
    }

    public string TemplateId => "test.chart";

    public LocalizedText Name => ContractTestValues.Text("Test chart");

    public IReadOnlyList<AccountTemplateEntry> Accounts { get; }

    public IAccountRoleMapping RoleMapping => this;

    public IReadOnlySet<AccountRole> MappedRoles => new HashSet<AccountRole>(_mapping.Keys);

    public bool TryGetAccount(AccountRole role, out AccountCode account) =>
        _mapping.TryGetValue(role, out account);
}
