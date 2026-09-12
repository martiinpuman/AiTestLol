# ADR-0007 — Multi-tenancy: database per tenant

- **Status:** Accepted (2026-09-11) — the isolation model is **locked by the product owner**; everything else in this ADR is the architect's design
- **Deciders:** product owner (isolation model), architect (all mechanisms)
- **Supersedes:** —
- **Superseded by:** **in part** by **ADR-0034 (2026-09-12)** — §3.5's code block, which declares `ITenantConnectionResolver` and `TenantConnection` in `Aurora.Platform.Tenancy.Contracts`; both are `internal` to `Aurora.Platform.Tenancy` (ADR-0034 §6). Every other sentence of §3.5 stands. And **in part** by ADR-0027 — §4.1 (the tenant `DbContext` constructor signature) and §7.5 (which access paths the skew check gates). Every other section of this ADR stands. §3.2 (strategy 3 is a constraint, never a resolution source, and a host that maps to no tenant is refused rather than resolved from the claim) and §3.3 (what is checked at circuit creation and at each revalidation) are **made precise** by ADR-0029 §4 — clarified, not changed — **except** §3.2's `403` for a routing violation, which ADR-0029 Amendment 1 (M-1) changes to a `404` identical to the unknown-host response, because the two status codes together enumerated the customer list. ADR-0029 Amendment 2 (A2.5) also makes §3.3 explicit: the circuit pins the **`TenantId`**, and a `TenantScope` is opened per unit of work and disposed with it — which is what §3.2's "once per unit of work" and §10.4's stale-scope trap already require.
- **Amended by:** **ADR-0035 (2026-09-12)** — §3.4 names `InstalledPackages` and leaves it undefined; ADR-0035 §2 supplies the definition, including the `Active`/`TryGetActive` split that keeps "installed" and "usable" from being one question. **ADR-0033 (2026-09-12)** — §4 and §12.3 are unchanged in design; ADR-0033 attaches the threat model they were built against and states plainly that they are **not** a control against code executing inside the process, which ADR-0008 §9.2 puts there. **ADR-0034 (2026-09-12)** — §9.2 gains one uniqueness constraint on `catalog.database_cluster (host, port)`, and §4.3's stamp is named as *the* control for routing identity rather than defence in depth. **ADR-0032 (2026-09-11)** — §4.1 and §4.2 state the guarantee in prose; ADR-0032 states the boundary a fitness rule can bind to (which DI registrations are banned, whether a member *returning* a tenant `DbContext` is a violation, how the catalog context is identified, and that allow-lists match exact assembly names). **ADR-0040 (2026-09-12)** — §3.4's "no module can fabricate a `TenantScope`" and §12.3's **compile-time** framing are both **falsified as written** by one `[UnsafeAccessor]` declaration in an assembly on nobody's friend list (executed). The mechanisms are unchanged and still worth having; ADR-0040 §2.2 names the **tenancy origination set** as the boundary they actually draw, §4 states what governs adding to it, §4.6 refuses `[UnsafeAccessor]` in `src/` repository-wide, and §5 restates the guarantee with the three routes that remain. **ADR-0042 (2026-09-12)** — §11.4's offboarding state machine is unchanged; its `PendingDeletion`/`Deleted` bullets gain an identity rule, a corrected step order and a gate on the irreversible statement. **No decision in this ADR is reversed.** The amendment notes below mark the sections affected.
- **Related:** ADR-0003 (EF Core), ADR-0004 (PostgreSQL), ADR-0005 (Blazor Server), ADR-0008 (Country Packages), ADR-0018 (audit, retention, erasure)

> This is the load-bearing ADR of the project. Tenancy is the one decision that cannot be retrofitted: every module's data access, every background job, every test and every operational procedure is shaped by it. Read §3, §4 and §10 before writing any data access code.

---

## 1. Context

Aurora ERP serves many independent businesses from one deployment. `CLAUDE.md` fixes the isolation model: **one PostgreSQL database per tenant, plus one shared *catalog* database** holding the tenant registry, routing, subscriptions, platform identity and installed-package records — and **no tenant business data**.

The requirements this ADR must satisfy, taken from `CLAUDE.md` and the quality ranking in `../architecture/overview.md` §4:

1. A developer **must not be able to obtain a `DbContext` without a resolved tenant** — "a compile-time or container-level guarantee, not a convention".
2. Every module has automated tests proving a request in tenant A's context cannot read or write tenant B's database — **including through background jobs and integration events**.
3. Tenant lifecycle is first-class: provisioning creates and migrates a database; migrations roll out safely across every tenant database and are **resumable**; offboarding exports and then destroys exactly one tenant's data.
4. The catalog database is the only shared store.

Two physical facts constrain everything below. PostgreSQL allocates **one backend process per connection**, so connection count — not disk, not CPU — is the first thing that breaks as tenants multiply. And `CREATE DATABASE` **cannot run inside a transaction block** and must be issued from a connection to a *different* database, so provisioning cannot be a transaction.

---

## 2. Options considered

The product owner chose option C. The alternatives are recorded because a future reader will ask, and because option D remains the escape hatch this design deliberately keeps open.

| Option | Pros | Cons |
|---|---|---|
| **A. Shared schema, tenant key column + row-level security** | Cheapest per tenant by an order of magnitude; one database to migrate, back up and monitor; connection pooling is trivial; a report across all tenants is one query | Every table carries a tenant key and every query depends on an RLS policy being present and correct — one missing policy or one `BYPASSRLS` role is a cross-tenant data breach; noisy neighbours share a buffer cache; "delete exactly one tenant" is a large, slow, error-prone `DELETE` cascade; per-tenant point-in-time restore is effectively impossible |
| **B. Schema per tenant in one database** | Cheaper than a database each; still gives a natural namespace and a per-tenant dump | Thousands of schemas × tens of tables explodes the system catalogs and degrades planning time; EF Core's model would vary per tenant (schema name is part of the model), which breaks the shared compiled model in ADR-0003 §2; `search_path` juggling under transaction pooling is exactly the session-state trap ADR-0004 forbids; still one `max_connections` budget and one WAL stream |
| **C. Database per tenant** *(chosen — locked)* | The isolation boundary is the one the database itself enforces: a connection to tenant A's database physically cannot see tenant B's tables, whatever the application does; `pg_dump`/`DROP DATABASE` make per-tenant backup, restore, export and deletion first-class one-liners; data residency is a routing decision; a runaway query damages one tenant; per-tenant restore-to-point-in-time is achievable | Connection fan-out is the binding constraint (§6); migrations must be orchestrated across N databases and can partially fail (§7); provisioning is a saga, not a transaction (§8); no cross-tenant query, so operator reporting must be a fan-out job; highest per-tenant idle cost |
| **D. Tiered hybrid: shared schema for the long tail, dedicated database for large customers** | Best cost/isolation curve; the long tail is cheap and the customers who pay for isolation get it | Two data-access paths, two migration stories, two test matrices — roughly double the platform work, and the shared path carries all of option A's risks anyway |

