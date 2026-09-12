# ADR-0028 — The tenant audit store: schema, append-only enforcement and the write path

- **Status:** Accepted (2026-09-11) — **amended 2026-09-11 (Amendment 1): §2 is replaced, and one Consequences bullet and one Options-table cell are corrected.** **Amended 2026-09-12 (Amendment 2): §2 mechanism 3's truncate half is replaced — the guard is attached to every partition, the `DEFAULT` partition included, by the same statement that creates or attaches it, instead of by a partition job that does not exist.** The decision — the `audit` schema, monthly partitioning, the write path and the hash chain — is unchanged by both.
- **Deciders:** architect
- **Supersedes:** ADR-0004 rule 5's table name (`platform.audit_event`), ADR-0007 §9.1's prose placing audit in the `platform` schema, and the same table name where it appears in ADR-0007 §11.5. Everywhere those documents say `platform.audit_event`, read `audit.audit_event`. ADR-0018's decisions are unchanged; this ADR fixes the mechanics it left to the implementer.
- **Superseded by:** —
- **Related:** ADR-0018 (audit, retention, erasure), ADR-0004 rule 5, ADR-0007 §4.3 / §9.2, ADR-0027 (`TenantAccess`), `../product/specs/SPEC-001-tenant-provisioning-and-first-login.md` BR-7/AC-6, `../reviews/B-05.md` finding M-1, the third security review of ADR-0029 (PR #5) finding M-1 — `../BACKLOG.md` `FOLLOWUP-026`

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
- *Plus* a `BEFORE TRUNCATE … FOR EACH STATEMENT` trigger on the parent **and on every partition, the `DEFAULT` partition included, created by the same migration statement that creates or attaches the partition** — **replaced by Amendment 2 (2026-09-12); the original sentence, which reached only partitions created by a job that does not exist, is quoted there with what was wrong with it.** The verified limitation it rests on is unchanged: **truncate triggers are not cloned to partitions**, by `CREATE TABLE … PARTITION OF` or by `ALTER TABLE … ATTACH PARTITION` (the row-level `UPDATE`/`DELETE` trigger of this mechanism *is* cloned by both). `aurora_app` never holds `TRUNCATE` on the parent or on any partition — that is mechanism 1, and its ACL assertion is what keeps it true. What none of this reaches is a role that can execute DDL; that is stated below and in Amendment 2 rather than claimed as covered.

**What none of the three prevents, and what detects it.** A role that can execute DDL on the table can `ALTER TABLE … DISABLE TRIGGER`, `DROP TRIGGER`, drop a partition or drop the table. That role is `aurora_migrator`, reachable only through the DDL path of ADR-0027 and never from a request — `aurora_app` has no DDL. Against a DDL-capable role the control is **detection, not prevention**: the hash chain of §5. *Editing* any row is detected wherever it is, because the stored hash no longer matches the one recomputed from the row; *removing* rows is detected anywhere but the tail, because it breaks the following row's `prev_hash`. `VerifyAsync` reports the first broken link. Two cases are added to the tamper test of the writer row (**B-16.2** in `../BACKLOG.md`'s split of this ADR's work — schema and enforcement, then writer and chain): disable the trigger as the owner, edit a row, re-enable, verify — and drop a partition, verify.

**The one case the chain alone does not catch is truncation of the tail.** Deleting the most recent rows leaves a chain that is internally consistent, and **the emptied trail is the degenerate case of it: a chain of zero rows verifies clean, and so does a chain that restarts from a first row whose `prev_hash` is the 32 zero bytes of §5.** The mechanism for that is ADR-0018's daily job writing the chain head to `catalog.operator_audit_event`, which is **not built and not scheduled** — it needs the row that creates that table (`../architecture/solution-layout.md` §6.4 item 5) and a scheduler for platform jobs (`../BACKLOG.md` `FOLLOWUP-031`, which ships `Aurora.Platform.Jobs`). Until both exist, tail truncation by a DDL-capable role is **undetected**. This is a limitation of the system as it will be after both audit rows land, stated here rather than described as covered; Amendment 2 is what removes the case that needed no DDL at all.

**The catalog's own append-only tables.** `catalog.operator_audit_event` and `catalog.erasure_replay_log` **do not exist**. No task had created them, B-05 did not, and this ADR does not create them — `../architecture/solution-layout.md` **§6.4 item 5** is the row that does, and until it is transcribed and built the two tables are still absent. That migration: grants `aurora_app` exactly `SELECT, INSERT`; records that decision in `CatalogSchemaAllowlist.AppRolePrivileges` **with the writer named**; attaches the mechanism-3 trigger; and asserts `pg_default_acl` holds **no** row for schema `catalog`, because mechanism 2 must not be copied there. What B-05 did do is delete the blanket `ALTER DEFAULT PRIVILEGES … GRANT` that would otherwise have handed those tables `UPDATE` and `DELETE` on creation.

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
- `../architecture/solution-layout.md` §6.4 re-specifies **B-16.1**'s acceptance criteria, and §6.1's B-16.1 row is marked as overridden. The project-manager must transcribe the new criteria into `../BACKLOG.md`'s B-16.1 row, which still quotes the no-op statement and the criterion that cannot fail.
- Withdrawing the retrofit sentence left two ADR-0007 tables and the tail-truncation follow-up depending on a row nobody owned. `../architecture/solution-layout.md` **§6.4 item 5** creates `catalog.operator_audit_event` and `catalog.erasure_replay_log` with the grants, the trigger and the zero-row `pg_default_acl` assertion; the follow-up depends on it by name. SPEC-001 BR-7 already has B-07.1 and B-07.4 writing to a table nothing created.
- The event-trigger option (`ddl_command_end`, which would attach the guard trigger to any new table in `audit` automatically) was considered and **rejected**: `CREATE EVENT TRIGGER` requires superuser on PostgreSQL 17.11 — verified, `ERROR: permission denied to create event trigger … HINT: Must be superuser` — and ADR-0004 rule 2 defines no superuser role (`aurora_admin` has `CREATEDB`; `aurora_migrator` owns schemas). Adopting it would mean requiring superuser in every environment, which is a larger and less portable commitment than the property is worth. The positive default grant reaches the same "protected by where it is created" property for the privilege half, and the trigger half stays explicit per table.

---

## Amendment 2 (2026-09-12) — the truncate guard reached only partitions a job creates, and no such job exists

**Raised by** the third security review of ADR-0029 (PR #5) finding M-1, which **executed** `TRUNCATE catalog.authentication_event_default` as the owner and emptied the trail while the parent's guard was in place; carried in `../BACKLOG.md` as `FOLLOWUP-026`. **Amended by** the architect, with every statement below executed against `postgres:17-alpine` — **PostgreSQL 17.11**, the pinned image — on 2026-09-12, as `aurora_migrator` (the schema owner) and as `aurora_app` (the request-path role). The decision of this ADR — `audit.audit_event` in its own schema in the tenant database, monthly partitioning, `IAuditWriter` taking `TenantAccess` and the caller's `DbContext`, the pinned hash chain — is **unchanged**. What changes is the second half of §2 mechanism 3.

### What §2 mechanism 3 used to say

> - *Plus* a `BEFORE TRUNCATE … FOR EACH STATEMENT` trigger, **with a verified limitation: truncate triggers are not cloned to partitions.** `TRUNCATE audit.audit_event` was refused; `TRUNCATE audit.audit_event_2026_09` succeeded and emptied the table. Therefore the job that pre-creates partitions (§3) attaches the truncate trigger to each partition it creates, and its test truncates a **job-created** partition as the owner and expects `42501`. `aurora_app` never holds `TRUNCATE` — that is mechanism 1, and its ACL assertion is what keeps it true.

### What was wrong with it

1. **It hangs the guarantee on a job that does not exist.** `Aurora.Platform.Jobs` is in no bootstrap row; the partition pre-creation job of §3 is `FOLLOWUP-031`, named in three documents before it had an id. A mechanism whose only carrier is unbuilt work protects nothing today, and Amendment 1 withdrew a sentence for exactly this shape — describing work nobody had done.
2. **The `DEFAULT` partition is not job-created and never will be.** The migration creates it (§3). So even on the day the job ships, the rule as written does not reach the partition the reviewer actually truncated — and the `DEFAULT` partition is where every row lands whenever no month partition covers `occurred_at`, which without the job is every row after the pre-created months run out.
3. **Nothing detects the result.** §2 already records that the hash chain does not detect truncation of the tail, and an emptied table is the degenerate tail truncation: zero rows verify clean, and a trail that restarts from a first row with the 32 zero bytes of §5 verifies clean too. So the two halves compose into the finding: **a tenant's financial audit trail can be emptied and then pass `VerifyAsync`.**
4. **The cost of the attack was one statement.** Not a `DROP TRIGGER`, not a `DISABLE TRIGGER`, not a partition drop — a plain `TRUNCATE` naming a partition, available to the schema owner with no other DDL. It sat below the threshold the ADR had declared its detection-not-prevention boundary.

Executed, reproducing it on `audit.audit_event` with three rows and the guard on the parent only:

| Statement, as `aurora_migrator` | Result | Rows left in the parent |
|---|---|---|
| `TRUNCATE audit.audit_event` | `42501` | 3 |
| `TRUNCATE audit.audit_event_default` | `TRUNCATE TABLE` | 1 |
| `TRUNCATE audit.audit_event_2026_09` | `TRUNCATE TABLE` | **0** |

`pg_trigger` after the migration shows why: the row-level guard (`tgtype` 27 = `ROW` 1 + `BEFORE` 2 + `DELETE` 8 + `UPDATE` 16) is present on the parent **and both partitions**; the truncate guard (`tgtype` 34 = `BEFORE` 2 + `TRUNCATE` 32, statement-level) is present **on the parent only**. `ALTER TABLE … ATTACH PARTITION` behaves the same way: the attached partition came out carrying the row-level guard and no truncate guard.

### The decision — one clause, for every append-only partitioned table this system has

**Every partition of an append-only partitioned table carries the `BEFORE TRUNCATE … FOR EACH STATEMENT` guard, the `DEFAULT` partition included, created by the same migration statement that creates or attaches the partition; and the invariant that holds it is asserted over the partition tree, never over the code that creates partitions.**

It is one clause because it is written here, about the shape rather than about a table: it binds `audit.audit_event` in every tenant database and `catalog.authentication_event` in the catalog (ADR-0029 A2.1 partitions it monthly with a `DEFAULT` partition) and anything partitioned that is added to either later. `../architecture/solution-layout.md` §6.4 item 1 criterion 5 is the row form for the first, and §6.4 item 5 carries it for the catalog.

1. **Creation.** One migration helper issues the `CREATE TABLE … PARTITION OF` or `ALTER TABLE … ATTACH PARTITION` **and** the `CREATE TRIGGER` together, in the same migration transaction. The migration creates the `DEFAULT` partition and the initial month partitions through it, so the guard exists before any job does, and `FOLLOWUP-031`'s partition job — when it is built — calls the same helper rather than carrying its own copy of the rule.
2. **The invariant, which is what is actually enforced.** For every relation in `pg_partition_tree('<parent>')`, leaves and intermediate parents alike, there is a non-internal trigger with `tgtype & 32 <> 0` (TRUNCATE), `tgtype & 2 <> 0` (BEFORE) and **`tgenabled IN ('O','A')`**. The probe prints `leaf_partitions` and `guarded_leaves`, asserts they are equal, and fails when `leaf_partitions < 2` — the floor is the initial month partition plus `DEFAULT`. Nothing prevents someone writing a raw `CREATE TABLE … PARTITION OF` that skips the helper, and that is deliberate: as with mechanism 1's ACL comparison, **the assertion is on the resulting state, never on which statement produced it.** The helper is how you satisfy the invariant; the invariant is what catches you.
3. **`tgenabled IN ('O','A')`, not `tgenabled <> 'D'`.** Executed: `ALTER TABLE <partition> ENABLE REPLICA TRIGGER <guard>` leaves `tgenabled = 'R'`. The trigger is not disabled, the naive predicate counts it as a guard, and the `TRUNCATE` **succeeds**. A guard enumeration written the obvious way reports green over a partition that can be emptied.
4. **`TRUNCATE` appears in no relation's ACL — parent or partition — for any role but the owner `aurora_migrator`.** That is mechanism 1's ACL comparison extended to partitions, and it is already true: executed, `aurora_app` truncating the parent gets `42501: permission denied for table`. Naming it here is the answer to "who may ever hold `TRUNCATE`": exactly one role, the DDL-path role of ADR-0027, and any other holder is a finding.
5. **The parent keeps its own guard, and it is not what closes this hole.** Executed: with the parent's guard dropped and every leaf guarded, `TRUNCATE <parent>` is still refused — a parent truncate fires each leaf's guard. The parent's guard stays because it is the only thing that refuses a truncate of a parent with **zero** partitions, which is the state a failed migration can leave behind.

**One executed fact both row forms depend on: a partition's ACL is not the parent's, and it differs by schema.** In `audit`, mechanism 2's schema-wide default grant reaches partitions too — a partition created by `aurora_migrator` came out `{aurora_migrator=arwdDxtm/aurora_migrator, aurora_app=ar/aurora_migrator}`, so `aurora_app` holds `SELECT, INSERT` on every partition. In a schema with **no** default privileges — `catalog`, by §2's closing paragraph and ADR-0029 A2.7 point 4 — the same `CREATE TABLE … PARTITION OF` leaves `relacl` **null**, and the partition is unreachable to `aurora_app` in every direction: a routed `INSERT` through the parent succeeds, because routing checks the **parent's** ACL, while a direct `INSERT`, `SELECT` or `TRUNCATE` on the partition returns `42501`. All executed. So a privilege comparison that carries `audit`'s expectation into `catalog` would accept a grant that makes a partition directly reachable and would never say so; `../architecture/solution-layout.md` §6.4 item 5 criterion 3 records the catalog's expectation — **empty** — in the place the allow-list is specified.

### What demonstrates each mechanism working, and what demonstrates it failing

A mechanism that cannot fail is not a check, so each row carries both halves. The right-hand column is work in the suite, not prose here: the failure is produced, not asserted.

| Mechanism | Demonstrated working by | Demonstrated failing by |
|---|---|---|
| The guard on every partition | As `aurora_migrator`, the owner: `TRUNCATE <parent>`, `TRUNCATE <month partition>`, `TRUNCATE <DEFAULT partition>` and the multi-table `TRUNCATE <p1>, <p2>` each return `42501`, and `count(*)` is unchanged after all four. **Executed 2026-09-12: three rows in, three rows after four refused truncates** | Create a partition with a bare `CREATE TABLE … PARTITION OF` that bypasses the helper, then truncate it as the owner: **executed — `TRUNCATE TABLE`, and its row is gone.** This is the reviewer's attack, reproduced inside the suite so that it stays reproduced |
| The partition-tree enumeration | On the migrated database it prints `leaf_partitions 2 / guarded_leaves 2 / guarded_parents 1` and asserts equality — **executed, that is the green output** | The same unguarded partition takes it to **`leaf_partitions 3 / guarded_leaves 2`** and the assertion fails. Executed |
| `tgenabled IN ('O','A')` | The guarded partition counts as guarded | `ENABLE REPLICA TRIGGER` on one partition: the correct predicate reports `0` for it and the test goes red; **the naive `tgenabled <> 'D'` reports `1` and the `TRUNCATE` succeeds.** Both executed, side by side |
| The count floor | A green run prints a non-zero population — `leaf_partitions 2 / guarded_leaves 2` — so a reader can tell "all guarded" from "nothing found" | Point the probe at a parent with no partitions: it must fail rather than print a clean `0 of 0`. `CLAUDE.md` self-check #2 — a probe that measured nothing must not report PASS |
| `TRUNCATE` held by one role only | `aurora_app` truncating the parent returns `42501: permission denied for table`. Executed | Grant `TRUNCATE` on any relation in the schema to any other role and mechanism 1's ACL comparison fails on the extra privilege |

### The limitation, written down instead of a claim

**A role that can execute DDL on the table can still empty it, and nothing this project has built detects that.** Executed: `ALTER TABLE <partition> DISABLE TRIGGER <guard>` followed by `TRUNCATE <partition>` succeeds; so does dropping the guard, and so does `DETACH PARTITION` followed by `DROP TABLE`. The hash chain does not cover it, because removing the newest rows — or all of them — is a tail truncation, and §2 already records that the chain cannot see one.

So the honest statement of where this leaves us: **this clause removes the case that needed no DDL at all and leaves the case that needs DDL undetected.** That is a smaller residual than the one the reviewer found, and it is the same boundary §2 already drew for `DROP TRIGGER` and dropped partitions — it is not a new exposure, but it is also not covered, and it must not be written as if it were.

What would cover it is **a chain head held outside the tenant database**: ADR-0018's daily job writing the head of each tenant's chain to `catalog.operator_audit_event`, where a shortened or restarted chain contradicts a head recorded yesterday. It needs two things that do not exist — the row that creates `catalog.operator_audit_event` (`../architecture/solution-layout.md` §6.4 item 5) and a scheduler for platform jobs (`FOLLOWUP-031`, which ships `Aurora.Platform.Jobs`) — and it is therefore **named as the residual's cover, not written into this clause**. Writing it in would repeat the exact defect this amendment exists to fix: a guarantee carried by unbuilt work.

### Two directions considered and not taken, with the executed reason

- **Revoke `TRUNCATE` from the owning role.** It does bite — `REVOKE TRUNCATE ON <partition> FROM aurora_migrator` takes the owner's ACL entry from `arwdDxtm` to `arwdxtm` and the owner's `TRUNCATE` then returns `42501: permission denied for table`, executed. But the owner re-grants to itself in one statement: `GRANT TRUNCATE ON <partition> TO aurora_migrator`, **issued as `aurora_migrator`, no elevation**, and the next `TRUNCATE` returns `TRUNCATE TABLE`. Executed. So against the role this is aimed at it is a speed bump, and adopting it would put a second per-partition expectation into mechanism 1's exact-ACL comparison that has to be maintained in lockstep with the guard — the per-table memory this ADR exists to remove. The half of the direction that is worth keeping is the *naming*, which is part 4 of the clause above: `TRUNCATE` belongs to `aurora_migrator` and to no one else, and mechanism 1 already executes that assertion.
- **A chain-head row inside the tenant database.** It is the only direction that survives a DDL-capable attacker — but only when the head sits somewhere that attacker cannot reach. Inside the tenant database it does not: the role that drops the guard rewrites the head in the same session. And a running head needs `UPDATE`, in the one schema whose entire premise (Options table, chosen row) is that every table in it is append-only, so it would either break mechanism 2's schema-wide `SELECT, INSERT` default or move the head to `platform`, where `aurora_app` can rewrite it without any DDL at all. Kept as the residual's cover in its out-of-database form, above.

### What changed as a result

- §2 mechanism 3's truncate bullet is replaced in place, and the closing tail-truncation paragraph now names the degenerate case (a chain of zero rows verifies clean) and the two rows the cover depends on.
- `../architecture/solution-layout.md` §6.4 item 1 criterion 5 is restated to this clause, and its criterion 2 says **partitions included** — which mechanism 1 already required and the row form had dropped.
- `../architecture/solution-layout.md` §6.4 item 5 carries the clause for `catalog`, conditionally: the two tables it creates are not partitioned, and `catalog.authentication_event` is partitioned wherever ADR-0029 A2.8's ownership question lands it.
- **Not changed here, and needing a row:** ADR-0029 A2.7 point 3 and `../architecture/solution-layout.md` §6.2's `B-18.9` row both still say the truncate guard is *"attached to every partition the partition job creates"*. Both are outside this amendment's scope and were left untouched deliberately. They inherit the corrected mechanism through their existing citation of ADR-0028 Amendment 1 and §6.4 items 1 and 5, but the literal job-created wording now contradicts it and must be restated by whoever next owns those two documents.
