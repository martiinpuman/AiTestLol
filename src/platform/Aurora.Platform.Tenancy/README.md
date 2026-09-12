# `Aurora.Platform.Tenancy`

The tenancy platform module: the **catalog database** and, from B-06 on, everything that turns a
request into a connection to one tenant's database.

Contracts: [`Aurora.Platform.Tenancy.Contracts`](../Aurora.Platform.Tenancy.Contracts) — the
identifiers and states other modules may name.
Decision of record: [`ADR-0007`](../../../docs/decisions/ADR-0007-multi-tenancy-database-per-tenant.md).

---

## What is here after B-05

The catalog database only: schema `catalog`, five entity types, their EF configurations, the
`InitialCatalog` migration and `AddCatalogDatabase()` for the composition root.

| Table | Holds | Source |
|---|---|---|
| `catalog.database_cluster` | One PostgreSQL cluster tenants are placed on: endpoint, maintenance database, the three role secret *references*, placement cap, state | ADR-0007 §3.5, §6, §9.2 |
| `catalog.tenant` | The registry row every request reads: identity, key, routing, lifecycle, dates | §3.1, §9.2, §11.4 |
| `catalog.tenant_host` | Host names that resolve to a tenant, one primary each | §3.2 strategy 1 |
| `catalog.subscription` | What a tenant is entitled to, over a half-open span of days | §9.2 |
| `catalog.installed_package` | Which Country Packages a tenant has, and in what state | §9.2, ADR-0008 §4 |

**After B-19**, the catalog's two append-only trails — created by raw SQL in the
`AppendOnlyTrails` migration, mapped by no entity, written by rows that have not landed yet:

| Table | Holds | Source |
|---|---|---|
| `catalog.operator_audit_event` | The platform side of the tenant boundary: a provisioning attempt that failed or was retried and the successful platform-side event, a time-boxed, reason-coded support-access grant, the daily record of each tenant's audit chain head — append-only | ADR-0007 §9.2, ADR-0010 rule 8, ADR-0018 §1, ADR-0028 §6, SPEC-001 BR-7 |
| `catalog.erasure_replay_log` | One row per erasure executed in a tenant — subject reference, requester, when, which fields, never the erased values — re-applied after any restore; append-only | ADR-0007 §11.5, ADR-0018 §6 |

### Which §9.2 tables are deliberately *not* here yet

ADR-0007 §9.2 lists the catalog's full eventual contents. B-05 lands the registry and routing core;
every remaining table is named by a later `solution-layout.md` §6 row and is that task's to create,
so that the migration that adds it also adds the code that uses it:

| Table | Owner |
|---|---|
| `catalog.tenant_provisioning`, `catalog.tenant_provisioning_step` | B-07 (provisioning saga, §8) |
| `catalog.migration_run`, `catalog.migration_run_tenant` | B-08 (migration runner, §7.3) |
| `catalog.identity_user`, `catalog.user_tenant_membership` | Identity module (ADR-0009) |
| `catalog.outbox` | Messaging platform module (ADR-0015) |
| `catalog.feature_flag`, `catalog.feature_flag_tenant_override` | Configuration platform module (ADR-0011) |
| `quartz.*` | Jobs platform module (ADR-0014) — its own schema, not `catalog` |

Whoever adds one must, in the same commit, add its columns to `CatalogSchemaAllowlist.Columns` —
`AppendOnlyColumns` for a table created by raw SQL that no entity maps — (or the ADR-0007 §9.3
guard fails) and record what `aurora_app` may do to it in
`CatalogSchemaAllowlist.AppRolePrivileges` — each grant with the task, saga step or module that
issues it — granting exactly that in the migration that creates it. Nothing arrives by default: the
catalog sets no default privilege that grants — its one `ALTER DEFAULT PRIVILEGES` revokes, closing
every function `aurora_migrator` creates to `PUBLIC`, because PostgreSQL's default for a function is
the inverse of its default for a table — so a forgotten grant is a `42501` at first use and a
forgotten record fails `CatalogPrivilegeTests`, which compares the record with the ACL PostgreSQL
actually holds on every object in the schema — the schema itself (`pg_namespace.nspacl`), tables and
sequences (`pg_class.relacl`), columns (`pg_attribute.attacl`), functions and procedures
(`pg_proc.proacl`), types (`pg_type.typacl`), a `NULL` ACL read as the owner default it means — for
the role, for `PUBLIC` and for every role it is a member of by any route, entry by entry, both ways.
A column grant reads back as `UPDATE(column)`; a grant option, a grant that reaches the role through
another role (inherited, or one `SET ROLE` away), a function left at PostgreSQL's `EXECUTE TO PUBLIC`,
and a privilege this code had never heard of (PostgreSQL 17's `MAINTAIN` was the one the security
review used) are reported as themselves rather than missed. A function, sequence or type is recorded
in `CatalogSchemaAllowlist.AppRoleObjectPrivileges` the way a table is in `AppRolePrivileges` — `[]`
for one the role may not touch, which is what ADR-0028 §2's guard function
`refuse_append_only_change()` is: a trigger fires for `aurora_app` without `aurora_app` holding
`EXECUTE` on it. An append-only table — `operator_audit_event`, `erasure_replay_log` — records
`SELECT, INSERT` and nothing else (ADR-0004 rule 5), and `CatalogPrivilegeAllowlistTests` holds the
record to that. A partitioned table records what the role holds on every partition of its tree in
`CatalogSchemaAllowlist.PartitionPrivileges`, keyed by the root — **empty**, the catalog's shape,
because a partition in a schema with no default privilege has no ACL and is reached through its
parent and never directly; the entry must be present and empty, or the oracle reports every
partition of the tree as undecided (none exists yet; `catalog.authentication_event`, B-18.9, is
the first). A write privilege with no component to name is not granted. That is the intended
friction: the allowlist diffs are where a reviewer sees a new catalog object arrive, and what the
request path may do to it.

