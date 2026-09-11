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
/// <remarks>
/// Every failure case below feeds the guard the <em>real</em> model columns plus one synthetic
/// offender, so each asserts both halves of the rule at once: the offender is reported, and
/// nothing else is. A guard that reported the whole schema would pass a test that only looked for
/// its own message.
/// </remarks>
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
        IReadOnlyCollection<CatalogColumn> columns = ColumnsOf(context.Model);

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, CatalogSchemaAllowlist.Columns);

        // The count is the answer to "could this have measured nothing?" - an empty model would
        // fail on the stale-allowlist rule, but saying the number here means a reader of the test
        // output knows what was inspected.
        columns.Count.ShouldBe(CatalogSchemaAllowlist.Columns.Sum(table => table.Value.Count));
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
        violations.ShouldContain(violation => violation.Contains("table catalog.customer_summary names tenant business data ('customer')", StringComparison.Ordinal));
    }

    [Fact]
    public void A_business_data_table_is_reported_even_when_its_columns_are_innocent_and_it_is_allowlisted()
    {
        // ADR-0007 §9.3 permits "a catalog-owned summary table". This is the check that the
        // permission cannot be spent on a table named after the tenant data it aggregates: adding
        // invoice_summary to the allowlist - the one rule with a legitimate yes - does not silence
        // the word rule, which has no exception list at all.
        Dictionary<string, IReadOnlySet<string>> allowlist = new(CatalogSchemaAllowlist.Columns, StringComparer.Ordinal)
        {
            ["invoice_summary"] = new HashSet<string>(StringComparer.Ordinal) { "tenant_id", "count" },
        };
        List<CatalogColumn> columns =
        [
            .. ModelColumns(),
            new CatalogColumn("invoice_summary", "tenant_id", "uuid"),
            new CatalogColumn("invoice_summary", "count", "integer"),
        ];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, allowlist);

        violations.ShouldHaveSingleItem()
            .ShouldContain("table catalog.invoice_summary names tenant business data ('invoice')");
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
            .. admitted,
            new CatalogColumn("tenant", "phone_number", "text"),
        ];

        CatalogSchemaGuard.Violations(admitted, AllowlistWithIdentityUser).ShouldBeEmpty();
        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(notAdmitted, AllowlistWithIdentityUser);

        violations.Count.ShouldBe(2, string.Join(Environment.NewLine, violations));
        violations.ShouldContain(violation => violation.Contains("tenant.phone_number is not in the catalog allowlist", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("tenant.phone_number looks like personal data ('phone')", StringComparison.Ordinal));
    }

    [Fact]
    public void A_person_name_column_is_reported_and_a_column_that_merely_starts_with_its_qualifier_is_not()
    {
        // The regression behind CatalogSchemaAllowlist.PersonalDataPhrases. 'last' as a single word
        // flagged catalog.tenant.last_activity_at, which holds no person; the fix must not have
        // bought that by letting last_name through. One test, both directions.
        Dictionary<string, IReadOnlySet<string>> allowlist = new(AllowlistWithIdentityUser, StringComparer.Ordinal)
        {
            ["identity_user"] = new HashSet<string>(StringComparer.Ordinal) { "id", "email_normalized", "last_name", "last_seen_at" },
        };
        List<CatalogColumn> columns =
        [
            .. ModelColumns(),
            new CatalogColumn("identity_user", "id", "uuid"),
            new CatalogColumn("identity_user", "email_normalized", "character varying(320)"),
            new CatalogColumn("identity_user", "last_seen_at", "timestamp with time zone"),
            new CatalogColumn("identity_user", "last_name", "text"),
        ];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, allowlist);

        violations.ShouldHaveSingleItem()
            .ShouldContain("identity_user.last_name looks like personal data ('last_name')");
    }

    [Fact]
    public void A_stale_allowlist_row_is_reported_so_the_list_stays_exact()
    {
        List<CatalogColumn> columns = [.. ModelColumns().Where(column => column.Column != "last_activity_at")];

        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations(columns, CatalogSchemaAllowlist.Columns);

        violations.ShouldHaveSingleItem().ShouldContain("allowlisted column catalog.tenant.last_activity_at does not exist");
    }

    [Fact]
    public void Observing_nothing_is_reported_as_loudly_as_observing_the_wrong_thing()
    {
        // CLAUDE.md self-check 2: a check that cannot tell "all good" from "nothing ran" is not a
        // check. Hand the guard an empty schema - what a mistyped query or an unmigrated database
        // produces - and it reports every allowlisted column as missing rather than success.
        IReadOnlyList<string> violations = CatalogSchemaGuard.Violations([], CatalogSchemaAllowlist.Columns);

        violations.Count.ShouldBe(CatalogSchemaAllowlist.Columns.Sum(table => table.Value.Count));
        violations.ShouldAllBe(violation => violation.Contains("does not exist", StringComparison.Ordinal));
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
