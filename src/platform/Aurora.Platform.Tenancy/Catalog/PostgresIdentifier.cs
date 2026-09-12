using System.Diagnostics.CodeAnalysis;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The spelling of a PostgreSQL name the catalog will later hand to <c>CREATE DATABASE</c> or a
/// connection string: something that needs no quoting and cannot be truncated.
/// </summary>
/// <remarks>
/// PostgreSQL folds an unquoted identifier to lower case and silently truncates it at 63 bytes.
/// A name the registry stores must survive both, so it is lower-case ASCII, starts with a letter
/// or underscore, and is at most 63 characters — which, being ASCII, is also 63 bytes.
/// </remarks>
internal static class PostgresIdentifier
{
    public const int MaxLength = 63;

    public static bool IsWellFormed([NotNullWhen(true)] string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(text[0]) && text[0] != '_')
        {
            return false;
        }

        foreach (char character in text)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