**The architect's position, for the record.** For 10–250-employee SMBs, the recommendation had been option A with RLS plus a documented promotion path to a dedicated database. The product owner chose option C, explicitly, for the stronger isolation (`docs/HUMAN_INBOX.md`, 2026-09-10). That is a legitimate and defensible choice — it buys real, structural guarantees that option A only approximates — and this ADR designs for it without relitigating it. The cost is written honestly in §13 Consequences, and the single seam that would let us reach option D later (`ITenantConnectionResolver`) is specified in §3 so that the option is never closed off.

---

## 3. Decision — tenant resolution and connection selection

### 3.1 What a tenant is

A **tenant** is one paying customer: one subscription, one database, one set of installed Country Packages. Inside a tenant there are one or more **companies** (legal entities). Authorization is scoped to *both* (ADR-0010). A user belongs to one or more tenants; an external accountant is the motivating case for many.

`TenantId` is a `readonly record struct` wrapping a UUIDv7 (time-ordered, index-friendly). It is never a string, never an `int`, and never the tenant's slug — the slug is a display and routing concern that can be renamed; the id cannot.

### 3.2 Resolution order

The tenant is resolved **once per unit of work**, by the first strategy that matches:

| # | Strategy | Used by |
|---|---|---|
| 1 | Host header → `catalog.tenant_host` (`acme.aurora.example`, or a custom domain) | Web UI, public API |
| 2 | Path segment `/t/{tenantKey}/...` | Multi-tenant users (the external accountant) switching context |
| 3 | `tid` claim on the authenticated principal, cross-checked against 1/2 | All authenticated requests |
| 4 | Explicit `TenantId` argument | Background jobs, the outbox dispatcher, provisioning, the migration runner (§10) |

If strategies 1/2 and 3 disagree, the request is **rejected** (`403`, audited as a routing violation). It is never "resolved" by preferring one of them.

### 3.3 The lifetime problem, and why `IHttpContextAccessor` is banned

Blazor Server has **no HTTP request during an interactive render** (ADR-0005 rule 2). A component that reads the tenant from `IHttpContextAccessor` works in development, works on the first render in production, and returns `null` or — far worse — a *stale* value once the circuit is live. Therefore:

- The tenant is resolved during circuit creation by a `CircuitHandler` and **pinned to the circuit** for its whole life.
- A user switching tenant or company **tears down and re-creates the circuit** (a full navigation, not a state mutation). The tenant/company switcher in `docs/design/components.md` §18 is specified this way for exactly this reason.
- An architecture fitness test forbids `IHttpContextAccessor` in every assembly except `Aurora.Web`'s resolution middleware.

### 3.4 `TenantScope`

> **Amended by [ADR-0035](ADR-0035-installed-packages-defined-and-the-ddl-handle-confirmed.md) §2 (2026-09-12) — what `InstalledPackages` is.** The `Packages` property below cites "ADR-0008 §4", which describes what a package contributes to a *tenant database* and never defined the set a scope carries. ADR-0035 §2 is that definition: immutable and copied at scope open, one entry per package id or the whole set refused, `None` the only empty value, and `(PackageId, Version, State)` carried as the catalog's **text** rather than the Country Package contract types (fitness rule L2, and so that a package-contract MAJOR bump is not a tenancy change). **Read §2.3 before using it:** `Entries`/`TryGet` return a package in *any* state, so capability and slot resolution must read `Active`/`TryGetActive` — otherwise a `Deactivated` package still fills a core slot and ADR-0008 §5.3's deactivation switches nothing off.

`TenantScope` is the single representation of "we know which tenant we are in". It is a sealed class in `Aurora.Platform.Tenancy.Contracts` with **no public constructor**:

```csharp
public sealed class TenantScope : IAsyncDisposable
{
    internal TenantScope(...);                      // only Aurora.Platform.Tenancy may construct one

    public TenantId          TenantId       { get; }
    public string            TenantKey      { get; }
    public string            ResidencyRegion{ get; }
    public SchemaVersion     SchemaVersion  { get; }   // checked in §7.5
    public InstalledPackages Packages       { get; }   // ADR-0008 §4
    public TenantAccessReason Reason        { get; }   // Request | Job | Outbox | Provisioning | Migration | OperatorSupport
    public bool              IsActive       { get; }   // false after disposal — see §10.4
}
```

`[assembly: InternalsVisibleTo("Aurora.Platform.Tenancy")]` and the tenancy test assembly are the only grants. **No module, no component and no job can fabricate a `TenantScope`.** That is the first half of the guarantee in §4.

> **Amended by [ADR-0040](ADR-0040-the-tenancy-origination-set-is-the-boundary.md) §2 and §5 (2026-09-12) — the sentence above is FALSE as written; the mechanism it describes is unchanged and still worth having.** `[UnsafeAccessor(UnsafeAccessorKind.Constructor)]` on an `extern` declaration mints a live `TenantScope` from an assembly no `[InternalsVisibleTo]` names, with **no reflection, no cast, no permission and no compiler diagnostic** — executed, ADR-0040 §2.1 probe A, which printed `active=True reason=OperatorSupport`. It runs the constructor, so §12.3's hollow-scope defence does not apply to it, and its signature names only public types, so **naming is sufficient to construct**.
>
> What holds instead is ADR-0040 §5: *no assembly outside the **tenancy origination set** obtains a `TenantScope` unless some first-party source file declares the route* — an `[UnsafeAccessor]` declaration, a reflection call, or a friend assembly's member whose signature erases the type (ADR-0040 §3.5, executed: fourteen such doors, zero reported). Every assembly may freely **name, hold, cast, pass and re-export** a scope it was handed; that is the design, not a leak (§4.5). ADR-0040 §2.2 names the origination set as the boundary this actually draws — *the line between origination that is invisible in review and origination that announces itself* — and §4 states what governs adding to it. **Do not "fix" this by hardening `TenantScope`:** ADR-0040 §4.7 refuses exactly that, citing ADR-0033 §2's final paragraph, and records the one trigger that would change the answer.

### 3.5 `ITenantConnectionResolver`

> **Superseded in part by [ADR-0034](ADR-0034-routing-uniqueness-is-physical-and-the-stamp-is-the-control.md) §6 (2026-09-12).** `ITenantConnectionResolver` and `TenantConnection` are **`internal` to `Aurora.Platform.Tenancy`**, not declared in `.Contracts`. `TenantConnection.ConnectionString` is a live credential, and a type in a contracts assembly is nameable by every module, host and job that references it; `internal` upholds this section's own goal — *"Nothing else in the system learns what a connection string looks like"* — by construction rather than by a fitness rule. Everything else below is unchanged. The first outside consumer (`Aurora.TestKit`'s counting resolver) gets `[InternalsVisibleTo]` under ADR-0034 §6.1's three conditions, not a promotion.

```csharp
public interface ITenantConnectionResolver
{
    ValueTask<TenantConnection> ResolveAsync(TenantId tenantId, CancellationToken ct);
}

public sealed record TenantConnection(
    string ConnectionString,      // fully formed, including credentials from the secret store
    string ClusterId,
    string DatabaseName,
    string ResidencyRegion);
```

