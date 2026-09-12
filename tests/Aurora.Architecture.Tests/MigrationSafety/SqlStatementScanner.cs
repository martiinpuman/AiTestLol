using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Aurora.Architecture.Tests.MigrationSafety;

/// <summary>What a finding is: something a category must justify, or something the scanner could not read.</summary>
internal enum SqlFindingKind
{
    /// <summary>A statement ADR-0007 §7.2 allows only in a <c>Contract</c> migration.</summary>
    Destructive,

    /// <summary>Code the scanner does not read, so the statement can hide anything.</summary>
    Unscannable,
}

/// <summary>One finding: what was matched and how it reads.</summary>
internal sealed record SqlFinding(SqlFindingKind Kind, string What);

/// <summary>
/// One SQL statement as the scanner saw it: where it is, how it starts, and what was found in it.
/// </summary>
/// <param name="Location">"statement 3", or "statement 3 › body › statement 1" inside a dollar quote.</param>
/// <param name="Head">The first word, upper-cased; empty when the statement starts with something else.</param>
/// <param name="Excerpt">The first few tokens, for a message.</param>
/// <param name="IsTopLevel">False inside a dollar-quoted body.</param>
/// <param name="HasProceduralBody">The statement carries a dollar-quoted body.</param>
/// <param name="Findings">What was matched in this statement, in token order.</param>
internal sealed record SqlStatement(
    string Location,
    string Head,
    string Excerpt,
    bool IsTopLevel,
    bool HasProceduralBody,
    ImmutableArray<SqlFinding> Findings);

/// <summary>Every statement of one command text, top level and inside bodies, in document order.</summary>
internal sealed record SqlScanReport(ImmutableArray<SqlStatement> Statements)
{
    public int TopLevelStatements => Statements.Count(static statement => statement.IsTopLevel);
}

/// <summary>
/// Reads the SQL a migration will run and names what in it a category must justify.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it matches, on tokens, anywhere in a statement and inside every dollar-quoted body:</b>
/// <c>DROP</c> of anything but a constraint, default, <c>NOT NULL</c>, identity or expression;
/// <c>TRUNCATE</c> of a table; <c>DELETE FROM</c> and <c>MERGE … THEN DELETE</c>; any
/// <c>RENAME</c>; <c>ALTER [COLUMN] … TYPE</c>; <c>ALTER [COLUMN] … SET NOT NULL</c>;
/// <c>SET SCHEMA</c>; and <c>ADD [COLUMN] … NOT NULL</c> with no <c>DEFAULT</c> and no
/// <c>GENERATED</c>. Matching on tokens is what makes case, whitespace, newlines, comments and
/// string literals irrelevant: <c>drop /* x */ table</c> and <c>DROP TABLE</c> are the same
/// three tokens, and <c>'DROP TABLE'</c> is one literal.
/// </para>
/// <para>
/// <b>What it refuses to read, and reports as unscannable rather than clean:</b> dynamic SQL
/// (<c>EXECUTE</c> of anything but a trigger's <c>FUNCTION</c>/<c>PROCEDURE</c> binding or the
/// <c>EXECUTE</c> privilege of a <c>GRANT</c>/<c>REVOKE</c>); a call
/// to a schema-qualified function or procedure outside <c>pg_catalog</c>, whose body was written
/// elsewhere; and any text the tokenizer cannot finish. A function <i>defined</i>, <i>dropped</i>,
/// bound to a trigger or named as a column default is a reference, not a call, and is not one of
/// these.
/// </para>
/// <para>
/// <b>What it cannot see, stated:</b> a call to an <i>unqualified</i> user function is read as a
/// built-in. That holds only while every function a migration creates is schema-qualified, which
/// every migration on this project is (schemas own their objects, ADR-0007 §7.1); a migration
/// creating an unqualified function is the case that would break it. And it reads the SQL of a
/// migration's <c>Up</c>: <c>Down</c> is never run in production (ADR-0007 §7.4, "rollback is not
/// a database operation") and is where an Expand's own drops legitimately live.
/// </para>
/// </remarks>
internal static class SqlStatementScanner
{
    /// <summary>The <c>DROP</c> targets that widen or tidy rather than remove: not destructive.</summary>
    private static readonly ImmutableHashSet<string> NonDestructiveDropTargets =
        Keywords("CONSTRAINT", "DEFAULT", "NOT", "IDENTITY", "EXPRESSION");

