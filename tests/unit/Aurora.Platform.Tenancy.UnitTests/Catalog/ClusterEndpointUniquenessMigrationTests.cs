using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// What the <c>ClusterEndpointUniqueness</c> migration emits, read from the SQL EF generates for
/// it with no database: ADR-0034 §3.1's statement and the lower-case host check beside it, inside
/// one transaction, and nothing destructive. The gate's stage 6 runs this with Docker stopped. The migration's behaviour
/// against real rows — the loud failure over duplicates, the property it serves — is the
/// integration project's (<c>ClusterEndpointUniquenessMigrationTests</c>,
/// <c>CatalogRoutingUniquenessTests</c>), and nothing here stands in for it.
/// </summary>
public sealed partial class ClusterEndpointUniquenessMigrationTests
{
    private const string Adr0034Section31 = "CREATE UNIQUE INDEX ux_database_cluster_host_port ON catalog.database_cluster (host, port);";
    private const string LowerCaseHost = "ALTER TABLE catalog.database_cluster ADD CONSTRAINT ck_database_cluster_host_lower_case CHECK (host = lower(host));";

    private readonly ITestOutputHelper _output;

    public ClusterEndpointUniquenessMigrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_migration_emits_ADR_0034_3_1s_statement_and_the_lower_case_host_check_inside_one_transaction_and_nothing_destructive()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        List<string> ids = context.Database.GetMigrations().ToList();
        int position = ids.FindIndex(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0, "the migration must exist and have a predecessor to be scripted from");

        string script = context.GetService<IMigrator>().GenerateScript(fromMigration: ids[position - 1], toMigration: ids[position]);
        _output.WriteLine(script);

        // Two schema statements, the ADR's index and the check, counted - so a third statement
        // smuggled into the same migration is a different number rather than a missed line - and
        // nothing that removes or rewrites what exists.
        int schemaStatements = SchemaStatement().Count(script);
        int destructive = DestructiveStatement().Count(script);
        _output.WriteLine($"schema statements: {schemaStatements}; destructive: {destructive}");
        script.ShouldContain(Adr0034Section31);
        script.ShouldContain(LowerCaseHost);
        schemaStatements.ShouldBe(2, "an expand-only migration that adds one index and one check emits two schema statements");
        destructive.ShouldBe(0);
        script.ShouldNotContain("CONCURRENTLY", customMessage: "transactional on purpose: a failed CONCURRENTLY leaves an INVALID index behind, a failed transaction leaves nothing");

        // Transactional, so that against duplicates neither the index nor the history row survives.
        script.ShouldContain("START TRANSACTION;");
        script.ShouldContain("COMMIT;");
    }

    /// <summary>A statement that removes or rewrites what exists: the contract half of expand/contract (ADR-0007 §7.2).</summary>
    [GeneratedRegex(@"^(DROP|DELETE|UPDATE|TRUNCATE|ALTER TABLE \S+ (DROP|ALTER|RENAME))", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DestructiveStatement();

    /// <summary>Any DDL or DML verb at the start of a statement, the insert into the migrations history excepted however the provider quotes it.</summary>
    [GeneratedRegex(@"^(CREATE|ALTER|DROP|DELETE|UPDATE|TRUNCATE|INSERT INTO (?!\S*__EFMigrationsHistory))", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SchemaStatement();
}
