using System;
using System.Collections.Generic;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Taxation;

/// <summary>
/// Extension point 2 — the effective-dated tax rules a package contributes (ADR-0008 §7).
/// </summary>
/// <remarks>
/// <para>
/// The package supplies rules. It does not compute tax: core's tax engine resolves a rule and
/// applies it, so two jurisdictions cannot disagree about what "apply a rate to an amount" means,
/// and there is one place where the arithmetic is checked.
/// </para>
/// <para>
/// Every resolution takes an explicit business date. There is deliberately no overload without one:
/// a convenience method that defaulted to today would reprice a two-year-old invoice at today's
/// rate the first time somebody reprinted it, and that is the bug effective dating exists to
/// prevent. <c>EffectiveDatingTests</c> fails the build if such an overload is ever added.
/// </para>
/// <para>
/// Resolution is also keyed by company and tax registration, for the reason given on
/// <see cref="TaxRegistrationId"/>: v1 passes a company's single primary registration, and the
/// signature already admits the day it does not.
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.TaxRuleSet)]
public interface ITaxRuleProvider
{
    /// <summary>
    /// The period this package claims to have rules for. Outside it, resolution fails rather than
    /// extrapolating: a package that does not know the 2035 rate must say so.
    /// </summary>
    DateRange CoveragePeriod { get; }

    /// <summary>Every tax code this package defines, in any period.</summary>
    IReadOnlySet<TaxCode> TaxCodes { get; }

    /// <summary>
    /// The rule in force for <paramref name="code"/> on <paramref name="asOf"/>, or a failure
    /// naming why there is none.
    /// </summary>
    /// <param name="code">The jurisdiction's tax code, as carried on the document line.</param>
    /// <param name="company">The company the document belongs to.</param>
    /// <param name="registration">The tax registration the document is filed under.</param>
    /// <param name="asOf">
    /// The document's own business date — never <c>today</c>, and never a value the implementation
    /// picks for itself.
    /// </param>
    Result<TaxRuleVersion> Resolve(
        TaxCode code,
        CompanyId company,
        TaxRegistrationId registration,
        DateOnly asOf);

    /// <summary>
    /// Every version of <paramref name="code"/> this package has, oldest first.
    /// </summary>
    /// <remarks>
    /// The whole history rather than one version, so it needs no date. This is what the package
    /// contract test reads to prove a code's versions leave no gap across
    /// <see cref="CoveragePeriod"/> — overlaps are already impossible, because the rate table's
    /// exclusion constraint refuses them in the database.
    /// </remarks>
    IReadOnlyList<TaxRuleVersion> History(TaxCode code);
}

/// <summary>
/// Extension point 2, second half — which of a jurisdiction's codes core should reach for when all
/// it knows is the category (ADR-0008 §7).
/// </summary>
/// <remarks>
/// Core knows it is selling a standard-rated item, or exporting, or reverse-charging. It does not
/// know that the code for that is <c>GST15</c> in New Zealand. This mapping is how a
/// country-agnostic default becomes a jurisdiction's code — and it is dated, because which code
/// carries the standard rate is exactly the sort of thing a tax reform changes.
/// </remarks>
public interface ITaxCategoryMapping
{
    /// <summary>The categories this package has a default code for.</summary>
    IReadOnlySet<TaxCategory> MappedCategories { get; }

    /// <summary>
    /// The code core should use for <paramref name="category"/> on <paramref name="asOf"/>, or a
    /// failure if this jurisdiction has none.
    /// </summary>
    Result<TaxCode> DefaultCodeFor(TaxCategory category, DateOnly asOf);
}
