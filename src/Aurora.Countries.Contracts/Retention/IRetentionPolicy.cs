using System;
using System.Collections.Generic;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Retention;

/// <summary>
/// The kinds of record a jurisdiction sets a retention period for. A core registry, like
/// <see cref="Accounting.AccountRole"/>: core knows the categories, the package knows the years.
/// </summary>
public enum RecordCategory
{
    /// <summary>Ledger entries, journals and the books of account.</summary>
    AccountingRecord = 1,

    /// <summary>Tax returns, tax calculations and their supporting evidence.</summary>
    TaxRecord = 2,

    /// <summary>Invoices, credit notes, orders and delivery documents.</summary>
    CommercialDocument = 3,

    /// <summary>Payroll records, where a jurisdiction sets a separate period for them.</summary>
    PayrollRecord = 4,

    /// <summary>Customer and supplier master data, including personal data.</summary>
    PartyMasterData = 5,
}

/// <summary>What happens when a person asks to be erased and a retention period has not lapsed.</summary>
/// <remarks>
/// The two answers a jurisdiction can give, and they are not interchangeable. Erasing an accounting
/// record on request breaks the statutory books; refusing erasure forever breaks the data-protection
/// right. Deferral is how both hold: the erasure is scheduled for the day retention lapses
/// (ADR-0007 §11.5, ADR-0018).
/// </remarks>
public enum ErasureHandling
{
    /// <summary>Erase when asked. The record carries no statutory retention.</summary>
    EraseOnRequest = 1,

    /// <summary>Record the request and erase on the day the retention period lapses.</summary>
    DeferUntilRetentionLapses = 2,
}

/// <summary>
/// How long one category of record must be kept, under the law in force over a stated period.
/// </summary>
/// <param name="Category">What the rule is about.</param>
/// <param name="MinimumYears">The minimum retention, in years from the end of the financial year.</param>
/// <param name="Immutable">Whether the record may not be altered once created.</param>
/// <param name="Erasure">What an erasure request does before retention lapses.</param>
/// <param name="Validity">The period this rule was the law.</param>
/// <param name="SourceVersion">The package version that contributed it.</param>
[EffectiveDated]
public sealed record RetentionRule(
    RecordCategory Category,
    int MinimumYears,
    bool Immutable,
    ErasureHandling Erasure,
    DateRange Validity,
    PackageVersion SourceVersion)
{
    /// <summary>The category this rule governs.</summary>
    /// <remarks>
    /// Each member re-declares its parameter so the rule checks itself: a retention rule with a
    /// negative period, or one in force on no day, would install cleanly and then decide when
    /// somebody's data is destroyed.
    /// </remarks>
    /// <exception cref="ArgumentException">The category is not a defined one.</exception>
    public RecordCategory Category { get; } = Enum.IsDefined(Category)
        ? Category
        : throw new ArgumentException($"'{Category}' is not a record category.", nameof(Category));

    /// <summary>The minimum retention, in years.</summary>
    public int MinimumYears { get; } = MinimumYears >= 0
        ? MinimumYears
        : throw new ArgumentException(
            "A retention period cannot be negative.",
            nameof(MinimumYears));

    /// <summary>What an erasure request does before retention lapses.</summary>
    public ErasureHandling Erasure { get; } = Enum.IsDefined(Erasure)
        ? Erasure
        : throw new ArgumentException($"'{Erasure}' is not an erasure handling.", nameof(Erasure));

    /// <summary>The period this rule was the law.</summary>
    public DateRange Validity { get; } = Validity.IsEmpty
        ? throw new ArgumentException("A retention rule in force on no day is not a rule.", nameof(Validity))
        : Validity;

    /// <summary>The package version that contributed it.</summary>
    public PackageVersion SourceVersion { get; } = SourceVersion.IsSpecified
        ? SourceVersion
        : throw new ArgumentException(
            "A contributed rule records the package version it came from.",
            nameof(SourceVersion));

    /// <summary>
    /// The first day a record created on <paramref name="recordDate"/> may be destroyed under this
    /// rule.
    /// </summary>
    public DateOnly RetainUntil(DateOnly recordDate) => recordDate.AddYears(MinimumYears);
}

/// <summary>
/// Extension point 9 — how long a jurisdiction requires records to be kept (ADR-0008 §7).
/// </summary>
/// <remarks>
/// Pure declaration: the package states the rule, core enforces it. The researcher flagged this
/// point as not evidenced against a primary legal source, which is a reason to be careful about the
/// <i>values</i> a package ships, not about the shape of the contract — a declaration cannot be
/// wrong about anything except the law it states, and that is the package author's to get right and
/// to cite.
/// </remarks>
[ExtensionPoint(CountryPackageCapability.RetentionPolicy)]
public interface IRetentionPolicy
{
    /// <summary>The categories this package has rules for.</summary>
    IReadOnlySet<RecordCategory> Categories { get; }

    /// <summary>
    /// The rule in force for <paramref name="category"/> on <paramref name="asOf"/> — the date the
    /// record was created, not today. A record created under a seven-year rule stays under it when
    /// the law later changes to five.
    /// </summary>
    Result<RetentionRule> RuleFor(RecordCategory category, DateOnly asOf);

    /// <summary>Every version of the rule for <paramref name="category"/>, oldest first.</summary>
    IReadOnlyList<RetentionRule> History(RecordCategory category);
}