    /// <summary>After <c>ADD</c>, these begin something that is not a column definition.</summary>
    private static readonly ImmutableHashSet<string> NonColumnAddTargets =
        Keywords("CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "EXCLUDE", "VALUE", "TABLE", "GENERATED");

    /// <summary>
    /// A schema-qualified name followed by <c>(</c> is a reference and not a call when the word
    /// before it is one of these. The list is an exemption, so a word missing from it produces a
    /// false positive - the loud direction.
    /// </summary>
    private static readonly ImmutableHashSet<string> QualifiedNameIsAReferenceAfter = Keywords(
        "TABLE", "INTO", "ON", "ONLY", "REFERENCES", "FUNCTION", "PROCEDURE", "ROUTINE", "AGGREGATE",
        "DEFAULT", "EXISTS", "VIEW", "TYPE", "COPY");

    public static SqlScanReport Scan(string sql)
    {
        List<SqlStatement> statements = [];
        ScanText(sql, "statement", isTopLevel: true, statements);
        return new SqlScanReport([.. statements]);
    }

    private static void ScanText(string sql, string locationPrefix, bool isTopLevel, List<SqlStatement> into)
    {
        ImmutableArray<SqlToken> tokens;
        try
        {
            tokens = SqlTokenizer.Tokenize(sql);
        }
        catch (UnscannableSqlException unreadable)
        {
            into.Add(new SqlStatement(
                locationPrefix,
                Head: string.Empty,
                Excerpt: Excerpt(sql),
                isTopLevel,
                HasProceduralBody: false,
                [new SqlFinding(SqlFindingKind.Unscannable, "cannot be read to the end: " + unreadable.Message)]));
            return;
        }

        int ordinal = 0;
        foreach (ImmutableArray<SqlToken> statement in Split(tokens))
        {
            ordinal++;
            string location = FormattableString.Invariant($"{locationPrefix} {ordinal}");
            ImmutableArray<SqlFinding> findings = [.. Findings(statement)];
            bool hasBody = statement.Any(static token => token.Kind == SqlTokenKind.DollarBody);

            into.Add(new SqlStatement(
                location,
                Head: statement[0].Kind == SqlTokenKind.Word ? statement[0].Text.ToUpperInvariant() : string.Empty,
                Excerpt: Excerpt(statement),
                isTopLevel,
                hasBody,
                findings));

            foreach (SqlToken body in statement.Where(static token => token.Kind == SqlTokenKind.DollarBody))
            {
                ScanText(body.Text, location + " › body › statement", isTopLevel: false, into);
            }
        }
    }

    private static IEnumerable<ImmutableArray<SqlToken>> Split(ImmutableArray<SqlToken> tokens)
    {
        ImmutableArray<SqlToken>.Builder current = ImmutableArray.CreateBuilder<SqlToken>();

        foreach (SqlToken token in tokens)
        {
            if (token.IsPunctuation(";"))
            {
                if (current.Count > 0)
                {
                    yield return current.ToImmutable();
                    current.Clear();
                }
            }
            else
            {
                current.Add(token);
            }
        }

        if (current.Count > 0)
        {
            yield return current.ToImmutable();
        }
    }

    private static IEnumerable<SqlFinding> Findings(ImmutableArray<SqlToken> t)
    {
        for (int i = 0; i < t.Length; i++)
        {
            SqlToken token = t[i];
            SqlToken? next = i + 1 < t.Length ? t[i + 1] : null;

            if (token.Is("DROP") && next is { IsName: true })
            {
                SqlToken target = next.Is("IF") && At(t, i + 2).Is("EXISTS") && At(t, i + 3).IsName ? t[i + 3] : next;
                if (!(target.Kind == SqlTokenKind.Word && NonDestructiveDropTargets.Contains(target.Text)))
                {
                    yield return Destructive("DROP " + (target.Kind == SqlTokenKind.Word ? target.Text.ToUpperInvariant() : target.Display));
                }
            }

            if (token.Is("TRUNCATE") && next is { IsName: true } && !next.Is("ON"))
            {
                yield return Destructive("TRUNCATE");
            }

            if (token.Is("DELETE") && next is not null && next.Is("FROM"))
            {
                yield return Destructive("DELETE FROM");
            }

            if (token.Is("THEN") && next is not null && next.Is("DELETE"))
            {
                yield return Destructive("MERGE … THEN DELETE");
            }

            if (token.Is("RENAME"))
            {
                yield return Destructive("RENAME");
            }

            if (token.Is("SET") && next is not null && next.Is("SCHEMA"))
            {
                yield return Destructive("SET SCHEMA");
            }

            if (token.Is("ALTER"))
            {
                foreach (SqlFinding finding in AlterColumnFindings(t, i + 1))
                {
                    yield return finding;
                }
            }

            if (token.Is("ADD") && AddsANotNullColumnWithoutDefault(t, i + 1))
            {
                yield return Destructive("ADD COLUMN … NOT NULL without a DEFAULT");
            }

            if (token.Is("EXECUTE") && !IsStaticExecute(next))
            {
                yield return Unscannable("EXECUTE runs SQL built at run time, which the scanner does not read");
            }

            if (IsCallOfAQualifiedFunction(t, i))
            {
                yield return Unscannable(
                    $"calls {t[i].Display}.{t[i + 2].Display}(…), a function whose body the scanner does not read");
            }
        }
    }

    /// <summary><c>ALTER [COLUMN] name [SET DATA] TYPE …</c> and <c>ALTER [COLUMN] name SET NOT NULL</c>.</summary>
    private static IEnumerable<SqlFinding> AlterColumnFindings(ImmutableArray<SqlToken> t, int j)
    {
        if (At(t, j).Is("COLUMN"))
        {
            j++;
        }

        if (!At(t, j).IsName)
        {
            yield break;
        }

        j++;

        if (At(t, j).Is("SET") && At(t, j + 1).Is("DATA"))
        {
            j += 2;
        }

        if (At(t, j).Is("TYPE"))
        {
            yield return Destructive("ALTER COLUMN … TYPE");
        }
        else if (At(t, j).Is("SET") && At(t, j + 1).Is("NOT") && At(t, j + 2).Is("NULL"))
        {
            yield return Destructive("ALTER COLUMN … SET NOT NULL");
        }
    }

    /// <summary>
    /// <c>ADD [COLUMN] [IF NOT EXISTS] name … NOT NULL</c> with neither <c>DEFAULT</c> nor
    /// <c>GENERATED</c> at the column definition's own nesting level, before the next top-level
    /// comma or the end of the statement.
    /// </summary>
    private static bool AddsANotNullColumnWithoutDefault(ImmutableArray<SqlToken> t, int j)
    {
        if (At(t, j).Is("COLUMN"))
        {
            j++;
        }

        if (At(t, j).Is("IF") && At(t, j + 1).Is("NOT") && At(t, j + 2).Is("EXISTS"))
        {
            j += 3;
        }

        SqlToken name = At(t, j);
        if (!name.IsName || (name.Kind == SqlTokenKind.Word && NonColumnAddTargets.Contains(name.Text)))
        {
            return false;
        }

        bool notNull = false;
        bool hasDefault = false;
        int depth = 0;

        for (int k = j + 1; k < t.Length; k++)
        {
            SqlToken token = t[k];

            if (token.IsPunctuation("("))
            {
                depth++;
            }
            else if (token.IsPunctuation(")"))
            {
                depth--;
            }
            else if (depth == 0)
            {
                if (token.IsPunctuation(","))
                {
                    break;
                }

                notNull |= token.Is("NOT") && At(t, k + 1).Is("NULL");
                hasDefault |= token.Is("DEFAULT") || token.Is("GENERATED");
            }
        }

        return notNull && !hasDefault;
    }

    /// <summary>
    /// <c>EXECUTE FUNCTION</c>/<c>EXECUTE PROCEDURE</c> binds a trigger, and <c>EXECUTE ON</c> or
    /// <c>EXECUTE,</c> is the privilege in a <c>GRANT</c>/<c>REVOKE</c>; every other <c>EXECUTE</c>
    /// runs text the scanner does not have.
    /// </summary>
    private static bool IsStaticExecute(SqlToken? next) =>
        next is not null && (next.Is("FUNCTION") || next.Is("PROCEDURE") || next.Is("ON") || next.IsPunctuation(","));

    /// <summary><c>schema.name(</c> where the schema is not <c>pg_catalog</c> and the word before is not a reference context.</summary>
    private static bool IsCallOfAQualifiedFunction(ImmutableArray<SqlToken> t, int i)
    {
        if (!(t[i].IsName && At(t, i + 1).IsPunctuation(".") && At(t, i + 2).IsName && At(t, i + 3).IsPunctuation("(")))
        {
            return false;
        }

        if (string.Equals(t[i].Text, "PG_CATALOG", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        SqlToken before = At(t, i - 1);
        return !(before.Kind == SqlTokenKind.Word && QualifiedNameIsAReferenceAfter.Contains(before.Text));
    }

    private static readonly SqlToken None = new(SqlTokenKind.Punctuation, string.Empty, -1);

    private static ImmutableHashSet<string> Keywords(params string[] words) =>
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, words);

    private static SqlToken At(ImmutableArray<SqlToken> t, int index) =>
        index >= 0 && index < t.Length ? t[index] : None;

    private static SqlFinding Destructive(string what) => new(SqlFindingKind.Destructive, what);

    private static SqlFinding Unscannable(string what) => new(SqlFindingKind.Unscannable, what);

    /// <summary>The first few tokens, with punctuation attached the way it was written.</summary>
    private static string Excerpt(ImmutableArray<SqlToken> statement)
    {
        var text = new System.Text.StringBuilder();

        foreach (SqlToken token in statement.Take(8))
        {
            bool attach = token.IsPunctuation(".") || token.IsPunctuation(",") || token.IsPunctuation(")")
                || text.Length == 0 || text[^1] == '.' || text[^1] == '(';
            text.Append(attach ? string.Empty : " ").Append(token.Display);
        }

        return text.ToString();
    }

    private static string Excerpt(string sql)
    {
        string flat = string.Join(' ', sql.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 60 ? flat : flat[..60] + "…";
    }
}
