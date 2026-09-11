using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Countries.Contracts.Accounting;
using Aurora.Countries.Contracts.Localization;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Reporting;

/// <summary>
/// Extension point 3 — a statutory report a jurisdiction requires, declared rather than coded
/// (ADR-0008 §7).
/// </summary>
/// <remarks>
/// <para>
/// The definition is data: boxes, and what each box sums. What a box may sum is deliberately
/// narrow — account roles and tax categories, or other boxes — and <see cref="ReportBoxSource"/>
/// is a closed hierarchy, so that narrowness is enforced by the type system rather than by review.
/// </para>
/// <para>
/// The reason is stated in ADR-0008 §11: a declarative box mapping is a small DSL, and a small DSL
/// that can be extended becomes a query language. Boxes that reference roles and categories survive
/// a core schema change, because roles and categories are core's own vocabulary; boxes that could
/// reference tables would not. If a report cannot be expressed this way, the answer is a new core
/// reporting primitive — which is a MINOR core-contract bump — and not a more powerful DSL.
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.StatutoryReport)]
public interface IStatutoryReportDefinition
{
    /// <summary>The slot this report fills, such as the periodic VAT/GST return.</summary>
    StatutoryReportSlot Slot { get; }

    /// <summary>The report's name as a user sees it.</summary>
    LocalizedText Name { get; }

    /// <summary>The period this definition claims to cover. Outside it, resolution fails.</summary>
    DateRange CoveragePeriod { get; }

    /// <summary>
    /// The definition in force on <paramref name="asOf"/> — the date of the period being reported,
    /// never today. A return for a period two years ago has to be reproduced with the boxes that
    /// applied then.
    /// </summary>
    Result<StatutoryReportVersion> VersionAsOf(DateOnly asOf);

    /// <summary>Every version of this definition, oldest first.</summary>
    IReadOnlyList<StatutoryReportVersion> History();
}

/// <summary>
/// Extension point 3, second half — how a computed report is turned into the file a tax authority
/// accepts.
/// </summary>
/// <remarks>
/// Rendering only. Sending it is <see cref="Documents.IDocumentTransport"/>'s job, which is the same
/// abstraction e-invoicing sends through: filing and invoicing differ in what they send, not in what
/// sending means.
/// </remarks>
public interface IFilingFormat
{
    /// <summary>A stable id for this format, such as a published schema or return version.</summary>
    string FormatId { get; }

    /// <summary>The report slot this format files.</summary>
    StatutoryReportSlot Slot { get; }

    /// <summary>Renders a computed report into the filing format.</summary>
    Result<FilingDocument> Render(StatutoryReportResult result);
}

/// <summary>
/// The slot a statutory report fills — core's name for "the periodic VAT return", independent of
/// what any jurisdiction calls it (ADR-0008 §6.1).
/// </summary>
/// <remarks>
/// Two active packages may not fill the same slot for the same company. That is refused at install
/// rather than merged, because merging two jurisdictions' returns into one produces a number that
/// is wrong in both (ADR-0008 §8.3).
/// </remarks>
public readonly record struct StatutoryReportSlot
{
    private readonly string? _value;

    private StatutoryReportSlot(string value) => _value = value;

    /// <summary>The slot name.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw new InvalidOperationException(
        "This StatutoryReportSlot names no slot; build slots with StatutoryReportSlot.Create.");

    /// <summary>Whether this value names a slot at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>The periodic consumption-tax return — VAT return, GST return, and their like.</summary>
    public static StatutoryReportSlot PeriodicTaxReturn { get; } = new("PeriodicTaxReturn");

    /// <summary>The annual financial statements filing.</summary>
    public static StatutoryReportSlot AnnualAccounts { get; } = new("AnnualAccounts");

    /// <summary>A listing of cross-border sales, where a jurisdiction requires one.</summary>
    public static StatutoryReportSlot CrossBorderSalesListing { get; } = new("CrossBorderSalesListing");

    /// <summary>Reads a slot name, rejecting empty text.</summary>
    public static Result<StatutoryReportSlot> Create(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? PackageManifestErrors.Invalid("report.slot", "A report slot name is required.")
            : Result.Success(new StatutoryReportSlot(value));

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified report slot>";
}

