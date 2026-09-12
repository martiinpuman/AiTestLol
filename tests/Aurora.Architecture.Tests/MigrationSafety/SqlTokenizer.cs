using System;
using System.Collections.Immutable;

namespace Aurora.Architecture.Tests.MigrationSafety;

/// <summary>
/// The scanner could not read a piece of SQL to the end, so nothing can be said about it.
/// </summary>
/// <remarks>
/// Distinct from "found nothing" on purpose. A scanner that gives up half way through a command
/// and reports the half it read as clean is the defect this project has met repeatedly: a parser
/// that reads a subset of its input and reports as though it read all of it. This exception is
/// how the tokenizer refuses to do that; the rule turns it into a violation.
/// </remarks>
internal sealed class UnscannableSqlException : Exception
{
    public UnscannableSqlException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Splits PostgreSQL text into the tokens the migration scanner reasons about, following the
/// lexical rules of the PostgreSQL lexer for everything that can hide a keyword: comments,
/// string literals in every spelling, quoted identifiers and dollar quoting.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is discarded:</b> whitespace, <c>--</c> comments and nested <c>/* */</c> comments.
/// <b>What is kept but never read:</b> string literals - a <c>DROP TABLE</c> inside quotes is
/// data, not a statement. <b>What is kept and read again:</b> dollar-quoted bodies, because a
/// function or <c>DO</c> body is code and a destructive statement inside one is real DDL.
/// </para>
/// <para>
/// <b>What stops it:</b> an unterminated literal, identifier, comment or dollar quote, and any
/// character it has no rule for. Each throws <see cref="UnscannableSqlException"/> rather than
/// yielding the tokens read so far.
/// </para>
/// </remarks>
internal static class SqlTokenizer
{
    public static ImmutableArray<SqlToken> Tokenize(string sql)
    {
        ImmutableArray<SqlToken>.Builder tokens = ImmutableArray.CreateBuilder<SqlToken>();
        int i = 0;

        while (i < sql.Length)
        {
            char c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && At(sql, i + 1) == '-')
            {
                i = EndOfLine(sql, i);
            }
            else if (c == '/' && At(sql, i + 1) == '*')
            {
                i = SkipBlockComment(sql, i);
            }
            else if (c == '\'')
            {
                i = ReadStandardLiteral(sql, i, i, tokens);
            }
            else if (c == '"')
            {
                i = ReadQuotedIdentifier(sql, i, tokens);
            }
            else if (c == '$')
            {
                i = ReadDollar(sql, i, tokens);
            }
            else if (IsLiteralPrefix(c) && At(sql, i + 1) == '\'')
            {
                i = c is 'E' or 'e'
                    ? ReadEscapeLiteral(sql, i, i + 1, tokens)
                    : ReadStandardLiteral(sql, i, i + 1, tokens);
            }
            else if (c is 'U' or 'u' && At(sql, i + 1) == '&' && At(sql, i + 2) is '\'' or '"')
            {
                i = At(sql, i + 2) == '\''
                    ? ReadStandardLiteral(sql, i, i + 2, tokens)
                    : ReadQuotedIdentifier(sql, i + 2, tokens);
            }
            else if (IsWordStart(c))
            {
                i = ReadWord(sql, i, tokens);
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(At(sql, i + 1))))
            {
                i = ReadNumber(sql, i, tokens);
            }
            else if (c is '(' or ')' or ',' or ';' or '.' or '[' or ']')
            {
                tokens.Add(new SqlToken(SqlTokenKind.Punctuation, c.ToString(), i));
                i++;
            }
            else if (IsOperatorChar(c))
            {
                i = ReadOperator(sql, i, tokens);
            }
            else
            {
                throw new UnscannableSqlException(
                    FormattableString.Invariant($"unexpected character '{c}' (U+{(int)c:X4}) at offset {i}"));
            }
        }