This is the **only** place that knows how a tenant maps to physical storage. It reads `catalog.tenant` + `catalog.database_cluster`, caches for 60 seconds in `HybridCache` under a tenant-prefixed key, and is invalidated on tenant state change. Because every caller goes through it, moving a tenant to another cluster, introducing a read replica, or — the escape hatch — returning a *shared* connection string with an RLS session variable for a future low-cost tier (option D) is a change **inside this one implementation**. Nothing else in the system learns what a connection string looks like.

**Credentials.** Stage 1 (bootstrap): one `aurora_app` login role per cluster; every tenant database has `REVOKE ALL ON DATABASE ... FROM PUBLIC` plus an explicit `GRANT CONNECT TO aurora_app`. Stage 2 (before the first paying customer, backlog item): **one `aurora_app_<tenantKey>` login role per tenant database**, its password held in the secret store and referenced — never stored — by `catalog.database_cluster.app_secret_ref`. Stage 2 makes cross-tenant access impossible at the *database* level, not merely the application level. Because `ITenantConnectionResolver` returns a complete connection string, stage 2 changes one class and zero callers.

---

## 4. The structural guarantee: no `DbContext` without a `TenantScope`

> **Amended by [ADR-0033](ADR-0033-the-tenancy-trust-boundary-is-the-process.md) (2026-09-12) — what these four layers are a control against.** No layer's design changes. What ADR-0033 adds is the threat model: layers 1–2 stop **developer error**, layer 3 stops a **mis-routed connection**, layer 4 stops a **compromised connection string**. None of them is a control against code executing inside this process — and ADR-0008 §9.2 executes Country Package assemblies there. Reflection on the `internal` constructor of a `TenantScope` is not blocked, .NET offers no mechanism to block it, and a forged scope routed to the tenant it names is *confirmed* by §4.3's stamp rather than caught by it. ADR-0033 §5 names the control that does apply (admission), the residual, and what must demonstrate each.

> **Amended by [ADR-0034](ADR-0034-routing-uniqueness-is-physical-and-the-stamp-is-the-control.md) §4 (2026-09-12) — "defence in depth" is the wrong word for §4.3.** For **routing identity**, §4.3's stamp is *the* control: a catalog constraint can only narrow the claims that can be stored, never establish which physical database a connection reached. It follows that a row writing tenant routing rows may not merge before the stamp assertion is in effect on the path it writes for (ADR-0034 §5). For "no `DbContext` without a tenant", the ordering in this section is unchanged.

`CLAUDE.md` demands a compile-time or container-level guarantee. This is it, in four layers. Layers 1–2 are the guarantee; layers 3–4 are defence in depth, because a guarantee nobody can observe failing is a guarantee nobody trusts.

### 4.1 Layer 1 — the type system (compile time)

