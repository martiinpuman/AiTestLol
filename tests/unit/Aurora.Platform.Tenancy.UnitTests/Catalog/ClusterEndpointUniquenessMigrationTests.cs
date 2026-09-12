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
/// it with no database: ADR-0034 §3.1's one statement, inside a transaction, and nothing
/// destructive. The gate's stage 6 runs this with Docker stopped. The migration's behaviour
/// against real rows — the loud failure over duplicates, the property it serves — is the
/// integration project's (<c>ClusterEndpointUniquenessMigrationTests</c>,
/// <c>CatalogRoutingUniquenessTests</c>), and nothing here stands in for it.
/// </summary>
public sealed partial class ClusterEndpointUniquenessMigrationTests
{
    private const string Adr0034Section31 = "CREATE UNIQUE INDEX ux_database_cluster_host_port ON catalog.database_cluster (host, port);";

    private readonly ITestOutputHelper _output;

    public ClusterEndpointUniquenessMigrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_migration_emits_ADR_0034_3_1s_statement_inside_a_transaction_and_nothing_else_that_changes_the_schema()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        List<string> ids = context.Database.GetMigrations().ToList();
        int position = ids.FindIndex(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0, "the migration must exist and have a predecessor to be scripted from");

        string script = context.GetService<IMigrator>().GenerateScript(fromMigration: ids[position - 1], toMigration: ids[position]);
        _output.WriteLine(script);

        // One schema statement, and it is the ADR's. Counted, so a second CREATE, an ALTER or a
        // DROP smuggled into the same migration is a different number rather than a missed line.
        int schemaStatements = SchemaStatement().Count(script);
        _output.WriteLine($"schema statements: {schemaStatements}");
        script.ShouldContain(Adr0034Section31);
        schemaStatements.ShouldBe(1, "an expand-only migration that creates one index emits one schema statement");
        script.ShouldNotContain("CONCURRENTLY", customMessage: "transactional on purpose: a failed CONCURRENTLY leaves an INVALID index behind, a failed transaction leaves nothing");

        // Transactional, so that against duplicates neither the index nor the history row survives.
        script.ShouldContain("START TRANSACTION;");
        script.ShouldContain("COMMIT;");
    }

    /// <summary>Any DDL or DML verb at the start of a statement, the insert into the migrations history excepted however the provider quotes it.</summary>
    [GeneratedRegex(@"^(CREATE|ALTER|DROP|DELETE|UPDATE|TRUNCATE|INSERT INTO (?!\S*__EFMigrationsHistory))", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SchemaStatement();
}