        return tokens.ToImmutable();
    }

    private static char At(string sql, int index) => index < sql.Length ? sql[index] : '\0';

    private static bool IsLiteralPrefix(char c) => c is 'E' or 'e' or 'B' or 'b' or 'X' or 'x' or 'N' or 'n';

    private static bool IsWordStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsWordChar(char c) => c is '_' or '$' || char.IsLetterOrDigit(c);

    private static bool IsOperatorChar(char c) => "+-*/<>=~!@#%^&|`?:".Contains(c, StringComparison.Ordinal);

    private static int EndOfLine(string sql, int from)
    {
        int end = sql.IndexOf('\n', from);
        return end < 0 ? sql.Length : end + 1;
    }

    private static int SkipBlockComment(string sql, int from)
    {
        int depth = 0;
        int i = from;

        while (i < sql.Length)
        {
            if (sql[i] == '/' && At(sql, i + 1) == '*')
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && At(sql, i + 1) == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        throw new UnscannableSqlException(
            FormattableString.Invariant($"block comment opened at offset {from} is never closed"));
    }

    /// <summary>A <c>'…'</c> literal, where a doubled quote is an escaped quote and backslash is ordinary.</summary>
    private static int ReadStandardLiteral(string sql, int tokenStart, int quote, ImmutableArray<SqlToken>.Builder tokens)
    {
        int i = quote + 1;

        while (i < sql.Length)
        {
            if (sql[i] == '\'')
            {
                if (At(sql, i + 1) == '\'')
                {
                    i += 2;
                    continue;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Literal, sql[(quote + 1)..i], tokenStart));
                return i + 1;
            }

            i++;
        }

        throw new UnscannableSqlException(
            FormattableString.Invariant($"string literal opened at offset {tokenStart} is never closed"));
    }

    /// <summary>An <c>E'…'</c> literal, where a backslash escapes the next character.</summary>
    private static int ReadEscapeLiteral(string sql, int tokenStart, int quote, ImmutableArray<SqlToken>.Builder tokens)
    {
        int i = quote + 1;

        while (i < sql.Length)
        {
            if (sql[i] == '\\')
            {
                i += 2;
            }
            else if (sql[i] == '\'')
            {
                if (At(sql, i + 1) == '\'')
                {
                    i += 2;
                    continue;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Literal, sql[(quote + 1)..i], tokenStart));
                return i + 1;
            }
            else
            {
                i++;
            }
        }

        throw new UnscannableSqlException(
            FormattableString.Invariant($"escape string literal opened at offset {tokenStart} is never closed"));
    }

    private static int ReadQuotedIdentifier(string sql, int quote, ImmutableArray<SqlToken>.Builder tokens)
    {
        int i = quote + 1;

        while (i < sql.Length)
        {
            if (sql[i] == '"')
            {
                if (At(sql, i + 1) == '"')
                {
                    i += 2;
                    continue;
                }

                string name = sql[(quote + 1)..i].Replace("\"\"", "\"", StringComparison.Ordinal);
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, name, quote));
                return i + 1;
            }

            i++;
        }

        throw new UnscannableSqlException(
            FormattableString.Invariant($"quoted identifier opened at offset {quote} is never closed"));
    }

    /// <summary>
    /// A dollar quote <c>$tag$…$tag$</c>, or a positional parameter <c>$1</c>. The tag may be empty
    /// and may not start with a digit, exactly as PostgreSQL has it.
    /// </summary>
    private static int ReadDollar(string sql, int start, ImmutableArray<SqlToken>.Builder tokens)
    {
        if (char.IsAsciiDigit(At(sql, start + 1)))
        {
            int end = start + 1;
            while (char.IsAsciiDigit(At(sql, end)))
            {
                end++;
            }

            tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..end], start));
            return end;
        }

        int tagEnd = start + 1;
        while (tagEnd < sql.Length && (sql[tagEnd] == '_' || char.IsLetterOrDigit(sql[tagEnd])))
        {
            tagEnd++;
        }

        if (At(sql, tagEnd) != '$')
        {
            throw new UnscannableSqlException(
                FormattableString.Invariant($"'$' at offset {start} opens neither a dollar quote nor a parameter"));
        }

        string tag = sql[start..(tagEnd + 1)];
        int bodyStart = tagEnd + 1;
        int close = sql.IndexOf(tag, bodyStart, StringComparison.Ordinal);

        if (close < 0)
        {
            throw new UnscannableSqlException(
                FormattableString.Invariant($"dollar quote {tag} opened at offset {start} is never closed"));
        }

        tokens.Add(new SqlToken(SqlTokenKind.DollarBody, sql[bodyStart..close], start));
        return close + tag.Length;
    }

    private static int ReadWord(string sql, int start, ImmutableArray<SqlToken>.Builder tokens)
    {
        int end = start + 1;
        while (end < sql.Length && IsWordChar(sql[end]))
        {
            end++;
        }

        tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..end], start));
        return end;
    }

    private static int ReadNumber(string sql, int start, ImmutableArray<SqlToken>.Builder tokens)
    {
        int end = start;
        while (end < sql.Length)
        {
            char c = sql[end];
            bool exponentSign = c is '+' or '-' && sql[end - 1] is 'e' or 'E';
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' || exponentSign))
            {
                break;
            }

            end++;
        }

        tokens.Add(new SqlToken(SqlTokenKind.Number, sql[start..end], start));
        return end;
    }

    private static int ReadOperator(string sql, int start, ImmutableArray<SqlToken>.Builder tokens)
    {
        int end = start;
        while (end < sql.Length
               && IsOperatorChar(sql[end])
               && !(sql[end] == '-' && At(sql, end + 1) == '-')
               && !(sql[end] == '/' && At(sql, end + 1) == '*'))
        {
            end++;
        }

        tokens.Add(new SqlToken(SqlTokenKind.Punctuation, sql[start..end], start));
        return end;
    }
}