> **Amended by ADR-0032 (2026-09-11) §4.2.** "The only public way to obtain one" is now bounded mechanically as well as by design: outside the assemblies that own a tenant `DbContext` nothing may **name** it (rule T16); no externally reachable member anywhere may **return** or expose one, including through a `ref`/`out` parameter (T17); and only `Aurora.Platform.Tenancy` may **construct** one (T18). `ITenantDbContextFactory<TContext>` needs no exemption from any of the three — it returns a type parameter, not a context type, so the rules cannot match it. The constructor rule in this section is unchanged (and its signature is ADR-0027 §1's).

> **Superseded in part by ADR-0027 §1.** The constructor's second parameter is `TenantAccess`, the abstract base of `TenantScope` (application path) and `TenantDatabaseHandle` (DDL path). The guarantee below is unchanged: neither proof type is constructible outside `Aurora.Platform.Tenancy`.

Every tenant `DbContext` has exactly one constructor, and it is **internal**:

```csharp
public sealed class SalesDbContext : DbContext
{
    internal SalesDbContext(DbContextOptions<SalesDbContext> options, TenantScope scope) : base(options)
        => Scope = scope;

    internal TenantScope Scope { get; }
}
```

The only public way to obtain one:

```csharp
public interface ITenantDbContextFactory<TContext> where TContext : DbContext
{
    ValueTask<TContext> CreateAsync(TenantScope scope, CancellationToken ct);
}
```

`scope` is a non-nullable required parameter. There is no overload without it, no `Current`-reading convenience method, and no `CreateAsync()`. **You cannot write the call without first holding a `TenantScope`, and §3.4 means you cannot manufacture one.** Nullable reference types are enabled solution-wide with `TreatWarningsAsErrors` (ADR-0002), so passing `null!` is a compile error that a reviewer will see as a deliberate act.

### 4.2 Layer 2 — the container

> **Amended by ADR-0032 (2026-09-11) §4.1.** "No tenant `DbContext` is ever registered" is read as: **every DI registration naming a tenant `DbContext` is banned, whatever the method** — `AddScoped<SalesDbContext>()` and a generic helper are registrations exactly as `AddDbContext<SalesDbContext>` is. **There is no sanctioned registration helper**; `AddTenantDbContext<T>()` must not be written, and ADR-0032 §4.1.3 states what a module writes instead. The fitness test described in the second bullet below is **amended in its scope** by ADR-0032 §5 — amended, not superseded: the decision stands and the mechanism is re-keyed, onto the call site's assembly and the container surface rather than the member name, because a rule that keys on a method name is defeated by renaming one.

- **No tenant `DbContext` is ever registered in the DI container.** No `AddDbContext<SalesDbContext>`, no `AddDbContextFactory<SalesDbContext>`, no `AddDbContextPool`. `IServiceProvider.GetRequiredService<SalesDbContext>()` throws, because nothing registered it. Only `CatalogDbContext` is registered conventionally (ADR-0003 rule 3).
- An **architecture fitness test** asserts, across the whole solution: no call to `AddDbContext*<T>` where `T` is a tenant context; no tenant context type has a public or protected constructor; no type outside `Aurora.Platform.Tenancy` implements `ITenantDbContextFactory<>`.
- `ITenantScopeAccessor.Current` exists for ambient reads inside a request or circuit, but it returns a **non-nullable** `TenantScope` and throws `NoTenantResolvedException` otherwise. It is registered only in `Aurora.Web` and is unavailable in the worker composition root, where scopes must come from `ITenantScopeFactory.OpenAsync(TenantId, ct)` (§10). **Corrected by ADR-0029 Amendment 2 (A2.4, 2026-09-12): `OpenAsync` takes no `TenantAccessReason` parameter.** The reason remains a property of the `TenantScope`, for the state gate and for logging, but it is bound by **which factory binding the composition root registered** — `Aurora.Web` → `Request`, `Aurora.Worker` → `Job` and `Outbox`, the provisioning saga host → `Provisioning` — with **`Migration` and `OperatorSupport` deliberately unbound in bootstrap** (`Migration` because DDL uses ADR-0027's `TenantDatabaseHandle`; `OperatorSupport` because the operator console does not exist, and when it does it arrives as a separate factory type validating a support-grant id, not as an enum value a developer may type). A reason the caller can type is a state gate the caller can skip. What must demonstrate it: fitness rule **T9** (`../architecture/testing-strategy.md` §5.3) asserts that `ITenantScopeFactory` exposes no reason parameter and that no call site outside `Aurora.Platform.Tenancy` names a `TenantAccessReason` value, against a deliberately-violating fixture; plus a test that an operator-support scope cannot be obtained from **any** bootstrap composition root.

### 4.3 Layer 3 — the connected-database identity check (runtime)

Layers 1 and 2 stop a developer from *forgetting* the tenant. They do not stop a **mis-routed connection string** — a catalog bug, a bad restore, a database renamed by hand, a cluster failover pointing at a stale replica. So every physical connection proves which tenant it reached.

At provisioning (§8, step 4) each tenant database is stamped:

```sql
create table platform.tenant_identity (
    only_row    boolean primary key default true check (only_row),
    tenant_id   uuid    not null,
    tenant_key  text    not null,
    stamped_at  timestamptz not null default now()
);
```

The `only_row` primary key with its `check` makes a second row impossible. The tenant data source is built with an `NpgsqlDataSourceBuilder` physical-connection initializer that, **once per physical connection**, reads `select tenant_id from platform.tenant_identity` and compares it with the expected `TenantId`. A mismatch throws `TenantRoutingViolationException`, fails the request, raises a high-severity alert and marks the tenant `SchemaBlocked`.

This is stronger than comparing `current_database()`: a database restored into a differently-named database, or a routing row edited to point at the wrong database, is still caught, because the identity travels *inside* the data, not in its name. Cost is one round trip per physical connection — amortised to nothing by pooling.

### 4.4 Layer 4 — least privilege (deployment)

The runtime connects as `aurora_app`, which has no DDL and no `CREATEDB` (ADR-0004 rule 2). Under §3.5 stage 2 it additionally has `CONNECT` on exactly one database. A compromised or buggy request cannot create, alter or drop anything — including another tenant's database.

### 4.5 What a module developer actually writes

```csharp
internal sealed class PostSalesInvoiceHandler(ITenantDbContextFactory<SalesDbContext> factory)
{
    public async Task<Result> HandleAsync(TenantScope scope, PostSalesInvoice command, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(scope, ct);
        ...
    }
}
```

`TenantScope` is an **explicit parameter on every application-service method**. It is not read from ambient state inside the handler. This is what makes a handler trivially testable against two tenants (§12) and what makes the background-job path (§10) identical to the request path instead of a special case.

---

## 5. Connection management and pooling

### 5.1 One `NpgsqlDataSource` per tenant, in a bounded LRU cache

Npgsql pools per connection string, so each tenant necessarily gets its own `NpgsqlDataSource`. Unbounded, that makes process memory and idle connections a function of tenant count. Therefore `ITenantDataSourceCache` is an **LRU bounded at 512 entries per process** (configurable). Eviction disposes the data source asynchronously after in-flight connections drain. The cache is the reason a process serving 5 000 tenants does not hold 5 000 pools.

### 5.2 Pool settings, and why each differs from the Npgsql default

| Setting | Value | Default | Why |
|---|---|---|---|
| `Minimum Pool Size` | `0` | 0 | An idle tenant must cost zero backends. Never raise this. |
| `Maximum Pool Size` | `10` (Web), `5` (Worker) | 100 | Per tenant, per process. Caps the blast radius of one tenant's slow queries; 100 per tenant would let a single tenant exhaust the cluster. |
| `Connection Idle Lifetime` | `30` s | 300 s | Returns backends quickly when a tenant goes quiet — the dominant cost driver in this model. |
| `Connection Pruning Interval` | `5` s | 10 s | Matches the shorter idle lifetime. |
| `Max Auto Prepare` | `0` (off) | 0 | Required by ADR-0004's transaction-pooling constraint. Re-evaluate only with PgBouncer ≥ 1.21 and explicit testing. |
| `Timeout` | `5` s | 15 s | Fail fast; a saturated pool should surface as an error, not as a 15-second hang holding a SignalR circuit. |
| `Command Timeout` | `30` s (Web), `300` s (Worker) | 30 s | Interactive work is bounded; batch work is not. |
| `Application Name` | `aurora-web:{tenantKey}` | — | Makes `pg_stat_activity` readable per tenant. Set in the connection string, **never** via `SET` (ADR-0004). Under PgBouncer, confirm `application_name` is tracked per client before relying on it. |

### 5.3 The arithmetic that matters

Worst-case backends against one cluster, without a proxy:

```
backends = (web instances + worker instances) x active tenants x max pool size
```

8 web + 4 worker instances, 200 concurrently-active tenants, pool 10 → **24 000 backends**. PostgreSQL is comfortable to roughly 500–1 000. The model fails not at "many tenants" but at "many *instances* × many *active* tenants", and that product grows the moment we scale out. This is why `../architecture/scalability.md` treats connection fan-out as bottleneck #1 and why PgBouncer is scheduled rather than deferred.

---

## 6. Scale limits, and the number at which this model must change

Stated plainly, because vagueness here becomes an outage later.

| Tenants per cluster | Topology | What is true |
|---|---|---|
| **≤ 100** | Direct Npgsql pooling, one cluster, no proxy. | Comfortable. Worst case ~12 instances × ~30 active tenants × 10 = 3 600 theoretical, but real concurrency keeps live backends in the low hundreds. Fleet migration wall-clock ≈ 100 × 8 s ÷ 8 parallel ≈ **2 minutes**. |
| **~1 000** | **PgBouncer in transaction pooling mode is mandatory.** `max_client_conn` ≈ 10 000, `default_pool_size` = 4 per (user, database), `max_db_connections` = 4, global `max_user_connections` capping total backends at ~600. | PgBouncer removes the *instance* multiplier: N app instances share one server-side pool per (user, database). It does **not** remove the linear term in tenant count — the pool is per (user, database) pair, so 600 concurrently-active tenants at 4 backends each is still 2 400 backends. **Active** tenants, not total tenants, is the real limit. Fleet migration ≈ 1 000 × 8 s ÷ 16 parallel ≈ **8 minutes**. |
| **~2 000** | Same, at the edge. | **Hard stop per cluster.** Beyond this, `pg_database`/`pg_class` catalog bloat, autovacuum scheduling across 2 000 databases, and a single WAL stream carrying every tenant's writes all degrade together. |
| **~10 000 total** | 10+ clusters, routed by `catalog.tenant.cluster_id`. Sharding is a routing-table update plus a database move — no code change (§3.5). | The binding constraint stops being connections and becomes **operational**: fleet migration wall-clock, backup windows, and the fact that a 1-in-1 000 migration failure rate means ~10 quarantined tenants on every release. §7's quarantine-and-continue behaviour is what makes this survivable. |

**The number at which the model must change: ~25 000 tenants total, or a median tenant ARR below roughly 10× the fully-loaded cost of a dedicated database, or fleet migration wall-clock exceeding 4 hours at maximum safe concurrency — whichever comes first.** At that point we add a **shared-schema tier with row-level security for the long tail** (option D) behind `ITenantConnectionResolver`, keeping dedicated databases for customers who pay for them. That is a new ADR superseding this one in part, not a rewrite: every module already receives its `TenantScope` explicitly, so the change is confined to the resolver, the factory and the migration runner.

**Design cap for this ADR: 1 000 tenants per cluster, 25 000 total** (assumption A1 in `../architecture/overview.md` §6).

---

## 7. Migration orchestration across every tenant database

### 7.1 Two version axes

A tenant database carries a **core schema version** (the union of module migrations, one `__EFMigrationsHistory` per module schema per ADR-0003 rule 1) and, per installed Country Package, a **package schema version** (ADR-0008 §4). The catalog records both per tenant. They move independently and both can be behind.

Order within one tenant: `platform` schema first, then module schemas in dependency order (§ `../architecture/modules.md`), then `pkg_*` schemas.

### 7.2 The expand/contract rule, enforced

`CLAUDE.md` requires "no destructive change in one step". The enforcement:

1. Every EF migration is annotated with a category: `[MigrationSafety(Category = Expand | Contract | DataOnly, Reason = "...")]`.
2. A **migration safety test** generates each migration's SQL and fails the build if it contains `DROP COLUMN`, `DROP TABLE`, `ALTER COLUMN ... TYPE`, `RENAME`, `ADD COLUMN ... NOT NULL` without a default, or `CREATE INDEX` without `CONCURRENTLY`, **unless** the migration is annotated `Contract` (for the first four) or `suppressTransaction: true` is used (for the last).
3. A **release gate test** reads the migration manifest and fails if a `Contract` migration ships in the same release as the `Expand` migration it contracts. A contract may only ship **one release after** its expand. This is the rule ADR-0003 rule 6 points at.
4. `CREATE INDEX CONCURRENTLY` requires `migrationBuilder.Sql(..., suppressTransaction: true)` and is mandatory for any index on a table that a live tenant writes to.

### 7.3 The runner

> **Refined by ADR-0027 §2–§3.** The direct `aurora_migrator` connection comes from `ITenantAdminConnectionFactory` and asserts `platform.tenant_identity` before any DDL; per-tenant execution (`ITenantSchemaMigrator`) and fleet orchestration are separate components.

State lives in the catalog:

```
catalog.migration_run(id, target_core_version, wave, state, started_at, finished_at, failure_budget)
catalog.migration_run_tenant(run_id, tenant_id, state, attempts, lease_owner, lease_expires_at,
                             started_at, finished_at, last_error, from_version, to_version)
```

`migration_run_tenant.state` ∈ `Pending | Leased | Running | Succeeded | Failed | Quarantined | Skipped`.

Workers claim work with `select ... from catalog.migration_run_tenant where run_id = $1 and state = 'Pending' order by tenant_id for update skip locked limit $2`, take a **lease** (5 min, heartbeated), then migrate. A crashed worker's lease expires and the tenant returns to `Pending`; `attempts` is incremented so a poison tenant cannot loop forever.

Within a tenant database, the runner takes a **session-level advisory lock** (`pg_advisory_lock(hashtext('aurora.migration'))`) so two runners can never migrate the same database. ADR-0004 forbids session-level locks — *for application code under transaction pooling*. The migration runner connects as `aurora_migrator` **directly to PostgreSQL, bypassing PgBouncer**, precisely so this lock is available. That is a deliberate, documented exception with its own connection path, not an oversight.

### 7.4 Resumability, partial failure, waves

- **Resumable:** re-running a run is idempotent. It picks up `Pending` and expired-lease tenants, skips `Succeeded`, and retries `Failed` while `attempts < 3`.
- **Partial failure does not stop the run.** A tenant that fails is set `Quarantined`, its `catalog.tenant.state` becomes `SchemaBlocked`, and **only that tenant** is served a maintenance page. Every other tenant keeps trading. This is the single most important property of the runner: with 10 000 tenants, some tenant will always fail, and a runner that halts the fleet on the first failure is a runner that can never finish.
- **Failure budget:** if quarantined tenants exceed `failure_budget` (default 0.5% of the wave, minimum 3), the run halts automatically and pages an operator. A systematic bug must not be applied to the whole fleet.
- **Waves:** `internal` (Aurora's own tenants) → `canary` (volunteers, ~1%) → `early` (10%) → `general` (100%). A wave must be `Succeeded` and soak for a configured period before the next starts.
- **Rollback is not a database operation.** A failed migration is fixed forward with a new migration. The only true rollback is a per-tenant restore (§11), and that loses committed business data, so it is an incident procedure, not a release procedure.

### 7.5 Version skew between code and schema

> **Superseded in part by ADR-0027 §4.** The check gates `TenantScope` opens only; the DDL path holds a `TenantDatabaseHandle` and has no scope factory to bypass. Without this the runner could never repair a `SchemaBlocked` tenant. The two constants live in `Aurora.Platform.Tenancy.Contracts`.

Because expand migrations deploy ahead of the code that uses them, **code version N must run correctly against schema versions N−1 and N**. Two constants ship with the code: `CurrentSchemaVersion` and `MinimumSupportedSchemaVersion` (= N−1).

`ITenantScopeFactory` checks on every scope open:

| Tenant schema version | Behaviour |
|---|---|
| `== CurrentSchemaVersion` | Normal. |
| `>= MinimumSupportedSchemaVersion` and `< Current` | Normal; the tenant is prioritised in the next migration wave. |
| `< MinimumSupportedSchemaVersion` | Scope open **fails**; tenant is `SchemaBlocked` and served a maintenance page; an alert fires. Better a visible maintenance page than silent queries against a schema the code no longer understands. |
| `> CurrentSchemaVersion` (code rolled back, schema did not) | Scope open **fails** on this instance; the instance reports version-behind and is drained. |

The check lives in the scope factory — one place, covering requests, jobs, outbox dispatch and operator access alike.

---

## 8. Tenant provisioning as an idempotent saga

`CREATE DATABASE` cannot participate in a transaction, so provisioning is a **saga**: a sequence of individually idempotent steps, each recorded in the catalog, resumable from any point, with a reaper for abandoned runs. This is the promise ADR-0004 makes; here it is, concretely.

```
catalog.tenant_provisioning(tenant_id, state, current_step, attempts, lease_owner, lease_expires_at, last_error)
catalog.tenant_provisioning_step(tenant_id, step, state, started_at, finished_at, detail)
```

| # | Step | Idempotency rule |
|---|---|---|
| 1 | `ReserveTenant` — insert `catalog.tenant` (state `Provisioning`), unique on `tenant_key`; choose `cluster_id` from `residency_region` + free capacity | Transactional; unique constraint makes a replay a no-op |
| 2 | `CreateDatabase` — `aurora_admin`, connected to the cluster's **maintenance database**, runs `CREATE DATABASE aurora_t_<key> OWNER aurora_migrator TEMPLATE template0 ENCODING 'UTF8'` | Catch SQLSTATE `42P04` (duplicate_database) and treat as success **only if** the existing database is owned by `aurora_migrator` **and** is either empty or already stamped with this `tenant_id`. Never adopt a database we cannot prove is ours. |
| 3 | `HardenDatabase` — `REVOKE ALL ON DATABASE ... FROM PUBLIC`; `GRANT CONNECT TO aurora_app`; `DROP SCHEMA public` (we use named schemas only); `CREATE EXTENSION IF NOT EXISTS btree_gist` (required by the effective-dating exclusion constraints in ADR-0008 §6.2) | All statements are `IF EXISTS`/idempotent |
| 4 | `StampIdentity` — create schema `platform`, create `platform.tenant_identity`, insert the single row | `insert ... on conflict do nothing`; verify the row matches this tenant or fail loudly |
| 5 | `MigrateSchema` — run all module migrations to `CurrentSchemaVersion` (§7) | EF migration history is itself the idempotency record |
| 6 | `SeedPlatformData` — default company, role set, permission catalogue, first admin membership, tenant settings | Natural keys + `on conflict do nothing` |
| 7 | `InstallPackages` — install the requested Country Packages (ADR-0008 §5.1) | Package installer is independently idempotent |
| 8 | `RegisterRouting` — write `catalog.tenant_host`, set `catalog.tenant.state = 'Active'`, record `core_schema_version` | Transactional |
| 9 | `Announce` — write `TenantProvisioned` to the **catalog** outbox | Outbox dedupe key = tenant id |

**Reaper.** A provisioning row in a non-terminal state with an expired lease is re-leased and resumed from `current_step`. After 5 attempts the tenant becomes `ProvisioningFailed` and an operator is paged. Compensation (`DROP DATABASE`) is **never** automatic and never blind: it runs only from the operator console, only for a tenant in `ProvisioningFailed`, and only after re-reading `platform.tenant_identity` and confirming it matches that tenant id.

**Target:** a new tenant is usable in under 60 seconds. The dominant cost is step 5; a pre-migrated `TEMPLATE` database is the optimisation if that target is missed — deliberately not done now, because template drift is its own failure mode.

---

## 9. The catalog database

### 9.1 Naming, to avoid a real collision

- The **catalog database** uses the schema `catalog`.
- **Each tenant database** has a schema `platform` (tenant identity, audit, outbox, settings, feature-flag overrides), plus one schema per module and `pkg_<id>` per package.

They are different databases; using different schema names makes every query and every migration unambiguous about which one it is talking to.

### 9.2 Tables

```
catalog.tenant(id, key, display_name, state, cluster_id, database_name, residency_region,
               core_schema_version, plan, created_at, activated_at, suspended_at,
               deletion_due_at, deleted_at, last_activity_at)
catalog.tenant_host(host pk, tenant_id, is_primary, verified_at)
catalog.database_cluster(id, region, host, port, maintenance_database,
                         admin_secret_ref, app_secret_ref, max_tenants, state)
catalog.subscription(id, tenant_id, plan, seats, valid_from, valid_to)
catalog.installed_package(tenant_id, package_id, version, state, installed_at, installed_by)  -- ADR-0008
catalog.identity_user(id, email_normalized unique, credential_ref, mfa_state, status, created_at)
catalog.user_tenant_membership(user_id, tenant_id, state, invited_at, accepted_at)
catalog.migration_run / catalog.migration_run_tenant                                          -- §7.3
catalog.tenant_provisioning / catalog.tenant_provisioning_step                                 -- §8
catalog.outbox                    -- platform-level integration events only
catalog.operator_audit_event      -- append-only; every operator action, incl. support access
catalog.erasure_replay_log        -- §11.5
catalog.feature_flag / catalog.feature_flag_tenant_override
quartz.*                          -- scheduler store (ADR-0014)
```

`catalog.tenant.state` ∈ `Provisioning | ProvisioningFailed | Active | Suspended | SchemaBlocked | Exporting | PendingDeletion | Deleted`.

### 9.3 The rule, and its one honest exception

**No tenant business data in the catalog.** No invoice, no customer, no ledger row, no product. If a platform feature seems to need one, the answer is a fan-out job that aggregates into a catalog-owned summary table, not a copy of the data.

The exception, stated openly: `catalog.identity_user` holds an email address, which is personal data. It is there because platform identity must be resolvable *before* a tenant is known — you cannot look up a user in a tenant database you have not yet chosen — and because an external accountant is one person across several tenants. The boundary is precise: the catalog holds **authentication** (who you are, how you prove it, which tenants you may enter); every tenant database holds **authorization** (what you may do there) and every per-tenant user profile. The GDPR consequence is handled in §11.5.

### 9.4 The catalog is a single point of failure, and is treated as one

Every request reads it. Mitigations: read-through `HybridCache` with a 60 s TTL on routing (a catalog outage degrades to serving already-cached tenants rather than a full outage); a hot standby with automatic failover; a hard rule that **no request-path query fans out across tenants**; and health probes that check the catalog and *this instance's* cached tenants only — never every tenant database, which would turn a health check into a fleet-wide connection storm.

---

## 10. Background jobs and integration events

This is where database-per-tenant leaks in practice: there is no HTTP request, no circuit and no ambient anything, so tenant context has to be carried deliberately. Three rules, all mechanically enforced.

### 10.1 Every tenant job carries its `TenantId` in its payload, by type

```csharp
public interface ITenantJob<TPayload> { Task ExecuteAsync(TenantScope scope, TPayload payload, CancellationToken ct); }
public interface IPlatformJob      { Task ExecuteAsync(CancellationToken ct); }   // no tenant data, ever

public interface ITenantJobScheduler
{
    Task EnqueueAsync<TJob, TPayload>(TenantId tenantId, TPayload payload, JobOptions? options, CancellationToken ct)
        where TJob : ITenantJob<TPayload>;
}
```

You cannot enqueue a tenant job without a `TenantId`, and you cannot execute one without receiving a `TenantScope` — the dispatcher opens the scope (`TenantAccessReason.Job`) and passes it in. The handler signature is identical to the request path (§4.5), so the same handler is reachable both ways and both are covered by the same tests.

`IPlatformJob` is the deliberately awkward alternative for fleet-level work (migration waves, provisioning, cluster capacity reporting). An **architecture fitness test asserts that no `IPlatformJob` implementation references any module's `.Domain`, `.Application` or `.Infrastructure` assembly.** A platform job that wants tenant data must enqueue tenant jobs; it may not read tenant data itself.

### 10.2 Recurring schedules fan out, they are not stored per tenant

One Quartz trigger per *schedule*, not per (tenant × schedule). The trigger fires an `IPlatformJob` that enumerates `catalog.tenant where state = 'Active'` and enqueues one `ITenantJob` per tenant, with a concurrency cap and jitter so 1 000 tenants do not start their nightly revaluation in the same second. This keeps the scheduler store O(schedules) instead of O(tenants × schedules) — the difference between ~30 triggers and ~300 000 at 10 000 tenants.

### 10.3 The outbox is per tenant, and dispatch must not fan out either

The transactional outbox (`platform.outbox`, ADR-0015) lives **in the tenant database**, written in the same transaction as the aggregate change. It has to: there is no distributed transaction between a tenant database and the catalog, and we will not introduce two-phase commit.

Naively, dispatch means polling N tenant databases — at 10 000 tenants, a connection storm every few seconds. Instead:

1. **In-process signal (low latency).** The instance that committed the write signals its local dispatcher immediately. Covers the common case in milliseconds.
2. **Activity-tiered sweep (correctness).** A platform job sweeps tenants by tier, read from `catalog.tenant.last_activity_at` (updated at most once per minute per tenant, from the tenant's own scope open): active in the last hour → every 5 s; last 24 h → every 60 s; otherwise → every 15 min. A tenant with no traffic costs no polling.
3. **Claiming** uses `for update skip locked` with a lease, so several dispatchers are safe.

Delivery is **at-least-once**; every consumer is idempotent on `(message_id)`. Ordering is guaranteed per aggregate only, via an ordering key, never globally.

### 10.4 Scope propagation, and the stale-scope trap

`TenantScope` is `IAsyncDisposable` and is **leased**. Using a disposed scope throws `TenantScopeExpiredException`. This exists to catch the classic bug: a request handler captures its scope in a closure, hands it to `Task.Run` or a queue, and the continuation runs minutes later against a tenant that has since been suspended, migrated or moved to another cluster.

Rules, enforced by fitness tests:
- A `TenantScope` is **never serialised** into a job payload, a message or a cache entry. Only `TenantId` crosses a process or queue boundary; the receiver opens a fresh scope.
- Integration events carry `TenantId` in their envelope, and the dispatcher asserts that the consumer's scope `TenantId` equals the envelope's. A handler cannot be invoked under a different tenant than the message's.
- **Cross-tenant integration events do not exist.** An event envelope has exactly one `TenantId`. Anything genuinely fleet-wide is a platform event in the catalog outbox and carries no tenant business data.
- In-process, cross-module events within one tenant are dispatched **after commit** with the producer's scope passed explicitly, never re-resolved.

---

## 11. Backup, restore, residency, offboarding and GDPR erasure

### 11.1 Backup

Two independent mechanisms, because they fail differently:

| Mechanism | Scope | RPO | Used for |
|---|---|---|---|
| Cluster PITR (continuous WAL archiving + weekly base backup) | Whole cluster | ≤ 5 min | Cluster loss; point-in-time recovery |
| Per-tenant logical dump (`pg_dump -Fc`), nightly, staggered | One tenant | ≤ 24 h | Single-tenant restore, offboarding export, migrating a tenant between clusters |

Backups are encrypted at rest, stored in the tenant's residency region, retention 35 days (statutory retention is a *tenant data* concern declared by the Country Package, not a backup concern — see ADR-0018).

### 11.2 Restoring exactly one tenant

**Never restore into the live database.** Always: restore into a new database, verify, then re-point `catalog.tenant.database_name`. Because routing is a catalog row, cut-over is a transaction and rollback is trivial.

- From a logical dump: `createdb` → `pg_restore` → verify `platform.tenant_identity` → re-stamp if the tenant id must change (clone for staging) → re-point routing. Typical SMB tenant: **RTO ≤ 1 hour**.
- To a point in time from cluster PITR: restore the *whole cluster* to a scratch instance at time T, `pg_dump` the one database, load it into a new database on the live cluster, re-point routing. This is expensive and slow (hours). It is the honest cost of sharing a WAL stream across tenants, and it is written here so nobody promises a customer a five-minute point-in-time restore.

A **quarterly restore drill** of one randomly-chosen tenant is a standing operational task. An untested backup is not a backup.

### 11.3 Data residency

`catalog.tenant.residency_region` → `catalog.database_cluster.region`. Routing never crosses regions, and a fitness test asserts the resolver rejects a cluster whose region differs from the tenant's.

The catalog is **regional** too: one catalog per region, plus a tiny **global directory** holding only `(tenant_key → region)` so a login can be routed. The global directory holds no personal data and no business data. Moving a tenant between regions is: suspend → dump → restore in the target region → re-point → delete source, i.e. the same machinery as §11.2.

### 11.4 Offboarding

> **Amended by [ADR-0042](ADR-0042-the-destroy-path-names-the-database-it-read.md) (2026-09-12).** The state machine below is unchanged. Two things in the `PendingDeletion` and `Deleted` bullets are not: **the order cannot execute** — `ALTER DATABASE … RENAME` fails while any session is connected and `REVOKE CONNECT` does not terminate one (ADR-0042 §1, executed) — and **neither statement can name a database from a claim**, because both run from the maintenance database and the tenant's identity is inside the database being destroyed (ADR-0034 §5's destroy row; the naming rule is ADR-0036 §4). ADR-0042 §3 gives the corrected five-step sequence including the pool eviction that precedes it, §4 refuses `DROP DATABASE … WITH (FORCE)` and gates the drop on the rename, and §5 makes the rename a saga step with a recorded intent. **Read ADR-0042 before implementing B-07.2.**

A state machine with deliberate delays, because irreversible operations should be hard to trigger by accident:

```
Active → Suspended (read-only, 30 days) → Exporting → PendingDeletion (30-day grace) → Deleted
```

- **Suspended:** `aurora_app` is restricted to read-only; the UI shows an end-of-service banner; jobs for the tenant are skipped.
- **Exporting:** produces (a) a `pg_dump -Fc` for portability back to us, and (b) an **open-format export** — CSV/JSON per aggregate plus the accounting-interchange format contributed by the tenant's Country Package (ADR-0008 extension point 6). Handed over as a signed, expiring download; the export bundle is itself audited.
- **PendingDeletion:** database renamed to `deleted_<key>_<date>`, `CONNECT` revoked from everyone but `aurora_admin`. Reversible for 30 days by renaming back.
- **Deleted:** `DROP DATABASE`, catalog row tombstoned (id, key, dates only), backups expire on their own schedule within 35 days. The deletion certificate — what, when, by whom, backup expiry date — is written to `catalog.operator_audit_event`.

### 11.5 GDPR: two different operations that are constantly confused

**(a) Tenant deletion** — the customer (the data controller) ends the contract. Answer: §11.4. Database-per-tenant makes this genuinely complete, which is the strongest argument for the model the product owner chose.

**(b) Data-subject erasure** — one individual asks a tenant to erase them. Deleting rows is **wrong**: an invoice naming that person is a statutory accounting record the tenant is legally required to keep, and posted ledger entries are immutable (`CLAUDE.md`). Answer: **pseudonymisation**.

- Personal identifiers are confined to designated places: `party.person`, `platform.audit_event.actor_display`, contact rows, and `catalog.identity_user` in the catalog. Erasure overwrites them with a tombstone (`"Erased subject 7f3a…"`) while keeping the surrogate key, so documents, ledger references and audit chains stay referentially intact and the accounting record stays balanced.
- Any property holding personal data is annotated `[PersonalData]`. A fitness test asserts every entity with a `[PersonalData]` property implements `IPseudonymisable` or appears in a reviewed exception list. This is how we avoid discovering a forgotten column during a regulator's deadline.
- **Erasure is deferred, not refused, while a statutory retention period covers the document.** The retention period is declared by the tenant's Country Package (extension point 9, ADR-0008). The request is recorded with a `due_at` and executed automatically when retention lapses; the data subject is told the date.
- **Backups cannot be edited.** An erasure is recorded in `catalog.erasure_replay_log`, retained longer than backup retention (35 days), and re-applied automatically after any restore. Restore-then-replay is the defensible answer; claiming we can reach into a backup is not.
- The erasure itself is audited — subject reference, tenant, requester, timestamp, fields affected — without logging the erased values.

Detail and legal framing: ADR-0018.

---

## 12. The tenant isolation test pattern

`CLAUDE.md` requires every module to prove isolation. A pattern that each module re-invents is a pattern each module gets subtly wrong, so the pattern is a **shared base class that modules inherit**, and a fitness test asserts every module inherits it.

### 12.1 The fixture

`TwoTenantDatabaseFixture` is an xUnit **collection fixture** (one PostgreSQL container per collection — container startup measured at ~9 s, `../architecture/testing-strategy.md`). It provisions, once per collection, through the **real provisioning saga** (§8): a catalog database and two tenant databases, A and B. Using the real saga means the tests also continuously prove provisioning works.

### 12.2 The mandatory contract

```csharp
public abstract class TenantIsolationContract<TFixture> where TFixture : TwoTenantDatabaseFixture
{
    protected abstract Task SeedAsync(TenantScope scope, CancellationToken ct);       // write one distinctive record
    protected abstract Task<IReadOnlyCollection<string>> ReadAllAsync(TenantScope scope, CancellationToken ct);
    protected abstract Task RunBackgroundJobAsync(TenantId tenantId, CancellationToken ct);
    protected abstract Task RaiseIntegrationEventAsync(TenantScope scope, CancellationToken ct);

    [Fact] public Task Data_written_in_A_is_not_readable_in_B();
    [Fact] public Task Data_written_in_B_is_not_readable_in_A();
    [Fact] public Task Background_job_for_A_writes_only_to_A();
    [Fact] public Task Integration_event_raised_in_A_is_handled_only_in_A();
    [Fact] public Task Reusing_a_disposed_scope_throws();
    [Fact] public Task Connecting_with_As_scope_to_Bs_connection_string_throws_TenantRoutingViolation();
}
```

The last test is the important one and the one developers would never think to write: it deliberately mis-routes — hands tenant A's `TenantScope` a connection string pointing at tenant B's database — and asserts the §4.3 identity check catches it. It proves the safety net itself still works, which is the only way to know the net has not quietly rotted.

**A module without a `TenantIsolationContract` subclass fails the architecture test suite.** Not a guideline.

### 12.3 Compile-time guarantees are tested as compile-time guarantees

> **Amended by [ADR-0033](ADR-0033-the-tenancy-trust-boundary-is-the-process.md) §5.1 (2026-09-12).** The list below is correct and unchanged. It is evidence about the compiler and about assembly metadata; it is **not** evidence about an adversary inside the process. ADR-0033 §3.1 records an executed demonstration that an assembly on nobody's `[InternalsVisibleTo]` list mints a valid, active `TenantScope` for an arbitrary tenant id, and ADR-0033 §5.6 D2 requires that demonstration to live in the repository so the residual cannot be silently believed closed.

The §4.1/§4.2 rules are verified by fitness tests over the assemblies, not by trying to compile bad code: no public/protected constructor on a tenant `DbContext`; no `AddDbContext*` registration of one; no `ITenantDbContextFactory<>` implementation outside `Aurora.Platform.Tenancy`; no `IHttpContextAccessor` outside the resolution middleware; no `TenantScope` field on a type that is registered as a singleton.

> **Amended by [ADR-0040](ADR-0040-the-tenancy-origination-set-is-the-boundary.md) §2, §3 and §5.2 (2026-09-12) — "compile-time" is the wrong word for this guarantee.** The tests below are correct about what they measure; the framing is not. `[UnsafeAccessor]` compiles clean in an assembly on nobody's friend list and runs the internal constructor (ADR-0040 §2.1 probe A, executed). Accessibility is enforced on the *signature of an accessor declaration*, never on the *target it resolves to* (probes B and C, executed).
>
> ADR-0040 also scopes the member-scan half: a fitness test over member *shapes* is a **design-integrity rule over the origination set's export surface**, not a security control, and it cannot be completed — `public static object Open()` is a door no signature scan can see (§3.5, executed). **Seven doors were found through it across five reviews; six were population defects, not verdict defects.** Two operative consequences for reviewers: a newly found member shape is a normal finding on the rule and **not** a re-opening of §3.4's guarantee (ADR-0040 §5.2), and the way to strengthen such a rule is ADR-0040 §3.2's inversion — enumerate every member at every accessibility, make *unreachability* the thing that must be proved, and report the excluded count — not an eighth shape term. The rule this section's framing actually creates is ADR-0040 §4.6: **no type in `src/` declares an `[UnsafeAccessor]` member**, repository-wide, because the same declaration reaches every `internal` member of every assembly.

---

## 13. Consequences

**Positive**

- Isolation is enforced by PostgreSQL itself. The worst application bug we can write cannot read another tenant's rows, because the connection cannot see them. Quality attribute #2 becomes a property of the deployment rather than of our diligence.
- Per-tenant backup, restore, export, region move and deletion are first-class one-liners. GDPR tenant deletion is genuinely complete, which is difficult to claim honestly under shared-schema.
- Noisy neighbours are bounded: a runaway query burns one database's share of the cluster, and per-tenant pool caps (§5.2) bound it further.
- Data residency is a routing decision, not a re-architecture.
- The EF model stays tenant-invariant (ADR-0003 rule 2), so one compiled model serves the fleet.

**Negative, and owned**

- **Connection fan-out is the defining constraint.** It forces PgBouncer at ~1 000 tenants, caps a cluster at ~2 000, and makes every "just poll all tenants" idea a production incident. §10.2 and §10.3 exist entirely because of this.
- **Every fleet operation is now a distributed job** with leases, partial failure and quarantine. The migration runner and provisioning saga are real software we must build, test and operate — a cost option A would not have incurred. This is the largest single line item in the bootstrap milestone.
- **No cross-tenant query.** Operator reporting, usage billing and fleet analytics are fan-out jobs writing into catalog summary tables. Ad-hoc "how many invoices did we process yesterday?" is not one SQL statement.
- **Per-tenant idle cost is non-zero** (database catalogs, autovacuum, backup slot, migration time). §5.2's `Minimum Pool Size=0` and short idle lifetime keep the connection component near zero; the rest is inherent. A free tier of thousands of dormant tenants is not viable on this model — which is the same finding as §6's change threshold.
- **A single-tenant point-in-time restore is slow** (§11.2). This must be reflected in whatever we tell customers; the fast path is the nightly logical dump with a 24 h RPO.
- **The catalog is a shared dependency on the request path** and must be treated as tier-0 infrastructure (§9.4).

**Disagreement on the record.** See §2. The architect's recommendation had been shared-schema with RLS plus a promotion path. The product owner chose stronger isolation. The mitigations are: `ITenantConnectionResolver` as the single seam (§3.5), the explicit change threshold in §6, and `TenantScope` passed explicitly everywhere so that a future tiered model changes the platform and not the modules.

---

## 14. Revisit when

- Tenant count approaches **1 000 on any single cluster** (introduce PgBouncer and cluster sharding — planned, not a new decision), or **25 000 in total** (new ADR for a tiered hybrid, superseding §2's option evaluation).
- Fleet migration wall-clock exceeds **4 hours** at maximum safe concurrency.
- A prospective customer requires a point-in-time restore RPO better than 24 hours for a single tenant (would force per-tenant WAL streams, i.e. a database cluster per large tenant).
- PostgreSQL ships a materially better connection model (a built-in pooler or a threaded backend), which would move every number in §6.
- A jurisdiction requires residency we cannot satisfy with a regional cluster.
