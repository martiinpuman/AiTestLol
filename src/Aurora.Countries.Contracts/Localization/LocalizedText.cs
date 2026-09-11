using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Localization;

/// <summary>
/// A user-facing string a package supplies, in every locale the package has it in.
/// </summary>
/// <remarks>
/// <para>
/// Account names, report box labels and validation messages come from the package, and they are as
/// user-facing as anything in core. <c>CLAUDE.md</c> allows no hard-coded user-facing strings, so
/// the seam cannot take a bare <see cref="string"/>: it would be a hole in the localization rule
/// exactly where a jurisdiction's own vocabulary enters the product.
/// </para>
/// <para>
/// Every value carries a base rendering, so lookup always answers. Falling back from
/// <c>mi-NZ</c> to <c>mi</c> to the base is the same order the .NET resource manager uses, and the
/// reason a package can ship <c>en</c> and add <c>mi-NZ</c> later without touching anything else.
/// </para>
/// </remarks>
public sealed class LocalizedText : IEquatable<LocalizedText>
{
    private readonly Dictionary<string, string> _byLocale;

    private LocalizedText(string baseText, Dictionary<string, string> byLocale)
    {
        BaseText = baseText;
        _byLocale = byLocale;
    }

    /// <summary>
    /// The rendering used when no locale matches — written in the product's base locale, English.
    /// </summary>
    public string BaseText { get; }

    /// <summary>The locales this text has a rendering in, not counting the base text.</summary>
    public IReadOnlyCollection<string> Locales => _byLocale.Keys;

    /// <summary>A text that is the same in every locale — a code, a symbol, a proper noun.</summary>
    public static Result<LocalizedText> Invariant(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? PackageManifestErrors.Invalid("localizedText", "A localized text may not be empty.")
            : Result.Success(new LocalizedText(text, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

    /// <summary>
    /// A text with a base rendering and zero or more locale-specific ones, keyed by BCP-47 tag.
    /// </summary>
    public static Result<LocalizedText> Create(string? baseText, IReadOnlyDictionary<string, string>? byLocale)
    {
        if (string.IsNullOrWhiteSpace(baseText))
        {
            return PackageManifestErrors.Invalid("localizedText", "A localized text may not be empty.");
        }

        Dictionary<string, string> renderings = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string tag, string text) in byLocale ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return PackageManifestErrors.Invalid(
                    "localizedText",
                    $"the rendering for '{tag}' is empty. Omit the locale instead: an empty string " +
                    $"shows a user nothing, where a missing one falls back to something readable.");
            }

            try
            {
                _ = CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                return PackageManifestErrors.Invalid(
                    "localizedText",
                    $"'{tag}' is not a culture this runtime has data for.");
            }

            renderings[tag] = text;
        }

        return Result.Success(new LocalizedText(baseText, renderings));
    }

    /// <summary>
    /// The rendering for <paramref name="culture"/>, falling back through the culture's parents to
    /// <see cref="BaseText"/>.
    /// </summary>
    public string For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        for (CultureInfo candidate = culture;
             !string.IsNullOrEmpty(candidate.Name);
             candidate = candidate.Parent)
        {
            if (_byLocale.TryGetValue(candidate.Name, out string? text))
            {
                return text;
            }
        }

        return BaseText;
    }

    /// <inheritdoc/>
    public bool Equals(LocalizedText? other) =>
        other is not null
        && string.Equals(BaseText, other.BaseText, StringComparison.Ordinal)
        && _byLocale.Count == other._byLocale.Count
        && _byLocale.All(entry =>
            other._byLocale.TryGetValue(entry.Key, out string? text)
            && string.Equals(entry.Value, text, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as LocalizedText);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(BaseText, _byLocale.Count);

    /// <inheritdoc/>
    public override string ToString() => BaseText;
}
