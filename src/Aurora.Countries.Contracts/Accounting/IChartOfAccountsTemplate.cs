using System.Collections.Generic;
using Aurora.Countries.Contracts.Localization;

namespace Aurora.Countries.Contracts.Accounting;

/// <summary>
/// Where an account sits in the accounting equation. Core needs this much and no more: the shape of
/// a jurisdiction's own account classification belongs to the package.
/// </summary>
public enum AccountKind
{
    /// <summary>Something the company owns.</summary>
    Asset = 1,

    /// <summary>Something the company owes.</summary>
    Liability = 2,

    /// <summary>The owners' residual interest.</summary>
    Equity = 3,

    /// <summary>Revenue earned.</summary>
    Income = 4,

    /// <summary>Cost incurred.</summary>
    Expense = 5,
}

/// <summary>One account in a Country Package's chart of accounts template.</summary>
/// <param name="Code">The account's code.</param>
/// <param name="Name">The account's name, in the locales the package ships.</param>
/// <param name="Kind">Where it sits in the accounting equation.</param>
/// <param name="Parent">
/// The account this one rolls up into, or <see langword="null"/> for a top-level account.
/// </param>
public readonly record struct AccountTemplateEntry(
    AccountCode Code,
    LocalizedText Name,
    AccountKind Kind,
    AccountCode? Parent);

/// <summary>
/// Extension point 1 — the chart of accounts a package seeds into <c>ledger.account</c>
/// (ADR-0008 §7).
/// </summary>
/// <remarks>
/// <para>
/// Seeded rows are tagged <c>source='Package'</c> with this package's id and version, so "what did
/// this package add?" stays a query rather than an archaeology exercise, and a tenant's own edit to
/// a seeded account is never silently overwritten on upgrade (ADR-0008 §4.2) — the failure NetSuite
/// managed bundles have.
/// </para>
/// <para>
/// A template contributes rows to a core table. It does not, and cannot, change that table's shape:
/// a package owns exactly one schema and may not add a column anywhere else (ADR-0008 §4.1 R2).
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.ChartOfAccountsTemplate)]
public interface IChartOfAccountsTemplate
{
    /// <summary>A stable id for this template, unique within the package.</summary>
    string TemplateId { get; }

    /// <summary>The template's name, as an operator choosing a chart sees it.</summary>
    LocalizedText Name { get; }

    /// <summary>The accounts to seed. Parents must appear in the same list.</summary>
    IReadOnlyList<AccountTemplateEntry> Accounts { get; }

    /// <summary>Which of those accounts fills each role core posts to.</summary>
    IAccountRoleMapping RoleMapping { get; }
}

/// <summary>
/// The mapping from a core <see cref="AccountRole"/> to an account in a package's template
/// (ADR-0008 §6.1).
/// </summary>
/// <remarks>
/// This is the whole reason core never names an account. Core resolves
/// <see cref="AccountRole.OutputTaxPayable"/> for a company and gets back whatever the installed
/// package decided that means in this jurisdiction.
/// </remarks>
public interface IAccountRoleMapping
{
    /// <summary>The roles this mapping fills.</summary>
    IReadOnlySet<AccountRole> MappedRoles { get; }

    /// <summary>
    /// The account filling <paramref name="role"/>, if this mapping fills it.
    /// </summary>
    bool TryGetAccount(AccountRole role, out AccountCode account);
}
