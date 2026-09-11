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
        }

        Result rollUp = EveryRollUpReachesTheTop(template.Accounts);
        if (rollUp.IsFailure)
        {
            return rollUp;
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

    /// <summary>
    /// Follows every account's parent chain up to a top-level account, refusing the first chain that
    /// comes back round to an account already on it.
    /// </summary>
    /// <remarks>
    /// A chart whose roll-up loops passes every check above and can never be totalled: whatever
    /// walks it later — a trial balance, a report box over a role — loops with it, in a tenant, at
    /// period end. This validation is the only gate before that, so the loop is refused here and
    /// spelled out. Every parent is already known to be defined, so the chain can be followed by
    /// lookup, and each account is followed once across all chains.
    /// </remarks>
    private static Result EveryRollUpReachesTheTop(IReadOnlyList<AccountTemplateEntry> accounts)
    {
        Dictionary<AccountCode, AccountCode?> parentOf =
            accounts.ToDictionary(account => account.Code, account => account.Parent);
        HashSet<AccountCode> reachesTheTop = [];

        foreach (AccountTemplateEntry account in accounts)
        {
            List<AccountCode> chain = [];
            HashSet<AccountCode> onChain = [];

            for (AccountCode? current = account.Code;
                 current is { } code && !reachesTheTop.Contains(code);
                 current = parentOf[code])
            {
                if (!onChain.Add(code))
                {
                    int first = chain.IndexOf(code);
                    return PackageManifestErrors.Invalid(
                        "chartOfAccounts.accounts",
                        DescribeLoop(chain.GetRange(first, chain.Count - first)));
                }

                chain.Add(code);
            }

            reachesTheTop.UnionWith(chain);
        }

        return Result.Success();
    }

    private static string DescribeLoop(List<AccountCode> loop) =>
        loop.Count == 1
            ? $"Account '{loop[0]}' is its own parent."
            : $"Account '{loop[0]}' rolls up into '{loop[1]}'"
              + string.Concat(loop.Skip(2).Select(code => $", which rolls up into '{code}'"))
              + $", which rolls up into '{loop[0]}', so none of them ever reaches a top-level account.";
}
