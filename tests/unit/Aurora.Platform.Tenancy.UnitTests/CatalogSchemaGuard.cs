using System;
using System.Collections.Generic;
using System.Linq;

namespace Aurora.Platform.Tenancy.Tests;

/// <summary>One column of the catalog schema, however it was observed.</summary>
public sealed record CatalogColumn(string Table, string Column, string StoreType);

/// <summary>
/// ADR-0007 §9.3 as a mechanism: <em>the catalog holds no tenant business data</em>.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into two test assemblies. The unit tests run it over the EF model, so
/// the rule is enforced in <c>verify.sh</c> stage 6 with no database; the integration tests run
/// it over <c>information_schema</c> of the migrated catalog, so a column added by raw SQL in a
/// migration is caught too.
/// </para>
/// <para>
/// <b>How it can fail — which is the point.</b> A test that enumerated today's tables and found
/// them fine would pass forever. Instead:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The allowlist is exact.</b> Every (table, column) in the catalog must be listed in
/// <see cref="CatalogSchemaAllowlist.Columns"/>, and every listed column must exist. A new column
/// fails until someone adds it here — in a diff a reviewer reads against §9.3 — and a removed
/// column fails until its row is removed, so the list cannot silently pre-approve anything.
/// </description></item>
/// <item><description>
/// <b>Names that mean business data fail even if allowlisted.</b> Whole snake_case words such as
/// <c>invoice</c>, <c>ledger</c> or <c>amount</c> (<see cref="CatalogSchemaAllowlist.BusinessDataWords"/>)
/// cannot be waved through by editing one list: they would need a second, explicit exception, and
/// there is deliberately no such list for business data.
/// </description></item>
/// <item><description>
/// <b>Monetary column types fail.</b> Nothing in the catalog is <c>numeric</c>; an amount has no
/// business here (ADR-0021 puts money in tenant ledgers).
/// </description></item>
/// <item><description>
/// <b>Personal data fails, with one recorded exception.</b> ADR-0007 §9.3 admits exactly one
/// personal-data column: <c>catalog.identity_user.email_normalized</c>, because a person must be
/// resolvable before a tenant is known. It is listed in
/// <see cref="CatalogSchemaAllowlist.PersonalDataExceptions"/> ahead of the Identity module that
/// will create it (ADR-0009), so that column and no other passes.
/// </description></item>
/// </list>
/// <para>
/// The rule's own failure path is proven by tests that feed it synthetic columns and assert the
/// violations, and by the integration test that adds an offending column inside a transaction it
/// then rolls back.
/// </para>
/// </remarks>
public static class CatalogSchemaGuard
{
    public static IReadOnlyList<string> Violations(
        IReadOnlyCollection<CatalogColumn> observed,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowlist)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(allowlist);

        List<string> violations = [];

        foreach (CatalogColumn column in observed)
        {
            if (!allowlist.TryGetValue(column.Table, out IReadOnlySet<string>? allowedColumns))
            {
                violations.Add(
                    $"table catalog.{column.Table} is not in the catalog allowlist. If it holds registry, " +
                    "routing, subscription, package, platform-identity or platform-job data (ADR-0007 9.2), " +
                    "add it to CatalogSchemaAllowlist.Columns; if it holds anything a tenant records in " +
                    "their own books, it does not belong in the catalog (9.3).");
            }
            else if (!allowedColumns.Contains(column.Column))
            {
                violations.Add(
                    $"column catalog.{column.Table}.{column.Column} is not in the catalog allowlist. Add it " +
                    "to CatalogSchemaAllowlist.Columns only after confirming it is not tenant business data " +
                    "(ADR-0007 9.3).");
            }

            foreach (string word in Words(column.Column))
            {
                if (CatalogSchemaAllowlist.BusinessDataWords.Contains(word))
                {
                    violations.Add(
                        $"column catalog.{column.Table}.{column.Column} names tenant business data ('{word}'). " +
                        "The catalog holds none (ADR-0007 9.3); a platform feature that seems to need it " +
                        "aggregates into a catalog-owned summary table with a name that says so.");
                }

                if (CatalogSchemaAllowlist.PersonalDataWords.Contains(word)
                    && !CatalogSchemaAllowlist.PersonalDataExceptions.Contains((column.Table, column.Column)))
                {
                    violations.Add(
                        $"column catalog.{column.Table}.{column.Column} looks like personal data ('{word}'). " +
                        "The catalog's one admitted personal-data column is identity_user.email_normalized " +
                        "(ADR-0007 9.3); anything else lives in a tenant database behind [PersonalData].");
                }
            }

            if (CatalogSchemaAllowlist.MonetaryStoreTypes.Any(type =>
                    column.StoreType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(
                    $"column catalog.{column.Table}.{column.Column} is {column.StoreType}, a monetary column type. " +
                    "Amounts live in tenant ledgers, never in the catalog (ADR-0007 9.3, ADR-0021).");
            }
        }

        HashSet<(string Table, string Column)> present = [.. observed.Select(column => (column.Table, column.Column))];
        foreach ((string table, IReadOnlySet<string> columns) in allowlist)
        {
            foreach (string column in columns)
            {
                if (!present.Contains((table, column)))
                {
                    violations.Add(
                        $"allowlisted column catalog.{table}.{column} does not exist. Remove it from " +
                        "CatalogSchemaAllowlist.Columns: a stale row silently pre-approves a future column.");
                }
            }
        }

        return violations;
    }

