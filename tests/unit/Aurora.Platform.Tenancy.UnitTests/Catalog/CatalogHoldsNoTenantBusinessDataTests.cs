using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Tests;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// ADR-0007 §9.3 over the EF model, plus the deliberately-violating inputs that prove the guard
/// fails when it should (testing-strategy.md §1 rule 4). The same guard runs over the migrated
/// schema in the integration tests.
/// </summary>
public sealed class CatalogHoldsNoTenantBusinessDataTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowlistWithIdentityUser =
        CatalogSchemaAllowlist.Columns
            .Append(new KeyValuePair<string, IReadOnlySet<string>>(
                "identity_user",
                new HashSet<string>(StringComparer.Ordinal) { "id", "email_normalized" }))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void The_catalog_model_holds_no_tenant_business_data()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(ColumnsOf(context.Model), CatalogSchemaAllowlist.Columns);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void No_property_in_the_catalog_model_is_a_kernel_money_or_quantity()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        IEnumerable<string> offenders = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(Money) || property.ClrType == typeof(Quantity))
            .Select(property => $"{property.DeclaringType.DisplayName()}.{property.Name}");

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void A_column_that_is_not_allowlisted_is_reported()
    {
        List<CatalogColumn> columns = [.. ModelColumns(), new CatalogColumn("tenant", "onboarding_note", "text")];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, CatalogSchemaAllowlist.Columns);

        violations.ShouldHaveSingleItem().ShouldContain("tenant.onboarding_note is not in the catalog allowlist");
    }

    [Fact]
    public void A_business_data_column_is_reported_three_ways_and_the_allowlist_cannot_wave_it_through()
    {
        // Even with the column allowlisted, the name and the type each fail on their own.
        Dictionary<string, IReadOnlySet<string>> allowlist = new(CatalogSchemaAllowlist.Columns, StringComparer.Ordinal)
        {
            ["tenant"] = new HashSet<string>(CatalogSchemaAllowlist.Columns["tenant"], StringComparer.Ordinal) { "invoice_total" },
        };
        List<CatalogColumn> columns = [.. ModelColumns(), new CatalogColumn("tenant", "invoice_total", "numeric(19,4)")];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, allowlist);

        violations.Count.ShouldBe(3, string.Join(Environment.NewLine, violations));
        violations.ShouldContain(violation => violation.Contains("names tenant business data ('invoice')", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("names tenant business data ('total')", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("numeric(19,4), a monetary column type", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_that_is_not_allowlisted_is_reported()
    {
        List<CatalogColumn> columns = [.. ModelColumns(), new CatalogColumn("customer_summary", "id", "uuid")];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, CatalogSchemaAllowlist.Columns);

        violations.Count.ShouldBe(2, string.Join(Environment.NewLine, violations));
        violations.ShouldContain(violation => violation.Contains("table catalog.customer_summary is not in the catalog allowlist", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("names tenant business data ('customer')", StringComparison.Ordinal));
    }

    [Fact]
    public void Personal_data_is_reported_except_for_the_one_column_ADR_0007_9_3_admits()
    {
        List<CatalogColumn> admitted =
        [
            .. ModelColumns(),
            new CatalogColumn("identity_user", "id", "uuid"),
            new CatalogColumn("identity_user", "email_normalized", "character varying(320)"),
        ];
        List<CatalogColumn> notAdmitted =
        [
            .. ModelColumns(),
            new CatalogColumn("identity_user", "id", "uuid"),
            new CatalogColumn("identity_user", "email_normalized", "character varying(320)"),
            new CatalogColumn("tenant", "phone_number", "text"),
        ];

        CatalogSchemaGuard.Violations(admitted, AllowlistWithIdentityUser).ShouldBeEmpty();
        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(notAdmitted, AllowlistWithIdentityUser);

        violations.Count.ShouldBe(2, string.Join(Environment.NewLine, violations));
        violations.ShouldContain(violation => violation.Contains("tenant.phone_number is not in the catalog allowlist", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("tenant.phone_number looks like personal data ('phone')", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stale_allowlist_row_is_reported_so_the_list_stays_exact()
    {
        List<CatalogColumn> columns = [.. ModelColumns().Where(column => column.Column != "last_activity_at")];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, CatalogSchemaAllowlist.Columns);

        violations.ShouldHaveSingleItem().ShouldContain("allowlisted column catalog.tenant.last_activity_at does not exist");
    }

    private static List<CatalogColumn> ModelColumns()
    {
        using CatalogDbContext context = OfflineCatalog.Open();
        return [.. ColumnsOf(context.Model)];
    }

    private static IReadOnlyCollection<CatalogColumn> ColumnsOf(IModel model) =>
    [
        .. model.GetEntityTypes().SelectMany(entity =>
            entity.GetProperties().Select(property =>
                new CatalogColumn(entity.GetTableName()!, property.GetColumnName(), property.GetColumnType()))),
    ];
}
