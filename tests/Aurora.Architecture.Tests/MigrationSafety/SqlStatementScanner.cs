using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

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
/// <param name="Location">"statement 3", or "statement 3 › body › statement 1" inside a body.</param>
/// <param name="Head">The first word, upper-cased; empty when the statement starts with something else.</param>
/// <param name="Excerpt">The first few tokens, for a message.</param>
/// <param name="IsTopLevel">False inside a body.</param>
/// <param name="HasProceduralBody">The statement carries a body: a dollar quote, or a literal where PostgreSQL reads one as code.</param>
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
/// <b>What it matches, on tokens, anywhere in a statement and inside every body:</b> <c>DROP</c> of
/// anything but a default, <c>NOT NULL</c>, identity or expression, and <c>DROP CONSTRAINT</c>
/// only with <c>CASCADE</c>; <c>TRUNCATE</c> of a table; <c>DELETE FROM</c> and
/// <c>MERGE … THEN DELETE</c>; any <c>RENAME</c>; <c>ALTER [COLUMN] … TYPE</c>;
/// <c>ALTER [COLUMN] … SET NOT NULL</c>; <c>SET SCHEMA</c>; <c>ADD [COLUMN] … NOT NULL</c> with no
/// <c>DEFAULT</c> and no <c>GENERATED</c>; the statements that switch an enforced invariant off -
/// <c>DISABLE TRIGGER</c>, <c>DISABLE RULE</c>, <c>ENABLE REPLICA TRIGGER</c>,
/// <c>ENABLE REPLICA RULE</c> (a replica-mode trigger never fires for an ordinary write, which for
/// a guard is <c>DISABLE</c> by another spelling), <c>DISABLE ROW LEVEL SECURITY</c>,
/// <c>NO FORCE ROW LEVEL SECURITY</c>, <c>DETACH PARTITION</c>, and
/// <c>SET session_replication_role</c> in any spelling including <c>set_config(…)</c>, which
/// suppresses ordinary triggers for the session that runs the migration; and
/// <c>CREATE OR REPLACE</c> of anything, because <c>OR REPLACE</c> exists to overwrite an object
/// that may already be there, and a body replaced is a body the scanner cannot compare with the
/// one it displaces. Matching on tokens is what makes case, whitespace, newlines, comments and
/// string literals irrelevant: <c>drop /* x */ table</c> and <c>DROP TABLE</c> are the same three
/// tokens, and <c>'DROP TABLE'</c> is one literal.
/// </para>
/// <para>
/// <b>What it reads again as code:</b> every dollar-quoted body, and a string literal in the two
/// positions where PostgreSQL reads a literal as code - the body of a <c>DO</c> and the body after
/// <c>AS</c> of a <c>CREATE [OR REPLACE] FUNCTION</c>/<c>PROCEDURE</c>. <c>DO $$…$$</c> and
/// <c>DO '…'</c> are the same statement; a quoted function body is the original spelling. Adjacent
/// string constants are one constant to PostgreSQL (its lexer joins constants separated by a
/// newline before the grammar sees them), so a run of literals in a body position is decoded and
/// joined before it is read - a body split across two literals is read whole, not half. A body in
/// a literal the tokenizer cannot decode (<c>U&amp;'…'</c>, a bit string, an <c>E'…'</c> with a
/// numeric escape) is reported as unscannable.
/// </para>
/// <para>
/// <b>What it refuses to read, and reports as unscannable rather than clean:</b> dynamic SQL
/// (<c>EXECUTE</c> of anything but a trigger's <c>FUNCTION</c>/<c>PROCEDURE</c> binding or the
/// <c>EXECUTE</c> privilege of a <c>GRANT</c>/<c>REVOKE</c>); a call to a schema-qualified
/// function or procedure outside <c>pg_catalog</c>, whose body was written elsewhere; and any text
/// the tokenizer cannot finish. A function <i>defined</i>, <i>dropped</i>, bound to a trigger or
/// named as a column default is a reference, not a call, and is not one of these; a
/// schema-qualified name before a parenthesis after <c>ON</c> is a table only when the nearest
/// statement verb before it is <c>CREATE</c>, <c>ALTER</c> or <c>DROP</c> - after a
/// <c>SELECT</c>, <c>JOIN</c> or a DML verb it is a join condition and the call is reported.
/// </para>
/// <para>
/// <b>What it cannot see, stated:</b> a call to an <i>unqualified</i> user function is read as a
/// built-in. That holds only while every function a migration creates is schema-qualified, which
/// every migration on this project is (schemas own their objects, ADR-0007 §7.1); a migration
/// creating an unqualified function is the case that would break it. A bare
/// <c>DROP CONSTRAINT</c> is not judged: by name alone the scanner cannot tell a <c>CHECK</c>,
/// whose replacement is a widening, from a <c>PRIMARY KEY</c>, <c>UNIQUE</c> or <c>EXCLUDE</c>,
/// whose removal is not. Whether a <c>CREATE OR REPLACE</c> actually displaces an existing object
/// is not known either - every one needs a <c>Contract</c>, and a new object is created without
/// <c>OR REPLACE</c>. And it reads the SQL of a migration's <c>Up</c>: <c>Down</c> is never run in
/// production (ADR-0007 §7.4, "rollback is not a database operation") and is where an Expand's
/// own drops legitimately live.
/// </para>
/// </remarks>
internal static class SqlStatementScanner
{
    /// <summary>The <c>DROP</c> targets that widen or tidy rather than remove: not destructive. <c>CONSTRAINT</c> is judged separately, on <c>CASCADE</c>.</summary>
    private static readonly ImmutableHashSet<string> NonDestructiveDropTargets =
        Keywords("DEFAULT", "NOT", "IDENTITY", "EXPRESSION");

