# ADR-0014 — Background jobs and scheduling

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0004 (PostgreSQL), ADR-0007 §10, ADR-0015 (outbox)

## Context

Aurora needs two different things that are often conflated:

- **Scheduling** — "run the FX revaluation at 02:00 on the last day of the month", fleet-wide, few in number, needs cron semantics, misfire handling and a clustered store.
- **Work items** — "recalculate valuation for tenant 42 after this receipt", per tenant, high in number, must be enqueued **transactionally with tenant business data**, and must never be visible to another tenant.

ADR-0007 §10 already fixes the constraints: a tenant job carries its `TenantId` by type, recurring schedules fan out rather than being stored per (tenant × schedule), and nothing may poll every tenant database.

## Options considered

| Option | Licence (verified 2026-09-11) | Pros | Cons |
|---|---|---|---|
| **Quartz.NET 4.0.1 for scheduling + our own per-tenant queue in the tenant database** *(chosen)* | **Apache-2.0**; 4.0 released 2026-09-03 targeting .NET 10 | Quartz is the mature, boring cron scheduler with clustering, misfire policies and an ADO job store; the per-tenant queue is a small table with `FOR UPDATE SKIP LOCKED`, and it is the only way to enqueue a job **in the same transaction as the business change** that caused it | Two mechanisms; we own the queue code (roughly 300 lines) |
| Hangfire | **LGPLv3** | Excellent dashboard; simple API | LGPL is not permissive; `CLAUDE.md` requires MIT/Apache-2.0/BSD. **Rejected on licensing.** Its storage model is also one shared job store, which fits database-per-tenant badly |
| MassTransit scheduling / job consumers | **Commercial from v9** (announced 2025; v8 security patches through 2026) | Mature, feature-rich | Fails the licence rule going forward, and brings a broker we do not need |
| Only `BackgroundService` + a hand-rolled timer | In-box | No cron semantics, no clustering, no misfire handling; every scheduling bug is ours to rediscover |
| PostgreSQL `pg_cron` | PostgreSQL licence | Runs in the database | Per-database extension across thousands of tenant databases; schedules become fleet state we cannot see in one place; needs superuser |

## Decision

**Quartz.NET for *when*, our own tenant-scoped queue for *what*.**

### 1. Scheduling (Quartz.NET, catalog database)

- Quartz's ADO job store lives in the **catalog**, schema `quartz`, clustered so several `Aurora.Worker` instances share it safely.
- **One trigger per schedule, never per (tenant × schedule)** (ADR-0007 §10.2). A trigger fires an `IPlatformJob` that enumerates active tenants and enqueues one tenant job each, with a concurrency cap and jitter. At 10 000 tenants this is ~30 triggers instead of ~300 000.
- Misfire policy is explicit per schedule. The default — fire immediately on recovery — is wrong for a month-end revaluation that missed its window; those use "do nothing and alert".

### 2. Work items (per-tenant queue, tenant database)

```
platform.job_queue(id, type, payload jsonb, state, run_at, attempts, max_attempts,
                   lease_owner, lease_expires_at, idempotency_key, correlation_id,
                   created_at, started_at, finished_at, last_error)
platform.job_dead_letter(...)
```

- **Enqueue is transactional with the business change.** This is the whole reason the queue lives in the tenant database and not in the catalog: a job that says "post the COGS for this shipment" must not exist if the shipment rolled back, and must always exist if it committed.
- Claim with `... where state='Ready' and run_at <= now() order by run_at for update skip locked limit n`, plus a heartbeated lease. A crashed worker's lease expires and the item returns to `Ready`.
- **Retry** with exponential backoff and jitter (`2^attempts` seconds, capped at 1 hour), `max_attempts` default 5, then dead-letter with an alert and tenant-visible status. A poison job must never spin.
- **`idempotency_key` is unique per tenant** so an at-least-once trigger produces one job.
- Dispatch is driven by the same activity-tiered sweep as the outbox (ADR-0007 §10.3) — an idle tenant costs no polling.
- Long-running jobs heartbeat and honour cancellation; a job that cannot report progress within its lease period is a job that needs splitting.

### 3. Rules

- `ITenantJob<TPayload>.ExecuteAsync(TenantScope scope, TPayload payload, ct)` — the scope is a parameter, identical to the request path, so the same handler is reachable and tested both ways (ADR-0007 §4.5).
- `IPlatformJob` may not reach tenant data (fitness rule T8).
- A fitness test asserts every registered job type has exactly one handler and a serializable payload that does **not** contain a `TenantScope` (rule T6).
- Jobs run in `Aurora.Worker`, never in `Aurora.Web`. A Blazor circuit that needs long work dispatches a job and subscribes to progress (ADR-0005 rule 5).
- Every job execution is traced with the tenant and correlation id of whatever caused it (ADR-0016).

## Consequences

- Positive: transactional enqueue removes the "job exists but its data does not" class of bug entirely.
- Positive: the queue is in the tenant's database, so it is isolated, backed up, restored and deleted with the tenant — no separate cleanup path on offboarding.
- Positive: Apache-2.0 throughout, with an actively maintained scheduler that already targets .NET 10.
- Negative: no Hangfire-style dashboard. We build a modest operator view over `platform.job_queue` and `catalog.migration_run` — a named backlog item, not a free feature.
- Negative: two mechanisms to understand. The rule is simple: *anything on a clock* is Quartz; *anything caused by a business change* is a tenant job.
- Negative: fan-out enqueue at 10 000 tenants is itself a job that takes minutes. It is chunked, resumable and rate-limited, like every other fleet operation in this system.

## Revisit when

Queue throughput per tenant exceeds what a single table with `SKIP LOCKED` handles comfortably (thousands per minute per tenant — well beyond assumption A4), or fleet-wide scheduling needs cross-tenant coordination Quartz cannot express.
