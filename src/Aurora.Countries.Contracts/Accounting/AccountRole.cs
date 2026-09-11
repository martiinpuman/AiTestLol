namespace Aurora.Countries.Contracts.Accounting;

/// <summary>
/// What core posting logic needs an account <i>for</i> — the slot a Country Package's chart of
/// accounts fills (ADR-0008 §6.1).
/// </summary>
/// <remarks>
/// <para>
/// This registry is the mechanical form of "the core must never contain
/// <c>if (country == "SE")</c>". Core code asks for <see cref="OutputTaxPayable"/>; it never names
/// 2610, or 23100, or GST Payable. The account number behind the role is jurisdiction-specific data
/// a package supplies, and an architecture fitness test bans account-number-shaped literals in the
/// domain and application layers so the shortcut cannot be taken quietly.
/// </para>
/// <para>
/// Adding a role is a MINOR core-contract bump, and it makes every existing package incomplete for
/// the posting that needs it — which is why <see cref="AccountRoles.RequiredForPosting"/> is a
/// separate, smaller set than "all of them": a package is only ever refused for a role that core
/// genuinely cannot post without.
/// </para>
/// </remarks>
public enum AccountRole
{
    /// <summary>The control account customer invoices are posted to.</summary>
    AccountsReceivableControl = 1,

    /// <summary>The control account supplier invoices are posted to.</summary>
    AccountsPayableControl = 2,

    /// <summary>Tax charged on sales and owed to the tax authority.</summary>
    OutputTaxPayable = 3,

    /// <summary>Tax paid on purchases and recoverable from the tax authority.</summary>
    InputTaxRecoverable = 4,

    /// <summary>The asset account stock on hand is carried at.</summary>
    InventoryAsset = 5,

    /// <summary>Where the cost of goods sold is recognised when stock leaves.</summary>
    CostOfGoodsSold = 6,

    /// <summary>Where a gain or loss from a change in exchange rate lands.</summary>
    FxGainLoss = 7,

    /// <summary>Accumulated result carried forward between financial years.</summary>
    RetainedEarnings = 8,

    /// <summary>
    /// Where the residual of a rounded allocation lands, so a document still balances to the cent
    /// (ADR-0021).
    /// </summary>
    RoundingResidual = 9,

    /// <summary>Where a posting goes when its real account is not yet known.</summary>
    Suspense = 10,
}