    /// <summary>After <c>ADD</c>, these begin something that is not a column definition.</summary>
    private static readonly ImmutableHashSet<string> NonColumnAddTargets =
        Keywords("CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "EXCLUDE", "VALUE", "TABLE", "GENERATED");

    /// <summary>
    /// A schema-qualified name followed by <c>(</c> is a reference and not a call when the word
    /// before it is one of these. The list is an exemption, so a word missing from it produces a
    /// false positive - the loud direction. <c>ON</c> is not here: it is judged by the nearest
    /// statement verb, because it also introduces a join condition.
    /// </summary>
    private static readonly ImmutableHashSet<string> QualifiedNameIsAReferenceAfter = Keywords(
        "TABLE", "INTO", "ONLY", "REFERENCES", "FUNCTION", "PROCEDURE", "ROUTINE", "AGGREGATE",
        "DEFAULT", "EXISTS", "VIEW", "TYPE", "COPY");

    /// <summary>The verbs that decide what an <c>ON</c> introduces: a target for DDL, a condition for a query.</summary>
    private static readonly ImmutableHashSet<string> StatementVerbs = Keywords(
        "CREATE", "ALTER", "DROP", "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "JOIN");

    private static readonly ImmutableHashSet<string> DdlVerbs = Keywords("CREATE", "ALTER", "DROP");

    private const string ReplicationRoleSetting = "session_replication_role";

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
            ImmutableArray<ImmutableArray<SqlToken>> bodies = Bodies(statement);

            into.Add(new SqlStatement(
                location,
                Head: statement[0].Kind == SqlTokenKind.Word ? statement[0].Text.ToUpperInvariant() : string.Empty,
                Excerpt: Excerpt(statement),
                isTopLevel,
                HasProceduralBody: !bodies.IsEmpty,
                [.. Findings(statement)]));

            foreach (ImmutableArray<SqlToken> body in bodies)
            {
                string? code = Code(body);

                if (code is null)
                {
                    into.Add(new SqlStatement(
                        location + " › body",
                        Head: string.Empty,
                        Excerpt: string.Join(' ', body.Select(static token => token.Display)),
                        IsTopLevel: false,
                        HasProceduralBody: false,
                        [Unscannable("a body written as a literal the scanner does not decode (U&'…', B'…', X'…', or an E'…' with a numeric escape)")]));
                }
                else
                {
                    ScanText(code, location + " › body › statement", isTopLevel: false, into);
                }
            }
        }
    }

