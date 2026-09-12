using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Aurora.Architecture.Tests.MigrationSafety;
using Aurora.Platform.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>MIG1</b> - every migration declares <c>Expand</c>, <c>Contract</c> or
/// <c>DataOnly</c> with a reason, and only a Contract names the Expand it contracts (ADR-0007
/// §7.2 rule 1, <c>testing-strategy.md</c> §5.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>What the mechanism inspects:</b> the <c>[MigrationSafety]</c> attribute on every
/// non-abstract type in production whose base chain reaches EF Core's <c>Migration</c>, read
/// back by reflection as the real attribute type. A missing attribute, a blank reason, a
/// Contract that names nothing, a Contract that names a migration not in the population or one
/// that is not an Expand, and an Expand or DataOnly that names anything are each a violation.
/// Two migrations with one id produce one violation, on the migration that lost the tie for the
/// id - the release gate (MIG3, B-09.3, not yet built) will link migrations by id, and the
/// survivor is the one it would link to, so the loser is what has to change.
/// </para>
/// <para>
/// <b>What it cannot see:</b> whether the reason is true. That is what MIG2 holds the generated
/// SQL to.
/// </para>
/// </remarks>
internal static class MigrationAnnotationRule
{
    public const string Id = "MIG1";

    public const string Name =
        "Every migration declares Expand, Contract or DataOnly with a reason; only a Contract names an Expand, and it exists";

    public static RuleOutcome Check(ImmutableArray<ScannedMigration> migrations)
    {
        ImmutableDictionary<string, ScannedMigration> byId = migrations
            .GroupBy(static migration => migration.Id, StringComparer.Ordinal)
            .ToImmutableDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        return RuleOutcome.From(Id, Name, "migrations", migrations.Length, migrations.SelectMany(migration => Judge(migration, byId)));
    }

    private static IEnumerable<RuleViolation> Judge(ScannedMigration migration, ImmutableDictionary<string, ScannedMigration> byId)
    {
        if (!ReferenceEquals(byId[migration.Id], migration))
        {
            yield return Violation(migration, $"shares the migration id {migration.Id} with {byId[migration.Id].TypeFullName}; ids must be unique");
        }

        if (migration.Safety is null)
        {
            yield return Violation(migration, "carries no [MigrationSafety]; every migration declares Expand, Contract or DataOnly and why (ADR-0007 §7.2)");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(migration.Safety.Reason))
        {
            yield return Violation(migration, $"is {migration.CategoryDisplay} with a blank reason; say why, in words a reviewer can check against the SQL");
        }

        string? contracts = migration.Safety.Contracts;

        if (migration.Category != MigrationCategory.Contract)
        {
            if (contracts is not null)
            {
                yield return Violation(migration, $"is {migration.CategoryDisplay} but names Contracts = \"{contracts}\"; only a Contract names the Expand it contracts");
            }

            yield break;
        }

        if (string.IsNullOrWhiteSpace(contracts))
        {
            yield return Violation(migration, "is a Contract that names no Expand; set Contracts to the migration id of the Expand it contracts, so the release gate can keep them a release apart");
        }
        else if (!byId.TryGetValue(contracts, out ScannedMigration? expand))
        {
            yield return Violation(migration, $"contracts \"{contracts}\", which is not a migration in this population");
        }
        else if (expand.Category != MigrationCategory.Expand)
        {
            yield return Violation(migration, $"contracts \"{contracts}\", which is {expand.CategoryDisplay}, not an Expand");
        }
    }

    private static RuleViolation Violation(ScannedMigration migration, string detail) =>
        new(migration.TypeFullName, ViolationSite.Annotation, detail);
}

/// <summary>
/// What one run of MIG2 read and decided, so a test can hold the counts to floors, not only the
/// verdict: a scan that reports success must be able to say how much it looked at.
/// </summary>
/// <param name="Migrations">Migrations examined.</param>
/// <param name="StatementsParsed">Statements read, top level and inside bodies.</param>
/// <param name="BodiesRead">Procedural bodies decoded and read again as code.</param>
/// <param name="DestructiveStatementsPermittedInContracts">Destructive findings a Contract was allowed.</param>
/// <param name="DropConstraintsExamined">Bare <c>DROP CONSTRAINT</c> findings judged under ADR-0037 §3.2.</param>
/// <param name="DropConstraintsAttributedToCheck">Of those, attributed to a <c>DropCheckConstraintOperation</c> corroborated by the source model, and cleared.</param>
/// <param name="TypeChangesExamined"><c>ALTER COLUMN … TYPE</c> findings judged under ADR-0037 §4.</param>
/// <param name="TypeChangesWidening">Of those, attributed to an <c>AlterColumnOperation</c> on the two-shape allowlist, and cleared.</param>
/// <param name="TriggersCreated">Triggers created, each of which must be followed by <c>ENABLE ALWAYS</c> (ADR-0037 §2.6).</param>
/// <param name="Violations">What was reported.</param>
internal sealed record MigrationScan(
    int Migrations,
    int StatementsParsed,
    int BodiesRead,
    int DestructiveStatementsPermittedInContracts,
    int DropConstraintsExamined,
    int DropConstraintsAttributedToCheck,
    int TypeChangesExamined,
    int TypeChangesWidening,
    int TriggersCreated,
    ImmutableArray<RuleViolation> Violations);

