# ADR-0042 — The destroy path names the database it read, and the escape hatch is on the wrong statement

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Amends:** **ADR-0007 §11.4** — the offboarding state machine is unchanged. Its `PendingDeletion` and `Deleted` bullets describe two statements and no identity rule, and they list the rename before the revoke in an order PostgreSQL refuses. §2–§6 below supply the rule and correct the order; §11.4 carries a dated banner pointing here.
- **Depends on:** **ADR-0036 §4 (`task/ARCH-MIGRATION-IDENTIFIERS`, PR #19, unmerged at the time of writing)**, which decides *which name* the two statements may use, and which adds the destroy-path row to ADR-0034 §5's scope table. This ADR decides *how the two statements are sequenced, what may reach them, and what each may conclude* — the part that belongs in ADR-0007 §11.4, where B-07.2's author will look. **It touches no file PR #19 owns.** If ADR-0036 is withdrawn before merge, §2's rule below is the one that must then be stated somewhere, because it is the only identity rule the destroy path has.
- **Superseded by:** —
- **Related:** ADR-0007 §4.3 (the stamp), §5.2 (per-tenant pools), §8 (provisioning is a saga, not a transaction), §9.2 (`catalog.operator_audit_event`), §11.4, ADR-0018 §6, ADR-0027 §2, ADR-0033 §2, ADR-0034 §4–§5
- **Raised by:** the orchestrator, relaying `task/ARCH-MIGRATION-IDENTIFIERS`'s finding that ADR-0034 §5's scope table had no destroy-path row. That branch deliberately did not edit ADR-0007 §11.4 to avoid colliding with this one.

> The two irreversible operations in the whole system run against a database they are not connected to, from a session that cannot see the tenant's identity. One of them has a `FORCE` option and the other does not — and it is the wrong one.

---

## 1. Executed first

`postgres:17-alpine`, 2026-09-12. A tenant database with a `platform.tenant_identity` table, and one other session connected to it.

**P8 — from a session connected to the tenant database:**

```
SELECT current_database();                                      -> aurora_t_acme
ALTER DATABASE aurora_t_acme RENAME TO deleted_acme_20260912;   -> ERROR: current database cannot be renamed
DROP DATABASE aurora_t_acme;                                    -> ERROR: cannot drop the currently open database
```

**P9 — from the maintenance database, one other session connected, each statement issued separately so attribution is unambiguous:**

```
SELECT count(*) FROM pg_stat_activity WHERE datname='aurora_t_acme';  -> 1
ALTER DATABASE aurora_t_acme RENAME TO deleted_acme_20260912;
    -> ERROR: database "aurora_t_acme" is being accessed by other users
       DETAIL: There is 1 other session using the database.
REVOKE CONNECT ON DATABASE aurora_t_acme FROM PUBLIC;                 -> (succeeds)
ALTER DATABASE aurora_t_acme RENAME TO deleted_acme_20260912;
    -> ERROR: database "aurora_t_acme" is being accessed by other users
SELECT count(*) FROM pg_stat_activity WHERE datname='aurora_t_acme';  -> 1
```

**P10 — the same state, the other statement:**

```
DROP DATABASE aurora_t_acme WITH (FORCE);                             -> (succeeds)
SELECT count(*) FROM pg_database WHERE datname LIKE '%acme%';         -> 0
```

Three facts follow, and the third is the one nobody has written down:

1. Both statements must be issued from a **different** database, which is where ADR-0034 §5's destroy row and ADR-0036 §4 start.
2. **`REVOKE CONNECT` does not terminate anything.** The session survived it and the rename failed again. A revoke closes the door; it does not empty the room.
3. **`DROP DATABASE` has `WITH (FORCE)` and `ALTER DATABASE … RENAME` has no equivalent.** The *reversible* step can be blocked indefinitely by one idle connection; the *irreversible* step ships with a way through. An operator who cannot get the rename to go will find that the drop does.

---

## 2. Decision 1 — the name named is the name read

> **`ALTER DATABASE … RENAME TO` and `DROP DATABASE` name the value `current_database()` returned on a connection to that database whose `TenantIdentityStamp` was asserted. Never a `database_name` read from `catalog.tenant`.**

This is ADR-0036 §4's rule; it is restated here because ADR-0007 §11.4 is where it will be looked for, and a rule stated only in the ADR that discovered it is a rule the next reader does not meet. The catalog's `database_name` is a *claim*; ADR-0034 §2 is the record of why a claim is not identity, and the destroy path is the one place where acting on a wrong claim cannot be undone.

**The chain, and where it stops.**

| Link | Established by | Stops at |
|---|---|---|
| The tenant's identity is inside the tenant's data | `platform.tenant_identity`, ADR-0007 §4.3 | holds |
| The connection that read it reached the tenant's database | `TenantIdentityStamp.AssertAsync` | **Asserted on no path today** — §7 |
| The name that connection reports is the tenant's database name | `current_database()` on that same connection | holds |
| The statement issued from the maintenance database acts on that database | the name, resolved again by PostgreSQL | **Check-then-act.** Between the read and the statement, nothing holds the name to the same database. ADR-0036 §4 names this window; the 30-day reversible rename and the deletion certificate cover it by detection, not prevention |

---

## 3. Decision 2 — the sequence, corrected

ADR-0007 §11.4's `PendingDeletion` bullet reads *"database renamed to `deleted_<key>_<date>`, `CONNECT` revoked from everyone but `aurora_admin`"*. P9 shows that order cannot execute: the rename fails while any session is connected, and the revoke does not remove one.

> **The destroy path's steps, in order:**
>
> 0. **Evict the tenant's data source from the pool registry** (ADR-0007 §5.2). A suspended tenant still has a pooled data source until something removes it, and a pool that reconnects after step 2 makes step 3 fail forever. This step is not optional and is the one an implementer will skip, because the failure it prevents looks like a flake.
> 1. **Connect to the tenant database, assert the stamp, read `current_database()`.** Everything after this names that value.
> 2. **`REVOKE CONNECT` from everyone but `aurora_admin`**, then **terminate the remaining backends** on that datname. Revoke first, so termination is not racing a reconnect; terminate second, because revoke alone leaves the room full (P9).
> 3. **`ALTER DATABASE <read name> RENAME TO deleted_<key>_<date>`**, issued from the maintenance database.
> 4. Thirty days later, **`DROP DATABASE deleted_<key>_<date>`** — see §4.

**Why step 1 survives step 3.** `platform.tenant_identity` is a table inside the database; the rename changes the name and nothing else. So the drop, thirty days later, can repeat step 1 against the *renamed* database and assert the same stamp. **The irreversible statement is not the one with the weaker check** — both steps can name a database they verified from the inside, and an implementation that verifies only before the rename has thrown away the cheaper of the two assurances.

---

## 4. Decision 3 — `WITH (FORCE)` is refused on the destroy path, and the drop is gated on the rename

`DROP DATABASE … WITH (FORCE)` terminates the sessions that are in the way (P10). By the time step 4 runs, the database has been revoked, emptied and renamed thirty days earlier; a session on it is an **anomaly**, and `FORCE` converts "something is wrong here" into "it is gone".

> **`DROP DATABASE … WITH (FORCE)` is not used on the destroy path.** The plain form is used and its failure is a stop, not an obstacle.
>
> **And `DROP DATABASE` is reachable only for a database whose name matches the recorded `deleted_<key>_<date>` for that tenant and whose stamp names that tenant.** A drop targeting a live, tenant-named database is refused by the saga before PostgreSQL is asked.

That second clause exists because of §1 fact 3. The reversible step is the one that can jam, and the irreversible step is the one with a documented way through; without a gate, the shortest path out of a jammed offboarding is the statement that cannot be undone. **The gate is in the saga, not in the database**, and that is where it stops: an operator with `aurora_admin` and a `psql` prompt is outside every sentence of this ADR. The control there is ADR-0007 §9.2's operator audit trail and §11.4's deletion certificate — detection, not prevention, and named as such.

---

## 5. Decision 4 — the rename is a saga step, not a statement

`CREATE DATABASE` cannot run in a transaction (ADR-0007 §1) and neither can these; ADR-0007 §8 already makes provisioning a saga for that reason. The destroy path is the same shape and is not written that way anywhere.

> **The target name is recorded in the catalog as an intent *before* step 3 is issued, and confirmed *after*.** A crash between the two leaves a database whose name the catalog already knows, which is findable; the reverse order leaves a database nobody can name.

**Where this stops:** nothing reconciles `pg_database` against `catalog.tenant` today, so an intent that was never confirmed is found by a human reading the table. A reconciliation job is the obvious answer and it is **not** decided here — it belongs with the fleet-operations row that owns the same problem for provisioning, and deciding it against no requirement is the failure this project has had to withdraw once.

---

## 6. What this ADR does not decide

- **Whether the 30-day windows are right.** They are ADR-0007 §11.4's and are unchanged.
- **Who may trigger offboarding, and with what approval.** That is ADR-0010's authorization model and the operator surface ADR-0033 §5.3 describes, which does not exist.
- **The reconciliation job** (§5).
- **Backup expiry**, which §11.4 already covers and which no statement here touches.

---

## 7. Present tense, so nothing here reads as live

- **`TenantIdentityStamp` is asserted on no path today.** The type ships with B-06.1a, which is in rework; the app-path initializer is B-06.2, not built; the DDL-path factory is B-07.1, not built; the destroy path is B-07.2, not built. Every sentence above that says "assert the stamp" is therefore a **specification**, exactly as ADR-0034 §5 says of its two other rows.
- **The destroy path itself does not exist.** No code renames or drops a tenant database.
- **P8, P9 and P10 are facts about PostgreSQL 17**, and they are true now. The rules built on them are not.

---

## 8. Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Name the read name, sequence the steps, gate the drop on the rename, refuse `FORCE`** *(chosen)* | Uses the one identity that travels inside the data, twice rather than once; the corrected sequence is executed rather than reasoned; the gate closes the shortest unsafe path out of a jam | Four steps where §11.4 implied two; the last link is still an operator with a prompt |
| B. State ADR-0036 §4's naming rule and stop | Smallest change; the identity question is the one that was asked | Leaves §11.4's impossible ordering in place, leaves `FORCE` unaddressed, and leaves the drop reachable without the rename — which is the part that makes a mistake permanent |
| C. Drop the tenant database by `oid` rather than by name | `oid` is not a claim | PostgreSQL has no `DROP DATABASE` by oid. There is no such statement |
| D. Keep the database forever and revoke all access instead | Nothing irreversible ever happens | Refuses the product's own promise. ADR-0007 §13 calls genuinely complete tenant deletion the strongest argument for database-per-tenant, and ADR-0018's erasure obligations are not met by a database nobody deleted |

---

## 9. Consequences

**Positive**

- The one path where ADR-0034 §4's control is structurally unavailable now has a written rule, in the section its implementer reads.
- §3's ordering is executed, so B-07.2 does not discover it by watching a rename fail.
- §4's gate closes a path that no review would have looked for, because it only appears when something else has already gone wrong.
- The rename preserving the stamp (§3) turns one identity assertion into two at no cost.

**Negative, and owned**

- **Check-then-act remains** (§2), and it is inherent: PostgreSQL resolves the name when the statement runs, not when the stamp was read.
- **Every control here is inside the saga.** `aurora_admin` with a prompt bypasses all of it, and the only answer is the audit trail. That is the same shape as ADR-0033 §5.3 and has the same honest limit.
- **Step 0 depends on a pool registry whose eviction API does not exist** (B-06.2 owns the data-source registry). The step is stated so that it is designed in rather than discovered.
- **§5's intent record adds a catalog write and a state nobody reconciles.** Named, not solved.

---

## 10. Revisit when

- **B-07.2 is specified.** Every rule here becomes an acceptance criterion then, and step 0 is the one to check first.
- **A rename jams in a real environment.** That is §4's gate doing its job or failing to; either way the incident is the evidence.
- **PostgreSQL gains a `FORCE` option on `ALTER DATABASE … RENAME`**, which would remove §1 fact 3's asymmetry and most of §4's reason.
- **A reconciliation job is scheduled** for provisioning (§5) — the destroy path's intent records join it rather than getting their own.
- **A regulator or customer requires deletion faster than the 30-day grace.** §11.4's windows are then the decision, not this ADR.
