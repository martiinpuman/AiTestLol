# ADR-0028 — The tenant audit store: schema, append-only enforcement and the write path

- **Status:** Accepted (2026-09-11) — **amended 2026-09-11 (Amendment 1): §2 is replaced, and one Consequences bullet and one Options-table cell are corrected. The decision — the `audit` schema, monthly partitioning, the write path and the hash chain — is unchanged.**
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
| **Own schema `audit`, privilege policy applied at schema granularity, plus a trigger** *(chosen — the pro is restated by Amendment 1; the second statement quoted here is a no-op and the corrected mechanism is a positive default grant)* | ~~`REVOKE UPDATE, DELETE ON ALL TABLES IN SCHEMA audit FROM aurora_app` **and** `ALTER DEFAULT PRIVILEGES … IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM aurora_app`~~ A **schema-wide default privilege** makes a table added to that schema next year append-only by where it is created instead of by memory — `audit` can carry one because every table in it is append-only, and `platform` cannot; the trigger catches the owner role too | One more schema per tenant database |
| Audit table inside `platform` (ADR-0004 rule 5 / ADR-0007 §9.1 as written) | One fewer schema | `platform` also holds `outbox`, `setting`, `feature_flag_override`, `job_run` — all of which **must** be `UPDATE`/`DELETE`-able by `aurora_app`. A schema-wide append-only policy is therefore impossible there, and every append-only guarantee falls back to a per-table grant someone must remember. That is exactly the B-05 M-1 failure, pre-installed |
| Audit in a separate database per tenant | Physically separable retention | A second database to provision, migrate, back up and restore per tenant, and no transactional write with the change it records — which ADR-0018 requires |

## Decision

### 1. Location

`audit.audit_event`, in the **tenant** database, schema `audit`, owned by the Audit platform module (`modules.md` §4). ADR-0018 and `modules.md` were already right; ADR-0004 rule 5 and ADR-0007 §9.1 are corrected by this ADR. The columns are ADR-0018 §1's, unchanged.

### 2. Append-only: what enforces it, what only detects it, and what nothing here prevents

*(Replaced by Amendment 1, 2026-09-11. The original text of this section is quoted in the amendment, with what was wrong with it.)*

Every statement in this section was executed against `postgres:17-alpine` — **PostgreSQL 17.11**, the pinned image — on 2026-09-11, as `aurora_migrator` (the schema owner) and as `aurora_app` (the request-path role). Where a statement does nothing, that is recorded instead of the statement.

**Every probe connects as the role it is testing** — `aurora_app` for mechanisms 1 and 2, `aurora_migrator` for mechanism 3 — and executes the forbidden statement, rather than asserting that a `GRANT` or `REVOKE` ran. A probe that runs as the fixture's superuser proves nothing: that is the test that passed on B-05 while the table was deletable.

**1. Grant — for the table itself.** `aurora_app` ends the migration holding exactly `SELECT, INSERT` on `audit.audit_event`, and nothing else: no `UPDATE`, no `DELETE`, no `TRUNCATE`, no column-level grant. Whether that comes from an explicit `GRANT` or from mechanism 2's default applying to the `CREATE TABLE` is an implementation detail; **the assertion is on the resulting ACL, never on which statement ran.**

- *Demonstrated by* comparing the **ACL itself** against the recorded decision, for **every relation in schema `audit`, partitions included** — `aclexplode(pg_class.relacl)` plus `pg_attribute.attacl`, projected for `aurora_app` **and** `PUBLIC` — and then, connected **as `aurora_app`**, executing `UPDATE` and `DELETE` and expecting SQLSTATE `42501`.
- *Why the ACL and not `has_table_privilege`:* finding H-1 of `../reviews/security-B-05.md`, executed. A column-level `GRANT UPDATE (detail) ON … TO aurora_app` leaves `has_table_privilege(…, 'UPDATE')` returning false while the `UPDATE` succeeds, and PostgreSQL 17 added a table privilege (`MAINTAIN`) that a hand-written privilege list does not contain. A per-privilege oracle is a test that reports green while the table is writable.

**2. Default privilege — for a table added to the schema later.**

```sql
ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit
  GRANT SELECT, INSERT ON TABLES TO aurora_app;      -- a positive grant, not a revoke
```

