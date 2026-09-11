# ADR-0028 — The tenant audit store: schema, append-only enforcement and the write path

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Supersedes:** ADR-0004 rule 5's table name (`platform.audit_event`), ADR-0007 §9.1's prose placing audit in the `platform` schema, and the same table name where it appears in ADR-0007 §11.5. Everywhere those documents say `platform.audit_event`, read `audit.audit_event`. ADR-0018's decisions are unchanged; this ADR fixes the mechanics it left to the implementer.
- **Superseded by:** —
- **Related:** ADR-0018 (audit, retention, erasure), ADR-0004 rule 5, ADR-0007 §4.3 / §9.2, ADR-0027 (`TenantAccess`), `../product/specs/SPEC-001-tenant-provisioning-and-first-login.md` BR-7/AC-6, `../reviews/B-05.md` finding M-1

## Context

The project-manager could not find a bootstrap task that builds a tenant-side audit write path, and asked whether it is implicit in B-07.2's `platform` schema work. It is not, and three documents disagree about where the table even lives:

- ADR-0004 rule 5 names `platform.audit_event`;
- ADR-0007 §9.1 says the tenant `platform` schema holds "tenant identity, audit, outbox, settings, feature-flag overrides";
- ADR-0018 §1 and `../architecture/modules.md` §4 name `audit.audit_event` in a schema `audit` owned by an Audit module.

Meanwhile SPEC-001 BR-7/AC-6 requires an audit event **inside the new tenant** at provisioning, SPEC-002 BR-5 requires one for a business write, `CLAUDE.md` requires every financial change to be audit-logged, and nothing in B-01 … B-15 builds `IAuditWriter`, the table, the append-only enforcement or the hash chain. `src/platform/` contains no projects at all today. A walking skeleton that cannot write an audit event does not demonstrate the property this product is sold on.

The B-05 review (finding M-1) makes the risk concrete rather than theoretical: `ALTER DEFAULT PRIVILEGES` granted `aurora_app` `DELETE` on every future table in the catalog schema, so `catalog.operator_audit_event` — ADR-0007 §9.2's "append-only" operator trail — was provably deletable by the request-path role. An append-only table is append-only because of a mechanism, never because of its name.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Own schema `audit`, privilege policy applied at schema granularity, plus a trigger** *(chosen)* | `REVOKE UPDATE, DELETE ON ALL TABLES IN SCHEMA audit FROM aurora_app` **and** `ALTER DEFAULT PRIVILEGES … IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM aurora_app` make a table added to that schema next year append-only by default instead of by memory; the trigger catches the owner role too | One more schema per tenant database |
| Audit table inside `platform` (ADR-0004 rule 5 / ADR-0007 §9.1 as written) | One fewer schema | `platform` also holds `outbox`, `setting`, `feature_flag_override`, `job_run` — all of which **must** be `UPDATE`/`DELETE`-able by `aurora_app`. A schema-wide append-only policy is therefore impossible there, and every append-only guarantee falls back to a per-table grant someone must remember. That is exactly the B-05 M-1 failure, pre-installed |
| Audit in a separate database per tenant | Physically separable retention | A second database to provision, migrate, back up and restore per tenant, and no transactional write with the change it records — which ADR-0018 requires |

## Decision

### 1. Location

`audit.audit_event`, in the **tenant** database, schema `audit`, owned by the Audit platform module (`modules.md` §4). ADR-0018 and `modules.md` were already right; ADR-0004 rule 5 and ADR-0007 §9.1 are corrected by this ADR. The columns are ADR-0018 §1's, unchanged.

### 2. Append-only, enforced three ways, each tested as the runtime role

1. `REVOKE UPDATE, DELETE ON ALL TABLES IN SCHEMA audit FROM aurora_app`;
2. `ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM aurora_app` — so a table added to `audit` later inherits the policy;
3. a `BEFORE UPDATE OR DELETE` trigger that raises, which also catches `aurora_migrator` (the owner, whom privileges do not restrain).

**The tests connect as `aurora_app` and as `aurora_migrator` and assert the statement fails.** A test that runs as the fixture's superuser, or that asserts the `REVOKE` statement was executed rather than probing `has_table_privilege` and running the `DELETE`, is the test that passed on B-05 while the table was deletable. The same three-way enforcement is retrofitted to `catalog.operator_audit_event` and `catalog.erasure_replay_log` (B-05 M-1's fix).