---

## What a reader will want justified

### `CatalogDbContext` is `internal`

ADR-0007 §4.2 carves the catalog out of the tenant-context rules — it is the one context registered
conventionally, because it is where tenants are resolved *from* and demanding a `TenantScope` to
reach it would be circular. That carve-out is about how the context is **constructed**, not about
who may name the type.

`AddCatalogDatabase` lives in this assembly, so `AddDbContext<CatalogDbContext>` compiles with the
type internal and the conventional registration costs nothing. Keeping it internal is what turns
§9.4's *"no request-path query fans out across tenants"* into a property of the type system: a
module cannot write a query over the tenant registry at all, and `modules.md` §4 keeps connection
strings inside the resolver. The two test assemblies see it through `InternalsVisibleTo`;
`CatalogContextAccessibilityTests` fails if the assembly ever exports anything but the DI extension.

### The catalog stores secret *references*, never secrets

`catalog.database_cluster` holds `admin_secret_ref`, `migrator_secret_ref` and `app_secret_ref` —
pointers into the host's secret store (ADR-0011 tier 2), never credentials. The catalog is backed
up, dumped for support and read by every request; a password in it would be in all three places.
The resolver (B-06.1) composes `Host=…;Username=aurora_app;Password=<from the store>` in memory at
request time and stores nothing. Role *names* are not stored either — `aurora_admin`,
`aurora_migrator` and `aurora_app` are the same on every cluster (ADR-0004 rule 2).

`SecretReference` refuses the shapes a credential arrives in by mistake (`=`, `;`, whitespace,
control characters), and `ck_database_cluster_secret_refs_are_references` repeats the check in the
database. It is a tripwire, not a guarantee: a bare password with none of those characters would
pass. What actually holds is that nothing here ever reads a secret's *value* out of the catalog.

`migrator_secret_ref` is not in ADR-0007 §9.2's column list even though §7.3 has the migration
runner connect as `aurora_migrator`. Treated as an ADR omission and added; raised in the B-05
summary.

### `catalog.tenant.state` declares the whole lifecycle from the first migration

All eight states of ADR-0007 §9.2 ship in `InitialCatalog`, not the ones B-05 uses. Three later
tasks stamp states this migration must already admit — `SchemaBlocked` (B-06.2 identity check §4.3,
B-08 quarantine §7.4) and `ProvisioningFailed` (B-07, the only state an operator may destroy a
database from, §8) — and widening a check constraint under live tenants is exactly what the
expand/contract rule exists to avoid.

The constraint's allowed values are generated from the `TenantState` enum's names
(`StateNames.CheckConstraintSql`), so the two cannot drift: adding a state means scaffolding a
migration, which is the visible event it should be. `CatalogTenantStateTests` stores and reads back
each of the eight and asserts four plausible near-misses are refused.

`Tenant` itself exposes only `Reserve` and `Activate` — the two ends of the provisioning saga. The
methods that reach the other six states arrive with the tasks that drive them, guarded the same way.

### The two append-only trails are append-only by mechanism, and the guard is asserted by its effect

`catalog.operator_audit_event` and `catalog.erasure_replay_log` (B-19; `solution-layout.md` §6.4
item 5; ADR-0028 §2 as amended) are created by raw SQL in `AppendOnlyTrails` — the guard they carry
is nothing the EF model can express — so nothing about them is asserted against the model: the
columns are read from `information_schema`, the grants from the ACL, and the writes are tried as
the role (`CatalogAppendOnlyTests`, `CatalogAppendOnlyGuardTests`, `CatalogPrivilegeTests`). Three
mechanisms:

1. **The grant.** `aurora_app` holds exactly `SELECT, INSERT` on each, recorded with its writers —
   the provisioning saga (B-07.1, B-07.4), ADR-0010 rule 8's support-access grant, ADR-0018 §1's
   chain-head job, the erasure path. As the role, an `INSERT` succeeds and `UPDATE`/`DELETE` are
   `42501` from the privilege check, before a row is looked at.
