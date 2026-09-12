using System;

namespace Aurora.Architecture.Tests.MigrationSafety;

/// <summary>The lexical classes of PostgreSQL text the migration scanner distinguishes.</summary>
internal enum SqlTokenKind
{
    /// <summary>An unquoted identifier or keyword, as written; keywords are compared case-insensitively.</summary>
    Word,

    /// <summary>A <c>"double-quoted"</c> identifier. Text is the name as written, case preserved.</summary>
    QuotedIdentifier,

    /// <summary>
    /// A string literal in any of its spellings (<c>'…'</c>, <c>E'…'</c>, <c>U&amp;'…'</c>,
    /// <c>B'…'</c>, <c>X'…'</c>). Data, and never read - except where PostgreSQL itself reads a
    /// literal as code: the body of <c>DO</c> and the body after <c>AS</c> of a
    /// <c>CREATE FUNCTION</c>/<c>PROCEDURE</c>, where the scanner decodes it and reads it again.
    /// </summary>
    Literal,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>Parentheses, comma, semicolon, dot, brackets, or an operator run.</summary>
    Punctuation,

    /// <summary>
    /// The text between a pair of dollar-quote tags (<c>$$…$$</c>, <c>$tag$…$tag$</c>). Not a
    /// literal to the scanner: it is code, and the scanner tokenizes it again.
    /// </summary>
    DollarBody,
}

/// <summary>
/// How a string literal was spelled, which decides whether its text can be recovered when it
/// stands where PostgreSQL reads a literal as code.
/// </summary>
internal enum SqlLiteralForm
{
    /// <summary>Not a literal.</summary>
    None,

    /// <summary><c>'…'</c>: a doubled quote is the escaped quote, backslash is ordinary.</summary>
    Standard,

    /// <summary><c>E'…'</c>: backslash escapes.</summary>
    Escape,

    /// <summary><c>U&amp;'…'</c>: Unicode escapes with an optional <c>UESCAPE</c>. Not decoded.</summary>
    Unicode,

    /// <summary><c>B'…'</c> or <c>X'…'</c>: a bit string. Not decoded.</summary>
    Binary,
}

/// <summary>One token of PostgreSQL text.</summary>
internal sealed record SqlToken(SqlTokenKind Kind, string Text, int Offset)
{
    /// <summary>For a <see cref="SqlTokenKind.Literal"/>, how it was spelled; otherwise <see cref="SqlLiteralForm.None"/>.</summary>
    public SqlLiteralForm LiteralForm { get; init; }

    /// <summary>This is the unquoted word <paramref name="keyword"/>, in any letter case.</summary>
    public bool Is(string keyword) =>
        Kind == SqlTokenKind.Word && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>This is the punctuation <paramref name="punctuation"/>.</summary>
    public bool IsPunctuation(string punctuation) =>
        Kind == SqlTokenKind.Punctuation && string.Equals(Text, punctuation, StringComparison.Ordinal);

    /// <summary>A word or a quoted identifier - anything that can name a schema object.</summary>
    public bool IsName => Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier;

    /// <summary>How the token reads in a violation message.</summary>
    public string Display => Kind switch
    {
        SqlTokenKind.Literal => "'…'",
        SqlTokenKind.DollarBody => "$…$",
        SqlTokenKind.QuotedIdentifier => "\"" + Text + "\"",
        _ => Text,
    };
}
