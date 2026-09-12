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
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT uq_invoice_number CASCADE;", "DROP CONSTRAINT … CASCADE")]
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT IF EXISTS uq_invoice_number CASCADE;", "DROP CONSTRAINT … CASCADE")]
    [InlineData("ALTER TABLE catalog.operator_audit_event DISABLE TRIGGER trg_append_only;", "DISABLE TRIGGER")]
    [InlineData("ALTER TABLE sales.invoice DISABLE RULE r_guard;", "DISABLE RULE")]
    [InlineData("ALTER TABLE sales.invoice DISABLE ROW LEVEL SECURITY;", "DISABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE sales.invoice NO FORCE ROW LEVEL SECURITY;", "NO FORCE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE sales.invoice DETACH PARTITION sales.invoice_2024;", "DETACH PARTITION")]
    [InlineData("ALTER TABLE catalog.operator_audit_event ENABLE REPLICA TRIGGER trg_append_only;", "ENABLE REPLICA TRIGGER")]
    [InlineData("ALTER TABLE catalog.operator_audit_event ENABLE TRIGGER trg_append_only;", "ENABLE TRIGGER (tgenabled 'O', a reduction from ALWAYS)")]
    [InlineData("ALTER TABLE sales.invoice ENABLE RULE r_guard;", "ENABLE RULE (tgenabled 'O', a reduction from ALWAYS)")]
    [InlineData("ALTER EVENT TRIGGER evt_guard DISABLE;", "ALTER EVENT TRIGGER … DISABLE")]
    [InlineData("ALTER EVENT TRIGGER evt_guard ENABLE;", "ALTER EVENT TRIGGER … ENABLE (a reduction from ALWAYS)")]
    [InlineData("ALTER EVENT TRIGGER evt_guard ENABLE REPLICA;", "ALTER EVENT TRIGGER … ENABLE REPLICA")]
    [InlineData("ALTER DATABASE aurora SET session_replication_role = 'replica';", "SET session_replication_role")]
    [InlineData("SELECT pg_catalog.set_config('session_replication_role', 'replica', true);", "SET session_replication_role")]
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT pk_invoice;", "DROP CONSTRAINT")]
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT IF EXISTS ck_invoice_total;", "DROP CONSTRAINT")]
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT uq_invoice_number RESTRICT;", "DROP CONSTRAINT")]
    [InlineData("ALTER TABLE sales.invoice DROP CONSTRAINT ck_invoice_total, ADD CONSTRAINT fk FOREIGN KEY (party_id) REFERENCES parties.party (id) ON DELETE CASCADE;", "DROP CONSTRAINT")]
    [InlineData("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_proc WHERE proname = 'f') THEN CREATE FUNCTION sales.f() RETURNS void LANGUAGE plpgsql AS 'BEGIN DROP TABLE sales.invoice; END'; END IF; END $$;", "DROP TABLE")]
    [InlineData("DO $$ BEGIN CREATE PROCEDURE sales.p() LANGUAGE sql AS 'DELETE FROM sales.invoice'; END $$;", "DELETE FROM")]
    [InlineData("ALTER TABLE sales.invoice ENABLE REPLICA RULE r_guard;", "ENABLE REPLICA RULE")]
    [InlineData("SET session_replication_role = 'replica';", "SET session_replication_role")]
    [InlineData("SET LOCAL session_replication_role TO replica;", "SET session_replication_role")]
    [InlineData("ALTER ROLE aurora_app SET session_replication_role = 'replica';", "SET session_replication_role")]
    [InlineData("SELECT set_config('session_replication_role', 'replica', false);", "SET session_replication_role")]
    [InlineData("CREATE OR REPLACE FUNCTION catalog.refuse_append_only_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$;", "CREATE OR REPLACE FUNCTION")]
    [InlineData("CREATE OR REPLACE VIEW sales.v AS SELECT 1;", "CREATE OR REPLACE VIEW")]
    [InlineData("CREATE OR REPLACE TRIGGER trg BEFORE UPDATE ON sales.invoice FOR EACH ROW EXECUTE FUNCTION sales.f();", "CREATE OR REPLACE TRIGGER")]
    [InlineData("DO 'BEGIN DR'\n'OP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("DO 'BEGIN '\n'DROP TA'\n'BLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("DO 'BEGIN '\nE'DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("CREATE FUNCTION sales.f() RETURNS void LANGUAGE plpgsql AS 'BEGIN '\n'DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("DO 'BEGIN DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("DO LANGUAGE plpgsql 'BEGIN DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("DO 'BEGIN DROP TABLE sales.invoice; END' LANGUAGE plpgsql;", "DROP TABLE")]
    [InlineData("DO E'BEGIN TRUNCATE sales.invoice; END';", "TRUNCATE")]
    [InlineData("DO 'BEGIN RAISE NOTICE ''quoted''; DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("DO E'BEGIN RAISE NOTICE \\'quoted\\'; DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("CREATE FUNCTION sales.purge() RETURNS void LANGUAGE sql AS 'DELETE FROM sales.invoice';", "DELETE FROM")]
    [InlineData("CREATE PROCEDURE sales.purge() LANGUAGE plpgsql AS 'BEGIN DROP TABLE sales.invoice; END';", "DROP TABLE")]
    [InlineData("CREATE FUNCTION sales.purge() RETURNS void AS 'BEGIN DROP TABLE sales.invoice; END' LANGUAGE plpgsql;", "DROP TABLE")]
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
    public void A_quoted_body_and_a_dollar_quoted_body_are_the_same_statement_and_are_read_the_same_way()
    {
        // The first review's B-1: DO $$…$$ and DO '…' are one statement in PostgreSQL, and the
        // scanner read only the first. Both spellings must produce the same finding at the same
        // location, and the statement must know it carries a body.
        SqlScanReport dollar = SqlStatementScanner.Scan("DO $$ BEGIN DROP TABLE sales.invoice; END $$;");
        SqlScanReport quoted = SqlStatementScanner.Scan("DO 'BEGIN DROP TABLE sales.invoice; END';");

        foreach (SqlScanReport report in new[] { dollar, quoted })
        {
            SqlStatement inner = report.Statements.Single(static s => s.Findings.Length > 0);
            inner.Findings.Single().What.ShouldBe("DROP TABLE");
            inner.Location.ShouldBe("statement 1 › body › statement 1");
            report.Statements.Single(static s => s.IsTopLevel).HasProceduralBody.ShouldBeTrue();
        }
    }

    [Fact]
    public void A_body_split_across_adjacent_literals_is_read_whole_and_located_as_one_body()
    {
        // The second review's N-2: PostgreSQL joins string constants separated by a newline before
        // the grammar sees them, so the two literals are one DO body. The scanner read the first
        // half only, and reported nothing.
        SqlScanReport report = SqlStatementScanner.Scan("DO 'BEGIN DR'\n'OP TABLE sales.invoice; END';");

        SqlStatement inner = report.Statements.Single(static s => s.Findings.Length > 0);
        inner.Findings.Single().What.ShouldBe("DROP TABLE");
        inner.Location.ShouldBe("statement 1 › body › statement 1");
        report.Statements.Count(static s => !s.IsTopLevel).ShouldBe(2, "BEGIN DROP TABLE … and END: one body, two statements");
    }

    [Fact]
    public void A_replaced_routine_is_named_and_its_new_body_is_still_read()
    {
        // CREATE OR REPLACE is destructive because the displaced body cannot be compared; the
        // replacement body is read all the same, so a drop inside it is a second finding.
        SqlScanReport report = SqlStatementScanner.Scan(
            "CREATE OR REPLACE FUNCTION sales.f() RETURNS void LANGUAGE plpgsql AS $$ BEGIN DROP TABLE sales.invoice; END $$;");

        Destructive(report).ShouldBe(["CREATE OR REPLACE FUNCTION", "DROP TABLE"]);
    }

    [Theory]
    [InlineData("DO $b1$ BEGIN DROP TABLE sales.invoice; END $b1$;", "DROP TABLE")]
    [InlineData("INSERT INTO sales.note (text) VALUES ('$$ DROP TABLE sales.invoice $$');", null)]
    [InlineData("SELECT \"a;b\" FROM sales.invoice;", null)]
    [InlineData("SELECT 1 -- ; DROP TABLE sales.invoice\n;", null)]
    [InlineData("SELECT 1 /* ; DROP TABLE sales.invoice */;", null)]
    [InlineData("UPDATE sales.invoice SET total = $1 WHERE id = $2;", null)]
    [InlineData("INSERT INTO sales.note (text) VALUES (U&'d!0061t!+000061' UESCAPE '!');", null)]
    [InlineData("DO $ab$ BEGIN RAISE NOTICE $a$ hi $a$; DROP TABLE sales.invoice; END $ab$;", "DROP TABLE")]
    [InlineData("SELECT a$b FROM sales.invoice;", null)]
    public void The_first_reviews_nine_lexical_attacks_still_hold(string sql, string? destructive)
    {
        // Re-run as standing cases after every change to the tokenizer or the decoder (CLAUDE.md's
        // eighth form): a tag with a digit, $$ as data inside a literal, ; inside a quoted
        // identifier, ; inside both comment forms, positional parameters, a UESCAPE literal, an
        // inner tag that is a prefix of the outer, and $ inside an identifier.
        SqlScanReport report = SqlStatementScanner.Scan(sql);

        report.TopLevelStatements.ShouldBe(1, report.Statements.ToString());
        Unscannable(report).ShouldBeEmpty();
        Destructive(report).ShouldBe(destructive is null ? [] : [destructive]);
    }

    [Fact]
    public void A_quoted_routine_body_nested_in_a_block_is_read_exactly_as_its_dollar_quoted_twin()
    {
        // The third review's N-1: inside a DO block the statement that creates the routine begins
        // with IF, so a body position decided from the statement's first token was never seen.
        const string quoted =
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_proc WHERE proname = 'f') THEN "
            + "CREATE FUNCTION catalog.refuse_append_only_change() RETURNS trigger LANGUAGE plpgsql AS "
            + "'BEGIN DROP TABLE catalog.operator_audit_event; RETURN NEW; END'; END IF; END $$;";
        const string dollar =
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_proc WHERE proname = 'f') THEN "
            + "CREATE FUNCTION catalog.refuse_append_only_change() RETURNS trigger LANGUAGE plpgsql AS "
            + "$fn$ BEGIN DROP TABLE catalog.operator_audit_event; RETURN NEW; END $fn$; END IF; END $$;";

        foreach (string sql in new[] { quoted, dollar })
        {
            SqlScanReport report = SqlStatementScanner.Scan(sql);

            SqlStatement inner = report.Statements.Single(static s => s.Findings.Length > 0);
            inner.Findings.Single().What.ShouldBe("DROP TABLE");
            inner.Location.ShouldBe("statement 1 › body › statement 1 › body › statement 1");
            report.BodiesRead.ShouldBe(2, "the DO body and the routine body, whichever way the routine body is quoted");
            report.Statements.Length.ShouldBe(7, "one top-level statement, three in the DO body, three in the routine body");
        }
    }

    [Fact]
    public void Reports_how_many_bodies_it_read()
    {
        SqlStatementScanner.Scan("SELECT 1;").BodiesRead.ShouldBe(0);
        SqlStatementScanner.Scan("DO $$ BEGIN NULL; END $$; DO 'BEGIN NULL; END';").BodiesRead.ShouldBe(2);
        SqlStatementScanner.Scan("DO U&'BEGIN NULL; END';").BodiesRead.ShouldBe(0, "a body it could not decode was not read, and is reported as such");
    }

    [Fact]
    public void Collects_the_triggers_a_statement_creates_and_the_ones_it_enables_ALWAYS()
    {
        SqlScanReport report = SqlStatementScanner.Scan(
            "CREATE TRIGGER trg_guard BEFORE DELETE ON sales.invoice FOR EACH STATEMENT EXECUTE FUNCTION sales.f(); "
            + "CREATE CONSTRAINT TRIGGER \"Trg_Two\" AFTER INSERT ON \"Sales\".\"Invoice\" FOR EACH ROW EXECUTE FUNCTION sales.f(); "
            + "ALTER TABLE sales.invoice ENABLE ALWAYS TRIGGER trg_guard; ALTER TABLE ONLY sales.other ENABLE ALWAYS TRIGGER ALL; "
            + "CREATE EVENT TRIGGER evt ON ddl_command_start EXECUTE FUNCTION sales.f();");

        report.TriggersCreated.Select(static t => (t.Table, t.Name)).ShouldBe([("sales.invoice", "trg_guard"), ("Sales.Invoice", "Trg_Two")]);
        report.TriggersEnabledAlways.Select(static t => (t.Table, t.Name)).ShouldBe([("sales.invoice", "trg_guard"), ("sales.other", "all")]);
    }

    /// <summary>
    /// One probe per suppression row of <c>docs/architecture/postgres-invariant-suppression.md</c>:
    /// the statement that writes the weakening value. A row is implemented when every probe of it
    /// is named destructive; the implemented set must equal <see cref="SqlStatementScanner.CoveredSuppressionRows"/>,
    /// so coverage is measured rather than asserted, and a row covered by accident or uncovered by
    /// regression is found here and not in a fourth review.
    /// </summary>
    private static readonly (string Row, string Sql)[] SuppressionProbes =
    [
        ("S1", "ALTER TABLE sales.t DISABLE TRIGGER g;"),
        ("S1", "ALTER TABLE sales.t ENABLE REPLICA TRIGGER g;"),
        ("S1", "ALTER TABLE sales.t ENABLE TRIGGER g;"),
        ("S2", "ALTER TABLE sales.t DISABLE RULE r;"),
        ("S2", "ALTER TABLE sales.t ENABLE REPLICA RULE r;"),
        ("S2", "ALTER TABLE sales.t ENABLE RULE r;"),
        ("S3", "SET session_replication_role = 'replica';"),
        ("S3", "SET LOCAL session_replication_role = 'replica';"),
        ("S3", "ALTER ROLE aurora_app SET session_replication_role = 'replica';"),
        ("S3", "ALTER DATABASE aurora SET session_replication_role = 'replica';"),
        ("S3", "SELECT set_config('session_replication_role', 'replica', false);"),
        ("S4", "CREATE OR REPLACE FUNCTION sales.guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$;"),
        ("S4", "CREATE OR REPLACE PROCEDURE sales.p() LANGUAGE sql AS $$ SELECT 1 $$;"),
        ("S5", "SET search_path = evil, sales;"),
        ("S6", "ALTER TABLE sales.t DISABLE ROW LEVEL SECURITY;"),
        ("S6", "ALTER TABLE sales.t NO FORCE ROW LEVEL SECURITY;"),
        ("S7", "ALTER TABLE sales.t ADD CONSTRAINT ck CHECK (total > 0) NOT VALID;"),
        ("S8", "SET CONSTRAINTS ALL DEFERRED;"),
        ("S8", "ALTER TABLE sales.t ALTER CONSTRAINT fk DEFERRABLE INITIALLY DEFERRED;"),
        ("S10", "ALTER EVENT TRIGGER evt DISABLE;"),
        ("S10", "ALTER EVENT TRIGGER evt ENABLE REPLICA;"),
        ("S10", "ALTER EVENT TRIGGER evt ENABLE;"),
        ("S11", "GRANT DELETE ON sales.t TO aurora_app;"),
        ("S11", "ALTER TABLE sales.t OWNER TO aurora_app;"),
        ("S11", "ALTER ROLE aurora_app BYPASSRLS;"),
        ("S12", "DROP TRIGGER g ON sales.t;"),
        ("S12", "CREATE OR REPLACE TRIGGER g BEFORE DELETE ON sales.t FOR EACH ROW WHEN (current_setting('x') = 'y') EXECUTE FUNCTION sales.guard();"),
    ];

    [Fact]
    public void The_suppression_rows_the_scanner_declares_it_covers_are_exactly_the_ones_it_implements()
    {
        // S9 (an invalid index after a failed concurrent build) has no statement to probe: it is
        // limb B by accident, and MIG4 (B-09.2) is where it is addressed.
        var implemented = SuppressionProbes
            .GroupBy(static probe => probe.Row, StringComparer.Ordinal)
            .Where(static group => group.All(probe => Destructive(SqlStatementScanner.Scan(probe.Sql)).Length > 0))
            .Select(static group => group.Key)
            .ToImmutableHashSet(StringComparer.Ordinal);

        implemented.ShouldBe(
            SqlStatementScanner.CoveredSuppressionRows.ToImmutableHashSet(StringComparer.Ordinal),
            "the rows the scanner declares it covers must be exactly the rows whose every probe it names destructive; "
            + $"implemented: {string.Join(", ", implemented.Order(StringComparer.Ordinal))}");

        SuppressionProbes
            .Where(static probe => !SqlStatementScanner.CoveredSuppressionRows.Contains(probe.Row))
            .Where(static probe => Destructive(SqlStatementScanner.Scan(probe.Sql)).Length > 0)
            .ShouldBeEmpty("a probe of an undeclared row is named destructive: declare the row, or the coverage is undocumented");
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
    [InlineData("INSERT INTO sales.note (text) VALUES ('BEGIN DROP TABLE sales.invoice; END');")]
    [InlineData("COMMENT ON TABLE sales.invoice IS 'BEGIN DROP TABLE sales.invoice; END';")]
    [InlineData("CREATE FUNCTION sales.f() RETURNS void LANGUAGE 'sql' AS $$ SELECT 1 $$;")]
    [InlineData("ALTER TABLE sales.invoice ENABLE ALWAYS TRIGGER trg_append_only, ENABLE ALWAYS RULE r_guard;")]
    [InlineData("ALTER EVENT TRIGGER evt_guard ENABLE ALWAYS;")]
    [InlineData("ALTER TABLE sales.invoice ENABLE ROW LEVEL SECURITY;")]
    [InlineData("INSERT INTO sales.note (text) VALUES ('a' || 'DROP TABLE sales.invoice');")]
    [InlineData("INSERT INTO sales.note (text) VALUES ('BEGIN '\n'DROP TABLE sales.invoice; END');")]
    [InlineData("SET search_path = sales, public;")]
    [InlineData("CREATE FUNCTION sales.guard_replacement() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$;")]
    [InlineData("ALTER TABLE sales.invoice ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;")]
    [InlineData("ALTER TABLE sales.invoice ATTACH PARTITION sales.invoice_2025 FOR VALUES FROM ('2025-01-01') TO ('2026-01-01');")]
    [InlineData("SELECT N'not a prefix in PostgreSQL';")]
    [InlineData("INSERT INTO sales.note VALUES ('it''s not a DROP TABLE');")]
    [InlineData("INSERT INTO sales.note VALUES (E'a backslash-escaped \\' quote, then DROP TABLE x');")]
    [InlineData("INSERT INTO sales.note VALUES (U&'unicode DROP TABLE');")]
    [InlineData("-- DROP TABLE sales.invoice\nSELECT 1;")]
    [InlineData("/* DROP TABLE /* nested */ sales.invoice */ SELECT 1;")]
    [InlineData("SELECT \"DROP TABLE\" FROM sales.invoice;")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN region DROP NOT NULL, ALTER COLUMN total DROP DEFAULT;")]
    [InlineData("ALTER TABLE sales.invoice ALTER COLUMN id DROP IDENTITY IF EXISTS, ALTER COLUMN x DROP EXPRESSION;")]
    [InlineData("CREATE TABLE sales.invoice (id int NOT NULL, note text NOT NULL);")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN region text NOT NULL DEFAULT 'none';")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN seq integer GENERATED ALWAYS AS IDENTITY NOT NULL;")]
    [InlineData("ALTER TABLE sales.invoice ADD COLUMN region text CHECK (region IS NOT NULL);")]
    [InlineData("ALTER TABLE sales.invoice ADD CONSTRAINT ck CHECK (total > 0) NOT VALID;")]
    [InlineData("ALTER TABLE sales.invoice ADD CONSTRAINT fk FOREIGN KEY (party_id) REFERENCES parties.party (id) ON DELETE RESTRICT;")]
    [InlineData("CREATE TRIGGER guard BEFORE UPDATE OR DELETE OR TRUNCATE ON sales.invoice FOR EACH STATEMENT EXECUTE FUNCTION sales.guard();")]
    [InlineData("ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;")]
    [InlineData("GRANT EXECUTE ON FUNCTION sales.guard() TO aurora_app;")]
    [InlineData("REVOKE EXECUTE, USAGE ON ALL FUNCTIONS IN SCHEMA sales FROM PUBLIC;")]
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
    [InlineData("DO 'BEGIN EXECUTE ''DROP TABLE sales.invoice''; END';", "EXECUTE")]
    [InlineData("DO $$ BEGIN CREATE FUNCTION sales.f() RETURNS void LANGUAGE plpgsql AS 'BEGIN EXECUTE ''DROP TABLE x''; END'; END $$;", "EXECUTE")]
    [InlineData("DO $$ BEGIN CREATE FUNCTION sales.f() RETURNS void LANGUAGE sql AS U&'DROP TABLE sales.invoice'; END $$;", "does not decode")]
    [InlineData("DO $$ BEGIN CREATE FUNCTION sales.f() RETURNS void LANGUAGE sql AS 'SELECT sales.rebuild_everything()'; END $$;", "calls sales.rebuild_everything(…)")]
    [InlineData("CREATE FUNCTION sales.purge() RETURNS void LANGUAGE plpgsql AS 'BEGIN EXECUTE ''DROP TABLE x''; END';", "EXECUTE")]
    [InlineData("DO U&'BEGIN DROP TABLE sales.invoice; END';", "does not decode")]
    [InlineData("DO E'BEGIN DROP TABLE sales.invoice; \\x41 END';", "does not decode")]
    [InlineData("DO 'BEGIN '\nU&'DROP TABLE sales.invoice; END';", "does not decode")]
    [InlineData("SELECT \\ 1;", "unexpected character")]
    [InlineData("DO 'BEGIN SELECT ''never closed; END';", "never closed")]
    [InlineData("INSERT INTO sales.a SELECT b.x FROM sales.b JOIN sales.c ON sales.rebuild(b.id) = c.id;", "calls sales.rebuild(…)")]
    [InlineData("CREATE VIEW sales.v AS SELECT b.x FROM sales.b JOIN sales.c ON sales.rebuild(b.id) = c.id;", "calls sales.rebuild(…)")]
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
    public void N_is_a_name_and_not_a_literal_prefix()
    {
        // n-4 of the first review: E, B, X and U& are the prefixes PostgreSQL defines. N'x' is the
        // identifier n followed by a literal, and a scanner that swallowed the n would have lost a
        // name token that a later rule may read.
        SqlTokenizer.Tokenize("SELECT N'x'").Select(static token => token.Kind)
            .ShouldBe([SqlTokenKind.Word, SqlTokenKind.Word, SqlTokenKind.Literal]);
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
            "update sales.invoice set total = 0; INSERT INTO sales.note VALUES (1); DO $$ BEGIN NULL; END $$; DO 'BEGIN NULL; END';");

        report.Statements.Where(static s => s.IsTopLevel).Select(static s => s.Head).ShouldBe(["UPDATE", "INSERT", "DO", "DO"]);
        report.Statements.Where(static s => s.IsTopLevel).Select(static s => s.HasProceduralBody).ShouldBe([false, false, true, true]);
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