### 3. Partitioned from day one

`audit.audit_event` is RANGE-partitioned monthly on `occurred_at`, with the next two months pre-created by a platform job and a `DEFAULT` partition that a health check asserts is empty. Every primary key and unique constraint includes `occurred_at`, because PostgreSQL requires the partition key in them — which is precisely why this cannot be retrofitted cheaply later. ADR-0018 already names monthly partitioning as the mitigation for audit volume; converting the largest table in a tenant database to a partitioned one, across every tenant, under lock, is not a migration we are willing to owe ourselves. The chain order is `(occurred_at, sequence)`.

### 4. The write path

`IAuditWriter` in `Aurora.Platform.Audit.Contracts`:

```
Task WriteAsync(TenantAccess access, DbContext transaction, AuditEvent e, CancellationToken ct);
```

- **`TenantAccess`, not `TenantScope`** (ADR-0027 §1), so the same writer serves a request, a job and the provisioning saga's own step 8 — which must write a tenant-side event before any user has ever signed in to that tenant.
- **The caller's `DbContext` is a required parameter.** ADR-0018 requires the audit row to be written in the same transaction as the change it records; passing the transaction owner makes that structural instead of a convention a reviewer has to catch. Two tests: a rolled-back business transaction leaves zero audit rows, and a forced audit-write failure rolls back the business change.
- **The actor is an explicit parameter**, `AuditActor(ActorType, ActorId?, Display)` — never read from ambient state. Provisioning's actor is the operator who requested it; there is no signed-in user, and an audit writer that can only describe a signed-in user cannot record provisioning, package installs or jobs.

### 5. The hash chain is specified, not left to the implementer

`hash = SHA256(prev_hash || canonical(row))`, `prev_hash` of a tenant's first row = 32 zero bytes. `canonical` is pinned in the ADR-level sense: a fixed, ordered field list; UTF-8; instants as RFC 3339 UTC with microsecond precision; decimals in invariant format with no trailing-zero normalisation; a single documented null sentinel. An unpinned canonical form is a chain that breaks on a framework upgrade and cannot be told apart from tampering.

Writes serialise per tenant with `pg_advisory_xact_lock(hashtext('aurora.audit'))` — per tenant database, so it is not a fleet-wide serialisation point. `VerifyAsync(range)` returns the first broken link. Its test writes N events, verifies, then **tampers with a row as the owner role and asserts verification fails**. A chain verifier that has never been shown failing is not evidence of anything.

### 6. Two sides of the tenant boundary, neither substituting for the other

SPEC-001 BR-7/AC-6: the tenant-side event goes to `audit.audit_event` in the new tenant database; the platform-side event goes to `catalog.operator_audit_event`. Provisioning attempts and failures are platform-side only, because the tenant database may not exist yet.

### 7. What is deferred, explicitly

The `[Auditable]` `SaveChangesInterceptor` (ADR-0018 §2) and the fitness rule banning `ExecuteUpdate`/`ExecuteDelete` on `[Auditable]` entities are a **second** piece of work. They are needed before the first business module writes data (B-15.1), not before provisioning can write one explicit event. Retention (`platform.retention_rule`), legal hold and subject erasure remain ADR-0018's, unscheduled here and not part of the walking skeleton.

## Consequences

- Positive: the walking skeleton can demonstrate the property the product is sold on, and SPEC-001 AC-6 becomes testable instead of aspirational.
- Positive: the schema-level privilege policy means the next append-only table in a tenant database is protected by where it is created, not by whether its author remembered ADR-0004 rule 5.
- Positive: `TenantAccess` in the writer's signature means provisioning, jobs and requests share one audit path and one set of tests.
- Negative: one more schema and one more migration set per tenant database, in the migration order of ADR-0007 §7.1 (`platform` → `audit` → module schemas → `pkg_*`).
- Negative: partitioning and the hash chain are more machinery than the walking skeleton strictly needs. Both are here because both are expensive to add after the table has rows in every tenant.
- Negative: a serialisation point per tenant on audit writes. ADR-0018 already names this and its fallback (chain per day rather than per event); measure before changing.

## Revisit when

Audit volume forces partitions out of the tenant database into an archive store; a regulator requires read auditing; or the per-tenant chain lock shows up in a latency profile, at which point ADR-0018's per-day chaining is the prepared answer.
