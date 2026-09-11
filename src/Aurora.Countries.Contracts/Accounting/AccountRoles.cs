using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Accounting;

/// <summary>
/// The roles core cannot post without, and the check that a package's chart of accounts fills them.
/// </summary>
public static class AccountRoles
{
    /// <summary>
    /// The roles a chart of accounts template must map before it can be installed.
    /// </summary>
    /// <remarks>
    /// Deliberately smaller than the full registry. A tenant that never holds stock does not need
    /// <see cref="AccountRole.InventoryAsset"/>, and refusing their package over it would be the
    /// contract inventing a requirement the business does not have. These five are the ones core
    /// posts to for any company that invoices anybody: without them the first sales invoice fails,
    /// and failing at install with a named role is a better day than failing at the first posting.
    /// </remarks>
    public static IReadOnlySet<AccountRole> RequiredForPosting { get; } =
        new ReadOnlySet<AccountRole>(new HashSet<AccountRole>
        {
            AccountRole.AccountsReceivableControl,
            AccountRole.AccountsPayableControl,
            AccountRole.OutputTaxPayable,
            AccountRole.InputTaxRecoverable,
            AccountRole.RoundingResidual,
        });

    /// <summary>
    /// Checks a template the way the installer does: every required role mapped, every mapped role
    /// pointing at an account the template actually defines, no duplicate codes, and every parent
    /// present.
    /// </summary>
    /// <remarks>
    /// All of this is checked before a single row is seeded. A template that maps
    /// <see cref="AccountRole.OutputTaxPayable"/> to an account it does not define would otherwise
    /// install cleanly and fail on the first tax posting, in a tenant, in production.
    /// </remarks>
    public static Result Validate(IChartOfAccountsTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.Accounts.Count == 0)
        {
            return PackageManifestErrors.Invalid(
                "chartOfAccounts.accounts",
                "A chart of accounts template with no accounts cannot be seeded.");
        }

        HashSet<AccountCode> defined = [];
        foreach (AccountTemplateEntry account in template.Accounts)
        {
            if (!account.Code.IsSpecified)
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.accounts",
                    "An account in the template has no code.");
            }

            if (!defined.Add(account.Code))
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.accounts",
                    $"Account '{account.Code}' is defined more than once.");
            }

            if (!Enum.IsDefined(account.Kind))
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.accounts",
                    $"Account '{account.Code}' has no valid account kind.");
            }
        }

        foreach (AccountTemplateEntry account in template.Accounts)
        {
            if (account.Parent is { } parent && !defined.Contains(parent))
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.accounts",
                    $"Account '{account.Code}' rolls up into '{parent}', which the template does not " +
                    $"define.");
            }

            if (account.Parent == account.Code)
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.accounts",
                    $"Account '{account.Code}' is its own parent.");
            }
        }

        AccountRole[] unmapped = [.. RequiredForPosting.Except(template.RoleMapping.MappedRoles).Order()];
        if (unmapped.Length > 0)
        {
            return PackageManifestErrors.Invalid(
                "chartOfAccounts.roleMapping",
                $"No account is mapped to [{string.Join(", ", unmapped)}]. Core posts to these by " +
                $"role and never by number, so a template that leaves one empty cannot carry an " +
                $"invoice.");
        }

        foreach (AccountRole role in template.RoleMapping.MappedRoles.Order())
        {
            if (!template.RoleMapping.TryGetAccount(role, out AccountCode account))
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.roleMapping",
                    $"'{role}' is listed as mapped but resolves to nothing.");
            }

            if (!defined.Contains(account))
            {
                return PackageManifestErrors.Invalid(
                    "chartOfAccounts.roleMapping",
                    $"'{role}' is mapped to account '{account}', which the template does not define.");
            }
        }

        return Result.Success();
    }
}
