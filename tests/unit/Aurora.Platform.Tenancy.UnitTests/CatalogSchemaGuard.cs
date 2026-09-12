using System;
using System.Collections.Generic;
using System.Linq;

namespace Aurora.Platform.Tenancy.Tests;

/// <summary>One column of the catalog schema, however it was observed.</summary>
public sealed record CatalogColumn(string Table, string Column, string StoreType);

/// <summary>
/// One privilege <c>aurora_app</c> holds on a catalog table, and the component that needs it.
/// </summary>
/// <param name="Privilege">
/// Spelled the way <c>CatalogPrivilegeTests</c> reads it back out of the ACL: a table privilege as
/// PostgreSQL names it (<c>SELECT</c>), a column privilege as <c>UPDATE(column)</c>.
/// </param>
/// <param name="NeededBy">
/// The task, saga step or module that issues the statement, so that the reviewer of the next grant
/// has something to compare against. A grant nobody can be named for is not a decision, and is
/// not made.
/// </param>
public sealed record AppRoleGrant(string Privilege, string NeededBy);

/// <summary>
/// One object in schema <c>catalog</c> that carries an ACL of its own, by the kind the ACL lives
/// on: <see cref="Table"/> (every relation a table privilege applies to — table, partitioned
/// table, view, materialized view, foreign table), <c>sequence</c>, <c>function</c>,
/// <c>procedure</c>, <c>type</c>, or the <see cref="Schema"/> itself. This is the key both the
/// record and <c>CatalogPrivilegeTests</c>' oracle use, so an object of one kind can never be
/// mistaken for an object of another with the same name. A function's name carries its identity
/// arguments, as PostgreSQL spells an overload: <c>touch_activity(uuid)</c>.
/// </summary>
public sealed record CatalogObject(string Kind, string Name)
{
    public const string Table = "table";
    public const string Schema = "schema";

    public static CatalogObject TableNamed(string name) => new(Table, name);

    /// <summary>The record in <see cref="CatalogSchemaAllowlist"/> that decides this object.</summary>
    public string Record =>
        Kind == Table ? nameof(CatalogSchemaAllowlist.AppRolePrivileges) : nameof(CatalogSchemaAllowlist.AppRoleObjectPrivileges);

    public override string ToString() => Kind switch
    {
        Table => $"catalog.{Name}",
        Schema => $"schema {Name}",
        _ => $"{Kind} catalog.{Name}",
    };
}