2. **The guards.** On each table a `BEFORE UPDATE OR DELETE … FOR EACH ROW` trigger and a
   `BEFORE TRUNCATE … FOR EACH STATEMENT` trigger, both `ENABLE ALWAYS`, raising `42501` with the
   guard's own message, so the owner — whom privileges do not restrain — is refused too. The
   truncate guard is on these unpartitioned tables deliberately: §6.4 item 5's "does not arise"
   is about cloning, and the `TRUNCATE` statement does not care whether a table is partitioned —
   without it the owner emptied either trail in one statement. What is asserted is that they
   *refuse*, never that they exist: as `aurora_migrator`, inside a transaction that is rolled
   back, an `UPDATE` and a `DELETE` of a seeded row on each table and a `TRUNCATE` of it must
   return `42501` and the guard's message, and the probe reports
   `relations probed / statements refused / silent` by `table/VERB`, floored at the two tables the
   record names. Beside it, a **binding check** follows the chain from what it asserts to what
   must be true, one catalog per link, and compares each link with what the migration wrote: the
   ACL decides who may issue a statement (the oracle above); `pg_trigger` decides which guard
   fires, when, for which rows and with what — `tgtype` exactly, `tgenabled 'A'`, `tgfoid`
   resolved to `catalog.refuse_append_only_change` *by schema*, `tgqual` null, `tgattr` empty,
   `tgnargs` zero; `pg_rewrite` can discard or redirect the statement before any guard fires — no
   rule; `pg_class.relrowsecurity` and `pg_policy` decide which rows it reaches — off, and none;
   and `pg_proc` holds what the guard the trigger names *actually does* — `pg_get_functiondef`'s
   rendering, body, language, security and configuration in one string, equal to a literal the
   test holds on its own, deliberately not read from the migration, because the bypass was proved
   by committing three lines into the migration's source. Each link is a way to make the table
   behave for the probe's session
   and not for an attacker's, and each was found by a review of this branch with every test green
   because the check had stopped one link short: a
   `WHEN (current_setting('aurora.maintenance', true) IS DISTINCT FROM 'on')` clause fires for
   every session that has not set that GUC, the probe included (first review, critical — the
   enumeration read `tgtype`, `tgenabled` and a schema-less `proname`); a rule `ON INSERT … WHERE
   current_setting(…) = 'on' DO INSTEAD NOTHING` discards an armed session's inserts with nothing
   refused (second review, high — the check was complete over `pg_trigger`, which is not the whole
   mechanism); and the body re-written to return for an armed session and raise, with the
   migration's own message, for every other (third review, blocker — the check resolved the
   function's name and stopped there). A fault theory runs fifteen tamper shapes — the function
   body hollowed two ways (`RETURN COALESCE(NEW, OLD)`, `RETURN NULL`), each guard disabled and
   dropped, the row guard re-created with a `WHEN` clause, the truncate guard re-created with one
   (PostgreSQL 17.11 accepts it on a statement trigger *and evaluates it* — executed: with the GUC
   set the `TRUNCATE` lands and the table reads back empty, so `tgqual` is load-bearing on both
   guards), the row guard re-created for `UPDATE OF` one column, the row guard rebound to a
   hollow `shadow_b19.refuse_append_only_change()`, the truncate guard passed an argument its
   function ignores, the discarding rule, the concealing policy, and the conditional body twice —
   bypassing `DELETE` on one table and `TRUNCATE` on the other with the same statement, because
   one function serves both tables and all three verbs — and for each states what the probe sees
   and what the binding check sees, executes the bypass with the GUC set for the shapes only the
   binding check sees and proves it by a `SELECT`, and requires that at least one side reports
   every shape. All rolled back. Under `session_replication_role = 'replica'`, which suppresses an
   ordinary trigger with `pg_trigger` unchanged, the `ALWAYS` guards still refuse. **None of these
   integration tests is in `verify.sh`'s merge gate today:** stage 6 filters `Category!=Integration`
   and stage 8 is pending (owned by B-11), so a migration that re-opened any of these doors would
   fail `dev-test.sh` on the integration project and still pass the gate.
3. **No default privilege for the schema.** `pg_default_acl` holds zero rows scoped to `catalog`. A
   copied `ALTER DEFAULT PRIVILEGES … IN SCHEMA catalog GRANT` takes the count to one and is caught;
   the `REVOKE` form writes no row and is not — survivable, because mechanism 1 asserts the
   resulting ACL of every catalog relation and never which statement produced it.