/// <summary>
/// Fitness rule <b>MIG2</b> - a destructive statement appears only in a <c>Contract</c>
/// migration; a <c>DataOnly</c> migration is plain data statements; a trigger a migration creates
/// is enabled <c>ALWAYS</c>; nothing a migration runs is something the scanner could not read
/// (ADR-0007 §7.2 rule 2 as amended by ADR-0037 §2–§4, <c>testing-strategy.md</c> §5.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>What the mechanism inspects:</b> the SQL Npgsql's own generator emits from each migration's
/// <c>UpOperations</c> (<see cref="MigrationPopulation"/>), every command, every statement, top
/// level and inside every body, through <see cref="SqlStatementScanner"/>. A destructive finding
/// is a violation unless the migration is a Contract - or unless the command that carries it is
/// attributed to an EF operation that proves it a widening: a bare <c>DROP CONSTRAINT</c> whose
/// command a <c>DropCheckConstraintOperation</c> produced, naming the same schema, table and
/// constraint, where that constraint is a check constraint in the migration's source model
/// (ADR-0037 §3.2); or an <c>ALTER COLUMN … TYPE</c> whose command an <c>AlterColumnOperation</c>
/// produced, from <c>varchar(n)</c> to a wider <c>varchar(m)</c> or from <c>varchar</c> to
/// <c>text</c>, standing alone with no <c>USING</c> and no <c>COLLATE</c> (ADR-0037 §4). The
/// constraint's <i>name</i> is never evidence; the operation's CLR type and EF's snapshot are.
/// An unscannable finding is a violation whatever the category. A migration whose SQL could not be
/// generated is a violation for the same reason. A DataOnly migration is additionally held to
/// top-level statements that begin with one of <see cref="DataOnlyHeads"/> and carry no
/// procedural body. And every trigger a migration creates must be followed, in the same migration,
/// by <c>ALTER TABLE … ENABLE ALWAYS TRIGGER</c> for that trigger (or <c>ALL</c>) on that table:
/// <c>CREATE TRIGGER</c> produces <c>tgenabled = 'O'</c>, which <c>session_replication_role</c>
/// suppresses, so a guard created without it is born weak and no suppression statement betrays it
/// (ADR-0037 §2.6, <c>postgres-invariant-suppression.md</c> S1).
/// </para>
/// <para>
/// <b>What it treats an unannotated migration as:</b> the strictest category. MIG1 reports the
/// missing annotation; this rule does not let its absence exempt anything.
/// </para>
/// <para>
/// <b>What it cannot see:</b> what the scanner cannot - stated on <see cref="SqlStatementScanner"/>.
/// Whether a cleared <c>CHECK</c> replacement actually widens is a human's call at Full tier
/// (ADR-0037 §3.3); this rule proves only the kind. The source model is EF's snapshot, not the
/// database. And it does not yet judge index concurrency (MIG4, B-09.2) or the release the
/// migration ships in (MIG3, B-09.3).
/// </para>
/// </remarks>
internal static partial class MigrationSafetyRule
{
    public const string Id = "MIG2";

    public const string Name =
        "Destructive SQL only in a Contract; a DataOnly migration is plain data statements; a created trigger is enabled ALWAYS; nothing the scanner cannot read";

    /// <summary>The statement heads a DataOnly migration may run at the top level of its script.</summary>
    public static readonly ImmutableHashSet<string> DataOnlyHeads =
        ["INSERT", "UPDATE", "MERGE", "WITH", "SELECT", "SET", "RESET"];

    public static RuleOutcome Check(ImmutableArray<ScannedMigration> migrations)
    {
        MigrationScan scan = Scan(migrations);

        return RuleOutcome.From(
            Id,
            Name,
            FormattableString.Invariant($"migrations ({scan.StatementsParsed} SQL statements parsed, {scan.BodiesRead} bodies read, {scan.DropConstraintsAttributedToCheck}/{scan.DropConstraintsExamined} DROP CONSTRAINT attributed, {scan.TypeChangesWidening}/{scan.TypeChangesExamined} type changes widening, {scan.TriggersCreated} triggers created)"),
            scan.Migrations,
            scan.Violations);
    }

