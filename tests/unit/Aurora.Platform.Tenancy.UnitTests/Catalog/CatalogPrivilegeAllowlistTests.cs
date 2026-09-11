using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Platform.Tenancy.Tests;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// The privilege allowlist names every catalog table and nothing else, so that a table added to
/// <see cref="CatalogSchemaAllowlist.Columns"/> without a decision on what <c>aurora_app</c> may
/// do to it fails here, in verify.sh stage 6 with Docker stopped, before the integration test
/// that compares the decision with what PostgreSQL actually granted (<c>CatalogPrivilegeTests</c>).
/// </summary>
public sealed class CatalogPrivilegeAllowlistTests
{
    [Fact]
    public void Every_catalog_table_has_a_privilege_decision_and_no_decision_names_a_table_that_does_not_exist()
    {
        IEnumerable<string> tables = CatalogSchemaAllowlist.Columns.Keys.Concat(CatalogSchemaAllowlist.InfrastructureColumns.Keys);

        CatalogSchemaAllowlist.AppRolePrivileges.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(tables.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_recorded_privilege_is_one_PostgreSQL_can_grant_on_a_table()
    {
        foreach ((string table, IReadOnlySet<string> privileges) in CatalogSchemaAllowlist.AppRolePrivileges)
        {
            privileges.ShouldBeSubsetOf(CatalogSchemaAllowlist.PostgresTablePrivileges, table);
        }
    }
}
