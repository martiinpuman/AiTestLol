using System;
using System.Collections.Generic;
using System.Globalization;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Localization;

/// <summary>
/// Extension point 8 — the locales, translated strings and format overrides a package ships
/// (ADR-0008 §7).
/// </summary>
/// <remarks>
/// <para>
/// Shippable on its own. A manifest may declare <c>localeOnly</c> and nothing but
/// <see cref="CountryPackageCapability.LocalePack"/>, which is how a language reaches the product
/// without a jurisdiction's fiscal rules coming with it. Business Central ships translation through
/// a separate pipeline from fiscal localization for exactly this reason, and the research took that
/// as evidence the two are separable even though products usually fuse them.
/// </para>
/// <para>
/// Presentation only. Nothing here may change what a number <i>is</i>: formats decide how an amount
/// is written, never how it is computed or rounded — that is <see cref="RoundingPolicy"/>, and it
/// arrives with a tax rule, not with a locale.
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.LocalePack)]
public interface ILocalePack
{
    /// <summary>The locales this pack covers.</summary>
    IReadOnlyList<CultureInfo> Locales { get; }

    /// <summary>
    /// A translated string, or <see langword="false"/> when this pack has none for that key.
    /// </summary>
    /// <remarks>
    /// Falling back is core's job, not the pack's: core knows the resource chain, the pack knows
    /// only what it ships.
    /// </remarks>
    bool TryGetString(CultureInfo culture, string key, out string value);

    /// <summary>
    /// What this jurisdiction writes differently from the runtime's own data for
    /// <paramref name="culture"/>, or a failure if the pack does not cover that culture.
    /// </summary>
    Result<LocaleFormats> FormatsFor(CultureInfo culture);
}

/// <summary>
/// A jurisdiction's format overrides on top of ICU's own data for a culture.
/// </summary>
/// <remarks>
/// Overrides, not a replacement. ICU already knows how <c>en-NZ</c> writes a date; a package states
/// only where its jurisdiction's official practice differs — a statutory date format on a return, a
/// currency symbol a tax authority insists on. Every override is optional, and a pack with none is a
/// perfectly good pack.
/// </remarks>
public sealed class LocaleFormats
{
    private LocaleFormats(
        CultureInfo baseCulture,
        string? shortDatePattern,
        string? longDatePattern,
        string? decimalSeparator,
        string? groupSeparator,
        string? currencySymbol,
        int? currencyDecimalDigits)
    {
        BaseCulture = baseCulture;
        ShortDatePattern = shortDatePattern;
        LongDatePattern = longDatePattern;
        DecimalSeparator = decimalSeparator;
        GroupSeparator = groupSeparator;
        CurrencySymbol = currencySymbol;
        CurrencyDecimalDigits = currencyDecimalDigits;
    }

    /// <summary>The culture being overridden.</summary>
    public CultureInfo BaseCulture { get; }

    /// <summary>The short date pattern, where the jurisdiction mandates one.</summary>
    public string? ShortDatePattern { get; }

    /// <summary>The long date pattern, where the jurisdiction mandates one.</summary>
    public string? LongDatePattern { get; }

    /// <summary>The decimal separator, where it differs from the culture's.</summary>
    public string? DecimalSeparator { get; }

    /// <summary>The digit group separator, where it differs from the culture's.</summary>
    public string? GroupSeparator { get; }

    /// <summary>The currency symbol, where it differs from the culture's.</summary>
    public string? CurrencySymbol { get; }

    /// <summary>How many decimal digits a currency amount is written with.</summary>
    public int? CurrencyDecimalDigits { get; }

    /// <summary>Whether this states any override at all.</summary>
    public bool HasOverrides =>
        ShortDatePattern is not null
        || LongDatePattern is not null
        || DecimalSeparator is not null
        || GroupSeparator is not null
        || CurrencySymbol is not null
        || CurrencyDecimalDigits is not null;

    /// <summary>Builds a set of overrides over <paramref name="baseCulture"/>.</summary>
    public static Result<LocaleFormats> Create(
        CultureInfo baseCulture,
        string? shortDatePattern = null,
        string? longDatePattern = null,
        string? decimalSeparator = null,
        string? groupSeparator = null,
        string? currencySymbol = null,
        int? currencyDecimalDigits = null)
    {
        ArgumentNullException.ThrowIfNull(baseCulture);

        if (decimalSeparator is { Length: 0 } || groupSeparator is { Length: 0 })
        {
            return PackageManifestErrors.Invalid(
                "locale.separators",
                "A separator override may not be the empty string. Omit it to keep the culture's own.");
        }

        if (currencyDecimalDigits is < 0 or > Currency.MaxMinorUnits)
        {
            return PackageManifestErrors.Invalid(
                "locale.currencyDecimalDigits",
                $"{currencyDecimalDigits} is outside 0..{Currency.MaxMinorUnits}.");
        }

        return Result.Success(new LocaleFormats(
            baseCulture,
            shortDatePattern,
            longDatePattern,
            decimalSeparator,
            groupSeparator,
            currencySymbol,
            currencyDecimalDigits));
    }

    /// <summary>
    /// The culture to format with: a copy of <see cref="BaseCulture"/> with these overrides applied.
    /// </summary>
    /// <remarks>
    /// A copy, because <see cref="CultureInfo.GetCultureInfo(string)"/> hands back a cached, shared
    /// instance and mutating it would change formatting for every tenant in the process.
    /// </remarks>
    public CultureInfo Apply()
    {
        CultureInfo culture = (CultureInfo)BaseCulture.Clone();

        if (ShortDatePattern is not null)
        {
            culture.DateTimeFormat.ShortDatePattern = ShortDatePattern;
        }

        if (LongDatePattern is not null)
        {
            culture.DateTimeFormat.LongDatePattern = LongDatePattern;
        }

        if (DecimalSeparator is not null)
        {
            culture.NumberFormat.NumberDecimalSeparator = DecimalSeparator;
            culture.NumberFormat.CurrencyDecimalSeparator = DecimalSeparator;
        }

        if (GroupSeparator is not null)
        {
            culture.NumberFormat.NumberGroupSeparator = GroupSeparator;
            culture.NumberFormat.CurrencyGroupSeparator = GroupSeparator;
        }

        if (CurrencySymbol is not null)
        {
            culture.NumberFormat.CurrencySymbol = CurrencySymbol;
        }

        if (CurrencyDecimalDigits is { } digits)
        {
            culture.NumberFormat.CurrencyDecimalDigits = digits;
        }

        return culture;
    }
}
