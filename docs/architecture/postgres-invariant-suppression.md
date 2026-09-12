# How a PostgreSQL invariant stops being enforced while it still looks enforced

**Owner:** architect · **Decided by:** [ADR-0037](../decisions/ADR-0037-what-the-migration-sql-scanner-may-conclude.md) §2 · **Status:** incomplete by construction — see §4

This file exists because one PostgreSQL fact has cost this project two review rounds on two different branches, and it would have cost a third. The fact is:

> **Disabled is not the only way to stop a trigger firing.**

- PR #9's fourth review found it from the *reader's* side: a guard check using the natural predicate `tgenabled <> 'D'` counted a replica-mode trigger as live **while the `TRUNCATE` it was guarding succeeded**.
- PR #16's second review found it from the *writer's* side: a migration containing `ALTER TABLE … ENABLE REPLICA TRIGGER` scanned clean, reversing the four `ENABLE ALWAYS TRIGGER` statements B-19 had spent to make its append-only guards survive replica mode.

Two branches, two mechanisms, one underlying truth, rediscovered at review cost each time. A fact that expensive does not belong in two review threads.

---

## 1. The two shapes, and why only one of them needs this file

ADR-0037 §2.1 splits weakening into two limbs.

**Limb A — removal.** Something is taken away: a column, a table, a key, an index, a row. It is **visible** — the catalog no longer holds it, and anyone who looks sees the absence. Limb A enumerates itself, because a removal names what it removes.

**Limb B — suppression.** The object remains. Its name remains. Its definition remains. Something *outside* its definition decides it does not take effect. **A reader of the catalog, of `information_schema`, or of the migration that created it, sees a guard — and it does not fire.**

Limb B is what this file enumerates, because limb B does not announce itself and cannot be found by reading the statement that caused it.

## 2. The audit method — reason from the catalog column, not from the statement

> **For every invariant the schema can declare, find the catalog column that records whether it is in force, enumerate that column's values, and only then enumerate the statements that write it.**

`pg_trigger.tgenabled` is the worked example and the reason for the rule. It holds **four** values, and only one of them is `D`. Reasoning from the statement — "what disables a trigger?" — finds `DISABLE TRIGGER`, and stops. Reasoning from the column — "what values can `tgenabled` take, and which of them mean this guard does not fire for an ordinary write?" — finds `D` **and** `R`, and then finds the statement that writes `R`. Both branches that paid for this fact reasoned from the statement.

The method converts an unanswerable question ("have we thought of everything?") into one with a finite answer somebody can be held to ("which catalog columns have we walked?").

## 3. The enumeration

**Evidence column:** `executed` — run against `postgres:17-alpine` on this project and the observed behaviour recorded; `documented` — stated in the PostgreSQL manual and cited; `asserted` — believed from knowledge of PostgreSQL, **not** checked here. An `asserted` row is a lead, not a fact. Any row a mechanism relies on should be moved to `executed` by the row that builds that mechanism.

