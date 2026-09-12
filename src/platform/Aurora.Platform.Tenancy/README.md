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
   record names. A fault theory hollows the function body two ways (`RETURN COALESCE(NEW, OLD)` and
   `RETURN NULL` — one statement each, leaving every `pg_trigger` column byte-identical), and
   disables and drops each of the two guards in turn, and watches the probe name exactly the
   silenced statements and go green again once rolled back. The `pg_trigger` enumeration
   (`tgtype 27` and `34`, `tgenabled 'A'`) stays as the locator that says which table and why;
   it is blind to a hollow body by construction. Under `session_replication_role = 'replica'`,
   which suppresses an ordinary trigger with `pg_trigger` unchanged, the `ALWAYS` guards still
   refuse.
3. **No default privilege for the schema.** `pg_default_acl` holds zero rows scoped to `catalog`. A
   copied `ALTER DEFAULT PRIVILEGES … IN SCHEMA catalog GRANT` takes the count to one and is caught;
   the `REVOKE` form writes no row and is not — survivable, because mechanism 1 asserts the
   resulting ACL of every catalog relation and never which statement produced it.

**What this does not cover, stated rather than implied.** `aurora_migrator`, as the DDL-path role,
can disable or drop either guard or replace the function's body, and then rewrite, remove or empty
a trail; nothing here detects that. That is the detection-not-prevention boundary ADR-0028 §2
draws; its cover is a chain head recorded outside the tenant database — in `operator_audit_event`,
by `FOLLOWUP-031`'s job — carried as `FOLLOWUP-026`. Neither table is partitioned, so Amendment 2's
partition clause (a guard created on every partition) does not arise here. And the writers, not
this module, keep personal data and erased values out of `detail` and `affected` (ADR-0018 §4, §6
point 6): a column cannot enforce that, and no test here claims to.

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