/// <summary>One version of a statutory report definition, in force over a stated period.</summary>
[EffectiveDated]
public sealed class StatutoryReportVersion
{
    /// <summary>Builds a version, refusing one whose boxes are empty or duplicated.</summary>
    public static Result<StatutoryReportVersion> Create(
        StatutoryReportSlot slot,
        IReadOnlyList<ReportBox> boxes,
        DateRange validity,
        PackageVersion sourceVersion)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        if (!slot.IsSpecified)
        {
            return PackageManifestErrors.Invalid("report.slot", "A report version needs a slot.");
        }

        if (boxes.Count == 0)
        {
            return PackageManifestErrors.Invalid("report.boxes", "A report with no boxes reports nothing.");
        }

        if (validity.IsEmpty)
        {
            return PackageManifestErrors.Invalid(
                "report.validity",
                "A report version in force on no day is not a version.");
        }

        HashSet<string> codes = new(StringComparer.Ordinal);
        foreach (ReportBox box in boxes)
        {
            if (!codes.Add(box.Code))
            {
                return PackageManifestErrors.Invalid("report.boxes", $"Box '{box.Code}' appears twice.");
            }
        }

        foreach (ReportBox box in boxes)
        {
            if (box.Source is ReportBoxSource.SumOfBoxes sum)
            {
                foreach (string referenced in sum.BoxCodes)
                {
                    if (!codes.Contains(referenced))
                    {
                        return PackageManifestErrors.Invalid(
                            "report.boxes",
                            $"Box '{box.Code}' sums box '{referenced}', which this version does not " +
                            $"define.");
                    }
                }
            }
        }

        Result sums = NoBoxSumsItself(boxes);
        if (sums.IsFailure)
        {
            return sums.Error;
        }

