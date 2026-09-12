using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Migrations;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The <c>ClusterEndpointUniqueness</c> migration applied to a catalog that already exists at the
/// state before it (ADR-0034 §5.1, expand/contract honesty): against two cluster rows on one
/// endpoint it fails loudly, names the duplicate and applies nothing; against a host in any
/// spelling but lower case, or a value that is not one host, it fails loudly, names the constraint
/// and applies nothing; against rows on distinct, canonical endpoints it applies, keeps every row
/// and re-runs as a no-op.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a scratch database and not the fixture's catalog.</b> The fixture's catalog is migrated
/// to the latest migration before any test runs, so the question this class asks — what the
/// migration does to rows that were there first — cannot be asked of it. Each test creates a
/// database of its own on the fixture's container, owned by <c>aurora_migrator</c> as the real
/// catalog is, migrates it as the migrator to the migration <em>before</em> this one, seeds rows as
/// the owner, and then migrates to the latest as the runner would. The database is dropped after.
/// </para>
/// <para>
/// <b>Why the failure must be loud, and what "loud" is held to.</b> A unique index that could be
/// created over duplicates would be no index. PostgreSQL refuses it with SQLSTATE <c>23505</c>,
/// naming the index and the duplicated key, and EF runs the migration inside a transaction, so
/// nothing of it survives — not the index, not the history row, and neither duplicate is touched.
/// The worse outcome ADR-0034 §5.1 names is a fleet with the defect that migrates clean and stays
/// broken; this is the test that says it cannot. Made non-unique, the index is created over the
/// duplicates and the first test fails at <c>Should.ThrowAsync</c> — executed once, after these
/// tests were first green, and recorded in B-20's handback.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class ClusterEndpointUniquenessMigrationTests
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string IndexName = "ux_database_cluster_host_port";
    private const string LowerCaseConstraint = "ck_database_cluster_host_lower_case";
    private const string WellFormedConstraint = "ck_database_cluster_host_well_formed";

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public ClusterEndpointUniquenessMigrationTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task Against_a_catalog_already_holding_two_cluster_rows_on_one_endpoint_the_migration_fails_naming_the_duplicate_and_applies_nothing()
    {
        await using ScratchCatalog scratch = await ScratchCatalog.CreateAsync(_catalog);
        await scratch.MigrateToTheStateBeforeAsync<ClusterEndpointUniqueness>();
        string host = Unique.Host();
        await scratch.InsertClusterRowAsync(host, 5432);
        await scratch.InsertClusterRowAsync(host, 5432);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(scratch.MigrateToLatestAsync);

        _output.WriteLine($"{refused.SqlState}: {refused.MessageText}");
        _output.WriteLine($"DETAIL: {refused.Detail}");
        refused.SqlState.ShouldBe(UniqueViolation);
        refused.ConstraintName.ShouldBe(IndexName);
        refused.TableName.ShouldBe("database_cluster");
        refused.Detail.ShouldNotBeNull("the duplicate is named");
        refused.Detail.ShouldContain(host);
        refused.Detail.ShouldContain("5432");

        IEnumerable<string> applied = await scratch.AppliedMigrationsAsync();
        IEnumerable<string> pending = await scratch.PendingMigrationsAsync();
        applied.ShouldNotContain(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal), "the history must not record a migration that did not apply");
        pending.ShouldContain(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal), "the migration is still pending, for the runner to retry once an operator has resolved the duplicate");
        (await scratch.IndexDefinitionAsync(IndexName)).ShouldBeNull("no index survives the failed transaction");
        (await scratch.ClusterRowCountAsync()).ShouldBe(2, "both rows are left for an operator to resolve; nothing is de-duplicated silently");
    }

    [Fact]
    public async Task Against_a_catalog_already_holding_a_host_in_another_case_the_migration_fails_naming_the_constraint_and_applies_nothing()
    {
        // Before this migration nothing refused the spelling, so a fleet could hold one. The
        // migration is one transaction, index first and then the check: the check fails, and the
        // index that had just been created goes with it - which is the proof that nothing of a
        // failed migration survives, not only the statement that failed.
        await using ScratchCatalog scratch = await ScratchCatalog.CreateAsync(_catalog);
        await scratch.MigrateToTheStateBeforeAsync<ClusterEndpointUniqueness>();
        const string mixedCase = "PG-Mixed.Internal";
        await scratch.InsertClusterRowAsync(mixedCase, 5432);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(scratch.MigrateToLatestAsync);

        _output.WriteLine($"{refused.SqlState}: {refused.MessageText}");
        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe(LowerCaseConstraint);
        refused.TableName.ShouldBe("database_cluster");

        (await scratch.PendingMigrationsAsync()).ShouldContain(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal));
        (await scratch.CheckConstraintDefinitionAsync(LowerCaseConstraint)).ShouldBeNull("no constraint survives the failed transaction");
        (await scratch.IndexDefinitionAsync(IndexName)).ShouldBeNull("the index created earlier in the same transaction does not survive it either");
        (await scratch.ClusterHostsAsync()).ShouldBe([mixedCase], "the row is left as it was for an operator to resolve; nothing is re-spelled silently");
    }

    [Fact]
    public async Task Against_a_catalog_already_holding_a_multi_host_list_the_migration_fails_naming_the_shape_constraint_and_applies_nothing()
    {
        // The fifth takeover shape (PR #18, second review): before this migration a host of
        // pg-1.internal,pg-2.internal was storable, and it opens pg-1.internal's server under a
        // string the index, the lower-case check and the endpoint comparison all took for another.
        await using ScratchCatalog scratch = await ScratchCatalog.CreateAsync(_catalog);
        await scratch.MigrateToTheStateBeforeAsync<ClusterEndpointUniqueness>();
        const string list = "pg-1.internal,pg-2.internal";
        await scratch.InsertClusterRowAsync(list, 5432);

        PostgresException refused = await Should.ThrowAsync<PostgresException>(scratch.MigrateToLatestAsync);

        _output.WriteLine($"{refused.SqlState}: {refused.MessageText}");
        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe(WellFormedConstraint);
        refused.TableName.ShouldBe("database_cluster");

        (await scratch.PendingMigrationsAsync()).ShouldContain(id => id.EndsWith("_" + nameof(ClusterEndpointUniqueness), StringComparison.Ordinal));
        (await scratch.CheckConstraintDefinitionAsync(WellFormedConstraint)).ShouldBeNull("no constraint survives the failed transaction");
        (await scratch.CheckConstraintDefinitionAsync(LowerCaseConstraint)).ShouldBeNull("nor the one created a statement earlier");
        (await scratch.IndexDefinitionAsync(IndexName)).ShouldBeNull("nor the index");
        (await scratch.ClusterHostsAsync()).ShouldBe([list], "the row is left as it was for an operator to resolve");
    }

    [Fact]
    public async Task Against_a_catalog_whose_cluster_rows_are_on_distinct_endpoints_the_migration_applies_keeps_every_row_and_re_runs_as_a_no_op()
    {
        // Three rows that the index must admit: one host on two ports, and a second host on the
        // first port. An index on host alone, or on port alone, would refuse one of them.
        await using ScratchCatalog scratch = await ScratchCatalog.CreateAsync(_catalog);
        await scratch.MigrateToTheStateBeforeAsync<ClusterEndpointUniqueness>();
        string host = Unique.Host();
        await scratch.InsertClusterRowAsync(host, 5432);
        await scratch.InsertClusterRowAsync(host, 5433);
        await scratch.InsertClusterRowAsync(Unique.Host(), 5432);
        int rowsBefore = await scratch.ClusterRowCountAsync();

        await scratch.MigrateToLatestAsync();

        string? definition = await scratch.IndexDefinitionAsync(IndexName);
        string? lowerCase = await scratch.CheckConstraintDefinitionAsync(LowerCaseConstraint);
        string? wellFormed = await scratch.CheckConstraintDefinitionAsync(WellFormedConstraint);
        _output.WriteLine($"cluster rows before: {rowsBefore}, after: {await scratch.ClusterRowCountAsync()}; index: {definition}; checks: {lowerCase} / {wellFormed}");
        definition.ShouldBe($"CREATE UNIQUE INDEX {IndexName} ON catalog.database_cluster USING btree (host, port)");
        lowerCase.ShouldBe("CHECK (((host)::text = lower((host)::text)))");
        wellFormed.ShouldNotBeNull().ShouldContain("~ '^[a-z0-9]");
        (await scratch.ClusterRowCountAsync()).ShouldBe(rowsBefore, "an expand-only migration removes nothing");
        (await scratch.PendingMigrationsAsync()).ShouldBeEmpty();

        // Idempotent: the runner re-runs migrations on resume (ADR-0007 §7.4).
        await scratch.MigrateToLatestAsync();
        (await scratch.PendingMigrationsAsync()).ShouldBeEmpty();
        (await scratch.ClusterRowCountAsync()).ShouldBe(rowsBefore);
    }
}