**What this does not cover, stated rather than implied.** `aurora_migrator`, as the DDL-path role,
can disable, drop, re-create or rebind either guard, replace the function's body, or attach a rule
or a policy, and then rewrite, remove, empty, silently discard writes to, or conceal a trail. Of
those acts, the effect probe sees the ones that let *its own* statements through — a hollowed body,
a disabled or dropped guard, a guard rebound to a function that does not raise; the binding check
sees the ones that change any link of the chain — a guard disabled, dropped, rebound, argued, or
made conditional by a `WHEN` clause or a column list; a rule; row-level security; a policy; and the
function's definition, which is where a body made conditional on the session lives — the shapes
the probe cannot see, because such a table behaves for the probe and not for the attacker. The harm
of the silent shapes is specific: an ADR-0010 rule 8 support-access grant never recorded, an
ADR-0007 §11.5 erasure never replayed after a restore, a trail that looks intact because nothing
was ever refused, only discarded. `aurora_app` can *arm* one (`SET aurora.maintenance = 'on'` is
any role's to run) but cannot plant one; planting is DDL. Until the check read `tgqual` and
`tgattr`, then `pg_rewrite` and `pg_policy`, then `pg_get_functiondef`, each of those paths was
invisible to both sides and shipped through a one-file migration diff with every test green — the
link stopped one column short, then one catalog short, then at the function's name. What
`pg_get_functiondef` cannot see: the definition is text, and what an identifier in it resolves to
at execution is the session's `search_path`, which this definition does not pin; the body's one
call is `format()`, and a shadowed `format()` cannot stop the `RAISE`, only change the message,
which the probe compares exactly. What neither sees, and nothing here detects: a transient tamper
between two runs — plant, act, remove — and a shape nobody enumerated, which is the general limit
of a fault theory; a `CHECK` constraint keyed on a GUC is the one shape that announces itself (an
armed insert fails loudly) and is not read. That is the detection-not-prevention boundary ADR-0028
§2 draws; its cover is a chain head recorded outside the tenant database — in
`operator_audit_event`, by `FOLLOWUP-031`'s job — carried as `FOLLOWUP-026`. Neither table is
partitioned, so Amendment 2's partition clause (a guard created on every partition) does not arise
here. And the writers, not this module, keep personal data and erased values out of `detail` and
`affected` (ADR-0018 §4, §6 point 6): a column cannot enforce that, and no test here claims to.

---

## What "tenant isolation" means for a shared database

The Definition of Done asks every task that adds data access for tenant isolation tests. The catalog
is the one store shared by design, so there is no tenant A row for a tenant B request to be kept out
of, and a test that partitioned it would prove nothing. What is real here is the blast radius of the
role the request path holds (`CatalogPrivilegeTests`, asserted by trying, as the role):

- **Downwards** — `aurora_app` has no DDL and cannot touch `__EFMigrationsHistory`, so it cannot
  alter the schema. But the routing decision does not live in the schema; it lives in rows — which
  tenant a host resolves to, which cluster and database a tenant resolves to, which host a cluster
  is — so what matters is which rows the role can write. It can read every registry table. It can
  move a tenant's `state`, `core_schema_version` and `last_activity_at` (B-06.2, B-06.3, B-08), and
  insert and update `installed_package` (the installer, B-13.2). It cannot create a tenant or a
  host: `INSERT` writes every column of a new row, and a new row is a routing decision — with the
  `INSERT` the first rework kept, the second security re-review created a tenant of its own that
  resolved to another tenant's database, and a self-verified host for a tenant it did not own — so
  `ReserveTenant` and `RegisterRouting` (B-07.1, B-07.4) run as the provisioning saga's own
  principal, not this role. It cannot write `database_cluster` at all; cannot update the columns a
  tenant or a host resolves by (`tenant.key`, `cluster_id`, `database_name`, `residency_region`;
  `tenant_host.host`, `tenant_id`); cannot delete or truncate any catalog row, because nothing on
  the request path deletes one — §11.4 tombstones a tenant, a subscription closes with `valid_to`,
  `DROP DATABASE` is `aurora_admin`'s; can add to the two append-only trails and never rewrite or
  remove a row of either, by grant and by a guard that refuses the owner too; and cannot call a
  function in `catalog` that no migration opened to it by name. Those limits are what stop a
  request holding the role from rebinding another
  tenant's hostname, sending one tenant's requests at another tenant's database, creating a routing
  row of its own, or repointing a cluster's `host` so the resolver dials an attacker carrying the
  real cluster credentials — the writes two security re-reviews made against earlier grants, and
  the eleven writes `The_app_role_cannot_repoint_where_a_tenant_or_a_host_resolves` now makes as
  the role, against the real migration, expecting `42501` for each. Belt and braces on the first
  of them: `ux_tenant_cluster_id_database_name` makes one database on one cluster one tenant's,
  whoever inserts. Every grant is recorded with the component that needs it in
  `CatalogSchemaAllowlist.AppRolePrivileges` (tables) and `AppRoleObjectPrivileges` (the schema,
  and any function, sequence or type that arrives); the record is held to its own rules in stage 6
  (`CatalogPrivilegeAllowlistTests`: no `DELETE`, no write on `database_cluster`, no `INSERT` on a
  table with routing columns and no `UPDATE` on a routing column, `USAGE` and nothing else on the
  schema, a named writer on every grant); and the database is held to the record by
  `CatalogPrivilegeTests`. A grant has a column axis and no row axis: `UPDATE(state)` reaches every
  tenant's row, and `installed_package` is writable for any tenant id. No grant can narrow that; a
  component that writes the catalog must name the tenant it acts for and check it against the
  resolved scope, which is B-06.2's, B-08's and B-13's obligation and the reason a request-path
  write to the catalog is the exception that needs a named component.
- **Sideways** — on the test fixture's cluster, `aurora_app` can open the catalog and no other
  database that accepts connections (`template1` and the maintenance database included, enumerated
  from `pg_database` rather than named). **That is a property of the databases, not of the role.**
  ADR-0007 §4.4's "`CONNECT` on exactly one database" holds *under §3.5 stage 2*, which is not what
  ships. Stage 1 — what ships — is one `aurora_app` login role per cluster with `GRANT CONNECT` on
  every tenant database, and PostgreSQL grants `CONNECT` on every new database to `PUBLIC`. What
  keeps the role out of a database today is §8 step 3 run on that database — `REVOKE ALL ON DATABASE
  … FROM PUBLIC`, then an explicit `GRANT CONNECT` — and
  `A_newly_created_database_is_open_to_the_app_role_until_section_8_step_3_hardens_it` shows the
  before and after on a database created the way the provisioner will create one. What catches a
  request that reaches the *wrong tenant's* database — which the role, by design, can — is §4.3's
  connected-database identity check, which B-06.2 builds and proves with a deliberate mis-route
  test; nothing in this task tests it. Be precise about what that check catches: a *mis-routed*
  request — a catalog bug, a bad restore, a hand-renamed database — because the initializer runs on
  every physical connection the platform opens. It does not catch an attacker who holds
  `aurora_app`'s credential and opens a connection of their own, never running the initializer:
  under stage 1 that one credential reaches every tenant database on the cluster, and their names
  are readable from `pg_database` from inside the catalog. That is the accepted stage-1 risk ADR-0007
  §3.5 states plainly, owned by `FOLLOWUP-001` (one `aurora_app_<tenantKey>` role per tenant
  database, before the first paying customer).

### What the request path does not write, and who decides

Clusters are operator seed data, written as `aurora_migrator`. Tenants and hosts are created, and a
tenant activated, by the provisioning saga (ADR-0007 §8 steps 1 and 8) — as a principal of its own,
never as `aurora_app`, because the request path may not create a routing decision. Which principal
that is — a fourth catalog role, or `aurora_admin`, which already holds the saga's `CREATE DATABASE`
— is the architect's decision, narrowed by the second security re-review to exactly that question;
B-07 grants it what it needs in a migration that names it. Subscriptions have no writer at all yet —
no backlog row bills anyone. The §11.4 offboarding transitions (`suspended_at`, `deletion_due_at`,
`deleted_at`, tombstoning a deleted tenant's routing columns) have no backlog row either and belong
with the same decision. Until each is landed the request path holds none of them, and the task that
lands one grants what it needs in its own migration, naming itself in the record.

### What B-06.2 and B-07 must add

Under stage 1 the role does not bound the blast radius; the identity check does. B-06.2 owes the
deliberate mis-route test that proves §4.3 — its backlog row says so, and it lands before B-07. B-07,
with B-10's `Aurora.TestKit`, then owes:

1. The six §12.2 tests over two real tenant databases provisioned by the real saga. **Under §3.5
   stage 1 those tests and B-06.2's mis-route are *the* cross-tenant control**, because `aurora_app`
   legitimately holds `CONNECT` on every tenant database.
2. §8 step 3 asserted by its effect, not by reading the statements back: an ungranted role attempting
   to connect and being refused — the lesson this task's own fixture taught, when a `REVOKE` issued
   by a non-owner silently changed nothing — plus `DROP SCHEMA public` and `btree_gist`.
3. The `pg_database` enumeration `CatalogPrivilegeTests` runs today, kept running once tenant
   databases exist: the app role opens the catalog and the tenant databases it was granted, and
   nothing else on the cluster.
4. When §3.5 stage 2 lands — one `aurora_app_<tenantKey>` role per tenant database — the "`CONNECT`
   on exactly one database" test that §4.4 will then justify.

---

## What B-06.1 adds: the connection resolver

`ITenantConnectionResolver` (ADR-0007 §3.5) — **the only code that builds a tenant connection
string** — with its routing cache and the §5.2 pool settings. All of it in `Routing/` and
`Secrets/`, all of it internal: the assembly exports its two DI extensions and the one enum a host
passes to them (`TenantPoolProfile`), and `CatalogContextAccessibilityTests` holds it to that.

| Piece | Does | Proven by |
|---|---|---|
| `CatalogTenantRoutingReader` | The one catalog read: `catalog.tenant` left-joined to `catalog.database_cluster`, one statement, as whichever role the context holds (`aurora_app` on the request path). Reads the admin and migrator secret references alongside the app one, so `ITenantAdminConnectionFactory` (B-07.1, ADR-0027 §2) reuses this read instead of writing a second query | `The_reader_joins_the_tenant_to_its_cluster_in_one_statement_as_the_app_role` counts the statements sent and reads both table names out of the SQL |
| `TenantRoutingCache` | The 60 s `HybridCache` entry under `c:tenant-routing:{tenantId}` (`CatalogCacheKey`, ADR-0012 rule 2; a discriminator is `[A-Za-z0-9._-]+`, refused otherwise). An unknown tenant throws and is never cached. **The row returned is the tenant's or nothing is:** a row whose `TenantId` is not the one asked for is refused at the read-through (never stored) and again when handed back from the cache (dropped), with `TenantRoutingMismatchException` — PR #14 M-1, where the reviewer composed a credentialed connection to another tenant's database from a row nothing compared | Unit tests count reads through a counting reader behind a real `HybridCache`: 1, then 1 (hit), then 2 after removal; +59 s hit and +61 s miss with a `FakeTimeProvider` driving both clocks the cache reads. A reader answering another tenant's row: refused twice, read twice, no credential fetched; a poisoned entry planted under the victim's key: refused, dropped, re-read once |
| `TenantRoutingCacheInvalidator` | A `SaveChangesInterceptor` on every `CatalogDbContext` the container hands out (`AddCatalogDatabase`): a tenant whose `state`, `key`, `cluster_id`, `database_name` or `residency_region` is saved has its entry removed after the save; a failed save removes nothing, and the next save on the same context replaces what was remembered, so a failed attempt's tenants are never evicted later either; an activity stamp alone removes nothing. **The watched list is held complete** (PR #14 M-3): every column of `catalog.tenant` is classified as routing, the state gate or outside routing (`TenantColumnClassification`, next to the tests), the three partition `Columns["tenant"]` with a count, and every fact `TenantRouting` carries names the watched column it is read from, each proven to change the resolve | **The row's non-negotiable proof**, against PostgreSQL through the production DI wiring on both sides: statements 1 → 1 (hit) → suspend through `Tenant.Suspend` and a real `SaveChangesAsync` → 2 (miss), and the cached row then reads `Suspended`. Sever the interceptor's selection, or its wiring, and the third count stays at 1 — both were done before the tests were committed. Remove `key` from both lists in lockstep, the reviewer's fault, and three tests fail where 218 once passed |
| `TenantConnectionResolver` | Cache → state check (`Active` and `Suspended` route; every other state is refused naming the state) → credential from the secret store on every resolve → `TenantConnectionStringComposer` → a `ConnectionSecret`, which reveals the string through `Reveal()` and renders `<redacted>` on every other path: `ToString()`, interpolation, the record's own printing, `System.Text.Json` and any reflection-based sink, which finds no public property to read (PR #14 M-2) | `TenantConnectionResolverTests` (unit), the integration test that opens the resolved string and has the server report `current_database()`, `current_user` and `application_name`, and six rendering paths examined for the credential, none carrying it |
| `TenantPoolSettings` / `TenantPoolProfile` | The §5.2 table: Minimum Pool Size 0; Maximum 10 (Web) / 5 (Worker); Connection Idle Lifetime 30 s; Pruning Interval 5 s; Max Auto Prepare 0; Timeout 5 s; Command Timeout 30 s (Web) / 300 s (Worker); Application Name `aurora-web:{tenantKey}` / `aurora-worker:{tenantKey}` | `TenantPoolSettingsTests` parses the composed string back with `NpgsqlConnectionStringBuilder` and asserts all eight from there, counting them; the seven that differ from Npgsql's default are pinned against a fresh builder, so a composer that set nothing cannot pass on the two that equal it |
| `ISecretStore` / `EnvironmentSecretStore` | ADR-0011 tier 2: `env:NAME` is the environment variable `NAME`. The catalog holds the reference; the value enters the connection string in memory and nothing else. A vault or mounted-file store is a second implementation chosen in the composition root | `EnvironmentSecretStoreTests` |

Three things a reader will want stated:

- **What is cached is the routing row with the secret's reference, never the credential.** The
  store is consulted on every resolve, so a rotated secret takes effect on the next resolve with no
  catalog read (ADR-0011: rotation is an overlap window), and no password can reach an L2 backend
  when one arrives. `TenantConnection.ToString()` redacts the connection string (ADR-0016).
- **Which states route.** `Active` and `Suspended`: §11.4 keeps a suspended tenant readable behind a
  banner, so the application still connects and the read-only restriction is enforced elsewhere.
  `Provisioning`, `ProvisioningFailed`, `SchemaBlocked`, `PendingDeletion` and `Deleted` are refused
  for the reasons §7.4, §7.5, §4.3 and §11.4 give; `Exporting` is refused on the fail-closed reading
  until the export job (no backlog row yet) says what it needs. The DDL path never comes through the
  resolver (ADR-0027 §2); it reads through the same `ITenantRoutingReader` and applies its own rules.
- **What is not covered on this branch, stated plainly.** ADR-0007 §4.3's connected-database identity
  check — `TenantIdentityStamp`, `platform.tenant_identity`, `TenantRoutingViolationException` — is
  absent from this branch's tree. It exists as a type on `task/B-06.1a` and is wired into a
  connection nowhere until B-06.2 puts `AssertAsync` in the data source's physical-connection
  initializer. Until both land, a connection string this resolver composes is opened with no proof
  that the database on the other end is the tenant's. The catalog's own half of the routing
  question — that two cluster rows cannot name one endpoint, PR #14 H-1 — is closed by B-20
  (below): `ux_database_cluster_host_port`, ADR-0034 §3.3's property in
  `CatalogRoutingUniquenessTests` in place of this file's inertness guard, and one cluster row per
  container in the test bed. The M-1 check above never caught that shape (the row genuinely is the
  attacker's); the index does, and the stamp is what decides identity where a name cannot.
- **What invalidation promises, and what it does not.** The interceptor sees every state change
  written through the entity; a change written around the change tracker (raw SQL, `ExecuteUpdate`)
  is bounded by the entry's 60 s lifetime, as is another instance's copy until an L2 exists
  (ADR-0012). A read already in flight when the entry is removed can store the row it read a moment
  earlier — cache-aside has no version to compare — so the guarantee is "the next resolve after a
  committed state change re-reads the catalog", with the same 60 s bound in the racing case.

`Tenant` gains `Suspend` (ADR-0007 §11.4, the first offboarding step; only an active tenant, only a
UTC instant) — the state change the proof above drives. `suspended_at` is not a column `aurora_app`
may write, so the suspend runs as the owner in the tests and the request path's privilege record is
unchanged.

`TenantRoutingMismatchException` is branch-local and temporary: it is the catalog-side sibling of
`task/B-06.1a`'s `TenantRoutingViolationException` (§4.3, database-side, in `.Contracts`), carries the
same facts, and folds into `TenantRoutingViolationException.Mismatch(...)` when that branch merges;
the orchestrator owns the fold. `AddCatalogDatabase` refuses a second call, as
`AddTenantConnectionResolver` always did (PR #14 L-4); `EnvironmentSecretStore` tells a variable that
is set but empty from one that is not set (L-3); `Forget`, which no observable behaviour depended on,
is gone, and the property it was meant to protect is proven instead (L-1).

Where the interface lives: ADR-0007 §3.5 placed `ITenantConnectionResolver` and `TenantConnection`
in `Aurora.Platform.Tenancy.Contracts`; **ADR-0034 §6 decides they are `internal` to this assembly**
— the ADR moves, the code stays — because `TenantConnection` carries a credential and a type in a
`.Contracts` assembly is nameable by every module in the solution, whereas `internal` upholds
§3.5's own goal by construction. Every consumer named so far (B-06.2, B-06.3, B-07.1) lives here;
the first consumer outside, `Aurora.TestKit`'s counting resolver (B-18.5, consumed by B-10), gets
`[InternalsVisibleTo]` under ADR-0034 §6.1's three conditions, never promotion.

---

## What B-20 adds: the catalog constrains the physical endpoint

`ux_database_cluster_host_port` — `CREATE UNIQUE INDEX … ON catalog.database_cluster (host, port)`,
the `ClusterEndpointUniqueness` migration, declared on the EF model — is ADR-0034 §3.1, and it
closes the **third** executed variant of the tenant-takeover finding: two cluster rows on one
server, one tenant each, the attacker's `database_name` copied from the victim's, both resolving to
one connection string. With it the composition is a proof: `cluster_id → (host, port)` is injective,
`fk_tenant_cluster_in_region` makes it total for every non-deleted tenant,
`ux_tenant_cluster_id_database_name` makes `(cluster_id, database_name)` unique, so
`(host, port, database_name)` — the triple the resolver composes — is unique across `catalog.tenant`.
Beside it, in the same migration, `ck_database_cluster_host_lower_case` (`host = lower(host)`;
ADR-0036 §3, added to ADR-0034 as §3.4) holds the host to its canonical spelling as `tenant_host`
already was: the index is byte-exact, so it can only make `cluster_id → (host, port)` injective on
the endpoint if one endpoint has one spelling. And the host is *one host*: `CanonicalHost` states what
a `database_cluster.host` is — RFC 1123 labels of lower-case ASCII letters, digits and inner
hyphens, 1 to 63 each, joined by single dots, 253 at most, of which an IPv4 literal is a special
case — and `ck_database_cluster_host_well_formed` evaluates the same grammar in the database, so
raw SQL meets it as `DatabaseCluster.Register` does (ADR-0036 §4.2 chose the constraint as the
mechanism; the entity's copy binds only callers of `Register`, of which there is none in production
yet). A grammar, not a deny-list: three characters had been refused one review at a time — `/`,
then a dot in the wrong place, then `,`, the multi-host separator that let
`pg-1.internal,pg-2.internal` past the index, the lower-case check and the endpoint comparison as a
fifth takeover shape (PR #18, second review; Npgsql opens whichever host answers, so it is the
same server under another string). Excluded by construction: a multi-host list and a Unix-socket
directory — the two shapes ADR-0036 §6 names as breaking the endpoint triple — a scheme, a port
suffix, a path, a stray or doubled dot, and any upper-case or non-ASCII character. That last is
why the grammar is ASCII, **executed on `postgres:17-alpine` in `CatalogHostGrammarTests`** rather
than reasoned: the lower-case check's `lower()` depends on the database's `lc_ctype` for a
non-ASCII letter —

```
datctype of the catalog: en_US.utf8
lower('pg.Über.internal') under C: pg.Über.internal (host = lower(host) holds: admitted)
lower('pg.Über.internal') under en_US.utf8: pg.über.internal (host = lower(host) fails: refused)
lower('pg.ÜBER.internal') under C: pg.Über.internal; under en_US.utf8: pg.über.internal
C: 'pg.Über.internal' = lower(host) is True; insert refused 23514 on ck_database_cluster_host_well_formed
en_US.utf8: insert refused 23514 on ck_database_cluster_host_lower_case
```

— so `pg.Über.internal` beside `pg.über.internal` is two rows on a `C`-collated catalog and one on
the fixture's, with the lower-case check alone. `pg.ÜBER.internal` is **not** that witness: `B`,
`E` and `R` are ASCII upper case and fold under both (an earlier version of this paragraph cited
it; it does not reproduce). The shape check refuses both spellings under both collations, and the
entity's grammar and the constraint's regular expression are held equal over 25 cases by executing
both — on the fixture's `en_US.utf8` catalog and on a `C`-collated one (`cases: 25; agree under
en_US.utf8: 25; agree under C: 25`), because a POSIX class such as `[[:alpha:]]` follows the
database's `lc_ctype` while an explicit range is matched by code point, so the expression uses
ranges only and the migration's remarks say why. The catalog's `lc_ctype` is pinned nowhere
(`FOLLOWUP-061`). `TenantHost` keeps a private declaration of essentially the same grammar for
`tenant_host.host`: two declarations, and nothing compares them — the fitness rule ADR-0043 chose
for the analogous pair is a separate row. An internationalised name is stored as punycode, which is what DNS carries. An IPv6 literal
(`::1`, `[::1]`) is outside the grammar pending the architect: ADR-0036 §3 reasons about folding
one, the row has never accepted one, and admitting it is a decision about what a cluster endpoint
may be, not a spelling rule.

**The acceptance criterion is the property, not the index** — ADR-0036 §2.3, which replaced
ADR-0034 §3.3 after this row found §3.3 vacuous: *no two non-deleted tenants resolve to the same
physical endpoint*, the endpoint being the `(host, port, database)` triple parsed with
`NpgsqlConnectionStringBuilder` out of the string the real resolver produced, host compared
ignoring case, database ordinally, port exactly — never the whole string. A connection string is a
serialisation of an intent, not a description of a destination (§2.1): `Application Name` carries
the tenant key by construction, `Username`, `Password` and every ADR-0007 §5.2 pool setting can
differ between two strings that open one database, so equality over the whole string cannot fail.
ADR-0036 §2.4 splits the demonstration in three, and this module carries all three:

- **D1, the comparator, as a unit test** (`ResolvedEndpointComparisonTests`, over
  `ResolvedEndpoint`/`EndpointCollisions` in a source file linked into both test projects as
  `CatalogSchemaGuard.cs` is). One realistic string with every §5.2 field set, one dimension varied
  per case, each case first proving the two strings differ: `Application Name`, host case,
  `Password`, `Username`, `Maximum Pool Size` and `Command Timeout` are one endpoint; database
  case, database name, port and host name are not. Beside it, ADR-0034 §3.3 as written is executed
  over the same input — whole-string collisions `0` where endpoint collisions are `6` — so the
  vacuity stays executable. D1 never touches the catalog and so nothing the catalog refuses can
  disarm it.
- **D2, the fleet scan, as an integration test** (`CatalogRoutingUniquenessTests`). It asks the
  catalog, as the owner, to store every executed shape — variant 1; variant 3 with an `Active`
  attacker and with a `Provisioning` one (PR #18 M-3); variant 3 with the host in another case
  (M-1); variant 3 as a multi-host list, the fifth shape, which printed `ADMITTED … collisions: 0`
  and passed against the previous migration — records whether each was admitted or refused by a
  constraint, then takes an endpoint for
  every non-deleted tenant from the production registration: parsed out of the string the real
  `ITenantConnectionResolver` produced where the application path may connect, and where it
  refuses on state, out of the string the real composer produces over the row the real reader read
  — what the resolver does after its state gate — with the two required to agree on every tenant
  that has both, so the assertion stays a property of the resolver's output and a `Provisioning`
  row, the one B-07.1's adoption rule acts on, is compared rather than skipped. It compares every
  pair with the same comparator, prints tenants, pairs compared and collisions, and fails on zero
  of either. **What the comparison backstops, stated exactly** (PR #18, third review): `Parse`
  refuses the two shapes ADR-0036 §6 names — a multi-host list and a Unix-socket directory — and
  a row it cannot project is reported as unprojectable and fails the scan on its own; equality
  folds case and one trailing dot, the two spellings of one name a resolver treats as one;
  everything else non-canonical (a leading or doubled dot, a hyphen at a label's edge, a non-ASCII
  letter) is compared as given and is the shape check's to refuse. Executed with the shape check
  removed from `Up` only, so all three are admitted:

  ```
  variant 3 as a multi-host list …: ADMITTED
  variant 3 with a trailing dot …: ADMITTED
  variant 3 as a socket directory …: ADMITTED
  tenants: 5 non-deleted; 3 resolved …; unprojectable: 2; pairs compared: 3; collisions: 1
    t-f8473afdfc4e (Active): 'pg-05eb98736c77.internal,pg-decoy.internal' is a multi-host list, which is not one endpoint …
    t-cbe953ddec85 (Active): '/var/run/postgresql' is a Unix-socket directory, which is not one endpoint …
    t-326db14263e1 (Active) and t-bd58b4dbadf0 (Active) -> pg-05eb98736c77.internal:5432/aurora_t_t_326db14263e1
  ```

  — the list and the socket directory error, the trailing dot collides. D2 is a regression guard,
  not a proof: once the index and the checks are in place no seed can construct a collision
  through the normal write path.
- **D3, the one-shot demonstration, recorded rather than standing** — the fix removed the
  evidence. D2 run before `ux_database_cluster_host_port` existed, against a catalog seeded with
  variant 3, verbatim:

  ```
  takeover shapes attempted: 2
    variant 1: a second tenant row on the victim's cluster, copying its database_name: refused, 23505 on ux_tenant_cluster_id_database_name
    variant 3: a second cluster row on the victim's cluster's host and port, and a tenant on it copying its database_name: ADMITTED
  tenants: 3 non-deleted, 3 resolved, 0 not routable (); pairs compared: 3; collisions: 1
    t-19e54e1994e3 and t-acbe37ef31b3 -> pg-9312cf93ed70.internal:5432/aurora_t_t_19e54e1994e3
  Shouldly.ShouldAssertException : collisions should be empty but had
    ADR-0034 §3.3: two non-deleted tenants resolve to one physical database - t-19e54e1994e3 and t-acbe37ef31b3 -> pg-9312cf93ed70.internal:5432/aurora_t_t_19e54e1994e3.
  ```

  Reproduced after the migration by making the index non-unique and removing the check from `Up`
  only: `collisions: 6` over 10 pairs, the `Provisioning` attacker and the upper-cased attacker
  (host folded onto the victim's) among them. Both runs are on PR #18.

**What the comparison establishes:** no two non-deleted tenants name the same host in any
spelling, port and database, so a further variant that differs in none of those fails D2 whichever
index it walked around. **What it does not:** that no two tenants reach the same server.
`localhost` beside `127.0.0.1` — an IP literal beside a host name, a CNAME, a second DNS record, a
failover alias — are different triples here and different rows in the catalog (executed in PR #18's
review: three rows, one database, `collisions: 0`), and no comparison of stored names can close
that; `TenantIdentityStamp` is the control (ADR-0034 §4). D2 replaced B-06.1's inertness guard,
deleted rather than repaired the day the hole closed, as its message directed. The model's
own declaration of the index (`CatalogModelTests`) and the SQL the migration emits
(`ClusterEndpointUniquenessMigrationTests`, unit) are checked in stage 6 with Docker stopped; both
are tripwires on the configuration and say so, and neither stands in for the property.

**The migration fails loudly against a catalog that already holds the defect** (ADR-0034 §5.1).
`ClusterEndpointUniquenessMigrationTests` (integration) creates a catalog of its own, migrates it to
the state before this migration, seeds two rows on one endpoint and runs the migration: SQLSTATE
`23505`, `could not create unique index "ux_database_cluster_host_port"`, `Key (host, port)=(…) is
duplicated` — and, because the migration is transactional, no index, no history row, both rows
intact and the migration still pending for the runner to retry once an operator has resolved the
duplicate. A mixed-case host at the same state fails it with `23514`, `check constraint
"ck_database_cluster_host_lower_case" of relation "database_cluster" is violated by some row`, and a
multi-host list with `23514` naming `ck_database_cluster_host_well_formed` — and the index and
check created a statement earlier are rolled back with it, the row left as it was spelled.
Transactional on purpose: `CONCURRENTLY` cannot run in a transaction and leaves an
`INVALID` index behind on failure, and ADR-0007 §7.2 reserves it for tables a live tenant writes to,
which the catalog's operator seed data is not. Rows on distinct endpoints — one host on two ports,
two hosts on one port — migrate, keep every row and re-run as a no-op. Npgsql redacts a `DETAIL`
on the client unless the connection asks (`Include Error Detail`); the test's connection asks, so
the server is held to naming the duplicate, and the runner's connection (B-08) decides what an
operator sees.

**What the index does not and cannot cover, stated rather than left to be inferred** (ADR-0034 §2,
§3.2; ADR-0036 §2.4). Two names for one server — a CNAME, a second DNS record, a failover alias, an
IP literal beside a host name. (A host spelled in another case, and a multi-host list, were on this
list until ADR-0036 §3 and PR #18's second review; the two checks, the grammar and the comparator's
folding close those halves, and only those.) The catalog stores what it was told; a
constraint over a name narrows what can be stored and never establishes identity.
`TenantIdentityStamp` is the control for that (ADR-0034 §4), on every physical connection, and it
is in effect on no path until B-06.2 and B-07.1 wire it. Two shapes the index is wrong for if they
ever become rows: a read replica, which ADR-0007 §3.5 keeps inside the resolver and never as a row;
and PgBouncer, which arrives as a second endpoint pair on one row with a unique index of its own,
never as a second row. Either is ADR-0034 §9's revisit trigger. A tenant `database_name` equal to a
cluster's `maintenance_database` on the same endpoint is not on this axis either; the `aurora_t_`
naming convention and B-07.1's safe-adoption rule are what cover it.

**One cluster row per container in the tests.** `CatalogDatabaseFixture.ThisServerAsClusterAsync`
seeds the one `database_cluster` row for the test container on first use, with an app secret
reference the resolver's `EnvironmentSecretStore` can answer for the life of the fixture; every
routing test bed and the end-to-end resolve test place their tenants on it. Until B-20 each seeded a
row of its own for that endpoint — variant 3 as a fixture, the shape ADR-0034 §3.2 forbids — and
under the index, measured before the fixture changed, every such test but the first in the run
failed with `23505`: five of the six that seeded a row, plus the guard, six red where one was meant.

---

## Running the tests

```
source scripts/dev-env.sh
dotnet test tests/unit/Aurora.Platform.Tenancy.UnitTests        # no database, verify.sh stage 6
dotnet test tests/integration/Aurora.Platform.Tenancy.IntegrationTests   # needs Docker
```

The integration project has one xUnit collection and therefore one `postgres:17-alpine` container.
The fixture builds the cluster the way a deployment does — three roles, a database owned by
`aurora_migrator`, migrations applied as `aurora_migrator`, tests talking to it as `aurora_app`, every
other database on the cluster hardened per ADR-0007 §8 step 3 — so the privilege model under test is
the one that ships, not the container's superuser, and what `CatalogPrivilegeTests` proves about the
cluster is what a hardened cluster would show.
