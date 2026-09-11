using System;
using System.Collections.Generic;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Reporting;

/// <summary>
/// A statutory report after core has filled every box — the input to filing.
/// </summary>
/// <remarks>
/// Core computes the figures; the package renders them. That split is what keeps the arithmetic in
/// one audited place: a package that computed its own totals could disagree with the ledger, and the
/// ledger is the store of record.
/// </remarks>
public sealed class StatutoryReportResult
{
    private readonly Dictionary<string, Money> _boxValues;

    private StatutoryReportResult(
        StatutoryReportSlot slot,
        CompanyId company,
        TaxRegistrationId registration,
        DateRange period,
        Dictionary<string, Money> boxValues)
    {
        Slot = slot;
        Company = company;
        Registration = registration;
        Period = period;
        _boxValues = boxValues;
    }

    /// <summary>The slot reported.</summary>
    public StatutoryReportSlot Slot { get; }

    /// <summary>The company the return is for.</summary>
    public CompanyId Company { get; }

    /// <summary>The tax registration the return is filed under.</summary>
    public TaxRegistrationId Registration { get; }

    /// <summary>The period reported.</summary>
    public DateRange Period { get; }

    /// <summary>The box codes filled.</summary>
    public IReadOnlyCollection<string> BoxCodes => _boxValues.Keys;

    /// <summary>
    /// Builds a result, refusing an empty period or a set of boxes in more than one currency.
    /// </summary>
    /// <remarks>
    /// One return, one currency. Two currencies on one return is a mistake that renders without
    /// complaint and files a number no authority can read.
    /// </remarks>
    public static Result<StatutoryReportResult> Create(
        StatutoryReportSlot slot,
        CompanyId company,
        TaxRegistrationId registration,
        DateRange period,
        IReadOnlyDictionary<string, Money> boxValues)
    {
        ArgumentNullException.ThrowIfNull(boxValues);

        if (!slot.IsSpecified)
        {
            return PackageManifestErrors.Invalid("report.slot", "A report result needs a slot.");
        }

        if (period.IsEmpty)
        {
            return PackageManifestErrors.Invalid(
                "report.period",
                "A report covering no day at all reports nothing.");
        }

        if (boxValues.Count == 0)
        {
            return PackageManifestErrors.Invalid("report.boxValues", "A report result has no boxes.");
        }

        Currency? currency = null;
        foreach ((string code, Money value) in boxValues)
        {
            if (currency is null)
            {
                currency = value.Currency;
                continue;
            }

            if (value.Currency != currency)
            {
                return PackageManifestErrors.Invalid(
                    "report.boxValues",
                    $"Box '{code}' is in {value.Currency} but the return is in {currency}. A return " +
                    $"is filed in one currency.");
            }
        }

        return Result.Success(new StatutoryReportResult(
            slot,
            company,
            registration,
            period,
            new Dictionary<string, Money>(boxValues, StringComparer.Ordinal)));
    }

    /// <summary>The value of one box, or a failure if the report has no such box.</summary>
    public Result<Money> ValueOf(string boxCode) =>
        _boxValues.TryGetValue(boxCode, out Money value)
            ? Result.Success(value)
            : Error.NotFound(
                "country_package.report.box_missing",
                $"This {Slot} result has no box '{boxCode}'.");
}

/// <summary>A rendered filing, ready for a transport to send or a user to download.</summary>
/// <param name="FormatId">The format that produced it.</param>
/// <param name="FileName">A suggested file name.</param>
/// <param name="MediaType">The IANA media type of <paramref name="Content"/>.</param>
/// <param name="Content">The bytes.</param>
public sealed record FilingDocument(
    string FormatId,
    string FileName,
    string MediaType,
    ReadOnlyMemory<byte> Content);
