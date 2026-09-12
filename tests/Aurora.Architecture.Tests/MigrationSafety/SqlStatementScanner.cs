using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Aurora.Architecture.Tests.MigrationSafety;

/// <summary>What a finding is: something a category must justify, or something the scanner could not read.</summary>
internal enum SqlFindingKind
{
    /// <summary>A statement ADR-0037 §2 classes as destructive, which only a <c>Contract</c> may run - unless MIG2 attributes it to an operation that proves it a widening.</summary>
    Destructive,

    /// <summary>Code the scanner does not read, so the statement can hide anything.</summary>
    Unscannable,
}

/// <summary>One finding: what was matched and how it reads.</summary>
internal sealed record SqlFinding(SqlFindingKind Kind, string What)
{
    /// <summary>For a <c>DROP CONSTRAINT</c> or an <c>ALTER COLUMN … TYPE</c>: the table the statement names, lower-cased where unquoted.</summary>
    public string? Table { get; init; }

    /// <summary>For a <c>DROP CONSTRAINT</c>: the constraint; for an <c>ALTER COLUMN … TYPE</c>: the column. Lower-cased where unquoted.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// For an <c>ALTER COLUMN … TYPE</c>: the three conditions ADR-0037 §4 attaches to a widening -
    /// no <c>USING</c>, no <c>COLLATE</c>, and no other action in the same <c>ALTER TABLE</c>.
    /// </summary>
    public bool StandsAlone { get; init; }
}

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

/// <summary>A trigger a statement creates, or enables <c>ALWAYS</c>: the table and the trigger name, lower-cased where unquoted (<c>all</c> for <c>ENABLE ALWAYS TRIGGER ALL</c>).</summary>
internal sealed record TriggerReference(string Table, string Name, string Location);

/// <summary>
/// Every statement of one command text, top level and inside bodies, in document order - with
/// what was walked to get there, so a scan can say how much it read and not only what it found.
/// </summary>
/// <param name="Statements">Every statement, top level and nested, in document order.</param>
/// <param name="BodiesRead">How many procedural bodies - dollar-quoted or literal runs - were decoded and read again as code.</param>
/// <param name="TriggersCreated">Every <c>CREATE [CONSTRAINT] TRIGGER … ON table</c>, for ADR-0037 §2.6's born-weak check.</param>
/// <param name="TriggersEnabledAlways">Every <c>ALTER TABLE … ENABLE ALWAYS TRIGGER name|ALL</c>.</param>
internal sealed record SqlScanReport(
    ImmutableArray<SqlStatement> Statements,
    int BodiesRead,
    ImmutableArray<TriggerReference> TriggersCreated,
    ImmutableArray<TriggerReference> TriggersEnabledAlways)
{
    public int TopLevelStatements => Statements.Count(static statement => statement.IsTopLevel);
}

