using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Migrations;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The host rule's two halves held together, and the reason it is a grammar rather than a
/// lower-case rule, executed on <c>postgres:17-alpine</c> rather than asserted: the entity's
/// grammar and <c>ck_database_cluster_host_well_formed</c>'s regular expression agree case by
/// case; <c>lower()</c> folds a non-ASCII upper-case letter under the cluster's <c>en_US.utf8</c>
/// and not under <c>C</c>, so <c>host = lower(host)</c> alone admits <c>pg.Über.internal</c> on a
/// <c>C</c>-collated catalog and refuses it on the fixture's; and the shape check refuses it on
/// both.
/// </summary>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogHostGrammarTests
{
    private const string CheckViolation = "23514";
    private const string LowerCaseConstraint = "ck_database_cluster_host_lower_case";
    private const string WellFormedConstraint = "ck_database_cluster_host_well_formed";

    /// <summary>
    /// The witness for the ASCII rule: only a non-ASCII upper-case letter, so
    /// <c>IsAsciiLetterUpper</c> and the ASCII half of <c>lower()</c> both pass it through and what
    /// remains is the collation. <c>pg.ÜBER.internal</c> is not that witness — <c>B</c>, <c>E</c>
    /// and <c>R</c> are ASCII upper case and fold under every collation (PR #18, second review).
    /// </summary>
    private const string NonAsciiUpper = "pg.Über.internal";
    private const string AsciiAndNonAsciiUpper = "pg.ÜBER.internal";

    /// <summary>One list of cases, each with what the grammar says, evaluated by both halves.</summary>
    private static readonly IReadOnlyList<(string Host, bool WellFormed)> Cases =
    [
        ("pg-1.internal", true),
        ("10.0.0.5", true),
        ("localhost", true),
        ("xn--pg-bfa.internal", true),
        ("a", true),
        (new string('a', 63) + ".internal", true),
        (string.Join('.', Enumerable.Repeat(new string('b', 61), 4)) + ".c", true),
        ("pg-1.internal,pg-2.internal", false),
        ("/var/run/postgresql", false),
        ("::1", false),
        ("[::1]", false),
        ("pg-1.internal:5432", false),
        ("postgres://pg-1.internal", false),
        ("PG-1.internal", false),
        ("pg.über.internal", false),
        (NonAsciiUpper, false),
        (AsciiAndNonAsciiUpper, false),
        (".pg-1.internal", false),
        ("pg-1.internal.", false),
        ("pg-1..internal", false),
        ("-pg.internal", false),
        ("pg-.internal", false),
        ("pg 1.internal", false),
        (new string('a', 64) + ".internal", false),
        ("", false),
    ];

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogHostGrammarTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task The_entitys_grammar_and_the_check_constraints_grammar_agree_on_every_case()
    {
        // The same expression the constraint evaluates, over a parameter instead of the column.
        string sql = "SELECT " + CanonicalHost.CheckConstraintSql.Replace("host ~ ", "@host ~ ", StringComparison.Ordinal);
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();

        List<string> disagreements = [];
        foreach ((string host, bool expected) in Cases)
        {
            bool entity = CanonicalHost.IsWellFormed(host);
            await using var command = new NpgsqlCommand(sql, owner);
            command.Parameters.AddWithValue("host", host);
            bool database = (bool)(await command.ExecuteScalarAsync())!;
            if (entity != expected || database != expected)
            {
                disagreements.Add($"'{host}': expected {expected}, entity {entity}, database {database}");
            }
        }

        _output.WriteLine($"cases: {Cases.Count}; agree: {Cases.Count - disagreements.Count}");
        Cases.Count.ShouldBeGreaterThanOrEqualTo(20);
        disagreements.ShouldBeEmpty();
    }

    [Fact]
    public async Task lower_folds_a_non_ASCII_upper_case_letter_under_the_clusters_ctype_and_not_under_C()
    {
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT current_setting('lc_ctype'), lower(@u COLLATE \"C\"), lower(@u), lower(@m COLLATE \"C\"), lower(@m)", owner);
        command.Parameters.AddWithValue("u", NonAsciiUpper);
        command.Parameters.AddWithValue("m", AsciiAndNonAsciiUpper);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        string ctype = reader.GetString(0);
        string underC = reader.GetString(1);
        string underCtype = reader.GetString(2);
        string mixedUnderC = reader.GetString(3);
        string mixedUnderCtype = reader.GetString(4);

        _output.WriteLine($"lc_ctype of the catalog: {ctype}");
        _output.WriteLine($"lower('{NonAsciiUpper}') under C: {underC} (host = lower(host) {(underC == NonAsciiUpper ? "holds: admitted" : "fails: refused")})");
        _output.WriteLine($"lower('{NonAsciiUpper}') under {ctype}: {underCtype} (host = lower(host) {(underCtype == NonAsciiUpper ? "holds: admitted" : "fails: refused")})");
        _output.WriteLine($"lower('{AsciiAndNonAsciiUpper}') under C: {mixedUnderC}; under {ctype}: {mixedUnderCtype}");

        ctype.ShouldStartWith("en_US", customMessage: "the image's cluster locale, without which the comparison is one collation against itself");
        underC.ShouldBe(NonAsciiUpper, "C folds ASCII only, so the lower-case check alone admits the row");
        underCtype.ShouldBe("pg.über.internal", "the cluster's ctype folds Ü, so the lower-case check refuses the row");
        mixedUnderC.ShouldNotBe(AsciiAndNonAsciiUpper, "B, E and R fold under C too: ÜBER is refused by the lower-case check under both and is not the witness");
        mixedUnderCtype.ShouldBe("pg.über.internal");
    }

    [Fact]
    public async Task On_a_C_collated_catalog_the_lower_case_check_alone_would_admit_pg_Uber_and_the_shape_check_refuses_it()
    {
        await using ScratchCatalog underC = await ScratchCatalog.CreateAsync(_catalog, locale: "C");
        await underC.MigrateToLatestAsync();

        bool lowerCaseHolds = await underC.ScalarAsync<bool>("SELECT @host = lower(@host)", ("host", NonAsciiUpper));
        PostgresException refusedUnderC = await Should.ThrowAsync<PostgresException>(() => underC.InsertClusterRowAsync(NonAsciiUpper, 5432));

        // On the fixture's en_US.utf8 catalog both checks fail; the lower-case one is named
        // because PostgreSQL evaluates check constraints in name order.
        PostgresException refusedUnderCtype = await Should.ThrowAsync<PostgresException>(() => InsertIntoTheFixtureCatalogAsync(NonAsciiUpper));

        _output.WriteLine($"C: '{NonAsciiUpper}' = lower(host) is {lowerCaseHolds}; insert refused {refusedUnderC.SqlState} on {refusedUnderC.ConstraintName}");
        _output.WriteLine($"en_US.utf8: insert refused {refusedUnderCtype.SqlState} on {refusedUnderCtype.ConstraintName}");
        lowerCaseHolds.ShouldBeTrue("under C the row satisfies host = lower(host); the shape check is what refuses it");
        refusedUnderC.SqlState.ShouldBe(CheckViolation);
        refusedUnderC.ConstraintName.ShouldBe(WellFormedConstraint);
        refusedUnderCtype.SqlState.ShouldBe(CheckViolation);
        refusedUnderCtype.ConstraintName.ShouldBe(LowerCaseConstraint);
    }

    [Fact]
    public void The_migration_this_rule_ships_in_is_the_one_under_test()
    {
        // The two tests above run the constraint as the latest migration created it; this pins
        // which migration that is, so a later one that rewrote the check would move this file too.
        typeof(ClusterEndpointUniqueness).Namespace.ShouldBe("Aurora.Platform.Tenancy.Migrations");
    }

    private async Task InsertIntoTheFixtureCatalogAsync(string host)
    {
        await using NpgsqlConnection owner = await _catalog.OpenMigratorConnectionAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO catalog.database_cluster (id, region, host, port, maintenance_database, admin_secret_ref, migrator_secret_ref, app_secret_ref, max_tenants, state) "
            + "VALUES (@id, 'nz', @host, 5432, 'postgres', 'ref:a', 'ref:m', 'ref:p', 10, 'Accepting')",
            owner);
        command.Parameters.AddWithValue("id", Unique.ClusterId().Value);
        command.Parameters.AddWithValue("host", host);
        await command.ExecuteNonQueryAsync();
    }
}
