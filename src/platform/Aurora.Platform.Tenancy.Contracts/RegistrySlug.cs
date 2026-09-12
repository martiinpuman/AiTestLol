using System.Diagnostics.CodeAnalysis;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The one spelling the registry's text identifiers share: lower-case ASCII letters and digits,
/// single hyphens between them, starting with a letter.
/// </summary>
/// <remarks>
/// A <see cref="TenantKey"/> is used verbatim as a DNS label, as a URL path segment and — with
/// hyphens turned into underscores — inside PostgreSQL database and role names (ADR-0007 §3.5,
/// §8, §11.4). The intersection of what all four accept is exactly this: no upper case (DNS and
/// PostgreSQL fold it), no underscore (not a DNS label character), no leading digit or hyphen, no
/// trailing or doubled hyphen. Keeping it in one place means a <see cref="ClusterId"/> cannot
/// quietly acquire a different rule.
/// </remarks>
internal static class RegistrySlug
{
    public static bool IsWellFormed([NotNullWhen(true)] string? text, int minLength, int maxLength)
    {
        if (text is null || text.Length < minLength || text.Length > maxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(text[0]))
        {
            return false;
        }

        bool previousWasHyphen = false;
        for (int index = 1; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character))
            {
                return false;
            }

            previousWasHyphen = false;
        }

        return !previousWasHyphen;
    }

    public static string Describe(int minLength, int maxLength) =>
        $"{minLength} to {maxLength} characters of lower-case ASCII letters and digits, " +
        "single hyphens between them, starting with a letter";
}
