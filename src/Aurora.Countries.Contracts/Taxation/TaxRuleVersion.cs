using System;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Taxation;

/// <summary>
/// How a jurisdiction groups a tax code, using the internationally standardised categories of the
/// EN 16931 semantic model (UNCL 5305).
/// </summary>
/// <remarks>
/// A core registry, not a package invention. Core reporting and e-invoicing both group by category,
/// and if every package named its own groups nothing could be summed across two jurisdictions or
/// mapped into one e-invoice. The package's own codes are as specific as it likes; the category is
/// how core understands them.
/// </remarks>
public enum TaxCategory
{
    /// <summary>Standard rate (UNCL 5305 <c>S</c>).</summary>
    Standard = 1,

    /// <summary>Zero rated (<c>Z</c>) — taxable, at zero percent, and reported as taxable.</summary>
    ZeroRated = 2,

    /// <summary>Exempt (<c>E</c>) — not taxable, which is not the same as taxed at zero.</summary>
    Exempt = 3,

    /// <summary>Reverse charge (<c>AE</c>) — the customer accounts for the tax.</summary>
    ReverseCharge = 4,

    /// <summary>Intra-community supply (<c>K</c>).</summary>
    IntraCommunitySupply = 5,

    /// <summary>Export outside the tax territory (<c>G</c>).</summary>
    Export = 6,

    /// <summary>Outside the scope of tax (<c>O</c>).</summary>
    OutOfScope = 7,
}

/// <summary>What a tax rate is applied to.</summary>
public enum TaxBasis
{
    /// <summary>Each line's net amount, taxed and rounded line by line.</summary>
    LineNet = 1,

    /// <summary>The document's net total per tax code, taxed and rounded once.</summary>
    DocumentNet = 2,
}

/// <summary>
/// One version of one tax rule: what a code meant, over the period it meant it (ADR-0008 §6.2).
/// </summary>
/// <remarks>
/// <para>
/// Effective dating is the whole point. A rate change ships as a new version of this row plus a
/// close-out of the previous one — data, not a deployment — and re-printing a two-year-old invoice
/// resolves the version that was in force on the invoice's date, not the one in force today.
/// </para>
/// <para>
/// The rounding policy travels with the rule rather than being chosen by core, because how tax is
/// rounded is part of what a jurisdiction decides. <see cref="TaxOn"/> is where the two meet.
/// </para>
/// </remarks>
/// <param name="Code">The jurisdiction's code for this treatment.</param>
/// <param name="Category">How core should group it.</param>
/// <param name="Rate">The rate in force over <paramref name="Validity"/>.</param>
/// <param name="Basis">What the rate applies to.</param>
/// <param name="Rounding">How the resulting tax is rounded, per this jurisdiction's rule.</param>
/// <param name="Validity">The half-open period this version was in force.</param>
/// <param name="SourceVersion">The package version that contributed it, for audit.</param>
[EffectiveDated]
public sealed record TaxRuleVersion(
    TaxCode Code,
    TaxCategory Category,
    TaxRate Rate,
    TaxBasis Basis,
    RoundingPolicy Rounding,
    DateRange Validity,
    PackageVersion SourceVersion)
{
    /// <summary>
    /// The jurisdiction's code for this treatment.
    /// </summary>
    /// <remarks>
    /// Each member below re-declares its parameter so the rule checks itself on construction. A
    /// package that ships a rule with no rate, or one in force on no day, fails at install with the
    /// member named — rather than at the first posting, in a tenant, with a tax figure that is
    /// quietly wrong.
    /// </remarks>
    /// <exception cref="ArgumentException">The code is unspecified.</exception>
    public TaxCode Code { get; } = Code.IsSpecified
        ? Code
        : throw new ArgumentException("A tax rule needs a tax code.", nameof(Code));

    /// <summary>How core groups this rule for reporting and e-invoicing.</summary>
    public TaxCategory Category { get; } = Enum.IsDefined(Category)
        ? Category
        : throw new ArgumentException($"'{Category}' is not a tax category.", nameof(Category));

    /// <summary>The rate in force over <see cref="Validity"/>.</summary>
    public TaxRate Rate { get; } = Rate.IsSpecified
        ? Rate
        : throw new ArgumentException(
            "A tax rule needs a rate. Use TaxRate.Zero for a zero-rated supply — a rate nobody set " +
            "and a rate set to zero are different facts.",
            nameof(Rate));

    /// <summary>What the rate applies to.</summary>
    public TaxBasis Basis { get; } = Enum.IsDefined(Basis)
        ? Basis
        : throw new ArgumentException($"'{Basis}' is not a tax basis.", nameof(Basis));

    /// <summary>How the resulting tax is rounded.</summary>
    public RoundingPolicy Rounding { get; } = Rounding.IsSpecified
        ? Rounding
        : throw new ArgumentException(
            "A tax rule needs an explicit rounding policy: how tax is rounded is part of what the " +
            "jurisdiction decides, so core has no default to fall back on.",
            nameof(Rounding));

    /// <summary>The half-open period this version was in force.</summary>
    public DateRange Validity { get; } = Validity.IsEmpty
        ? throw new ArgumentException("A tax rule that is in force on no day is not a rule.", nameof(Validity))
        : Validity;

    /// <summary>The package version that contributed this rule.</summary>
    public PackageVersion SourceVersion { get; } = SourceVersion.IsSpecified
        ? SourceVersion
        : throw new ArgumentException(
            "A contributed rule records the package version it came from; auditing a tax figure " +
            "means being able to say which package release produced it.",
            nameof(SourceVersion));

    /// <summary>
    /// The tax on <paramref name="taxableAmount"/> under this rule, rounded the way this
    /// jurisdiction rounds.
    /// </summary>
    public Money TaxOn(Money taxableAmount) => Rate.ApplyTo(taxableAmount, Rounding);
}