    private static IEnumerable<string> Words(string columnName) =>
        columnName.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToLowerInvariant());
}

/// <summary>What the catalog schema may contain. Edited deliberately, reviewed against ADR-0007 §9.3.</summary>
public static class CatalogSchemaAllowlist
{
    /// <summary>The tables the catalog model maps, and every column of each (ADR-0007 §9.2).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Columns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["database_cluster"] = Set(
                "id", "region", "host", "port", "maintenance_database",
                "admin_secret_ref", "migrator_secret_ref", "app_secret_ref", "max_tenants", "state"),
            ["tenant"] = Set(
                "id", "key", "display_name", "state", "cluster_id", "database_name", "residency_region",
                "core_schema_version", "plan", "created_at", "activated_at", "suspended_at",
                "deletion_due_at", "deleted_at", "last_activity_at"),
            ["tenant_host"] = Set("host", "tenant_id", "is_primary", "verified_at"),
            ["subscription"] = Set("id", "tenant_id", "plan", "seats", "valid_from", "valid_to"),
            ["installed_package"] = Set("tenant_id", "package_id", "version", "state", "installed_at", "installed_by"),
        };

    /// <summary>
    /// Tables in the <c>catalog</c> schema that no entity maps: EF Core's migrations history.
    /// Present in the migrated database, absent from the model.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> InfrastructureColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["__EFMigrationsHistory"] = Set("MigrationId", "ProductVersion"),
        };

    /// <summary>
    /// Whole snake_case words that name what a tenant records in their own books. No exception
    /// list exists for these on purpose.
    /// </summary>
    public static readonly IReadOnlySet<string> BusinessDataWords = Set(
        "invoice", "invoices", "customer", "customers", "vendor", "vendors", "supplier", "party", "parties",
        "ledger", "journal", "posting", "account", "accounts", "amount", "amounts", "price", "prices",
        "cost", "costs", "total", "totals", "balance", "balances", "stock", "inventory", "warehouse",
        "item", "items", "sku", "product", "products", "order", "orders", "payment", "payments",
        "tax", "vat", "gst", "iban", "bank", "currency", "revenue", "sales", "purchase", "purchases");

    /// <summary>Whole snake_case words that name a natural person's data (ADR-0018).</summary>
    public static readonly IReadOnlySet<string> PersonalDataWords = Set(
        "email", "phone", "mobile", "birth", "birthday", "passport", "given", "family", "first", "last",
        "surname", "person", "address", "street", "national", "ssn");

    /// <summary>
    /// The one personal-data column ADR-0007 §9.3 admits, recorded before the Identity module
    /// creates it (ADR-0009) so that it passes and nothing else does.
    /// </summary>
    public static readonly IReadOnlySet<(string Table, string Column)> PersonalDataExceptions =
        new HashSet<(string Table, string Column)> { ("identity_user", "email_normalized") };

    /// <summary>PostgreSQL store types that carry an amount.</summary>
    public static readonly IReadOnlyList<string> MonetaryStoreTypes = ["numeric", "decimal", "money"];

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