    /// <summary>
    /// The runs of tokens PostgreSQL reads as code: every dollar-quoted body, and a string literal
    /// where a literal is a body - after a statement-leading <c>DO</c> (its <c>LANGUAGE</c> name
    /// excepted), and after <c>AS</c> in a <c>CREATE [OR REPLACE] FUNCTION</c>/<c>PROCEDURE</c>, a
    /// second literal after a comma there being the link symbol of a C function. A literal body
    /// takes every literal adjacent to it into its run, because PostgreSQL joins adjacent string
    /// constants into one before the grammar sees them.
    /// </summary>
    private static ImmutableArray<ImmutableArray<SqlToken>> Bodies(ImmutableArray<SqlToken> t)
    {
        ImmutableArray<ImmutableArray<SqlToken>>.Builder bodies = ImmutableArray.CreateBuilder<ImmutableArray<SqlToken>>();
        bool isDo = t[0].Is("DO");
        bool isRoutineDefinition = t[0].Is("CREATE")
            && t.Take(5).Any(static token => token.Is("FUNCTION") || token.Is("PROCEDURE"));

        for (int i = 0; i < t.Length; i++)
        {
            SqlToken token = t[i];

            if (token.Kind == SqlTokenKind.DollarBody)
            {
                bodies.Add([token]);
            }
            else if (token.Kind == SqlTokenKind.Literal)
            {
                SqlToken before = At(t, i - 1);
                bool afterAs = before.Is("AS")
                    || (before.IsPunctuation(",") && At(t, i - 2).Kind == SqlTokenKind.Literal && At(t, i - 3).Is("AS"));

                if ((isDo && !before.Is("LANGUAGE")) || (isRoutineDefinition && afterAs))
                {
                    int end = i;
                    while (At(t, end + 1).Kind == SqlTokenKind.Literal)
                    {
                        end++;
                    }

                    bodies.Add(t[i..(end + 1)]);
                    i = end;
                }
            }
        }

        return bodies.ToImmutable();
    }

    /// <summary>The code a body run stands for, or <c>null</c> when any literal in it cannot be decoded.</summary>
    private static string? Code(ImmutableArray<SqlToken> body)
    {
        if (body.Length == 1 && body[0].Kind == SqlTokenKind.DollarBody)
        {
            return body[0].Text;
        }

        var code = new StringBuilder();

        foreach (SqlToken literal in body)
        {
            string? part = SqlTokenizer.DecodeLiteral(literal);
            if (part is null)
            {
                return null;
            }

            code.Append(part);
        }

        return code.ToString();
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

                if (target.Is("CONSTRAINT"))
                {
                    if (HasCascadeBeforeNextAction(t, i + 1))
                    {
                        yield return Destructive("DROP CONSTRAINT … CASCADE");
                    }
                }
                else if (!(target.Kind == SqlTokenKind.Word && NonDestructiveDropTargets.Contains(target.Text)))
                {
                    yield return Destructive("DROP " + (target.Kind == SqlTokenKind.Word ? target.Text.ToUpperInvariant() : target.Display));
                }
            }

