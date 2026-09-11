using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aurora.Platform.Tenancy.Tests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// The privilege allowlist names every catalog table and nothing else, so that a table added to
/// <see cref="CatalogSchemaAllowlist.Columns"/> without a decision on what <c>aurora_app</c> may
/// do to it fails here, in verify.sh stage 6 with Docker stopped, before the integration test
/// that compares the decision with what PostgreSQL actually granted (<c>CatalogPrivilegeTests</c>).
/// </summary>
public sealed partial class CatalogPrivilegeAllowlistTests
{
    private readonly ITestOutputHelper _output;

    public CatalogPrivilegeAllowlistTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Every_catalog_table_has_a_privilege_decision_and_no_decision_names_a_table_that_does_not_exist()
    {
        IEnumerable<string> tables = CatalogSchemaAllowlist.Columns.Keys.Concat(CatalogSchemaAllowlist.InfrastructureColumns.Keys);

        CatalogSchemaAllowlist.AppRolePrivileges.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(tables.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_recorded_privilege_is_spelled_the_way_the_ACL_reads_back()
    {
        // A table privilege is the upper-case word PostgreSQL's aclexplode returns; a column
        // privilege is that word with the column in parentheses, no space. The integration test
        // builds the same spelling from relacl and attacl, so any other form - "select", "UPDATE
        // (state)", a stray "WITH GRANT OPTION" - could never match and would fail there as a
        // privilege the role does not hold. Failing here is the cheaper place.
        List<(string Table, string Privilege)> recorded = [.. CatalogSchemaAllowlist.AppRolePrivileges
            .SelectMany(table => table.Value.Select(privilege => (table.Key, privilege)))];

        _output.WriteLine($"Checked the spelling of {recorded.Count} recorded privileges.");
        recorded.ShouldNotBeEmpty();

        foreach ((string table, string privilege) in recorded)
        {
            PrivilegeLabel().IsMatch(privilege).ShouldBeTrue($"{privilege} on catalog.{table}");
        }
    }

    [GeneratedRegex("^[A-Z]+(\\([a-z_][a-z0-9_]*\\))?$")]
    private static partial Regex PrivilegeLabel();
}
