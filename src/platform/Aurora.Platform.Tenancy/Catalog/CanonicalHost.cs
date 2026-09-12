using System;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// What <c>catalog.database_cluster.host</c> is, stated as a grammar rather than as a list of
/// characters it must not contain: a host name in canonical form — RFC 1123 labels of lower-case
/// ASCII letters, digits and inner hyphens, 1 to 63 characters each, joined by single dots, 253
/// characters in all at most — of which an IPv4 literal is a special case (digit-only labels).
/// <see cref="CheckConstraintSql"/> is the same grammar as PostgreSQL evaluates in
/// <c>ck_database_cluster_host_well_formed</c>, and <c>CatalogHostGrammarTests</c> holds the two
/// equal by executing both over one list of cases.
/// </summary>
/// <remarks>
/// <para>
/// An allow-list, deliberately. Three characters had been refused one review at a time — <c>/</c>,
/// then a dot in the wrong place, then <c>,</c>: the multi-host separator that let
/// <c>pg-1.internal,pg-2.internal</c> past the index, past the lower-case check and past the
/// endpoint comparison as a fifth variant of the tenant-takeover shape (PR #18, second review) —
/// and a deny-list grows by one character per finding. A grammar excludes by construction what it
/// does not name: a multi-host list and a Unix-socket directory (the two shapes ADR-0036 §6 names
/// as breaking the endpoint triple), a scheme, a port suffix, a path, any upper-case or non-ASCII
/// character (so the database's collation-dependent <c>lower()</c> never meets one), a stray or
/// doubled dot, a hyphen at either end of a label.
/// </para>
/// <para>
/// Outside the grammar on purpose, pending the architect: an IPv6 literal (<c>::1</c>,
/// <c>[::1]</c>). ADR-0036 §3 reasons that folding one is lossless; the row has never accepted
/// one, and admitting it is a decision about what a cluster endpoint may be, not a spelling rule.
/// </para>
/// </remarks>
internal static class CanonicalHost
{
    public const int MaxLength = 253;
    public const int MaxLabelLength = 63;

    /// <summary>
    /// The grammar as a PostgreSQL regular expression over the <c>host</c> column: one label,
    /// then any number of dot-and-label. Explicit ranges only — <c>[a-z0-9-]</c>, never a POSIX
    /// class such as <c>[[:alpha:]]</c> — because a POSIX class is evaluated against the database's
    /// <c>lc_ctype</c> and this check must mean the same thing on a <c>C</c>-collated catalog as
    /// on an <c>en_US.utf8</c> one; a range is matched by code point under every collation, which
    /// <c>CatalogHostGrammarTests</c> executes under both. The column's <c>varchar(253)</c> is the
    /// total-length bound.
    /// </summary>
    public const string CheckConstraintSql =
        "host ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$'";

    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public static bool IsWellFormed(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (string label in host.Split('.'))
        {
            if (!IsWellFormedLabel(label))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWellFormedLabel(string label)
    {
        if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (char character in label)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
