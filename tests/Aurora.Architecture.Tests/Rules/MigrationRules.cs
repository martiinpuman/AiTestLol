using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.MigrationSafety;
using Aurora.Platform.Tenancy.Contracts;

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
/// Two migrations with one id are both violations, because the release gate (MIG3) links
/// migrations by id.
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

/// <summary>What one run of MIG2 read, so a test can hold the count to a floor, not only the verdict.</summary>
internal sealed record MigrationScan(
    int Migrations,
    int StatementsParsed,
    int DestructiveStatementsPermittedInContracts,
    ImmutableArray<RuleViolation> Violations);

/// <summary>
/// Fitness rule <b>MIG2</b> - a destructive statement appears only in a <c>Contract</c>
/// migration; a <c>DataOnly</c> migration is plain data statements; nothing a migration runs is
/// something the scanner could not read (ADR-0007 §7.2 rule 2, <c>testing-strategy.md</c> §5.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>What the mechanism inspects:</b> the SQL Npgsql's own generator emits from each migration's
/// <c>UpOperations</c> (<see cref="MigrationPopulation"/>), every command, every statement, top
/// level and inside every dollar-quoted body, through <see cref="SqlStatementScanner"/>. A
/// destructive finding is a violation unless the migration is a Contract. An unscannable finding
/// is a violation whatever the category - a migration the scanner cannot read is not clean, it
/// is unread. A migration whose SQL could not be generated at all is a violation for the same
/// reason. A DataOnly migration is additionally held to top-level statements that begin with one
/// of <see cref="DataOnlyHeads"/> and carry no procedural body.
/// </para>
/// <para>
/// <b>What it treats an unannotated migration as:</b> the strictest category. MIG1 reports the
/// missing annotation; this rule does not let its absence exempt anything.
/// </para>
/// <para>
/// <b>What it cannot see:</b> what the scanner cannot - stated on <see cref="SqlStatementScanner"/>.
/// And it does not yet judge index concurrency (MIG4) or the release the migration ships in
/// (MIG3); those are separate rules.
/// </para>
/// </remarks>
internal static class MigrationSafetyRule
{
    public const string Id = "MIG2";

    public const string Name =
        "Destructive SQL only in a Contract; a DataOnly migration is plain data statements; nothing the scanner cannot read";

    /// <summary>The statement heads a DataOnly migration may run at the top level of its script.</summary>
    public static readonly ImmutableHashSet<string> DataOnlyHeads =
        ["INSERT", "UPDATE", "MERGE", "WITH", "SELECT", "SET", "RESET"];

    public static RuleOutcome Check(ImmutableArray<ScannedMigration> migrations)
    {
        MigrationScan scan = Scan(migrations);

        return RuleOutcome.From(
            Id,
            Name,
            FormattableString.Invariant($"migrations ({scan.StatementsParsed} SQL statements parsed)"),
            scan.Migrations,
            scan.Violations);
    }

    public static MigrationScan Scan(ImmutableArray<ScannedMigration> migrations)
    {
        int statements = 0;
        int permitted = 0;
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

            for (int c = 0; c < migration.Commands.Length; c++)
            {
                SqlScanReport report = SqlStatementScanner.Scan(migration.Commands[c].CommandText);
                statements += report.Statements.Length;

                foreach (SqlStatement statement in report.Statements)
                {
                    string where = FormattableString.Invariant($"command {c + 1}, {statement.Location}");

                    foreach (SqlFinding finding in statement.Findings)
                    {
                        if (finding.Kind == SqlFindingKind.Unscannable)
                        {
                            violations.Add(Violation(migration, $"{where}: «{statement.Excerpt}» {finding.What}; a migration the scanner cannot read is not clean, it is unread"));
                        }
                        else if (migration.Category == MigrationCategory.Contract)
                        {
                            permitted++;
                        }
                        else
                        {
                            violations.Add(Violation(
                                migration,
                                $"{where}: {finding.What} in «{statement.Excerpt}» is allowed only in a Contract migration, "
                                + $"and this one is {migration.CategoryDisplay} (ADR-0007 §7.2)"));
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
        }

        return new MigrationScan(migrations.Length, statements, permitted, [.. violations]);
    }

    private static RuleViolation Violation(ScannedMigration migration, string detail) =>
        new(migration.TypeFullName, ViolationSite.Statement, $"[{migration.Id}] {detail}");
}