- *Why this form.* The `REVOKE` form this ADR originally specified does nothing at all. PostgreSQL's built-in default already grants `aurora_app` no privilege on a new table, so there is nothing to revoke: the statement succeeds, `pg_default_acl` stays **empty** (verified: `0 rows`), and a later ordinary `GRANT SELECT, INSERT, UPDATE, DELETE` on a new `audit` table is completely unrestrained (verified: `UPDATE t · DELETE t`). The positive grant does create the row — verified as `defaclacl = {aurora_app=ar/aurora_migrator}` — and it determines the ACL of the next table: a table created afterwards by `aurora_migrator` in `audit` came out `{aurora_migrator=arwdDxtm/aurora_migrator, aurora_app=ar/aurora_migrator}`, i.e. `SELECT` and `INSERT` for the app role and nothing more.
- *Demonstrated by* two assertions, both required: (a) `pg_default_acl` holds exactly one row for (`aurora_migrator`, schema `audit`, object type `r`) with that ACL; and (b) a table **created in `audit` after the migration has run** gives `aurora_app` exactly `{SELECT, INSERT}`, with an executed `INSERT` that succeeds and an executed `UPDATE`/`DELETE` that returns `42501`.
- *Why (b) must assert the positive half too.* A probe that only checks "`UPDATE` is refused on the new table" **passes against the no-op**, because a new table grants `aurora_app` nothing whatsoever. Asserting the exact set `{SELECT, INSERT}` fails in both directions: too little (the default-ACL row is missing, so the `INSERT` probe fails) and too much (someone widened it, so the `UPDATE` probe fails).
- *What it does not do.* A default ACL does not restrain a later explicit `GRANT`; nothing in PostgreSQL does. Grant drift is **detected** by mechanism 1's ACL comparison, not prevented. And the default applies only to tables created by `aurora_migrator`: a table created in `audit` by any other role gets no ACL entry for `aurora_app` at all (verified — `relacl` null, every privilege false), which is fail-closed rather than silently open.

**3. Trigger — against every ordinary DML path, including the owner.** A `BEFORE UPDATE OR DELETE … FOR EACH ROW` trigger on the partitioned parent `audit.audit_event`, raising `42501`.

- *Verified:* the trigger can be created on a partitioned parent; it is **cloned to a partition created afterwards** (`CREATE TABLE … PARTITION OF` produced a partition carrying the trigger); and it raised for `aurora_migrator` — the owner, whom privileges do not restrain — both through the parent and against the partition directly.
- *Plus* a `BEFORE TRUNCATE … FOR EACH STATEMENT` trigger, **with a verified limitation: truncate triggers are not cloned to partitions.** `TRUNCATE audit.audit_event` was refused; `TRUNCATE audit.audit_event_2026_09` succeeded and emptied the table. Therefore the job that pre-creates partitions (§3) attaches the truncate trigger to each partition it creates, and its test truncates a **job-created** partition as the owner and expects `42501`. `aurora_app` never holds `TRUNCATE` — that is mechanism 1, and its ACL assertion is what keeps it true.

**What none of the three prevents, and what detects it.** A role that can execute DDL on the table can `ALTER TABLE … DISABLE TRIGGER`, `DROP TRIGGER`, drop a partition or drop the table. That role is `aurora_migrator`, reachable only through the DDL path of ADR-0027 and never from a request — `aurora_app` has no DDL. Against a DDL-capable role the control is **detection, not prevention**: the hash chain of §5. *Editing* any row is detected wherever it is, because the stored hash no longer matches the one recomputed from the row; *removing* rows is detected anywhere but the tail, because it breaks the following row's `prev_hash`. `VerifyAsync` reports the first broken link. Two cases are added to the tamper test of the writer row (**B-16.2** in `../BACKLOG.md`'s split of this ADR's work — schema and enforcement, then writer and chain): disable the trigger as the owner, edit a row, re-enable, verify — and drop a partition, verify.

**The one case the chain alone does not catch is truncation of the tail.** Deleting the most recent rows leaves a chain that is internally consistent. The mechanism for that is ADR-0018's daily job writing the chain head to `catalog.operator_audit_event`, which is **not built and not scheduled**; until it is, tail truncation by a DDL-capable role is undetected. This is a limitation of the system as it will be after both audit rows land, stated here rather than described as covered.

**The catalog's own append-only tables.** `catalog.operator_audit_event` and `catalog.erasure_replay_log` **do not exist**. No task has created them, B-05 did not, and this ADR does not create them. When the task that does lands, that migration: grants `aurora_app` exactly `SELECT, INSERT`; records that decision in `CatalogSchemaAllowlist.AppRolePrivileges` **with the writer named**; and attaches the mechanism-3 trigger. What B-05 did do is delete the blanket `ALTER DEFAULT PRIVILEGES … GRANT` that would otherwise have handed those tables `UPDATE` and `DELETE` on creation.

Mechanism 2 is **not** applied to the `catalog` schema and must not be: `catalog.tenant` and its neighbours legitimately need `UPDATE`, so a schema-wide default of `SELECT, INSERT` would be wrong there and would have to be overridden table by table — the per-table memory this ADR exists to remove. That asymmetry is precisely why the audit table lives in its own schema, in a tenant database and in the catalog alike.

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
- Positive (**restated by Amendment 1**): the schema-level **default grant** of `SELECT, INSERT` means the next table created in `audit` by the DDL-path role is append-only to `aurora_app` by where it is created, not by whether its author remembered ADR-0004 rule 5 — and a test that creates a table after the migration and probes it fails if the default-ACL row is missing. The original wording of this bullet rested on `ALTER DEFAULT PRIVILEGES … REVOKE`, which is a no-op; see Amendment 1.
- Positive: `TenantAccess` in the writer's signature means provisioning, jobs and requests share one audit path and one set of tests.
- Negative: one more schema and one more migration set per tenant database, in the migration order of ADR-0007 §7.1 (`platform` → `audit` → module schemas → `pkg_*`).
- Negative: partitioning and the hash chain are more machinery than the walking skeleton strictly needs. Both are here because both are expensive to add after the table has rows in every tenant.
- Negative: a serialisation point per tenant on audit writes. ADR-0018 already names this and its fallback (chain per day rather than per event); measure before changing.