/// <summary>
/// Reads the SQL a migration will run and names what in it a category must justify.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule it applies (ADR-0037 §2.1):</b> a statement is destructive when, from some prior
/// state the migration does not control, it narrows what the schema offers (limb A, removal -
/// visible, with a widening exception) or reduces the set of executions in which a declared
/// invariant is enforced (limb B, suppression - the object stays named and defined, and no widening
/// exception). Spelling is evidence of effect and never a substitute for it.
/// </para>
/// <para>
/// <b>Limb A, matched on tokens anywhere in a statement and inside every body:</b> <c>DROP</c> of
/// anything but a default, <c>NOT NULL</c>, identity or expression; <c>DROP CONSTRAINT</c> - bare
/// or with <c>CASCADE</c> - which MIG2 may attribute to a typed check-constraint operation and clear
/// (ADR-0037 §3.2), never on the constraint's name; <c>TRUNCATE</c> of a table; <c>DELETE FROM</c>
/// and <c>MERGE … THEN DELETE</c>; any <c>RENAME</c>; <c>ALTER [COLUMN] … TYPE</c>, which MIG2 may
/// attribute to an <c>AlterColumnOperation</c> on ADR-0037 §4's two-shape allowlist and clear;
/// <c>ALTER [COLUMN] … SET NOT NULL</c>; <c>SET SCHEMA</c>; <c>ADD [COLUMN] … NOT NULL</c> with no
/// <c>DEFAULT</c> and no <c>GENERATED</c>; <c>DETACH PARTITION</c>.
/// </para>
/// <para>
/// <b>Limb B, by the catalog column that records "in force"</b> (<c>postgres-invariant-suppression.md</c>,
/// rows <see cref="CoveredSuppressionRows"/>): <c>pg_trigger.tgenabled</c> and
/// <c>pg_rewrite.ev_enabled</c> - <c>DISABLE</c> (<c>D</c>), <c>ENABLE REPLICA</c> (<c>R</c>), and
/// plain <c>ENABLE</c> (<c>O</c>, a reduction from <c>A</c>, the one mode that cannot reduce), for
/// triggers, rules and event triggers; the <c>session_replication_role</c> GUC in every spelling
/// (<c>SET</c>, <c>SET LOCAL</c>, <c>ALTER ROLE|DATABASE … SET</c>, <c>set_config(…)</c>); the
/// body a trigger executes, through <c>CREATE OR REPLACE</c> of anything (ADR-0037 §2.5: a
/// migration runs once under an advisory lock, so <c>OR REPLACE</c> buys no idempotence it needs,
/// only a meaning that depends on state it does not control, where <c>CREATE</c> fails loudly);
/// row-level security through <c>DISABLE ROW LEVEL SECURITY</c> and <c>NO FORCE ROW LEVEL SECURITY</c>;
/// and trigger scope through <c>DROP TRIGGER</c> and <c>CREATE OR REPLACE TRIGGER</c>.
/// </para>
/// <para>
/// <b>What it reads again as code:</b> every dollar-quoted body, and a string literal where
/// PostgreSQL reads a literal as code - after <c>DO [LANGUAGE name]</c>, and after <c>AS</c> in a
/// statement that defines a <c>FUNCTION</c> or <c>PROCEDURE</c> - judged from the tokens around
/// the literal and not from how the statement begins, because inside a procedural block a
/// statement begins with <c>BEGIN</c>, <c>IF</c> or <c>DECLARE</c> and the routine it creates is
/// nested. Adjacent string constants are one constant to PostgreSQL, so a run of literals is
/// decoded and joined before it is read. A body in a literal the tokenizer cannot decode
/// (<c>U&amp;'…'</c>, a bit string, an <c>E'…'</c> with a numeric escape) is reported as unscannable.
/// </para>
/// <para>
/// <b>What it refuses to read, and reports as unscannable rather than clean:</b> dynamic SQL
/// (<c>EXECUTE</c> of anything but a trigger's <c>FUNCTION</c>/<c>PROCEDURE</c> binding or the
/// <c>EXECUTE</c> privilege of a <c>GRANT</c>/<c>REVOKE</c>); a call to a schema-qualified
/// function or procedure outside <c>pg_catalog</c>, whose body was written elsewhere; and any text
/// the tokenizer cannot finish. A function <i>defined</i>, <i>dropped</i>, bound to a trigger or
/// named as a column default is a reference, not a call; a schema-qualified name before a
/// parenthesis after <c>ON</c> is a table only when the nearest statement verb before it is
/// <c>CREATE</c>, <c>ALTER</c> or <c>DROP</c>.
/// </para>
/// <para>
/// <b>What it cannot see, stated:</b> a call to an <i>unqualified</i> user function is read as a
/// built-in, which holds only while every function a migration creates is schema-qualified. A
/// <c>CREATE RULE … DO INSTEAD NOTHING</c> on an existing table is <c>DISABLE RULE</c> from the
/// other side and is not judged - the architect's ruling on it is pending, and creating an object
/// is otherwise the additive direction. Suppression rows the scanner does not cover are named by
/// their absence from <see cref="CoveredSuppressionRows"/>. And it reads a migration's <c>Up</c>:
/// <c>Down</c> is never run in production (ADR-0007 §7.4).
/// </para>
/// </remarks>
internal static class SqlStatementScanner
{
    /// <summary>
    /// The rows of <c>docs/architecture/postgres-invariant-suppression.md</c> whose statements this
    /// scanner names as destructive. Declared here and asserted equal to what the scanner implements
    /// by <c>SqlScannerTests</c>, so that coverage is measured and not asserted: S1 trigger firing
    /// mode, S2 rule firing mode, S3 the replication-role GUC, S4 the body a trigger executes, S6
    /// the two row-level-security switches (not <c>OWNER TO</c>), S10 event-trigger firing mode,
    /// S12 trigger scope through re-creation. Not covered: S5 (search-path shadowing), S7
    /// (<c>NOT VALID</c>), S8 (deferral), S9 (an invalid index), S11 (ACLs and ownership).
    /// </summary>
    public static readonly ImmutableArray<string> CoveredSuppressionRows = ["S1", "S2", "S3", "S4", "S6", "S10", "S12"];