| Id | What records "in force" | Values / states that weaken it | Statements that write it | Evidence |
|---|---|---|---|---|
| **S1** | `pg_trigger.tgenabled` | `D` never fires · `R` fires **only** when `session_replication_role = 'replica'`, so never for an ordinary write · `O` does not fire under `'replica'`, so it is weaker than `A` | `ALTER TABLE … DISABLE TRIGGER` (`D`) · `… ENABLE REPLICA TRIGGER` (`R`) · `… ENABLE TRIGGER` (`O`, a reduction from `A`) · `… ENABLE ALWAYS TRIGGER` (`A`, the top — the only one that cannot reduce) · **`CREATE OR REPLACE TRIGGER`, which resets the mode to `O` while mentioning no mode at all** | `executed`. `R`: PR #9's review, PostgreSQL 17.11 — the `TRUNCATE` landed while the predicate `tgenabled <> 'D'` reported the guard live. `CREATE OR REPLACE TRIGGER`: `postgres:17-alpine`, 2026-09-12 — `enable always` gave `tgenabled=A`, the identical `create or replace trigger` that followed gave `tgenabled=O` |
| **S2** | `pg_rewrite.ev_enabled` | The same four-value scheme as S1 | `ALTER TABLE … {DISABLE / ENABLE / ENABLE REPLICA / ENABLE ALWAYS} RULE` | `executed` — `postgres:17-alpine`, 2026-09-12: a fresh rule read `ev_enabled=O`, `enable replica rule` gave `R`, `enable always rule` gave `A`. The scheme is S1's, confirmed rather than assumed |
| **S3** | the `session_replication_role` GUC | `'replica'` suppresses **every** `O` trigger and rule in reach, for the whole session, naming none of them | `SET` · `SET LOCAL` · `ALTER ROLE … SET` · `ALTER DATABASE … SET` · `set_config(…)` | `documented`. **Open:** it is a `SUSET` parameter, so a non-superuser `aurora_migrator` without `GRANT SET` may be unable to issue it at all — a second and stronger line than any scanner. **Nobody on this project has established which**, and no deployment exists. ADR-0037 §2.7 |
| **S4** | `pg_proc.prosrc` — the body the trigger executes | Any body that no longer refuses. `pg_trigger` stays byte-identical | `CREATE OR REPLACE FUNCTION` / `PROCEDURE` | `executed` (B-19's review: a hollow body — `RETURN NULL`, which a statement-level trigger ignores — left every `pg_trigger` column identical while the `TRUNCATE` landed). Closed at runtime by comparing `pg_get_functiondef`; closed in migrations by ADR-0037 §2.5 |
| **S5** | an unqualified name inside a function body | The body's identifiers resolve through the **caller's** `search_path` unless the function pins its own. A shadowed function or operator changes what the guard does without touching the guard | `SET search_path` in the calling session · creating a shadowing object in an earlier schema | `asserted` — named in `20260912020620_AppendOnlyTrails.cs`'s own analysis, which notes the body's one call is `format()` and that shadowing it can change the message but not stop the `RAISE` |
| **S6** | `pg_class.relrowsecurity`, `pg_class.relforcerowsecurity` | RLS off; or on but not forced, in which case the **table owner bypasses every policy** | `ALTER TABLE … DISABLE ROW LEVEL SECURITY` · `… NO FORCE ROW LEVEL SECURITY` · `ALTER TABLE … OWNER TO` (changes *who* bypasses) · `ALTER ROLE … BYPASSRLS` | `documented` for the owner-bypass rule; `asserted` that `OWNER TO` is therefore a suppression vector |
| **S7** | `pg_constraint.convalidated` | `false` — the constraint is listed in the catalog and **does not hold for rows that existed when it was added** | `ALTER TABLE … ADD CONSTRAINT … NOT VALID`, until a matching `VALIDATE CONSTRAINT` | `documented`. This is *deliberate* limb B for one release (ADR-0037 §3.3); what makes it acceptable is that validation is scheduled, and **nothing schedules it** |
| **S8** | `pg_constraint.condeferrable` / `condeferred`, plus `SET CONSTRAINTS` | A deferrable constraint is **not enforced during the transaction**, only at commit. Rows that violate it are visible to every statement in between | `CREATE CONSTRAINT TRIGGER … DEFERRABLE` · `ADD CONSTRAINT … DEFERRABLE` on `UNIQUE`, `PRIMARY KEY`, `EXCLUDE` or a foreign key · `SET CONSTRAINTS … DEFERRED` | `executed`, and the previous scope claim here was **false**. See below |
| **S9** | `pg_index.indisvalid`, `pg_index.indisready` | An invalid index is ignored for querying and must be found and rebuilt out of band | a **failed** `CREATE INDEX CONCURRENTLY` — which is outside the migration's transaction and so is not rolled back with it | `documented`. This is limb B *by accident* rather than by statement, and it is why ADR-0037 §6 lets a load-bearing unique index be built transactionally |
| **S10** | `pg_event_trigger.evtenabled` | The same four-value scheme as S1, for event triggers | `ALTER EVENT TRIGGER … {DISABLE / ENABLE / ENABLE REPLICA / ENABLE ALWAYS}` | `executed` — `postgres:17-alpine`, 2026-09-12: created `evtenabled=O`, `enable replica` gave `R`, `enable always` gave `A`. Nothing on this project uses event triggers today, so the row is a lead and not a live exposure |
| **S11** | the object's ACL and owner | A privilege that permits the very write a guard exists to refuse; an owner who bypasses what a grantee cannot | `GRANT` · `ALTER … OWNER TO` · `ALTER DEFAULT PRIVILEGES` | `executed` in the opposite direction (B-05's nineteen attack shapes all return `42501`, and `CatalogPrivilegeTests` compares the live ACL against a declared oracle) |
| **S12** | trigger *scope* — `pg_trigger.tgtype`, `tgqual`, `tgattr` | A guard re-created with a `WHEN` clause, or for `UPDATE OF` one column, fires for a narrower set of statements than the one it replaced, while still being a trigger with the same name on the same table | `DROP TRIGGER` + `CREATE TRIGGER` · `CREATE OR REPLACE TRIGGER` | `executed` (B-19's review, PostgreSQL 17.11: a guard re-created with a `WHEN` clause keyed on a GUC fires for every session that has not set it — the probe included — while a session that has set it deletes and truncates freely) |

### S8's correction, because a wrong scope clause in this file is worse than no row

The first version of S8 said *"only constraint triggers and foreign keys are deferrable"*. That is wrong, and it was wrong in the direction that makes a reader stop looking. Executed on `postgres:17-alpine`, 2026-09-12:

- **An `EXCLUDE` constraint declared `DEFERRABLE` defers.** Two rows that violate `exclude using gist (id with =, during with &&)` both inserted successfully inside `begin; set constraints all deferred;` and were **both visible to a `select` in the same transaction**; the violation was raised at `commit`. So `UNIQUE`, `PRIMARY KEY` and `EXCLUDE` are all in scope, not just foreign keys and constraint triggers.
- **Deferral moves the check; it does not remove it.** The commit failed. S8's suppression value is bounded to the *inside* of the transaction — which is exactly where a guard that must refuse a statement, or logic that reads its own writes, lives.
- **`ALTER TABLE … ALTER CONSTRAINT` cannot make an existing non-FK constraint deferrable on PostgreSQL 17**: `ERROR: constraint "ex_b" of relation "s8b" is not a foreign key constraint`. The only route for an existing exclusion constraint is `DROP CONSTRAINT` + re-`ADD … DEFERRABLE`, and that `DROP CONSTRAINT` is already destructive under ADR-0037 §3.2.

**What this means for `ex_subscription_no_overlap`.** It is created without `DEFERRABLE` (`20260911172124_InitialCatalog.cs:219`), so `condeferrable = false` and `SET CONSTRAINTS` cannot touch it. It is **not exposed today**. What is missing is the witness: `CatalogSchemaTests.cs:122` reads `contype` only and asserts `'x'`. **Nothing pins `condeferrable = false`**, so a future drop-and-re-add as `DEFERRABLE INITIALLY DEFERRED` leaves that test green while the no-overlap invariant stops holding inside every transaction. The row that closes it reads `condeferrable` and `condeferred` beside `contype`, for every constraint the catalog relies on and not only this one.

### The positive rule that goes with S1

`CREATE TRIGGER` produces `tgenabled = 'O'`. **A migration that creates a guard trigger and does not follow it with `ALTER TABLE … ENABLE ALWAYS TRIGGER` has built a guard that `session_replication_role = 'replica'` suppresses.** Nothing in ADR-0037 §2's destructive set catches it, because nothing was suppressed — the guard was born weak. B-19 spent four statements on this deliberately and said so in the migration.

**No mechanism enforces this today.** It is stated in the present tense on purpose.

## 4. What this file is not

- **An `asserted` row is a claim, and one of them has already been falsified.** S8's scope clause was `asserted` and was wrong. Promoting rows is not bookkeeping: it is how this file avoids becoming the thing it was written to prevent. S2, S10 and S1's `CREATE OR REPLACE TRIGGER` limb were promoted on 2026-09-12 by executing them.
- **It is not complete, and it cannot claim to be.** §2 gives the method that finds the next row; it does not guarantee the last row has been found. The completeness of the table rests on human knowledge of PostgreSQL, which is precisely the link that broke twice. Naming the method is the mitigation, not a fix.
- **It is not a mechanism.** Nothing executes this file. A check that claims to cover suppression **lists the row ids it covers and asserts that its declared set equals its implemented set** — that makes a check's coverage measurable without pretending the table behind it is complete. A check that covers S1 and S4 and says so is worth more than one that says "suppression".
- **It is not only MIG2's.** Three mechanisms read it: MIG2's destructive set (ADR-0037 §2), B-19's runtime guard assertions (`CatalogAppendOnlyGuardTests`), and whatever checks a tenant database's guards after a restore or a provisioning run. A fact three mechanisms need lives where all three can find it.

## 5. When to come back

- **A row arrives from an incident rather than from an audit.** That is the signal the method in §2 is not being run, and the method is the whole value of this file.
- **Any row moves from `asserted` to `executed`** — record what was run, on which image, and what was observed.
- **A guard is written as a constraint trigger**, which puts S8 in reach of it.
- **A tenant database grows row-level security**, which puts S6 on the hot path rather than the catalog's.