    public static MigrationScan Scan(ImmutableArray<ScannedMigration> migrations)
    {
        var totals = new Totals();
        List<RuleViolation> violations = [];

        foreach (ScannedMigration migration in migrations)
        {
            if (migration.GenerationFailure is not null)
            {
                violations.Add(Violation(
                    migration,
                    $"its Up() SQL could not be generated, so nothing in it was scanned: {migration.GenerationFailure}"));
                continue;
            }

            List<TriggerReference> created = [];
            List<TriggerReference> enabledAlways = [];

            for (int c = 0; c < migration.Commands.Length; c++)
            {
                GeneratedCommand command = migration.Commands[c];
                SqlScanReport report = SqlStatementScanner.Scan(command.CommandText);
                totals.Statements += report.Statements.Length;
                totals.Bodies += report.BodiesRead;
                created.AddRange(report.TriggersCreated);
                enabledAlways.AddRange(report.TriggersEnabledAlways);

                foreach (SqlStatement statement in report.Statements)
                {
                    string where = FormattableString.Invariant($"command {c + 1}, {statement.Location}");

                    foreach (SqlFinding finding in statement.Findings)
                    {
                        RuleViolation? violation = Judge(migration, command, statement, finding, where, totals);
                        if (violation is not null)
                        {
                            violations.Add(violation);
                        }
                    }

                    if (migration.Category == MigrationCategory.DataOnly && statement.IsTopLevel
                        && (!DataOnlyHeads.Contains(statement.Head) || statement.HasProceduralBody))
                    {
                        violations.Add(Violation(
                            migration,
                            $"{where}: «{statement.Excerpt}» is not a plain data statement; a DataOnly migration runs only "
                            + $"{string.Join(", ", DataOnlyHeads.Order(StringComparer.Ordinal))} at the top level and no procedural body"));
                    }
                }
            }

            totals.TriggersCreated += created.Count;
            violations.AddRange(BornWeak(migration, created, enabledAlways));
        }

        return new MigrationScan(
            migrations.Length,
            totals.Statements,
            totals.Bodies,
            totals.Permitted,
            totals.DropConstraintsExamined,
            totals.DropConstraintsAttributed,
            totals.TypeChangesExamined,
            totals.TypeChangesWidening,
            totals.TriggersCreated,
            [.. violations]);
    }

    private static RuleViolation? Judge(
        ScannedMigration migration, GeneratedCommand command, SqlStatement statement, SqlFinding finding, string where, Totals totals)
    {
        if (finding.Kind == SqlFindingKind.Unscannable)
        {
            return Violation(migration, $"{where}: «{statement.Excerpt}» {finding.What}; a migration the scanner cannot read is not clean, it is unread");
        }

        if (migration.Category == MigrationCategory.Contract)
        {
            totals.Permitted++;
            return null;
        }

        if (finding.What == "DROP CONSTRAINT")
        {
            totals.DropConstraintsExamined++;
            if (IsAttributedCheckConstraintDrop(migration, command, finding))
            {
                totals.DropConstraintsAttributed++;
                return null;
            }

            return Violation(
                migration,
                $"{where}: DROP CONSTRAINT in «{statement.Excerpt}» is allowed only in a Contract migration, and this one is "
                + $"{migration.CategoryDisplay}; a CHECK dropped through migrationBuilder.DropCheckConstraint() whose constraint "
                + "the previous migration's model declares is the one exception (ADR-0037 §3.2)");
        }

        if (finding.What == "ALTER COLUMN … TYPE")
        {
            totals.TypeChangesExamined++;
            if (IsAttributedWidening(command, finding))
            {
                totals.TypeChangesWidening++;
                return null;
            }

            return Violation(
                migration,
                $"{where}: ALTER COLUMN … TYPE in «{statement.Excerpt}» is allowed only in a Contract migration, and this one is "
                + $"{migration.CategoryDisplay}; a varchar widened, or a varchar to text, through migrationBuilder.AlterColumn() "
                + "with no USING, no COLLATE and no other action is the one exception (ADR-0037 §4)");
        }

        return Violation(
            migration,
            $"{where}: {finding.What} in «{statement.Excerpt}» is allowed only in a Contract migration, "
            + $"and this one is {migration.CategoryDisplay} (ADR-0007 §7.2, ADR-0037 §2)");
    }

