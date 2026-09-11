using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The jurisdiction a Country Package speaks for: its country, the currency its statutory
/// obligations are denominated in, and the locales it ships (ADR-0008 §3.2).
/// </summary>
/// <remarks>
/// This is the <b>only</b> place a country code appears in a shape core code can branch on, and core
/// code does not read it: it is manifest metadata used to present a package to an operator and to
/// pick the right package for a company's primary jurisdiction. Core logic resolves behaviour
/// through the extension points, never through this value — which is what "no
/// <c>if (country == "SE")</c>" means in practice.
/// </remarks>
public sealed partial class Jurisdiction : IEquatable<Jurisdiction>
{
    private Jurisdiction(string countryCode, string defaultCurrencyCode, IReadOnlyList<CultureInfo> locales)
    {
        CountryCode = countryCode;
        DefaultCurrencyCode = defaultCurrencyCode;
        Locales = locales;
    }

    /// <summary>The ISO 3166-1 alpha-2 country code, uppercase — <c>NZ</c>.</summary>
    public string CountryCode { get; }

    /// <summary>The ISO 4217 alpha-3 currency code, uppercase — <c>NZD</c>.</summary>
    /// <remarks>
    /// A code, not a <see cref="Currency"/>: how many minor units a currency has is a property of
    /// the currency and not of the jurisdiction, and a manifest that carried its own answer would be
    /// a second, unreviewed source for it.
    /// </remarks>
    public string DefaultCurrencyCode { get; }

    /// <summary>The locales this package ships, most-preferred first. Never empty.</summary>
    public IReadOnlyList<CultureInfo> Locales { get; }

    /// <summary>
    /// Reads a jurisdiction, rejecting an unknown country, a malformed currency code, or a locale
    /// the runtime does not know.
    /// </summary>
    /// <remarks>
    /// Locales are checked against ICU's predefined cultures rather than merely parsed, because
    /// <see cref="CultureInfo"/> will happily manufacture a culture from any well-formed tag: a
    /// typo would then survive install and surface as a locale with no data behind it.
    /// </remarks>
    public static Result<Jurisdiction> Create(
        string? countryCode,
        string? defaultCurrencyCode,
        IReadOnlyList<string>? locales)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || !CountryCodeShape().IsMatch(countryCode))
        {
            return PackageManifestErrors.Invalid(
                "jurisdiction.countryCode",
                $"'{countryCode}' is not an ISO 3166-1 alpha-2 country code such as 'NZ'.");
        }

        try
        {
            _ = new RegionInfo(countryCode);
        }
        catch (ArgumentException)
        {
            return PackageManifestErrors.Invalid(
                "jurisdiction.countryCode",
                $"'{countryCode}' is not a country this runtime knows.");
        }

        if (string.IsNullOrWhiteSpace(defaultCurrencyCode) || !CurrencyCodeShape().IsMatch(defaultCurrencyCode))
        {
            return PackageManifestErrors.Invalid(
                "jurisdiction.defaultCurrency",
                $"'{defaultCurrencyCode}' is not an ISO 4217 alpha-3 currency code such as 'NZD'.");
        }

        if (locales is null || locales.Count == 0)
        {
            return PackageManifestErrors.Invalid(
                "jurisdiction.locales",
                "A package must name at least one locale; English is the base locale of the product " +
                "but a package speaks for a jurisdiction, and its formats belong to a culture.");
        }

        List<CultureInfo> parsed = new(locales.Count);
        foreach (string tag in locales)
        {
            try
            {
                parsed.Add(CultureInfo.GetCultureInfo(tag, predefinedOnly: true));
            }
            catch (CultureNotFoundException)
            {
                return PackageManifestErrors.Invalid(
                    "jurisdiction.locales",
                    $"'{tag}' is not a culture this runtime has data for.");
            }
        }

        return Result.Success(new Jurisdiction(
            countryCode.ToUpperInvariant(),
            defaultCurrencyCode.ToUpperInvariant(),
            parsed));
    }

    /// <inheritdoc/>
    public bool Equals(Jurisdiction? other) =>
        other is not null
        && string.Equals(CountryCode, other.CountryCode, StringComparison.Ordinal)
        && string.Equals(DefaultCurrencyCode, other.DefaultCurrencyCode, StringComparison.Ordinal)
        && Locales.SequenceEqual(other.Locales);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Jurisdiction);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(CountryCode, StringComparer.Ordinal);
        hash.Add(DefaultCurrencyCode, StringComparer.Ordinal);
        foreach (CultureInfo locale in Locales)
        {
            hash.Add(locale.Name, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{CountryCode}/{DefaultCurrencyCode} [{string.Join(", ", Locales.Select(l => l.Name))}]";

    [GeneratedRegex(@"^[A-Za-z]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CountryCodeShape();

    [GeneratedRegex(@"^[A-Za-z]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCodeShape();
}
