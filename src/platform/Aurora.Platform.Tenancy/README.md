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
| `catalog.operator_audit_event`, `catalog.erasure_replay_log` | Audit platform module (ADR-0018, §11.5) |
| `catalog.feature_flag`, `catalog.feature_flag_tenant_override` | Configuration platform module (ADR-0011) |
| `quartz.*` | Jobs platform module (ADR-0014) — its own schema, not `catalog` |

Whoever adds one must add its columns to `CatalogSchemaAllowlist` in the same commit, or the
ADR-0007 §9.3 guard fails. That is the intended friction: the allowlist diff is where a reviewer
sees a new catalog table arrive.

---

## Three decisions a reader will want justified

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
The resolver (B-06) composes `Host=…;Username=aurora_app;Password=<from the store>` in memory at
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
tasks stamp states this migration must already admit — `SchemaBlocked` (B-06 identity check §4.3,
B-08 quarantine §7.4) and `ProvisioningFailed` (B-07, the only state an operator may destroy a
database from, §8) — and widening a check constraint under live tenants is exactly what the
expand/contract rule exists to avoid.

The constraint's allowed values are generated from the `TenantState` enum's names
(`StateNames.CheckConstraintSql`), so the two cannot drift: adding a state means scaffolding a
migration, which is the visible event it should be. `CatalogTenantStateTests` stores and reads back
each of the eight and asserts four plausible near-misses are refused.

`Tenant` itself exposes only `Reserve` and `Activate` — the two ends of the provisioning saga. The
methods that reach the other six states arrive with the tasks that drive them, guarded the same way.

---

## What "tenant isolation" means for a shared database

The Definition of Done asks every task that adds data access for tenant isolation tests. The catalog
is the one store shared by design, so there is no tenant A row for a tenant B request to be kept out
of, and a test that partitioned it would prove nothing. What is real here is the blast radius of the
role the request path holds (`CatalogPrivilegeTests`, asserted by trying, as the role):

- **Sideways** — `aurora_app` holds `CONNECT` on exactly one database (ADR-0007 §4.4), so a
  compromised or mis-routed request cannot reach another database on the cluster. This is the
  property that carries the weight once every tenant has a database of its own.
- **Downwards** — `aurora_app` has no DDL and cannot touch `__EFMigrationsHistory`, so it cannot
  alter the schema that decides where every tenant's data lives.

The per-tenant isolation contract of ADR-0007 §12.2 arrives with `Aurora.TestKit` (B-10), once there
are two tenant databases to keep apart.

---

## Running the tests

```
source scripts/dev-env.sh
dotnet test tests/unit/Aurora.Platform.Tenancy.UnitTests        # no database, verify.sh stage 6
dotnet test tests/integration/Aurora.Platform.Tenancy.IntegrationTests   # needs Docker
```

The integration project has one xUnit collection and therefore one `postgres:17-alpine` container.
The fixture builds the cluster the way a deployment does — three roles, a database owned by
`aurora_migrator`, migrations applied as `aurora_migrator`, tests talking to it as `aurora_app` — so
the privilege model under test is the one that ships, not the container's superuser.