    /// <summary>
    /// ADR-0037 §3.2, link by link: the command was produced by a <see cref="DropCheckConstraintOperation"/>;
    /// its schema, table and name are the ones the statement names; and the source model - EF's
    /// snapshot of the previous migration, never the database - declares that constraint as a
    /// check constraint. Any link missing, the drop is destructive.
    /// </summary>
    private static bool IsAttributedCheckConstraintDrop(ScannedMigration migration, GeneratedCommand command, SqlFinding finding)
    {
        if (command.Operation is not DropCheckConstraintOperation operation || finding.Table is null || finding.Name is null)
        {
            return false;
        }

        string operationTable = (operation.Schema is null ? operation.Table : operation.Schema + "." + operation.Table).ToLowerInvariant();

        return string.Equals(operationTable, finding.Table, StringComparison.Ordinal)
            && string.Equals(operation.Name.ToLowerInvariant(), finding.Name, StringComparison.Ordinal)
            && migration.SourceCheckConstraints.Any(constraint =>
                string.Equals(constraint.TableKey, finding.Table, StringComparison.Ordinal)
                && string.Equals(constraint.Name, operation.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ADR-0037 §4: the command was produced by an <see cref="AlterColumnOperation"/> naming the
    /// column the statement alters, the operation's old and new store types are one of the two
    /// allowed shapes, and the statement stands alone with no <c>USING</c> and no <c>COLLATE</c>.
    /// </summary>
    private static bool IsAttributedWidening(GeneratedCommand command, SqlFinding finding)
    {
        if (command.Operation is not AlterColumnOperation operation || finding.Table is null || finding.Name is null || !finding.StandsAlone)
        {
            return false;
        }

        string operationTable = (operation.Schema is null ? operation.Table : operation.Schema + "." + operation.Table).ToLowerInvariant();

        return string.Equals(operationTable, finding.Table, StringComparison.Ordinal)
            && string.Equals(operation.Name.ToLowerInvariant(), finding.Name, StringComparison.Ordinal)
            && IsWideningTextType(operation.OldColumn.ColumnType, operation.ColumnType);
    }

    /// <summary>
    /// The two shapes ADR-0037 §4 allows: <c>varchar(n)</c> to <c>varchar(m)</c> with <c>m &gt; n</c>,
    /// and <c>varchar[(n)]</c> to <c>text</c>. Everything else - every <c>numeric</c> change, every
    /// narrowing, an unknown old type - is not a widening.
    /// </summary>
    public static bool IsWideningTextType(string? oldType, string? newType)
    {
        (string Base, int? Length)? old = ParseTextType(oldType);
        (string Base, int? Length)? @new = ParseTextType(newType);

        if (old is null || @new is null || old.Value.Base != "varchar")
        {
            return false;
        }

        return @new.Value.Base switch
        {
            "text" => true,
            "varchar" => old.Value.Length is int n && @new.Value.Length is int m && m > n,
            _ => false,
        };
    }

    private static (string Base, int? Length)? ParseTextType(string? storeType)
    {
        if (storeType is null)
        {
            return null;
        }

        Match match = TextType().Match(storeType.Trim());
        if (!match.Success)
        {
            return null;
        }

        string @base = match.Groups["base"].Value.Equals("text", StringComparison.OrdinalIgnoreCase) ? "text" : "varchar";
        int? length = match.Groups["length"].Success ? int.Parse(match.Groups["length"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        return (@base, length);
    }

    [GeneratedRegex(@"^(?<base>text|varchar|character\s+varying)\s*(\(\s*(?<length>\d+)\s*\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextType();

    /// <summary>
    /// ADR-0037 §2.6's positive rule: a trigger a migration creates must be enabled <c>ALWAYS</c>
    /// on the same table in the same migration, by name or by <c>ALL</c>; otherwise it was born at
    /// <c>tgenabled = 'O'</c> and nothing in the destructive set will ever betray it.
    /// </summary>
    private static IEnumerable<RuleViolation> BornWeak(ScannedMigration migration, List<TriggerReference> created, List<TriggerReference> enabledAlways) =>
        created
            .Where(trigger => !enabledAlways.Any(enabled =>
                string.Equals(enabled.Table, trigger.Table, StringComparison.Ordinal)
                && (string.Equals(enabled.Name, trigger.Name, StringComparison.Ordinal) || string.Equals(enabled.Name, "all", StringComparison.Ordinal))))
            .Select(trigger => Violation(
                migration,
                $"{trigger.Location}: trigger {trigger.Name} on {trigger.Table} is created and never enabled ALWAYS in this migration; "
                + "CREATE TRIGGER produces tgenabled = 'O', which session_replication_role = 'replica' suppresses, so the guard is born "
                + $"weak - follow it with ALTER TABLE {trigger.Table} ENABLE ALWAYS TRIGGER {trigger.Name} (ADR-0037 §2.6, S1)"));

    private static RuleViolation Violation(ScannedMigration migration, string detail) =>
        new(migration.TypeFullName, ViolationSite.Statement, $"[{migration.Id}] {detail}");

    private sealed class Totals
    {
        public int Statements { get; set; }

        public int Bodies { get; set; }

        public int Permitted { get; set; }

        public int DropConstraintsExamined { get; set; }

        public int DropConstraintsAttributed { get; set; }

        public int TypeChangesExamined { get; set; }

        public int TypeChangesWidening { get; set; }

        public int TriggersCreated { get; set; }
    }
}
