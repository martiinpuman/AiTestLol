using System;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.MigrationSafety;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The SQL scanner behind MIG2, proven statement by statement: what it matches, what it lets
/// through, and what it refuses to read.
/// </summary>
/// <remarks>
/// <para>
/// A SQL scan is a parser, and a parser that reads a subset of its input and reports as though it
/// read all of it is the defect this project has met most often. So every case below is one of
/// three kinds, and the kind is in the test name: a destructive statement the scanner must
/// <b>name</b>; a statement that only looks destructive - in a comment, a literal, a trigger
/// event, a privilege - that it must let through <b>while still counting it</b>; and text it
/// must report as <b>unscannable</b> rather than clean.
/// </para>
/// <para>
/// The inputs are hand-written literals, deliberately: each is one lexical shape the PostgreSQL
/// lexer defines, and the point is to cover the shapes, not to sample them.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class SqlScannerTests
{
    // ---- Destructive statements the scanner must name ----------------------------------------

    [Theory]
    [InlineData("DROP TABLE sales.invoice;", "DROP TABLE")]
    [InlineData("drop\n  /* not seen */ table\n  sales.invoice", "DROP TABLE")]
    [InlineData("DROP TABLE IF EXISTS sales.invoice;", "DROP TABLE")]
    [InlineData("DROP INDEX sales.ix_invoice_number;", "DROP INDEX")]
    [InlineData("DROP SCHEMA archive CASCADE;", "DROP SCHEMA")]
    [InlineData("DROP FUNCTION sales.guard();", "DROP FUNCTION")]
    [InlineData("ALTER TABLE sales.invoice DROP COLUMN legacy;", "DROP COLUMN")]
    [InlineData("ALTER  TABLE  sales.invoice  DROP  legacy;", "DROP LEGACY")]
    [InlineData("ALTER TABLE sales.invoice DROP IF EXISTS legacy;", "DROP LEGACY")]
    [InlineData("ALTER TABLE \"Sales\".\"Invoice\" DROP COLUMN \"Legacy\";", "DROP COLUMN")]
    [InlineData("TRUNCATE sales.invoice;", "TRUNCATE")]
    [InlineData("TRUNCATE TABLE ONLY sales.invoice RESTART IDENTITY;", "TRUNCATE")]
    [InlineData("DELETE FROM sales.invoice WHERE posted_at IS NULL;", "DELETE FROM")]
    [InlineData("WITH stale AS (SELECT id FROM sales.invoice) DELETE FROM sales.invoice USING stale WHERE 1 = 1;", "DELETE FROM")]
    [InlineData("MERGE INTO sales.invoice t USING sales.stage s ON t.id = s.id WHEN MATCHED THEN DELETE;", "MERGE … THEN DELETE")]
    [InlineData("ALTER TABLE sales.invoice RENAME COLUMN number TO invoice_number;", "RENAME")]
    [InlineData("ALTER TABLE sales.invoice RENAME TO sales_invoice;", "RENAME")]
    [InlineData("ALTER INDEX sales.ix_old RENAME TO ix_new;", "RENAME")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN total TYPE numeric(10,2);", "ALTER COLUMN … TYPE")]
    [InlineData("ALTER TABLE sales.invoice ALTER total SET DATA TYPE text;", "ALTER COLUMN … TYPE")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN region SET NOT NULL;", "ALTER COLUMN … SET NOT NULL")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN region text NOT NULL;", "ADD COLUMN … NOT NULL without a DEFAULT")]
    [InlineData("ALTER TABLE sales.invoice ADD region text NOT NULL;", "ADD COLUMN … NOT NULL without a DEFAULT")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN IF NOT EXISTS region text NOT NULL;", "ADD COLUMN … NOT NULL without a DEFAULT")]
    [InlineData("ALTER TABLE sales.invoice SET SCHEMA archive;", "SET SCHEMA")]
    public void Names_a_destructive_statement(string sql, string expected)
    {
        SqlScanReport report = SqlStatementScanner.Scan(sql);

        Destructive(report).ShouldBe([expected], report.Statements.ToString());
        Unscannable(report).ShouldBeEmpty();
    }

    [Fact]
    public void Names_every_destructive_action_of_a_multi_action_ALTER_TABLE()
    {
        SqlScanReport report = SqlStatementScanner.Scan(
            "ALTER TABLE sales.invoice ALTER COLUMN total TYPE numeric(10,2), ALTER COLUMN region SET NOT NULL, DROP COLUMN legacy;");

        Destructive(report).ShouldBe(["ALTER COLUMN … TYPE", "ALTER COLUMN … SET NOT NULL", "DROP COLUMN"]);
    }

    [Fact]
    public void Names_a_destructive_statement_sharing_a_line_with_a_harmless_one_and_counts_both()
    {
        SqlScanReport report = SqlStatementScanner.Scan("CREATE TABLE sales.a (id int); TRUNCATE sales.b;");

        report.TopLevelStatements.ShouldBe(2);
        Destructive(report).ShouldBe(["TRUNCATE"]);
        report.Statements.Single(static s => s.Findings.Length > 0).Location.ShouldBe("statement 2");
    }

    // ---- Dollar-quoted bodies are code, and are read -----------------------------------------

    [Fact]
    public void Names_a_DROP_TABLE_hidden_in_a_DO_body()
    {
        SqlScanReport report = SqlStatementScanner.Scan("DO $$ BEGIN DROP TABLE sales.invoice; END $$;");

        SqlStatement inner = report.Statements.Single(static s => s.Findings.Length > 0);
        inner.Findings.Single().What.ShouldBe("DROP TABLE");
        inner.IsTopLevel.ShouldBeFalse();
        inner.Location.ShouldBe("statement 1 › body › statement 1");
        report.Statements.Single(static s => s.IsTopLevel).HasProceduralBody.ShouldBeTrue();
    }

    [Fact]
    public void Names_a_DROP_TABLE_two_dollar_quotes_deep_with_different_tags()
    {
        // A function created inside a DO block: the inner body's tag differs from the outer one's,
        // and the DROP is at the second level of nesting.
        SqlScanReport report = SqlStatementScanner.Scan(
            "DO $outer$ BEGIN CREATE FUNCTION sales.f() RETURNS void LANGUAGE plpgsql AS $fn$ BEGIN "
            + "DROP TABLE sales.invoice; END $fn$; END $outer$;");

        SqlStatement inner = report.Statements.Single(static s => s.Findings.Length > 0);
        inner.Findings.Single().What.ShouldBe("DROP TABLE");
        inner.Location.ShouldBe("statement 1 › body › statement 1 › body › statement 1");
    }

    [Fact]
    public void Names_a_TRUNCATE_inside_a_conditional_branch_of_a_body()
    {
        SqlScanReport report = SqlStatementScanner.Scan(
            "DO $$ BEGIN IF EXISTS (SELECT 1 FROM sales.invoice) THEN TRUNCATE sales.invoice; END IF; END $$;");

        Destructive(report).ShouldBe(["TRUNCATE"]);
    }

    // ---- Things that only look destructive are let through, and counted ----------------------

    [Theory]
    [InlineData("SELECT 'DROP TABLE sales.invoice';")]
    [InlineData("INSERT INTO sales.note VALUES ('it''s not a DROP TABLE');")]
    [InlineData("INSERT INTO sales.note VALUES (E'a backslash-escaped \\' quote, then DROP TABLE x');")]
    [InlineData("INSERT INTO sales.note VALUES (U&'unicode DROP TABLE');")]
    [InlineData("-- DROP TABLE sales.invoice\nSELECT 1;")]
    [InlineData("/* DROP TABLE /* nested */ sales.invoice */ SELECT 1;")]
    [InlineData("SELECT \"DROP TABLE\" FROM sales.invoice;")]
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT ck_invoice_total;")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN region DROP NOT NULL, ALTER COLUMN total DROP DEFAULT;")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN id DROP IDENTITY IF EXISTS, ALTER COLUMN x DROP EXPRESSION;")]
    [InlineData("CREATE TABLE sales.invoice (id int NOT NULL, note text NOT NULL);")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN region text NOT NULL DEFAULT 'none';")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN seq integer GENERATED ALWAYS AS IDENTITY NOT NULL;")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN region text CHECK (region IS NOT NULL);")]
    [InlineData("ALTER TABLE sales.invoice ADD CONSTRAINT ck CHECK (total > 0) NOT VALID;")]
    [InlineData("ALTER TABLE sales.invoice ADD CONSTRAINT fk FOREIGN KEY (party_id) REFERENCES parties.party (id) ON DELETE RESTRICT;")]
    [InlineData("CREATE TRIGGER guard BEFORE UPDATE OR DELETE OR TRUNCATE ON sales.invoice FOR EACH STATEMENT EXECUTE FUNCTION sales.guard();")]
    [InlineData("GRANT SELECT, DELETE, TRUNCATE ON sales.invoice TO aurora_app;")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN created_at SET DEFAULT now();")]
    [InlineData("ALTER TYPE sales.status ADD VALUE 'Void';")]
    [InlineData("CREATE FUNCTION sales.guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'append-only' USING ERRCODE = '42501'; END $$;")]
    [InlineData("SELECT setval(pg_get_serial_sequence('sales.invoice', 'id'), 1000);")]
    [InlineData("SELECT pg_catalog.setval('sales.invoice_id_seq', 1000);")]
    [InlineData("CREATE TABLE IF NOT EXISTS sales.invoice (id int);")]
    [InlineData("INSERT INTO sales.invoice (id) VALUES (1);")]
    [InlineData("CREATE INDEX ix ON sales.invoice (number);")]
    [InlineData("ALTER TABLE sales.invoice ADD CONSTRAINT ex EXCLUDE USING gist (party_id WITH =, daterange(valid_from, valid_to, '[)') WITH &&);")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN number SET DEFAULT sales.next_number();")]
    [InlineData("UPDATE sales.invoice SET total = total * 1.5e0 WHERE id = $1 AND note ~ '^x' AND tags @> '{}'::text[];")]
    public void Lets_a_harmless_statement_through_and_counts_it(string sql)
    {
        SqlScanReport report = SqlStatementScanner.Scan(sql);

        report.TopLevelStatements.ShouldBe(1, report.Statements.ToString());
        Destructive(report).ShouldBeEmpty();
        Unscannable(report).ShouldBeEmpty();
    }

    // ---- What it refuses to read is reported as unscannable, never as clean --------------------

    [Theory]
    [InlineData("DO $$ BEGIN EXECUTE 'DROP TABLE sales.invoice'; END $$;", "EXECUTE")]
    [InlineData("DO $$ BEGIN EXECUTE format('DROP TABLE %I', 'invoice'); END $$;", "EXECUTE")]
    [InlineData("SELECT sales.rebuild_everything();", "calls sales.rebuild_everything(…)")]
    [InlineData("DO $$ BEGIN PERFORM sales.rebuild_everything(); END $$;", "calls sales.rebuild_everything(…)")]
    [InlineData("CALL sales.rebuild_everything();", "calls sales.rebuild_everything(…)")]
    [InlineData("INSERT INTO sales.invoice (id) SELECT sales.next_id();", "calls sales.next_id(…)")]
    [InlineData("INSERT INTO sales.note VALUES ('never closed);", "never closed")]
    [InlineData("INSERT INTO sales.note VALUES (E'never closed\\');", "never closed")]
    [InlineData("SELECT \"never closed FROM sales.invoice;", "never closed")]
    [InlineData("DO $$ BEGIN DROP TABLE sales.invoice; END", "never closed")]
    [InlineData("/* never closed SELECT 1;", "never closed")]
    [InlineData("SELECT 1 § 2;", "unexpected character")]
    public void Reports_what_it_cannot_read_as_unscannable(string sql, string expectedFragment)
    {
        SqlScanReport report = SqlStatementScanner.Scan(sql);

        Unscannable(report).ShouldContain(
            what => what.Contains(expectedFragment, StringComparison.Ordinal),
            report.Statements.ToString());
    }

    [Fact]
    public void A_lexical_failure_inside_a_body_is_reported_at_the_body()
    {
        SqlScanReport report = SqlStatementScanner.Scan("DO $$ BEGIN SELECT 'never closed; END $$;");

        SqlStatement failure = report.Statements.Single(static s => s.Findings.Any(f => f.Kind == SqlFindingKind.Unscannable));
        failure.IsTopLevel.ShouldBeFalse();
        failure.Location.ShouldStartWith("statement 1 › body");
    }

    // ---- The shape the DataOnly rule reads ----------------------------------------------------

    [Fact]
    public void Reports_the_head_of_each_top_level_statement()
    {
        SqlScanReport report = SqlStatementScanner.Scan(
            "update sales.invoice set total = 0; INSERT INTO sales.note VALUES (1); DO $$ BEGIN NULL; END $$;");

        report.Statements.Where(static s => s.IsTopLevel).Select(static s => s.Head).ShouldBe(["UPDATE", "INSERT", "DO"]);
        report.Statements.Where(static s => s.IsTopLevel).Select(static s => s.HasProceduralBody).ShouldBe([false, false, true]);
    }

    [Fact]
    public void Empty_text_is_zero_statements_not_one_clean_one()
    {
        SqlStatementScanner.Scan("  -- nothing here\n").Statements.ShouldBeEmpty();
    }

    private static ImmutableArray<string> Destructive(SqlScanReport report) =>
    [
        .. report.Statements.SelectMany(static s => s.Findings)
            .Where(static f => f.Kind == SqlFindingKind.Destructive)
            .Select(static f => f.What),
    ];

    private static ImmutableArray<string> Unscannable(SqlScanReport report) =>
    [
        .. report.Statements.SelectMany(static s => s.Findings)
            .Where(static f => f.Kind == SqlFindingKind.Unscannable)
            .Select(static f => f.What),
    ];
}