        return Result.Success(new StatutoryReportVersion(slot, boxes, validity, sourceVersion));
    }

    /// <summary>
    /// A depth-first walk over what sums what, refusing the first box that includes itself —
    /// directly, or through any chain of other boxes.
    /// </summary>
    /// <remarks>
    /// A box that includes itself passes every shape check above and can never be computed: whatever
    /// evaluates it later either loops or overflows its stack, in a tenant, at filing time. This
    /// validation is the only gate before that, so the loop is refused here and spelled out. The
    /// walk is iterative so a package cannot choose the recursion depth of its own validation.
    /// </remarks>
    private static Result NoBoxSumsItself(IReadOnlyList<ReportBox> boxes)
    {
        Dictionary<string, IReadOnlyList<string>> summands = new(StringComparer.Ordinal);
        foreach (ReportBox box in boxes)
        {
            if (box.Source is ReportBoxSource.SumOfBoxes sum)
            {
                summands[box.Code] = sum.BoxCodes;
            }
        }

        HashSet<string> settled = new(StringComparer.Ordinal);
        foreach (string start in summands.Keys)
        {
            List<string>? loop = LoopFrom(start, summands, settled);
            if (loop is not null)
            {
                return PackageManifestErrors.Invalid("report.boxes", DescribeLoop(loop));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// The codes from the box that repeats round to itself, or <see langword="null"/> when nothing
    /// reachable from <paramref name="start"/> repeats. Every box the walk finishes with is added to
    /// <paramref name="settled"/>, so each box is walked once across all starts.
    /// </summary>
    private static List<string>? LoopFrom(
        string start,
        Dictionary<string, IReadOnlyList<string>> summands,
        HashSet<string> settled)
    {
        List<string> path = [start];
        HashSet<string> onPath = new(StringComparer.Ordinal) { start };
        Stack<int> nextSummand = new();
        nextSummand.Push(0);

        while (path.Count > 0)
        {
            string current = path[^1];
            int index = nextSummand.Pop();
            IReadOnlyList<string> codes = summands.TryGetValue(current, out IReadOnlyList<string>? found)
                ? found
                : [];

            if (index == codes.Count)
            {
                settled.Add(current);
                onPath.Remove(current);
                path.RemoveAt(path.Count - 1);
                continue;
            }

            nextSummand.Push(index + 1);
            string summand = codes[index];

            if (onPath.Contains(summand))
            {
                int first = path.IndexOf(summand);
                path.Add(summand);
                return path.GetRange(first, path.Count - first);
            }

            if (settled.Contains(summand))
            {
                continue;
            }

            path.Add(summand);
            onPath.Add(summand);
            nextSummand.Push(0);
        }

        return null;
    }

    private static string DescribeLoop(List<string> loop) =>
        loop.Count == 2
            ? $"Box '{loop[0]}' sums itself, so it can never be computed."
            : $"Box '{loop[0]}' sums '{loop[1]}'"
              + string.Concat(loop.Skip(2).Select(code => $", which sums '{code}'"))
              + ", so none of them can ever be computed.";

    private StatutoryReportVersion(
        StatutoryReportSlot slot,
        IReadOnlyList<ReportBox> boxes,
        DateRange validity,
        PackageVersion sourceVersion)
    {
        Slot = slot;
        Boxes = boxes;
        Validity = validity;
        SourceVersion = sourceVersion;
    }

    /// <summary>The slot this version fills.</summary>
    public StatutoryReportSlot Slot { get; }

    /// <summary>The boxes, in the order they appear on the return.</summary>
    public IReadOnlyList<ReportBox> Boxes { get; }

    /// <summary>The half-open period this version was in force.</summary>
    public DateRange Validity { get; }

    /// <summary>The package version that contributed it.</summary>
    public PackageVersion SourceVersion { get; }
}

/// <summary>One box on a statutory return.</summary>
/// <param name="Code">The box's code as the authority numbers it, such as <c>5</c> or <c>1A</c>.</param>
/// <param name="Label">Its label, in the locales the package ships.</param>
/// <param name="Source">What core must sum to fill it.</param>
public sealed record ReportBox(string Code, LocalizedText Label, ReportBoxSource Source);

/// <summary>
/// What a report box sums. A closed set: the only subclasses are the ones nested here, because the
/// base constructor is <c>private protected</c> and nothing outside this type can derive from it.
/// </summary>
/// <remarks>
/// This is the mechanism behind ADR-0008 §11's "deliberately restricted to stop it growing into a
/// query language". A package cannot add a case; core can, with a MINOR bump and a deliberate
/// decision about what core reporting can compute.
/// </remarks>
public abstract class ReportBoxSource
{
    private protected ReportBoxSource()
    {
    }

    /// <summary>The balance of every account filling one of a set of roles.</summary>
    /// <remarks>
    /// Roles, never account codes: the box stays correct when a tenant renumbers their chart, and
    /// the same definition works for any chart that fills the roles.
    /// </remarks>
    public sealed class AccountRoleBalances : ReportBoxSource
    {
        /// <summary>Sums the balances of the accounts filling <paramref name="roles"/>.</summary>
        public AccountRoleBalances(IReadOnlySet<AccountRole> roles, BalanceSide side)
        {
            ArgumentNullException.ThrowIfNull(roles);
            Roles = roles;
            Side = side;
        }

        /// <summary>The roles whose accounts are summed.</summary>
        public IReadOnlySet<AccountRole> Roles { get; }

        /// <summary>Which side of those accounts the box wants.</summary>
        public BalanceSide Side { get; }
    }

    /// <summary>The taxable or tax amount recorded against a set of tax categories.</summary>
    public sealed class TaxCategoryTotals : ReportBoxSource
    {
        /// <summary>Sums <paramref name="component"/> over <paramref name="categories"/>.</summary>
        public TaxCategoryTotals(IReadOnlySet<TaxCategory> categories, TaxAmountComponent component)
        {
            ArgumentNullException.ThrowIfNull(categories);
            Categories = categories;
            Component = component;
        }

        /// <summary>The categories summed.</summary>
        public IReadOnlySet<TaxCategory> Categories { get; }

        /// <summary>Which figure is summed.</summary>
        public TaxAmountComponent Component { get; }
    }

    /// <summary>Other boxes on the same return, added together.</summary>
    public sealed class SumOfBoxes : ReportBoxSource
    {
        /// <summary>Adds the boxes named by <paramref name="boxCodes"/>.</summary>
        public SumOfBoxes(IReadOnlyList<string> boxCodes)
        {
            ArgumentNullException.ThrowIfNull(boxCodes);
            BoxCodes = boxCodes;
        }

        /// <summary>The codes of the boxes added.</summary>
        public IReadOnlyList<string> BoxCodes { get; }
    }
}

/// <summary>Which side of an account balance a box wants.</summary>
public enum BalanceSide
{
    /// <summary>Debits only.</summary>
    Debit = 1,

    /// <summary>Credits only.</summary>
    Credit = 2,

    /// <summary>Debits less credits.</summary>
    Net = 3,
}

/// <summary>Which tax figure a box sums.</summary>
public enum TaxAmountComponent
{
    /// <summary>The amount the tax was calculated on.</summary>
    TaxableAmount = 1,

    /// <summary>The tax itself.</summary>
    TaxAmount = 2,
}