/// <summary>
/// ADR-0007 §9.3 as a mechanism: <em>the catalog holds no tenant business data</em>.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into two test assemblies. The unit tests run it over the EF model, so
/// the rule is enforced in <c>verify.sh</c> stage 6 with no database; the integration tests run
/// it over the migrated catalog's <c>pg_attribute</c>, so a column added by raw SQL in a
/// migration is caught too.
/// </para>
/// <para>
/// <b>How it can fail — which is the point.</b> A test that enumerated today's tables and found
/// them fine would pass forever. Five rules, each able to fire on its own:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The allowlist is exact.</b> Every (table, column) in the catalog must be listed in
/// <see cref="CatalogSchemaAllowlist.Columns"/>, and every listed column must exist. A new column
/// fails until someone adds it here — in a diff a reviewer reads against §9.3 — and a removed
/// column fails until its row is removed, so the list cannot silently pre-approve anything.
/// <b>This is also what makes an empty observation loud</b>: a query that returned no rows reports
/// every allowlisted column as missing rather than reporting success.
/// </description></item>
/// <item><description>
/// <b>Column names that mean business data fail even if allowlisted.</b> Whole snake_case words
/// such as <c>invoice</c>, <c>ledger</c> or <c>amount</c>
/// (<see cref="CatalogSchemaAllowlist.BusinessDataWords"/>) cannot be waved through by editing one
/// list: they would need a second, explicit exception, and there is deliberately no such list for
/// business data.
/// </description></item>
/// <item><description>
/// <b>Table names are checked the same way.</b> §9.3's own escape hatch is "a fan-out job that
/// aggregates into a catalog-owned summary table", and a <c>customer_summary</c> whose columns are
/// all called <c>id</c> and <c>count</c> would otherwise trip only the allowlist rule — the one
/// rule that has a legitimate way to say yes. Checking the table name means the aggregate has to
/// be named for the platform concept it serves, not for the tenant data it came from.
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
/// <b>Why personal data needs two lists and business data needs one.</b> The words that name a
/// natural person split in half. Some are unambiguous on their own — nothing called <c>email</c>
/// or <c>passport</c> is anything else. The rest are qualifiers that only mean a person next to
/// <c>name</c>: <c>first</c>, <c>last</c>, <c>given</c>, <c>family</c>. Matching those as single
/// words flagged <c>catalog.tenant.last_activity_at</c>, a routing column with no person in it, and
/// a guard that cries wolf on a legitimate column gets its allowlist widened until it stops
/// guarding. So they live in <see cref="CatalogSchemaAllowlist.PersonalDataPhrases"/> and match
/// only as adjacent word runs. <c>last_name</c> still fails; <c>last_activity_at</c> does not.
/// </para>
/// <para>
/// The rule's own failure path is proven by tests that feed it synthetic columns and assert each
/// violation, and by the integration test that adds an offending column to the real schema inside
/// a transaction it then rolls back.
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

        foreach (string table in observed.Select(column => column.Table).Distinct(StringComparer.Ordinal))
        {
            if (!allowlist.ContainsKey(table))
            {
                violations.Add(
                    $"table catalog.{table} is not in the catalog allowlist. If it holds registry, " +
                    "routing, subscription, package, platform-identity or platform-job data (ADR-0007 9.2), " +
                    "add it to CatalogSchemaAllowlist.Columns; if it holds anything a tenant records in " +
                    "their own books, it does not belong in the catalog (9.3).");
            }

            violations.AddRange(BusinessDataViolations($"table catalog.{table}", table));
            violations.AddRange(PersonalDataViolations($"table catalog.{table}", table, (table, string.Empty)));
        }

        foreach (CatalogColumn column in observed)
        {
            string what = $"column catalog.{column.Table}.{column.Column}";

            if (allowlist.TryGetValue(column.Table, out IReadOnlySet<string>? allowedColumns)
                && !allowedColumns.Contains(column.Column))
            {
                violations.Add(
                    $"{what} is not in the catalog allowlist. Add it " +
                    "to CatalogSchemaAllowlist.Columns only after confirming it is not tenant business data " +
                    "(ADR-0007 9.3).");
            }

            violations.AddRange(BusinessDataViolations(what, column.Column));
            violations.AddRange(PersonalDataViolations(what, column.Column, (column.Table, column.Column)));

            if (CatalogSchemaAllowlist.MonetaryStoreTypes.Any(type =>
                    column.StoreType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(
                    $"{what} is {column.StoreType}, a monetary column type. " +
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
                        "CatalogSchemaAllowlist.Columns: a stale row silently pre-approves a future column, " +
                        "and a whole allowlist reported missing means the schema was never read at all.");
                }
            }
        }

        return violations;
    }

    private static IEnumerable<string> BusinessDataViolations(string what, string name) =>
        Words(name)
            .Where(CatalogSchemaAllowlist.BusinessDataWords.Contains)
            .Select(word =>
                $"{what} names tenant business data ('{word}'). " +
                "The catalog holds none (ADR-0007 9.3); a platform feature that seems to need it " +
                "aggregates into a catalog-owned summary table with a name that says so.");

    private static IEnumerable<string> PersonalDataViolations(
        string what,
        string name,
        (string Table, string Column) identity)
    {
        if (CatalogSchemaAllowlist.PersonalDataExceptions.Contains(identity))
        {
            yield break;
        }

        string[] words = [.. Words(name)];

        foreach (string word in words.Where(CatalogSchemaAllowlist.PersonalDataWords.Contains))
        {
            yield return Explain(what, word);
        }

        foreach (string phrase in CatalogSchemaAllowlist.PersonalDataPhrases.Where(phrase => Contains(words, phrase)))
        {
            yield return Explain(what, phrase);
        }
    }

    private static string Explain(string what, string matched) =>
        $"{what} looks like personal data ('{matched}'). " +
        "The catalog's one admitted personal-data column is identity_user.email_normalized " +
        "(ADR-0007 9.3); anything else lives in a tenant database behind [PersonalData].";

    /// <summary>Whether <paramref name="words"/> contains <paramref name="phrase"/>'s words, adjacent and in order.</summary>
    private static bool Contains(string[] words, string phrase)
    {
        string[] parts = phrase.Split('_');

        for (int start = 0; start + parts.Length <= words.Length; start++)
        {
            if (words.Skip(start).Take(parts.Length).SequenceEqual(parts, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Words(string name) =>
        name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToLowerInvariant());
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
    /// The catalog's two append-only trails (ADR-0004 rule 5), and every column of each. Created by
    /// raw SQL in the <c>AppendOnlyTrails</c> migration — the guard trigger they carry is nothing
    /// the model can express — and mapped by no entity, because no row that writes them has
    /// landed yet: the provisioning saga (B-07.1, B-07.4) and the erasure path bring their own
    /// writers. Present in the migrated database, absent from the model, and held by
    /// <c>CatalogPrivilegeAllowlistTests</c> to <see cref="AppRolePrivileges"/>' append-only rule —
    /// <c>SELECT, INSERT</c> and nothing else.
    /// </summary>
    /// <remarks>
    /// The columns are derived, not transcribed: ADR-0007 §9.2 names <c>operator_audit_event</c>
    /// as "every operator action, incl. support access" and ADR-0007 §11.5 and ADR-0018 §6 name
    /// what an erasure record holds — subject reference, tenant, requester, timestamp, fields
    /// affected, never the erased values — without naming columns. The operator trail follows
    /// ADR-0018 §1's <c>audit_event</c> shape where it applies to the platform side (ADR-0028 §6):
    /// no <c>company_id</c>, because the catalog has none; no <c>before</c>/<c>after</c>, because an
    /// operator action has no entity to diff, so a single <c>detail</c>; no hash chain, because
    /// none is specified for this table — it is where ADR-0018 §1's daily job <em>records</em> each
    /// tenant's chain head. <c>reason_code</c> is ADR-0010 rule 8's "reason-coded" support-access
    /// grant; <c>actor_type</c> is SPEC-001 BR-7's <c>Operator</c> or <c>System</c>.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AppendOnlyColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["operator_audit_event"] = Set(
                "id", "occurred_at", "tenant_id", "actor_type", "actor_id", "action", "reason_code",
                "correlation_id", "detail"),
            ["erasure_replay_log"] = Set(
                "id", "tenant_id", "subject_ref", "requested_by", "executed_at", "affected", "correlation_id"),
        };

    /// <summary>The append-only tables by name: <see cref="AppendOnlyColumns"/>' keys.</summary>
    public static readonly IReadOnlySet<string> AppendOnlyTables = new HashSet<string>(AppendOnlyColumns.Keys, StringComparer.Ordinal);

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

    /// <summary>
    /// Whole snake_case words that name a natural person's data on their own, whatever they sit
    /// next to (ADR-0018).
    /// </summary>
    public static readonly IReadOnlySet<string> PersonalDataWords = Set(
        "email", "phone", "mobile", "msisdn", "birth", "birthday", "passport", "surname",
        "person", "address", "street", "ssn");

    /// <summary>
    /// Runs of adjacent snake_case words that name a natural person, whose first word on its own
    /// does not. See the guard's remarks: <c>last_name</c> is personal data, <c>last_activity_at</c>
    /// is a routing column, and one list cannot tell them apart.
    /// </summary>
    public static readonly IReadOnlySet<string> PersonalDataPhrases = Set(
        "first_name", "last_name", "given_name", "family_name", "middle_name", "full_name",
        "national_id", "national_identifier");

    /// <summary>
    /// The one personal-data column ADR-0007 §9.3 admits, recorded before the Identity module
    /// creates it (ADR-0009) so that it passes and nothing else does.
    /// </summary>
    public static readonly IReadOnlySet<(string Table, string Column)> PersonalDataExceptions =
        new HashSet<(string Table, string Column)> { ("identity_user", "email_normalized") };

    /// <summary>PostgreSQL store types that carry an amount.</summary>
    public static readonly IReadOnlyList<string> MonetaryStoreTypes = ["numeric", "decimal", "money"];

    /// <summary>
    /// The columns that are the routing decision (ADR-0007 §3.2, §3.5): which tenant a host
    /// resolves to, which cluster and database a tenant resolves to, and everything about a
    /// cluster, its host and its secret references included. The request path reads them and
    /// writes none of them, by any verb: no <c>UPDATE</c> in <see cref="AppRolePrivileges"/> names
    /// one, none is table-wide on a table that has one, and no table that has one grants
    /// <c>INSERT</c> — because <c>INSERT</c> writes every column of a new row, and a new row is a
    /// routing decision too. <c>CatalogPrivilegeAllowlistTests</c> holds the record to that, and
    /// <c>CatalogPrivilegeTests</c> holds the database to the record and tries the writes as the
    /// role.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RoutingColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["tenant"] = Set("key", "cluster_id", "database_name", "residency_region"),
            ["tenant_host"] = Set("host", "tenant_id"),
            ["database_cluster"] = Columns["database_cluster"],
        };

    /// <summary>
    /// What <c>aurora_app</c>, the role every request holds, may do to each catalog table: the
    /// grants the migrations issue, table by table, each with the component that needs it
    /// (ADR-0004 rule 2, ADR-0007 §4.4). There is deliberately no default. A table created without
    /// a grant of its own is closed to the role (the catalog sets no
    /// <c>ALTER DEFAULT PRIVILEGES</c>), a table without a row here fails
    /// <c>CatalogPrivilegeTests</c>, and an append-only table — ADR-0004 rule 5; the two in
    /// <see cref="AppendOnlyTables"/> — records <c>SELECT, INSERT</c> and nothing else, which
    /// <c>CatalogPrivilegeAllowlistTests</c> holds the record to. Edited deliberately, in the same
    /// commit as the migration that grants it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each entry is spelled the way <c>CatalogPrivilegeTests</c> reads it back out of the ACL —
    /// <c>pg_class.relacl</c> for a table privilege (<c>SELECT</c>), <c>pg_attribute.attacl</c> for
    /// a column privilege (<c>UPDATE(column)</c>) — and the comparison is entry by entry, both
    /// ways. Nothing enumerates what PostgreSQL can grant: an entry the record does not name is a
    /// difference whatever it is, including a privilege that did not exist when this was written.
    /// </para>
    /// <para>
    /// The decision, table by table. <c>database_cluster</c> is read-only: nothing in ADR-0007 has
    /// the application registering or editing a cluster; it is operator seed data, written as
    /// <c>aurora_migrator</c>. <c>tenant</c> and <c>tenant_host</c> are read-only on the routing
    /// decision: the request path creates neither a tenant nor a host, because <c>INSERT</c> writes
    /// every column of a new row and a new row is a routing decision — a tenant whose
    /// <c>database_name</c> is another tenant's, a host for a tenant the request does not own
    /// (the second security re-review, H-4). On <c>tenant</c> only the lifecycle columns a named
    /// request-path component moves are updatable, never the <see cref="RoutingColumns"/>.
    /// <c>subscription</c> has no writer yet — no backlog row bills anyone — so it is read-only
    /// until one does. <c>installed_package</c> is the package installer's. No table grants
    /// <c>DELETE</c>: §11.4 tombstones a tenant, a subscription closes with <c>valid_to</c>,
    /// <c>DROP DATABASE</c> is <c>aurora_admin</c>'s.
    /// </para>
    /// <para>
    /// What is deliberately not here, and whose it is. The provisioning saga's own catalog writes —
    /// <c>ReserveTenant</c> inserting the tenant (ADR-0007 §8 step 1), <c>RegisterRouting</c>
    /// inserting the host and moving the tenant to <c>Active</c> with its <c>core_schema_version</c>
    /// and <c>activated_at</c> (step 8), the reaper setting <c>ProvisioningFailed</c> — are not the
    /// request path's and are not granted to it. They are issued as the saga's own principal, which
    /// is the architect's decision to name; B-07 grants that principal what it needs in its own
    /// migration. The same holds for the §11.4 offboarding transitions — <c>suspended_at</c>,
    /// <c>deletion_due_at</c>, <c>deleted_at</c>, and tombstoning the routing columns of a deleted
    /// tenant — which have no backlog row yet. Until each is landed the request path holds none of
    /// them.
    /// </para>
    /// <para>
    /// A grant here has a column axis and no row axis. The catalog is shared by design, so
    /// <c>UPDATE(state)</c> reaches every tenant's row, not the one the request was resolved for,
    /// and <c>installed_package</c> is writable for any tenant id. No grant can narrow that; the
    /// caller must, by naming the tenant it acts for and checking it against the resolved scope.
    /// That obligation belongs to the tasks that issue the writes (B-06.2, B-08, B-13), and it is
    /// why a request-path write to the catalog is the exception that needs a named component and
    /// not the rule.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<AppRoleGrant>> AppRolePrivileges =
        new Dictionary<string, IReadOnlyList<AppRoleGrant>>(StringComparer.Ordinal)
        {
            ["database_cluster"] =
            [
                new("SELECT", "ITenantConnectionResolver (B-06.1) and ITenantAdminConnectionFactory (ADR-0027 §2) read the endpoint and the secret references to build a connection string; ReserveTenant (B-07.1) reads region, state and max_tenants to place a tenant"),
            ],
            ["tenant"] =
            [
                new("SELECT", "every request: ITenantConnectionResolver (B-06.1) resolves the tenant's cluster and database; ITenantScopeFactory (B-06.3) and the skew check (B-08.3) read state and core_schema_version"),
                new("UPDATE(state)", "the identity check (B-06.2), the migration runner's quarantine (B-08.2) and the skew check (B-08.3) set SchemaBlocked; the provisioning saga's own transitions are its principal's, not this role's"),
                new("UPDATE(core_schema_version)", "the migration runner after each tenant migrates (B-08.1)"),
                new("UPDATE(last_activity_at)", "the tenant's own scope open, at most once a minute (ADR-0007 §10.1; B-06.3)"),
            ],
            ["tenant_host"] =
            [
                new("SELECT", "every request: resolving the tenant from the request's host (ADR-0007 §3.2 strategy 1); RegisterRouting (B-07.4) inserts a host as the saga's principal, not this role"),
            ],
            ["subscription"] =
            [
                new("SELECT", "entitlement checks: a capability a customer pays for is evaluated from here (ADR-0011 rule 3); no task writes a subscription yet, so nothing else is granted"),
            ],
            ["installed_package"] =
            [
                new("SELECT", "the package loader and every capability lookup (ADR-0008 §4; B-12, B-13.1); the upgrade plan (ADR-0027 §5)"),
                new("INSERT", "the installer's final step (ADR-0008 §5.1 step 9; B-13.2), called from provisioning saga step 7 (B-07.4)"),
                new("UPDATE", "the installer moving state from Installing to Active or Failed (B-13.2); upgrade, deactivate and purge (ADR-0008 §4.2, §5.3; B-13.3)"),
            ],
            ["operator_audit_event"] =
            [
                new("SELECT", "the operator console reading a tenant's platform-side trail, and surfacing a support-access grant to the tenant it was granted on (ADR-0010 rule 8); the platform-side half of SPEC-001 AC-6 read back (B-07.4)"),
                new("INSERT", "the provisioning saga recording a failed or retried attempt (B-07.1) and the successful platform-side event (B-07.4; SPEC-001 BR-7); ADR-0010 rule 8's time-boxed, reason-coded support-access grant; ADR-0018 §1's daily job recording each tenant's audit chain head (FOLLOWUP-031). Never UPDATE or DELETE: ADR-0004 rule 5, enforced by the ACL and by the guard trigger in AppendOnlyTrails"),
            ],
            ["erasure_replay_log"] =
            [
                new("SELECT", "restore-then-replay: re-applying every recorded erasure to a restored tenant database (ADR-0007 §11.5; ADR-0018 §6 point 5)"),
                new("INSERT", "the erasure path recording an executed erasure - subject reference, tenant, requester, fields affected, never the erased values (ADR-0007 §11.5; ADR-0018 §6 points 5 and 6). Never UPDATE or DELETE: ADR-0004 rule 5, enforced by the ACL and by the guard trigger in AppendOnlyTrails"),
            ],
            ["__EFMigrationsHistory"] = [],
        };

    /// <summary>
    /// The kinds of object in <c>catalog</c> besides tables that carry an ACL of their own, and
    /// that the oracle therefore reads: the schema (<c>pg_namespace.nspacl</c>), sequences
    /// (<c>pg_class.relacl</c>), functions and procedures (<c>pg_proc.proacl</c>) and types
    /// (<c>pg_type.typacl</c>). Spelled the way <see cref="CatalogObject.Kind"/> spells them.
    /// </summary>
    public static readonly IReadOnlySet<string> ObjectKinds = Set(CatalogObject.Schema, "sequence", "function", "procedure", "type");

    /// <summary>
    /// What <c>aurora_app</c> may do to every object in <c>catalog</c> that is not a table, judged
    /// by the same oracle as <see cref="AppRolePrivileges"/> and subject to the same rule: an object
    /// without a row here fails <c>CatalogPrivilegeTests</c>, an entry a row does not name is a
    /// difference. Today that is the schema itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this record exists at all: PostgreSQL's default for a function is the inverse of its
    /// default for a table. A new table is closed to everyone but its owner; a new function is
    /// <c>EXECUTE</c> to <c>PUBLIC</c>, with no <c>GRANT</c> statement for a reviewer to notice, and
    /// a <c>SECURITY DEFINER</c> body runs as the schema owner — so one function in <c>catalog</c>
    /// handed the request path the column-level <c>UPDATE</c> it does not hold, while an oracle that
    /// read tables only reported a perfect match (the second security re-review, H-5). A type's
    /// default is <c>USAGE</c> to <c>PUBLIC</c> the same way. The migration closes the function
    /// default for everything <c>aurora_migrator</c> creates (<c>ALTER DEFAULT PRIVILEGES … REVOKE
    /// EXECUTE ON FUNCTIONS FROM PUBLIC</c>, the one default the catalog sets, and it revokes); the
    /// oracle reads a <c>NULL</c> ACL as the owner default it means, so a function created by any
    /// other role reads as <c>EXECUTE through PUBLIC</c> and fails.
    /// </para>
    /// <para>
    /// So a function a migration adds is recorded here as <c>[]</c> when it stays closed, which a
    /// trigger function can: PostgreSQL checks <c>EXECUTE</c> when the trigger is created, not
    /// when it fires. ADR-0028 §2 mechanism 3's guard that raises on the append-only tables,
    /// <c>refuse_append_only_change()</c>, is the first: it fires for <c>aurora_app</c> and the
    /// owner alike while <c>aurora_app</c> cannot call it. A function the request path is meant
    /// to call is recorded as <c>EXECUTE</c> with the caller named, and the migration grants it to
    /// <c>aurora_app</c> by name. A type is recorded once its migration has revoked
    /// <c>PUBLIC</c>'s <c>USAGE</c> and granted the role's.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<CatalogObject, IReadOnlyList<AppRoleGrant>> AppRoleObjectPrivileges =
        new Dictionary<CatalogObject, IReadOnlyList<AppRoleGrant>>
        {
            [new(CatalogObject.Schema, "catalog")] =
            [
                new("USAGE", "every request: the schema every catalog table lives in (ADR-0007 §9.1); nothing on the request path creates in it"),
            ],
            [new("function", "refuse_append_only_change()")] = [],
        };

    /// <summary>
    /// <see cref="AppRolePrivileges"/> and <see cref="AppRoleObjectPrivileges"/> as one map, keyed
    /// the way the oracle keys what it reads from the database.
    /// </summary>
    public static readonly IReadOnlyDictionary<CatalogObject, IReadOnlyList<AppRoleGrant>> AppRoleDecisions =
        AppRolePrivileges.Select(table => (Object: CatalogObject.TableNamed(table.Key), Grants: table.Value))
            .Concat(AppRoleObjectPrivileges.Select(other => (Object: other.Key, Grants: other.Value)))
            .ToDictionary(decision => decision.Object, decision => decision.Grants);

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
