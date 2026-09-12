using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Tests;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// ADR-0007 §9.3 over the migrated schema: the rule the unit tests apply to the EF model, applied
/// here to what PostgreSQL actually holds, so a column added by raw SQL in a migration is caught
/// as well. The second test adds an offending column inside a transaction it rolls back, and
/// watches the guard fail — the deliberately-violating fixture testing-strategy.md §1 rule 4 asks
/// every fitness test to ship with.
/// </summary>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogHoldsNoTenantBusinessDataTests
{
    private const string ColumnsSql =
        "SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod) " +
        "FROM pg_attribute a " +
        "JOIN pg_class c ON c.oid = a.attrelid " +
        "JOIN pg_namespace n ON n.oid = c.relnamespace " +
        "WHERE n.nspname = 'catalog' AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped";

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Allowlist =
        CatalogSchemaAllowlist.Columns
            .Concat(CatalogSchemaAllowlist.InfrastructureColumns)
            .Concat(CatalogSchemaAllowlist.AppendOnlyColumns)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private readonly CatalogDatabaseFixture _catalog;
    private readonly ITestOutputHelper _output;

    public CatalogHoldsNoTenantBusinessDataTests(CatalogDatabaseFixture catalog, ITestOutputHelper output)
    {
        _catalog = catalog;
        _output = output;
    }

    [Fact]
    public async Task The_migrated_catalog_schema_holds_no_tenant_business_data()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        IReadOnlyCollection<CatalogColumn> columns = await ColumnsAsync(connection, null);

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, Allowlist);

        // Say what was inspected, not only that it was clean, so a pass is not indistinguishable
        // from a query that returned no rows (CLAUDE.md self-check 2).
        _output.WriteLine(
            $"ADR-0007 9.3 checked {columns.Count} columns across "
            + $"{columns.Select(column => column.Table).Distinct(StringComparer.Ordinal).Count()} catalog tables.");

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
        columns.Count.ShouldBe(Allowlist.Sum(table => table.Value.Count));
    }

    [Fact]
    public async Task The_guard_fails_the_moment_a_business_data_column_is_added_to_the_schema()
    {
        await using NpgsqlConnection connection = await _catalog.OpenMigratorConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await using (var alter = new NpgsqlCommand(
                         "ALTER TABLE catalog.tenant ADD COLUMN invoice_total numeric(19,4)", connection, transaction))
        {
            await alter.ExecuteNonQueryAsync();
        }

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(await ColumnsAsync(connection, transaction), Allowlist);

        await transaction.RollbackAsync();

        violations.Count.ShouldBe(4, string.Join(Environment.NewLine, violations));
        violations.ShouldContain(v => v.Contains("tenant.invoice_total is not in the catalog allowlist", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("names tenant business data ('invoice')", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("names tenant business data ('total')", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("numeric(19,4), a monetary column type", StringComparison.Ordinal));

        // And once rolled back, the schema is clean again - the failure above was the column, not the guard.
        CatalogSchemaGuard.Violations(await ColumnsAsync(connection, null), Allowlist).ShouldBeEmpty();
    }

    private static async Task<IReadOnlyCollection<CatalogColumn>> ColumnsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(ColumnsSql, connection, transaction);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<CatalogColumn> columns = [];
        while (await reader.ReadAsync())
        {
            columns.Add(new CatalogColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return columns;
    }
}