## Revisit when

Audit volume forces partitions out of the tenant database into an archive store; a regulator requires read auditing; or the per-tenant chain lock shows up in a latency profile, at which point ADR-0018's per-day chaining is the prepared answer.

---

## Amendment 1 (2026-09-11) — §2's second mechanism did nothing, and its closing sentence described work that had not happened

**Raised by** `../reviews/security-B-05.md` finding H-3, which executed the statement rather than reading it. **Amended by** the architect. The decision of this ADR — `audit.audit_event` in its own schema in the tenant database, monthly partitioning, `IAuditWriter` taking `TenantAccess` and the caller's `DbContext`, the pinned hash chain — is **unchanged**. What changes is how §2 says append-only is enforced, which was wrong in two ways.

### What §2 used to say

> ### 2. Append-only, enforced three ways, each tested as the runtime role
>
> 1. `REVOKE UPDATE, DELETE ON ALL TABLES IN SCHEMA audit FROM aurora_app`;
> 2. `ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM aurora_app` — so a table added to `audit` later inherits the policy;
> 3. a `BEFORE UPDATE OR DELETE` trigger that raises, which also catches `aurora_migrator` (the owner, whom privileges do not restrain).
>
> **The tests connect as `aurora_app` and as `aurora_migrator` and assert the statement fails.** A test that runs as the fixture's superuser, or that asserts the `REVOKE` statement was executed rather than probing `has_table_privilege` and running the `DELETE`, is the test that passed on B-05 while the table was deletable. The same three-way enforcement is retrofitted to `catalog.operator_audit_event` and `catalog.erasure_replay_log` (B-05 M-1's fix).

### What was wrong with it

1. **Mechanism 2 is a no-op.** `ALTER DEFAULT PRIVILEGES … REVOKE` on a privilege the role never had by default creates no `pg_default_acl` row and changes nothing, now or ever. Verified on PostgreSQL 17.11: `0 rows`, and a subsequent explicit `GRANT … UPDATE, DELETE` on a new `audit` table succeeded. It was the stated reason for choosing the `audit` schema over `platform`, so the Options table and the Consequences bullet that rested on it were wrong too. This is the B-05 M-1 failure mode — a `REVOKE` whose effect was never asserted — pre-installed in the ADR written to avoid it.
2. **The acceptance criterion it generated could not fail.** "Create a table in the schema afterwards and re-probe that `UPDATE` is refused" passes against the no-op, because a new table grants `aurora_app` nothing at all. The corrected criterion asserts the **exact** privilege set, which fails in both directions.
3. **The closing sentence described work nobody had done.** `catalog.operator_audit_event` and `catalog.erasure_replay_log` do not exist; B-05 did not retrofit anything to them. A reader six months from now would have believed two tables were protected by a trigger and two revokes when they are protected by nothing, because they are not there.
4. **`has_table_privilege` was named as the probe.** `../reviews/security-B-05.md` H-1 then showed it blind to a column-level grant and short by one privilege on PostgreSQL 17 (`MAINTAIN`). The replacement §2 compares the ACL.

### What changed as a result

- §2 is replaced in full (above), naming three mechanisms that were each executed, plus what none of them prevents and what detects that instead.
- The Options-table cell for the chosen option and the corresponding Consequences bullet are restated in place, with the superseded wording struck through.
- `../architecture/solution-layout.md` §6.2 re-specifies **B-16.1**'s acceptance criteria, and §6.1's B-16.1 row is marked as overridden. The project-manager must transcribe the new criteria into `../BACKLOG.md`'s B-16.1 row, which still quotes the no-op statement and the criterion that cannot fail.
- The event-trigger option (`ddl_command_end`, which would attach the guard trigger to any new table in `audit` automatically) was considered and **rejected**: `CREATE EVENT TRIGGER` requires superuser on PostgreSQL 17.11 — verified, `ERROR: permission denied to create event trigger … HINT: Must be superuser` — and ADR-0004 rule 2 defines no superuser role (`aurora_admin` has `CREATEDB`; `aurora_migrator` owns schemas). Adopting it would mean requiring superuser in every environment, which is a larger and less portable commitment than the property is worth. The positive default grant reaches the same "protected by where it is created" property for the privilege half, and the trigger half stays explicit per table.
