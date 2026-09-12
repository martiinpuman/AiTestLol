# Scalability model

Status: accepted v1 · Author: architect · Date: 2026-09-11
Companion: `overview.md` §4 and §6, `../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §5–§6, `../decisions/ADR-0005-frontend-blazor-server.md`

> **The headline: this system does not get into trouble because a tenant is big. It gets into trouble because there are many tenants and each one is a separate database.** Every number below follows from that.

---

## 1. Assumed load

Figures are planning assumptions, each traceable to `overview.md` §6. They are stated so they can be falsified by the first ten tenants' telemetry rather than argued about.

| Dimension | Small tenant | Median tenant | Large tenant | Fleet design cap |
|---|---|---|---|---|
| Employees | 10 | 60 | 250 | — |
| Named ERP users (~40% of employees) | 4 | 25 | 100 | — |
| Peak concurrent users (~40% of named, A3) | 2 | 10 | 40 | — |
| Business documents per day (A4) | 20 | 150 | 800 | p99 2 000 |
| Lines per document | 3 | 5 | 50 (p95) | — |
| Journal lines per year | ~20 k | ~225 k | ~2.5 M | — |
| Database size after 5 years | < 1 GB | 2–5 GB | 30–60 GB | — |
| Tenants per cluster | — | — | — | **1 000 design, 2 000 hard stop** |
| Tenants total | — | — | — | **25 000 (A1)** |

**The reassuring half of this table:** a median tenant's database is a few gigabytes with a couple of million rows in its largest table. PostgreSQL does not notice that. Nothing in a single tenant's workload is difficult.

**The difficult half:** 1 000 of those databases share one cluster's connection budget, one WAL stream, one autovacuum scheduler and one migration window.

Report sizes we design for: trial balance ~2 000 rows (interactive), aged receivables ~5 000 rows (interactive), general ledger detail for a year ~250 000 rows (job), stock movement ledger ~500 000 rows (job), full data export (job, streamed).

---

## 2. Growth stages and their triggers

| Stage | Trigger | Topology | Work required |
|---|---|---|---|
| **S0 — bootstrap** | < 50 tenants | 1 web, 1 worker, 1 PostgreSQL cluster, in-memory cache. No proxy | None. This is the shipping configuration |
| **S1 — multi-instance** | > 300 concurrent circuits, or the first availability requirement | 2–4 web (sticky routing), 2 workers, same cluster. HybridCache gains an L2 (Valkey, ADR-0012) | Session affinity at the load balancer; L2 cache |
| **S2 — pooled** | **> 400 tenants on a cluster, or > 300 sustained backends** | PgBouncer in transaction pooling mode in front of the cluster (§4.3) | PgBouncer deployment and config; verify no session-state usage crept in |
| **S3 — sharded** | > 1 000 tenants on a cluster | Several clusters, routed by `catalog.tenant.cluster_id`; regional clusters for residency | A tenant-move procedure (dump/restore/re-point — already built for restore, ADR-0007 §11.2); capacity-aware placement in the provisioner |
| **S4 — read-offloaded** | Reporting load visibly affects transactional p95 | Read replicas per cluster; read models and report jobs target the standby via `NpgsqlMultiHostDataSource` | Replica routing in `ITenantConnectionResolver`; a rule that postings never read from a standby |
| **S5 — model change** | **> 25 000 tenants, or median tenant ARR below ~10× the fully-loaded cost of a dedicated database, or fleet migration wall-clock > 4 h** | A shared-schema tier with row-level security for the long tail, behind `ITenantConnectionResolver` | A new ADR superseding ADR-0007 §2's evaluation. Not a rewrite: modules already receive `TenantScope` explicitly |

Large-tenant escape hatch, available from S3 onward: any tenant exceeding **50 GB, 100 concurrent users or 2 000 documents/day** is moved to its own cluster. That is a routing-row change plus a database move — no code, no redesign. It is also the honest answer to "can you handle our biggest prospect?"

---

## 3. Blazor Server: circuits, memory and the per-instance ceiling

ADR-0005 accepts one **SignalR circuit per browser tab** with component state held on the server. That converts a user from a stateless request source into a resident memory cost, so the ceiling must be a number, not a feeling.

### 3.1 What a circuit costs

| Component | Budget |
|---|---|
| Circuit baseline (renderer, component tree, per-circuit DI scope) | ~250 KB |
| A data-dense ERP screen: grid page (≤ 100 rows) + filters + saved-view state + form model | 0.5–1 MB |
| **Planning budget per active circuit** | **1.5 MB** |
| Retained *disconnected* circuit (`DisconnectedCircuitMaxRetained` = 100, 3 min) | Full circuit state — these are not free |
| Retained *persisted* circuit state (.NET 10; `PersistedCircuitInMemoryMaxRetained` = 1 000, 2 h in memory / 8 h distributed) | 20–50 KB each → ~50 MB at the default of 1 000 |

### 3.2 The ceiling

For a **4 GiB / 2 vCPU** instance:

```
4096 MiB
- ~800 MiB   runtime, GC headroom, EF models, HybridCache L1, Npgsql data sources
- ~150 MiB   retained disconnected + persisted circuit state at defaults
= ~3100 MiB  available for active circuits
/ 1.5 MiB    per circuit
≈ 2000 circuits theoretical
```

**The stated ceiling is 1 000 concurrent circuits per 4 GiB / 2 vCPU instance, with an alert at 700 and autoscale at 800.** The theoretical figure is halved because GC pause behaviour, render-batch buffers and burst allocation degrade long before memory is literally exhausted, and because an ERP instance that is 95% full has no room to absorb a reconnect storm.

**CPU is not the binding constraint.** At roughly 5 ms of CPU per interaction and ~6 interactions per minute per active user, 1 000 circuits consume ~500 ms of CPU per second — a quarter of two vCPUs. Memory and database connections bind first, which is why the scaling lever is instance count rather than instance size.

### 3.3 Reconnect behaviour, stated honestly

- A network blip suspends the UI. The reconnection banner (`docs/design/app-shell.md`) is mandatory and editable controls freeze until the circuit is confirmed back — a user must never type for ten minutes into a dead circuit.
- Within `DisconnectedCircuitRetentionPeriod` (3 min, default kept) the **same** instance resumes the circuit with full state.
- Beyond that, .NET 10's persisted circuit state lets a reconnect land on a **different** instance with the state components explicitly declared as persistent. This is a genuine resilience gain over .NET 8 and it is why we do not raise the disconnected retention period: raising it multiplies full-state memory, while persisted state costs kilobytes.
- **Sticky routing is still required while connected** (ADR-0025 §2.1). An active circuit cannot move.
- A rolling deployment disconnects every circuit it touches. Deploy with draining, and prefer low-traffic windows; this is a real operational cost of the locked UI choice.

### 3.4 Rules that keep the number true

1. Component state holds identifiers and one page of data — never a result set, never a loaded aggregate graph (ADR-0005 rule 4).
2. Every grid pages server-side, ≤ 100 rows per page; the grid API makes any other shape impossible (ADR-0024 rule 4).
3. Anything longer than a second becomes a job (§5).
4. Circuit memory is measured and alerted per instance (ADR-0016 §5).

---

## 4. Connection fan-out — bottleneck #1

PostgreSQL allocates **one backend process per connection** (ADR-0004). Under database-per-tenant, connections multiply by both instance count and active-tenant count. This is the constraint that shapes the whole design.

### 4.1 The arithmetic without a proxy

```
backends = (web instances + worker instances) x active tenants x max pool size per tenant
```

8 web + 4 worker instances, 200 concurrently-active tenants, pool size 10 → **24 000 backends**. PostgreSQL is comfortable to roughly 500–1 000. The model fails not at "many tenants" but at "many *instances* × many *active* tenants", and that product grows precisely when we scale out to handle growth.

### 4.2 What we do before a proxy (S0–S1)

Per-tenant pool settings from ADR-0007 §5.2 — `Minimum Pool Size=0`, `Connection Idle Lifetime=30 s`, `Maximum Pool Size=10` (web) / `5` (worker) — plus the bounded LRU data-source cache (512 per process). An idle tenant costs **zero** backends; that is what makes a long tail of quiet tenants survivable at all.

### 4.3 PgBouncer, and what it does *not* fix

At stage S2, PgBouncer in **transaction pooling** mode sits in front of each cluster:

```
pool_mode = transaction
max_client_conn = 20000          # cheap: these are just sockets
default_pool_size = 4            # per (user, database) pair
min_pool_size = 0
reserve_pool_size = 2
reserve_pool_timeout = 3
max_db_connections = 6           # hard cap per tenant database
server_idle_timeout = 60
server_lifetime = 900
```

**What it fixes:** the *instance* multiplier. Twelve app instances now share one server-side pool per (user, database), so the formula loses a term.

**What it does not fix — and this is the point people miss:** PgBouncer's pool is **per (user, database) pair**. With one database per tenant, the sum across databases is still linear in the number of *active* tenants. 600 concurrently-active tenants at 4 backends each is 2 400 backends — still over the line. **Active tenants per cluster, not total tenants, is the real limit**, and PgBouncer raises it by roughly an order of magnitude rather than removing it.

**Constraints transaction pooling imposes on application code** (ADR-0004, restated because violating them works perfectly in development and fails in production):

- No `LISTEN`/`NOTIFY`.
- No session-level advisory locks. The migration runner needs them, so it connects **directly, bypassing PgBouncer** (ADR-0007 §7.3).
- No `SET` outside a transaction. `application_name` is a connection-string parameter, not a `SET`.
- `Max Auto Prepare = 0` unless PgBouncer ≥ 1.21 prepared-statement support is explicitly configured and tested.
- No cursors held across transactions.

### 4.4 The fan-out traps, and the rules that prevent them

Each of these looks harmless in code review and is a fleet-wide connection storm in production:

| Trap | Rule |
|---|---|
| A readiness probe that checks every tenant database | Probes touch the catalog and this instance's warm tenants only (ADR-0016 §6) |
| An outbox or job dispatcher polling every tenant every few seconds | Activity-tiered sweep; an idle tenant is polled every 15 minutes, not every 5 seconds (ADR-0007 §10.3) |
| One scheduler trigger per (tenant × schedule) | One trigger per schedule; a platform job fans out with a concurrency cap and jitter (ADR-0014) |
| "How many invoices did the fleet process yesterday?" as an ad-hoc query | Fan-out job writing into catalog summary tables. There are no cross-tenant queries (ADR-0007 §13) |
| A nightly job starting for all tenants at 02:00:00 | Jitter is mandatory on every fan-out enqueue |
| Per-tenant metric labels | `tenant.id` is a trace and log dimension, never a metric dimension (ADR-0016 §4) |

### 4.5 Migration wall-clock

The other fleet-shaped cost. At roughly 8 seconds of migration per tenant:

| Tenants | Parallelism | Wall clock |
|---|---|---|
| 100 | 8 | ~2 min |
| 1 000 | 16 | ~8 min |
| 10 000 | 32 | ~42 min |
| 25 000 | 32 | ~1 h 45 min |

Parallelism is capped by the connection budget, not by worker count — each concurrent migration holds a direct connection. **Exceeding 4 hours is an S5 trigger** (§2).

---

## 5. Long-running work: reports, imports, period close

**The threshold is explicit: any operation expected to return more than 10 000 rows or to run longer than 2 seconds is a job, not a request.**

- Interactive reports (trial balance, aged receivables, stock on hand) run inline against bounded queries with a hard row cap and a server-side page.
- Everything larger — general ledger detail, stock movement history, statutory returns, data exports, period close, FX revaluation, bulk imports — is dispatched to `Aurora.Worker` (ADR-0014). The circuit gets a job handle and subscribes to progress; it never blocks (ADR-0005 rule 5).
- Large outputs are **streamed**, not materialised: `IAsyncEnumerable` over a server-side cursor written straight to the output stream. A fitness test flags `ToListAsync` without a preceding `Take` (rule Q1) precisely to stop the other pattern appearing.
- Command timeouts differ by host on purpose: 30 s in `Aurora.Web`, 300 s in `Aurora.Worker`. A web request that needs five minutes is a design error, and the timeout should say so.
- Job results are stored per tenant with an expiry and delivered as a signed, expiring download.

---

## 6. The EF model, compiled models and cold start

**The model must be tenant-invariant** (ADR-0003 rule 2). This is a scalability rule, not a style rule: EF caches a model per context type per *model cache key*, so a model that varied by tenant would make process memory `O(tenants × contexts)` — thousands of models, each 1–5 MB. That is the difference between a 3 GB instance and an OOM loop.

- **A custom `IModelCacheKeyFactory` is banned**, asserted by a fitness test. This is the single change that would silently destroy the property above.
- **Country Packages do not vary the core model.** A package brings its **own** `DbContext` over its own `pkg_<key>` schema (ADR-0008 §4.1), so the installed-package set never enters a core model's cache key. Without this rule, the number of distinct core models would be the number of distinct package combinations.
- Model building costs ~50–200 ms per context; with roughly a dozen module contexts a cold instance spends 1–2 seconds building models **once per process** (not per tenant). **Compiled models (`dotnet ef dbcontext optimize`) are required before the first production release** and are generated in the build, because that second is paid on every deployment of every instance.
- The `NpgsqlDataSource` LRU cache (512 per process, ADR-0007 §5.1) keeps per-tenant connection objects bounded. Eviction thrash is a monitored metric — if it climbs, the working set of active tenants per instance exceeds the cache and either the cache grows or routing gets more affinity.

---

## 7. Caching and read models

- **Cache** (ADR-0012): tenant routing, permission sets, package sets, account-role mappings, localization. Every key is tenant-prefixed; nothing financial is cached; cache-aside only, so an empty cache is always correct and merely slower.
- **Read models** live in the `reporting` schema of each tenant database, built from integration events (ADR-0015): aged receivables, stock availability by warehouse, sales by customer and period, open-order value.
- **Every read model must be rebuildable from its sources by a replay job.** A read model that cannot be rebuilt has quietly become a store of record, and the next bug in its projection becomes unrecoverable data loss. This is a hard rule with a test per read model.
- Read models are eventually consistent, measured in milliseconds. Screens that require read-your-writes read from the producing module instead — that choice is made per screen, deliberately, and is noted in the screen spec.

---

## 8. Database-level scaling

- **Indexes:** every foreign key and every declared query filter is indexed, asserted by comparing the EF model against `pg_indexes` (`testing-strategy.md` §10). Index creation on live tables uses `CONCURRENTLY` (rule MIG4).
- **Partitioning is not the default.** `audit.audit_event` and `ledger.journal_entry_line` become monthly-partitioned **per tenant, only when that tenant exceeds ~5 million rows** in the table. Partitioning every tenant by default would put 84 partitions × 1 000 tenants into one cluster's catalogs — the exact catalog bloat that makes stage S3's hard stop arrive early.
- **Autovacuum tuning on churn tables.** `platform.outbox` and `platform.job_queue` are insert-then-delete workloads and are classic bloat sources. Per-table aggressive autovacuum settings, batched deletes, and a lower `fillfactor` on the queue tables. This is monitored per cluster, because the failure mode — steadily growing dead tuples across a thousand databases — is invisible until it is severe.
- **`numeric` over `float` everywhere** (ADR-0004 rule 3) costs some arithmetic speed. Irrelevant at these volumes and non-negotiable regardless.
- **Read replicas** at S4, used only by read models, report jobs and exports. Postings never read from a standby: replication lag plus a balance check is a correctness bug waiting to happen.

---

## 9. What we measure, and what each threshold triggers

| Metric | Watch | Act |
|---|---|---|
| Server backends per cluster | > 300 sustained | Stage S2: deploy PgBouncer |
| Tenants per cluster | > 400 / > 1 000 | S2 / S3 |
| PgBouncer `cl_waiting`, `maxwait` | any sustained wait | Raise `default_pool_size` or shard the cluster |
| Concurrent circuits per web instance | > 700 | Scale out; hard ceiling 1 000 (§3.2) |
| Web instance working set | > 75% | Scale out before GC degrades |
| Data-source cache evictions per minute | climbing | Active-tenant working set exceeds the cache; grow it or add routing affinity |
| p95 interactive latency | > 500 ms | Investigate before it becomes 2 s |
| Outbox lag (oldest undispatched row) | > 60 s | Dispatcher capacity or a poison message |
| Job queue oldest ready item | > 5 min | Worker capacity |
| Fleet migration wall-clock | > 2 h / > 4 h | Raise parallelism / stage S5 |
| Largest tenant database size | > 50 GB | Move that tenant to its own cluster |
| Dead tuple ratio on `outbox` / `job_queue` | > 20% | Autovacuum tuning on that cluster |

---

## 10. What we deliberately do not do

- **No sharding inside a tenant.** A tenant that outgrows one database is out of scope for this product tier (assumption A4); the answer is its own cluster, not a distributed schema.
- **No cross-tenant queries, ever.** Fleet reporting is a fan-out job into catalog summary tables. This is a real cost of the isolation model and it is worth paying.
- **No distributed cache at bootstrap.** L1 in-memory until more than one instance exists (ADR-0012).
- **No microservices** (ADR-0006). Reporting and DocumentExchange are the only plausible extractions, and neither is planned.
- **No event sourcing for the core domain** (ADR-0003). Accountants query current state relationally; that is what the ledger is.
- **No premature partitioning, no premature replicas, no premature broker.** Each has a stated trigger above. Adding them early costs operational complexity across every tenant database and buys nothing measurable.