    /// <summary>The <c>DROP</c> targets that widen or tidy rather than remove: not destructive. <c>CONSTRAINT</c> is judged separately.</summary>
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
        var walk = new Walk();
        ScanText(sql, "statement", isTopLevel: true, walk);
        return new SqlScanReport([.. walk.Statements], walk.BodiesRead, [.. walk.TriggersCreated], [.. walk.TriggersEnabledAlways]);
    }

    /// <summary>What one scan accumulates across every nesting level.</summary>
    private sealed class Walk
    {
        public List<SqlStatement> Statements { get; } = [];

        public List<TriggerReference> TriggersCreated { get; } = [];

        public List<TriggerReference> TriggersEnabledAlways { get; } = [];

        public int BodiesRead { get; set; }
    }

    private static void ScanText(string sql, string locationPrefix, bool isTopLevel, Walk walk)
    {
        ImmutableArray<SqlToken> tokens;
        try
        {
            tokens = SqlTokenizer.Tokenize(sql);
        }
        catch (UnscannableSqlException unreadable)
        {
            walk.Statements.Add(new SqlStatement(
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

            walk.Statements.Add(new SqlStatement(
                location,
                Head: statement[0].Kind == SqlTokenKind.Word ? statement[0].Text.ToUpperInvariant() : string.Empty,
                Excerpt: Excerpt(statement),
                isTopLevel,
                HasProceduralBody: !bodies.IsEmpty,
                [.. Findings(statement)]));

            CollectTriggers(statement, location, walk);

            foreach (ImmutableArray<SqlToken> body in bodies)
            {
                string? code = Code(body);

                if (code is null)
                {
                    walk.Statements.Add(new SqlStatement(
                        location + " › body",
                        Head: string.Empty,
                        Excerpt: string.Join(' ', body.Select(static token => token.Display)),
                        IsTopLevel: false,
                        HasProceduralBody: false,
                        [Unscannable("a body written as a literal the scanner does not decode (U&'…', B'…', X'…', or an E'…' with a numeric escape)")]));
                }
                else
                {
                    walk.BodiesRead++;
                    ScanText(code, location + " › body › statement", isTopLevel: false, walk);
                }
            }
        }
    }

    /// <summary>
    /// The runs of tokens PostgreSQL reads as code: every dollar-quoted body, and a string literal
    /// where a literal is a body - directly after <c>DO</c> or after <c>DO LANGUAGE name</c>, and
    /// after <c>AS</c> where the statement defines a <c>FUNCTION</c> or <c>PROCEDURE</c> (a second
    /// literal after a comma there being the link symbol of a C function). Decided from the tokens
    /// around the literal and never from the statement's first token: inside a procedural block the
    /// statement that creates a routine begins with <c>IF</c> or <c>BEGIN</c>, which is what let a
    /// nested quoted body go unread (PR #16, third review). A literal body takes every literal
    /// adjacent to it into its run, because PostgreSQL joins adjacent string constants into one
    /// before the grammar sees them.
    /// </summary>
    private static ImmutableArray<ImmutableArray<SqlToken>> Bodies(ImmutableArray<SqlToken> t)
    {
        ImmutableArray<ImmutableArray<SqlToken>>.Builder bodies = ImmutableArray.CreateBuilder<ImmutableArray<SqlToken>>();
        bool definesARoutine = t.Any(static token => token.Is("FUNCTION") || token.Is("PROCEDURE"));

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
                bool afterDo = before.Is("DO") || (At(t, i - 3).Is("DO") && At(t, i - 2).Is("LANGUAGE"));
                bool afterAs = before.Is("AS")
                    || (before.IsPunctuation(",") && At(t, i - 2).Kind == SqlTokenKind.Literal && At(t, i - 3).Is("AS"));

                if (afterDo || (definesARoutine && afterAs))
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
        string? alteredTable = AlteredTable(t);
        bool eventTrigger = IsEventTriggerStatement(t);

        for (int i = 0; i < t.Length; i++)
        {
            SqlToken token = t[i];
            SqlToken? next = i + 1 < t.Length ? t[i + 1] : null;

            if (token.Is("DROP") && next is { IsName: true })
            {
                SqlToken target = next.Is("IF") && At(t, i + 2).Is("EXISTS") && At(t, i + 3).IsName ? t[i + 3] : next;

                if (target.Is("CONSTRAINT"))
                {
                    int nameAt = t.IndexOf(target) + 1;
                    if (At(t, nameAt).Is("IF") && At(t, nameAt + 1).Is("EXISTS"))
                    {
                        nameAt += 2;
                    }

                    yield return HasCascadeBeforeNextAction(t, i + 1)
                        ? Destructive("DROP CONSTRAINT … CASCADE")
                        : Destructive("DROP CONSTRAINT") with { Table = alteredTable, Name = NameOf(At(t, nameAt)) };
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

            if (token.Is("DISABLE") && (next is null || (next.Is("TRIGGER") || next.Is("RULE")) || eventTrigger))
            {
                yield return Destructive(eventTrigger ? "ALTER EVENT TRIGGER … DISABLE" : "DISABLE " + next!.Text.ToUpperInvariant());
            }

            if (token.Is("ENABLE"))
            {
                foreach (SqlFinding finding in EnableFindings(t, i, eventTrigger))
                {
                    yield return finding;
                }
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
                foreach (SqlFinding finding in AlterColumnFindings(t, i + 1, alteredTable))
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
    /// The firing-mode lattice of <c>pg_trigger.tgenabled</c> (S1), <c>pg_rewrite.ev_enabled</c> (S2)
    /// and <c>pg_event_trigger.evtenabled</c> (S10): <c>ALWAYS</c> is the top and the only mode that
    /// cannot reduce from an unknown prior state; <c>REPLICA</c> never fires for an ordinary write;
    /// plain <c>ENABLE</c> writes <c>O</c>, a reduction from <c>A</c> (ADR-0037 §2.4).
    /// <c>ENABLE ROW LEVEL SECURITY</c> is not a firing mode and is clean.
    /// </summary>
    private static IEnumerable<SqlFinding> EnableFindings(ImmutableArray<SqlToken> t, int i, bool eventTrigger)
    {
        SqlToken next = At(t, i + 1);

        if (next.Is("ALWAYS") || next.Is("ROW"))
        {
            yield break;
        }

        if (next.Is("REPLICA"))
        {
            yield return Destructive(eventTrigger ? "ALTER EVENT TRIGGER … ENABLE REPLICA" : "ENABLE REPLICA " + At(t, i + 2).Text.ToUpperInvariant());
        }
        else if (next.Is("TRIGGER") || next.Is("RULE"))
        {
            yield return Destructive("ENABLE " + next.Text.ToUpperInvariant() + " (tgenabled 'O', a reduction from ALWAYS)");
        }
        else if (eventTrigger && next.Text.Length == 0)
        {
            yield return Destructive("ALTER EVENT TRIGGER … ENABLE (a reduction from ALWAYS)");
        }
    }

    /// <summary>
    /// <c>SET [LOCAL|SESSION] session_replication_role</c>, the same setting through
    /// <c>ALTER ROLE|DATABASE … SET</c>, and <c>[pg_catalog.]set_config('session_replication_role', …)</c>.
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
    private static IEnumerable<SqlFinding> AlterColumnFindings(ImmutableArray<SqlToken> t, int j, string? alteredTable)
    {
        if (At(t, j).Is("COLUMN"))
        {
            j++;
        }

        if (!At(t, j).IsName)
        {
            yield break;
        }

        SqlToken column = t[j];
        j++;

        if (At(t, j).Is("SET") && At(t, j + 1).Is("DATA"))
        {
            j += 2;
        }

        if (At(t, j).Is("TYPE"))
        {
            bool qualified = ActionTokens(t, j).Any(static token => token.Is("USING") || token.Is("COLLATE"));
            bool alone = !HasTopLevelComma(t);

            yield return Destructive("ALTER COLUMN … TYPE") with
            {
                Table = alteredTable,
                Name = NameOf(column),
                StandsAlone = alone && !qualified,
            };
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
            notNull |= token.Is("NOT") && At(t, t.IndexOf(token) + 1).Is("NULL");
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

    private static bool HasTopLevelComma(ImmutableArray<SqlToken> t)
    {
        int depth = 0;

        foreach (SqlToken token in t)
        {
            if (token.IsPunctuation("("))
            {
                depth++;
            }
            else if (token.IsPunctuation(")"))
            {
                depth--;
            }
            else if (depth == 0 && token.IsPunctuation(","))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The table an <c>ALTER TABLE [IF EXISTS] [ONLY] name</c> statement names, or <c>null</c>.</summary>
    private static string? AlteredTable(ImmutableArray<SqlToken> t)
    {
        if (!(t[0].Is("ALTER") && At(t, 1).Is("TABLE")))
        {
            return null;
        }

        int j = 2;
        if (At(t, j).Is("IF") && At(t, j + 1).Is("EXISTS"))
        {
            j += 2;
        }

        if (At(t, j).Is("ONLY"))
        {
            j++;
        }

        return QualifiedNameAt(t, j);
    }

    private static bool IsEventTriggerStatement(ImmutableArray<SqlToken> t) =>
        t[0].Is("ALTER") && At(t, 1).Is("EVENT") && At(t, 2).Is("TRIGGER");

    /// <summary>
    /// <c>CREATE [OR REPLACE] [CONSTRAINT] TRIGGER name … ON table</c> and
    /// <c>ALTER TABLE table ENABLE ALWAYS TRIGGER name|ALL|USER</c>, for ADR-0037 §2.6's rule that a
    /// trigger a migration creates is followed by <c>ENABLE ALWAYS</c> - <c>CREATE TRIGGER</c>
    /// produces <c>tgenabled = 'O'</c>, a guard born weak, which no suppression statement betrays.
    /// </summary>
    private static void CollectTriggers(ImmutableArray<SqlToken> t, string location, Walk walk)
    {
        for (int i = 1; i < t.Length; i++)
        {
            if (!t[i].Is("TRIGGER"))
            {
                continue;
            }

            SqlToken before = t[i - 1];

            if ((before.Is("CREATE") || before.Is("REPLACE") || before.Is("CONSTRAINT")) && At(t, i + 1).IsName && !At(t, i - 1).Is("EVENT"))
            {
                int on = i + 2;
                while (on < t.Length && !t[on].Is("ON"))
                {
                    on++;
                }

                string? table = QualifiedNameAt(t, on + 1);
                if (table is not null)
                {
                    walk.TriggersCreated.Add(new TriggerReference(table, NameOf(t[i + 1]), location));
                }
            }
            else if (before.Is("ALWAYS") && At(t, i - 2).Is("ENABLE") && At(t, i + 1).IsName)
            {
                string? table = AlteredTable(t);
                if (table is not null)
                {
                    walk.TriggersEnabledAlways.Add(new TriggerReference(table, NameOf(t[i + 1]), location));
                }
            }
        }
    }

    /// <summary>A possibly schema-qualified name at <paramref name="j"/>, lower-cased where unquoted.</summary>
    private static string? QualifiedNameAt(ImmutableArray<SqlToken> t, int j)
    {
        if (!At(t, j).IsName)
        {
            return null;
        }

        return At(t, j + 1).IsPunctuation(".") && At(t, j + 2).IsName
            ? NameOf(t[j]) + "." + NameOf(t[j + 2])
            : NameOf(t[j]);
    }

    /// <summary>How PostgreSQL stores an identifier: an unquoted one folded to lower case, a quoted one as written.</summary>
    private static string NameOf(SqlToken token) => token.Kind == SqlTokenKind.Word ? token.Text.ToLowerInvariant() : token.Text;

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
