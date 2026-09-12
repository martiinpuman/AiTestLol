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
/// The privilege record, held to its own rules in verify.sh stage 6 with Docker stopped: it names
/// every catalog table and nothing else, decides the schema and every other object the oracle
/// reads by a kind the oracle knows, spells each grant the way the ACL reads back, names the
/// component that needs each grant, and never lets the request path delete a row, create or
/// rewrite one it is routed by, or create in the schema. The integration test then holds the
/// database to the record (<c>CatalogPrivilegeTests</c>).
/// </summary>
/// <remarks>
/// Every test here is a check over static data, and each can fail in exactly one way: by the record
/// changing in the direction it forbids. That is the point — widening the record is the first step
/// of widening the grant, and this is where a reviewer sees it.
/// </remarks>
public sealed partial class CatalogPrivilegeAllowlistTests
{
    /// <summary>Every decision, tables and other objects alike, one row per grant.</summary>
    private static readonly IReadOnlyList<(CatalogObject Object, AppRoleGrant Grant)> Recorded =
    [
        .. CatalogSchemaAllowlist.AppRoleDecisions.SelectMany(decision => decision.Value.Select(grant => (decision.Key, grant))),
    ];

    /// <summary>The table decisions alone, by table name, for the rules that are about rows and columns.</summary>
    private static readonly IReadOnlyList<(string Table, AppRoleGrant Grant)> RecordedOnTables =
    [
        .. Recorded.Where(r => r.Object.Kind == CatalogObject.Table).Select(r => (r.Object.Name, r.Grant)),
    ];

    private readonly ITestOutputHelper _output;

    public CatalogPrivilegeAllowlistTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Every_catalog_table_has_a_privilege_decision_and_no_decision_names_a_table_that_does_not_exist()
    {
        IEnumerable<string> tables = CatalogSchemaAllowlist.Columns.Keys
            .Concat(CatalogSchemaAllowlist.InfrastructureColumns.Keys)
            .Concat(CatalogSchemaAllowlist.AppendOnlyColumns.Keys);

        CatalogSchemaAllowlist.AppRolePrivileges.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(tables.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void An_append_only_table_records_SELECT_and_INSERT_and_nothing_else()
    {
        // ADR-0004 rule 5 as a rule over the record, so that widening either trail's grant is
        // caught in stage 6 before a migration is ever written to match it. The integration probe
        // then holds the database to this record, and tries the writes as the role.
        _output.WriteLine(
            $"Checked {CatalogSchemaAllowlist.AppendOnlyTables.Count} append-only tables: "
            + $"{string.Join(", ", CatalogSchemaAllowlist.AppendOnlyTables.Order(StringComparer.Ordinal))}.");
        CatalogSchemaAllowlist.AppendOnlyTables.Count.ShouldBeGreaterThanOrEqualTo(2, "ADR-0007 9.2 names two");

        foreach (string table in CatalogSchemaAllowlist.AppendOnlyTables)
        {
            CatalogSchemaAllowlist.AppRolePrivileges[table]
                .Select(grant => grant.Privilege)
                .Order(StringComparer.Ordinal)
                .ShouldBe(["INSERT", "SELECT"], Case.Sensitive, $"catalog.{table} is append-only");
        }
    }

    [Fact]
    public void The_schema_is_decided_and_every_other_object_decision_names_a_kind_the_oracle_reads()
    {
        // The oracle keys what it reads by kind - schema, sequence, function, procedure, type -
        // so a decision recorded under a kind it never emits would be reported as an object that
        // does not exist, and a table recorded here rather than in AppRolePrivileges would be
        // reported twice. The schema is the one object of another kind the catalog has today,
        // and the request path may use it and not create in it: CREATE on the schema is what let
        // the second security re-review launder a write through an updatable view.
        List<CatalogObject> objects = [.. CatalogSchemaAllowlist.AppRoleObjectPrivileges.Keys];
        _output.WriteLine($"Checked {objects.Count} object decision(s) besides tables: {string.Join(", ", objects)}.");
        objects.ShouldNotBeEmpty();

        foreach (CatalogObject catalogObject in objects)
        {
            CatalogSchemaAllowlist.ObjectKinds.ShouldContain(catalogObject.Kind, $"{catalogObject} is recorded under a kind the oracle does not read");
        }

        CatalogSchemaAllowlist.AppRoleObjectPrivileges[new CatalogObject(CatalogObject.Schema, "catalog")]
            .Select(grant => grant.Privilege).ShouldBe(["USAGE"]);
    }

    [Fact]
    public void Every_recorded_privilege_is_spelled_the_way_the_ACL_reads_back()
    {
        // A privilege is the upper-case word PostgreSQL's aclexplode returns; a column privilege
        // is that word with the column in parentheses, no space. The integration test builds the
        // same spelling from the ACL, so any other form - "select", "UPDATE (state)", a stray
        // "WITH GRANT OPTION" - could never match and would fail there as a privilege the role
        // does not hold. Failing here is the cheaper place.
        _output.WriteLine($"Checked the spelling of {Recorded.Count} recorded grants.");
        Recorded.ShouldNotBeEmpty();

        foreach ((CatalogObject catalogObject, AppRoleGrant grant) in Recorded)
        {
            PrivilegeLabel().IsMatch(grant.Privilege).ShouldBeTrue($"{grant.Privilege} on {catalogObject}");
        }
    }

    [Fact]
    public void Every_recorded_privilege_names_the_task_or_the_decision_that_needs_it()
    {
        // The reviewer of the next catalog object has something to compare against only if each
        // grant says who issues the statement: a backlog task (B-nn, B-nn.n, FOLLOWUP-nnn) or the
        // ADR section that specifies the component. A grant that names neither was not decided.
        _output.WriteLine($"Checked {Recorded.Count} recorded grants for a named component.");
        Recorded.ShouldNotBeEmpty();

        foreach ((CatalogObject catalogObject, AppRoleGrant grant) in Recorded)
        {
            NamedComponent().IsMatch(grant.NeededBy).ShouldBeTrue($"{grant.Privilege} on {catalogObject}: '{grant.NeededBy}'");
        }
    }

    [Fact]
    public void A_column_privilege_names_a_column_the_table_has()
    {
        List<(string Table, string Column)> columnGrants =
            [.. RecordedOnTables.Select(r => (r.Table, ColumnOf(r.Grant.Privilege))).Where(r => r.Item2 is not null).Select(r => (r.Table, r.Item2!))];

        // The lifecycle columns of catalog.tenant are column grants, so this cannot be checking an
        // empty list.
        _output.WriteLine($"Checked {columnGrants.Count} column-level grants against the schema allowlist.");
        columnGrants.ShouldNotBeEmpty();

        foreach ((string table, string column) in columnGrants)
        {
            CatalogSchemaAllowlist.Columns[table].ShouldContain(column, $"catalog.{table}.{column} is granted but is not a column of the table");
        }
    }

    [Fact]
    public void No_grant_lets_the_request_path_delete_or_truncate_a_catalog_row()
    {
        // ADR-0007 §11.4 tombstones a tenant, a subscription closes with valid_to, and DROP
        // DATABASE is aurora_admin's: nothing on the request path deletes a catalog row, so no
        // grant may let it. The security re-review deleted another tenant's host row with the
        // DELETE B-05 first granted; this is the record's half of not granting it again.
        _output.WriteLine($"Checked {Recorded.Count} recorded grants for DELETE or TRUNCATE.");
        Recorded.ShouldNotBeEmpty();

        Recorded
            .Where(r => r.Grant.Privilege.StartsWith("DELETE", StringComparison.Ordinal) || r.Grant.Privilege.StartsWith("TRUNCATE", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_request_path_may_only_read_the_cluster_table()
    {
        // A cluster's host, port and secret references are what the resolver dials with the real
        // cluster credentials. Nothing in ADR-0007 has the application registering or editing one.
        CatalogSchemaAllowlist.AppRolePrivileges["database_cluster"].Select(grant => grant.Privilege).ShouldBe(["SELECT"]);
    }

    [Fact]
    public void No_grant_lets_the_request_path_write_a_column_a_tenant_or_a_host_resolves_by()
    {
        // Two re-reviews, two verbs. A table-wide UPDATE on a table with routing columns, or a
        // column UPDATE naming one, is the first takeover: rebind a host, repoint a tenant's
        // database. INSERT is the second: it writes every column of a new row by definition, so
        // INSERT on such a table creates a routing decision of the request's own - a tenant whose
        // database_name is another tenant's, a host for a tenant the request does not own - and no
        // column list can narrow it, because provisioning has to supply exactly those columns.
        // The request path therefore holds no INSERT on a routing table at all; the saga's writes
        // are another principal's.
        int examined = 0;
        foreach ((string table, AppRoleGrant grant) in RecordedOnTables)
        {
            if (!CatalogSchemaAllowlist.RoutingColumns.TryGetValue(table, out IReadOnlySet<string>? routing))
            {
                continue;
            }

            examined++;
            grant.Privilege.StartsWith("INSERT", StringComparison.Ordinal).ShouldBeFalse(
                $"{grant.Privilege} on catalog.{table} writes every column of a new row, the columns a tenant or a host resolves by included; "
                + "the request path may not create a routing decision");

            if (grant.Privilege.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                string? column = ColumnOf(grant.Privilege);
                column.ShouldNotBeNull($"UPDATE on catalog.{table} is table-wide, and {table} has columns a request is routed by");
                routing.ShouldNotContain(column, $"UPDATE({column}) on catalog.{table} lets the request path rewrite where a tenant or a host resolves");
            }
        }

        // catalog.tenant's SELECT and lifecycle-column UPDATE grants are grants on a table with
        // routing columns, so this loop cannot have examined nothing.
        _output.WriteLine($"Examined {examined} grants on tables that hold routing columns.");
        examined.ShouldBeGreaterThan(0);
    }

    private static string? ColumnOf(string privilege)
    {
        int open = privilege.IndexOf('(', StringComparison.Ordinal);
        return open < 0 ? null : privilege[(open + 1)..^1];
    }

    [GeneratedRegex("^[A-Z]+(\\([a-z_][a-z0-9_]*\\))?$")]
    private static partial Regex PrivilegeLabel();

    [GeneratedRegex("\\b(B-\\d{2}(\\.\\d)?|FOLLOWUP-\\d{3}|ADR-\\d{4})\\b")]
    private static partial Regex NamedComponent();
}
