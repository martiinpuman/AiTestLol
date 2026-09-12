using System;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The one statement of what a package id may look like: the rule <c>catalog.installed_package</c>
/// enforces on <c>package_id</c>, read by the domain, the entity configuration and the read-path
/// entry alike (ADR-0038 §2.2).
/// </summary>
/// <remarks>
/// <para>
/// A package id is the stem of the package's <c>pkg_&lt;id&gt;</c> schema (ADR-0008 §4.1), so its
/// character rule is a SQL identifier's and its length bound is what keeps <c>pkg_</c> plus the id
/// clear of PostgreSQL's 63-byte identifier truncation, which would otherwise map two packages
/// onto one schema without raising anything (ADR-0038 §2.3). Three places used to state the rule
/// separately; three copies of one rule need a mechanism to stay equal, and the cheapest is not to
/// have three copies. <see cref="CheckConstraintSql"/> generates the catalog's check constraint
/// byte for byte as the merged migration declares it, so this type touches no migration.
/// </para>
/// <para>
/// <see cref="CharacterPattern"/> is the rule as PostgreSQL's <c>~</c> reads it.
/// <see cref="IsWellFormed"/> is the same rule as .NET reads it, written as a character walk rather
/// than a .NET regular expression on purpose: .NET's <c>$</c> also matches before a trailing
/// newline, PostgreSQL's does not, and the two must accept exactly the same strings - a read-path
/// type stricter than the catalog turns a stored row into a scope that cannot open, and one looser
/// admits an id the catalog will refuse at the next write (ADR-0038 §2.1).
/// </para>
/// </remarks>
public static class PackageIdFormat
{
    /// <summary>A lower-case ASCII letter, then lower-case ASCII letters, digits and underscores - as PostgreSQL reads it.</summary>
    public const string CharacterPattern = "^[a-z][a-z0-9_]*$";

    /// <summary>The most characters a package id may have: <c>package_id varchar(32)</c>.</summary>
    public const int MaxLength = 32;

    /// <summary>The rule in words, for an exception message.</summary>
    public static string Description =>
        $"lower-case ASCII letters, digits and underscores, starting with a letter, at most {MaxLength} characters";

    /// <summary>Whether <paramref name="packageId"/> is a package id the catalog would accept.</summary>
    public static bool IsWellFormed(string packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        if (packageId.Length is 0 or > MaxLength || !char.IsAsciiLetterLower(packageId[0]))
        {
            return false;
        }

        foreach (char character in packageId)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The check-constraint expression that holds <paramref name="column"/> to
    /// <see cref="CharacterPattern"/>; the length bound is the column's own type.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="column"/> is null or blank.</exception>
    public static string CheckConstraintSql(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        return $"{column} ~ '{CharacterPattern}'";
    }
}