            if (token.Is("CREATE") && next is not null && next.Is("OR") && At(t, i + 2).Is("REPLACE"))
            {
                yield return Destructive("CREATE OR REPLACE " + At(t, i + 3).Text.ToUpperInvariant());
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

            if (SetsTheReplicationRole(t, i))
            {
                yield return Destructive("SET session_replication_role");
            }

            if (token.Is("DISABLE") && next is not null && (next.Is("TRIGGER") || next.Is("RULE")))
            {
                yield return Destructive("DISABLE " + next.Text.ToUpperInvariant());
            }

            if (token.Is("ENABLE") && next is not null && next.Is("REPLICA"))
            {
                yield return Destructive("ENABLE REPLICA " + At(t, i + 2).Text.ToUpperInvariant());
            }

            if (token.Is("DISABLE") && next is not null && next.Is("ROW"))
            {
                yield return Destructive("DISABLE ROW LEVEL SECURITY");
            }

            if (token.Is("NO") && next is not null && next.Is("FORCE"))
            {
                yield return Destructive("NO FORCE ROW LEVEL SECURITY");
            }

            if (token.Is("DETACH") && next is not null && next.Is("PARTITION"))
            {
                yield return Destructive("DETACH PARTITION");
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

    /// <summary>
    /// <c>SET [LOCAL|SESSION] session_replication_role</c>, the same setting through
    /// <c>ALTER ROLE|DATABASE … SET</c>, and <c>set_config('session_replication_role', …)</c>.
    /// </summary>
    private static bool SetsTheReplicationRole(ImmutableArray<SqlToken> t, int i)
    {
        SqlToken token = t[i];

        if (token.Is("SET"))
        {
            int name = At(t, i + 1).Is("LOCAL") || At(t, i + 1).Is("SESSION") ? i + 2 : i + 1;
            return At(t, name).Is(ReplicationRoleSetting);
        }

        return token.Is("SET_CONFIG")
            && At(t, i + 1).IsPunctuation("(")
            && At(t, i + 2).Kind == SqlTokenKind.Literal
            && string.Equals(At(t, i + 2).Text, ReplicationRoleSetting, StringComparison.OrdinalIgnoreCase);
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

        foreach (SqlToken token in ActionTokens(t, j + 1))
        {
            notNull |= token.Is("NOT") && At(t, IndexOf(t, token) + 1).Is("NULL");
            hasDefault |= token.Is("DEFAULT") || token.Is("GENERATED");
        }

        return notNull && !hasDefault;
    }

    /// <summary><c>CASCADE</c> at the action's own nesting level, before the next top-level comma or the end.</summary>
    private static bool HasCascadeBeforeNextAction(ImmutableArray<SqlToken> t, int from) =>
        ActionTokens(t, from).Any(static token => token.Is("CASCADE"));

    /// <summary>The tokens of one <c>ALTER TABLE</c> action from <paramref name="from"/>: depth-0 tokens up to the next top-level comma.</summary>
    private static IEnumerable<SqlToken> ActionTokens(ImmutableArray<SqlToken> t, int from)
    {
        int depth = 0;

        for (int k = from; k < t.Length; k++)
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
                    yield break;
                }

                yield return token;
            }
        }
    }

    private static int IndexOf(ImmutableArray<SqlToken> t, SqlToken token) => t.IndexOf(token);

    /// <summary>
    /// <c>EXECUTE FUNCTION</c>/<c>EXECUTE PROCEDURE</c> binds a trigger, and <c>EXECUTE ON</c> or
    /// <c>EXECUTE,</c> is the privilege in a <c>GRANT</c>/<c>REVOKE</c>; every other <c>EXECUTE</c>
    /// runs text the scanner does not have.
    /// </summary>
    private static bool IsStaticExecute(SqlToken? next) =>
        next is not null && (next.Is("FUNCTION") || next.Is("PROCEDURE") || next.Is("ON") || next.IsPunctuation(","));

    /// <summary>
    /// <c>schema.name(</c> where the schema is not <c>pg_catalog</c> and the word before is not a
    /// reference context. After <c>ON</c> the name is a table only when the nearest statement verb
    /// before it is DDL; after a query verb, <c>ON</c> introduces a condition and the call counts.
    /// </summary>
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

        if (before.Kind != SqlTokenKind.Word)
        {
            return true;
        }

        if (before.Is("ON"))
        {
            SqlToken? verb = NearestStatementVerbBefore(t, i - 1);
            return !(verb is not null && DdlVerbs.Contains(verb.Text));
        }

        return !QualifiedNameIsAReferenceAfter.Contains(before.Text);
    }

    private static SqlToken? NearestStatementVerbBefore(ImmutableArray<SqlToken> t, int index)
    {
        for (int k = index - 1; k >= 0; k--)
        {
            if (t[k].Kind == SqlTokenKind.Word && StatementVerbs.Contains(t[k].Text))
            {
                return t[k];
            }
        }

        return null;
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
        var text = new StringBuilder();

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
