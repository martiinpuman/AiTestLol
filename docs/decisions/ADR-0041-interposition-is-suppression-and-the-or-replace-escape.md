# ADR-0041 — Interposition is suppression, delegation is a third limb, and `CREATE OR REPLACE` gets no escape

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Supplements:** **ADR-0037 (`task/ARCH-MIGRATION-IDENTIFIERS`, PR #19, unmerged at the time of writing).** This ADR is a **supplement, not an amendment**: it changes no sentence of ADR-0037 and touches none of its files, so whoever merges PR #19 has nothing to reconcile. It adds to ADR-0037 §2 (§2 below) and confirms ADR-0037 §2.5 and §2.6 against the pressure the reviewer measured (§3 below).
  - **If PR #19 merges as written, both decisions below apply as written.**
  - **If ADR-0037 is withdrawn or materially changed before merge:** §2's ruling stands on its own — it is a ruling under ADR-0007 §7.2 rule 2 about which statements belong in the destructive set, and it needs only that rule to exist. §3's ruling has nothing left to attach to and must be re-read, because it is entirely a confirmation of ADR-0037 §2.5/§2.6 under pressure.
- **Superseded by:** —
- **Related:** ADR-0004 rule 5, ADR-0007 §7.2–§7.5, ADR-0027 §3–§4, ADR-0028 §2, ADR-0034 §3.1, ADR-0037 §2/§4/§5/§6, `../architecture/postgres-invariant-suppression.md` (rows to be added by that file's owner — see §4), `../architecture/testing-strategy.md` MIG1–MIG4
- **Raised by:** `ARCH-Q-CREATE-RULE` and `ARCH-Q-OR-REPLACE-ESCAPE`, from the third review of PR #16 (`task/B-09`)

> Two questions that sit on the same seam: what a `CREATE` may be concluded to do, and what a `CREATE OR REPLACE` may be excused for. Everything ruled below was executed against `postgres:17-alpine` except where it says otherwise — including one hypothesis of mine that the execution refuted.

---

## 1. Executed first, ruled second

Run on `postgres:17-alpine` on 2026-09-12, against a fixture that reproduces `catalog.operator_audit_event`'s shape: a table, a `refuse()` trigger function, a `BEFORE UPDATE OR DELETE … FOR EACH ROW` trigger set to `ENABLE ALWAYS`, and one row.

### P4 — `CREATE RULE … DO INSTEAD NOTHING`

```
CREATE RULE r_skip AS ON INSERT TO catalog.trail DO INSTEAD NOTHING;
INSERT INTO catalog.trail (what) VALUES ('after-rule');

 rows_in_trail        -> 1                        (the pre-rule row; the INSERT reported success)
 pg_trigger           -> trg_trail_append_only | A   (unchanged)
 information_schema   -> trail | BASE TABLE          (unchanged)
 pg_rewrite           -> r_skip | ev_type 3 | ev_enabled O | is_instead t
```

The `INSERT` raised nothing, returned success, and wrote no row. Every catalog a name-reading check looks at is byte-identical to the state before the statement. The single witness is a row in `pg_rewrite` that did not exist.

### P6 — `ALTER TABLE … SET UNLOGGED`

```
 pg_class.relpersistence   p  ->  u
 triggers still present    1
 information_schema        trail | BASE TABLE
```

### P5 — the hypothesis that was refuted

I expected `CREATE RULE "_RETURN" AS ON SELECT TO <table> DO INSTEAD SELECT …` to convert a table into a view and substitute what every reader sees. **PostgreSQL 17 refuses it:**

```
ERROR:  relation "plain" cannot have ON SELECT rules
DETAIL:  This operation is not supported for tables.
 pg_class.relkind -> r          (unchanged)
 SELECT * FROM catalog.plain -> 1 | real   (unchanged)
```

Recorded because it was in this ADR's draft as a ruled hazard and it is not one. It changes nothing below — §2.2 rules *every* `CREATE RULE` destructive, so the case would have been covered either way — but a ruling that had rested on it would have been a claim with no behaviour behind it.

### P7 — who may suppress

```
as aurora_app (SELECT, INSERT granted; not the owner):
  ALTER TABLE catalog.trail DISABLE TRIGGER trg_trail_append_only;  -> ERROR: must be owner of table trail
  CREATE RULE r_app AS ON INSERT TO catalog.trail DO INSTEAD NOTHING; -> ERROR: must be owner of table trail
  ALTER TABLE catalog.trail SET UNLOGGED;                           -> ERROR: must be owner of table trail

ALTER TABLE catalog.trail OWNER TO aurora_app;
as aurora_app (now the owner):
  ALTER TABLE catalog.trail DISABLE TRIGGER trg_trail_append_only;  -> succeeded
  pg_trigger -> trg_trail_append_only | D      (was A)
```

One `OWNER TO` statement, which at the moment it runs removes nothing and suppresses nothing, moved an `ENABLE ALWAYS` guard from unreachable to disablable by the application role — and the guard then went to `D`.

---

## 2. Decision 1 (`ARCH-Q-CREATE-RULE`) — interposition is suppression, and delegation is a third limb

### 2.1 The seam is not a seam

The question asks whether `CREATE RULE … DO INSTEAD NOTHING` sits between ADR-0037's two limbs: it *creates* an object (limb A's non-destructive direction) whose *effect* is limb B.

It does not sit between them, for two reasons, and the second is the one worth keeping.

**First, limb B's own text already covers it.** ADR-0037 §2.1: *"The object remains, its name remains, its definition remains, and something **outside** its definition decides it does not take effect."* A `DO INSTEAD NOTHING` rule is precisely a thing outside the table's, the trigger's and the constraint's definitions that decides they do not take effect. P4 is that sentence executed: the trigger's definition is unchanged at `tgenabled = 'A'`, and it never fires, because the statement that would have fired it was rewritten away before it reached anything.

**Second, "creating is the non-destructive direction" was never ADR-0037's rule.** §2.5 already rules `CREATE OR REPLACE` — a `CREATE` — destructive, and §2.1's second sentence is explicit: *"Spelling is evidence of effect and never a substitute for it."* Direction of statement was never the test. Effect was.

> **Ruling: `CREATE RULE … DO INSTEAD NOTHING` is destructive, limb B.**

### 2.2 The rule, wider than the instance

Ruling only the executed spelling would be the defect ADR-0037 §7.1 option B describes — a list complete only against the imagination of whoever last edited it. So:

> **Every `CREATE RULE` is destructive.**

The population is **zero** in this repository, and B-19's `AppendOnlyTrails` asserts at runtime that `pg_rewrite` holds no rule for either trail. `DO ALSO` is not carved out: it adds an action to somebody else's statement, which is interposition with a different sign, and the scanner cannot tell from tokens what the action does. The workaround for a legitimate rule is a trigger, which is what PostgreSQL's own documentation steers to; the cost today is nothing, and the check has no false positive it could acquire without someone deliberately writing the first `CREATE RULE` on this project.

**Also ruled destructive, same reasoning, same zero population:** `CREATE POLICY`, and `ALTER TABLE … ENABLE ROW LEVEL SECURITY` / `… FORCE ROW LEVEL SECURITY`. A policy interposes on *reads*: `pg_class.relrowsecurity` and `pg_policy` decide which rows a statement reaches, the table and its guards are untouched, and B-19's review executed exactly this shape (*"a forced row-level-security policy keyed the same way conceals every row from an armed session"*). Under database-per-tenant, RLS is not a tenancy mechanism — ADR-0007 §2 rejected option A — so a policy appearing in a migration is a thing that should cost a `Contract` and a Full-tier review. If a module ever wants RLS for a non-tenancy reason, that is §5's revisit trigger.

### 2.3 What §2.3's audit method would not have found, and the second question it needs

ADR-0037 §2.3's method is: *"For every invariant the schema can declare, find the catalog column that records whether it is in force, enumerate that column's values, and then enumerate the statements that write it."* That method finds `tgenabled`, `ev_enabled`, `indisvalid` — and it would **not** have found P4, because the suppression is not a value in a column of the suppressed object. It is a new row in a different catalog.

So the method needs a second question, and the two together are what makes it an enumeration rather than a habit:

> **Q1 (ADR-0037 §2.3, unchanged).** For every invariant the schema can declare, which catalog column records whether it is *in force*, what are that column's values, and which statements write it?
>
> **Q2 (new).** For every operation the schema performs, which catalog rows, **if added**, take effect *ahead of* it — and which statements add them?

Q1 finds degradation of an existing row. Q2 finds interposition by a new row. `pg_rewrite` is Q2's first answer and `pg_policy` its second; §2.4 is the third and is the one Q2 cannot decide.

### 2.4 `CREATE TRIGGER` — named, and deliberately **not** put in the destructive set

A `BEFORE … FOR EACH ROW` trigger whose function returns `NULL` silently skips the operation for that row. That is Q2's third answer and it is the same effect as P4 by a different catalog.

**It is not ruled destructive, and the reason is not that it is harmless.** It is that the scanner sees `CREATE TRIGGER`, not the function body's behaviour, and this repository's *existing* `AppendOnlyTrails` migration creates two `BEFORE UPDATE OR DELETE … FOR EACH ROW` triggers in an `Expand`. Ruling the statement destructive would turn a merged migration red at the moment MIG2 gained the rule — the MIG1-at-merge collision that ADR-0037 §6.3 records having been paid once already, caused a second time by a rule that cannot even decide the thing it fires on.

Where this is decidable is at runtime, after the fact, against the live catalog — which is where B-19's `CatalogAppendOnlyGuardTests` already reads `pg_trigger`, `pg_rewrite`, `pg_policy` and `pg_proc` and compares each with what the migration wrote. That is the same division of labour ADR-0034 §3/§4 draws between a narrowing and a control: **MIG2 removes the shapes a token stream can see are wrong; the guard check is what actually decides.**

> **Ruling: interposition by `CREATE TRIGGER` belongs to the runtime guard check, not to MIG2.** MIG2 must not claim to cover it, and any check claiming to cover suppression lists the row ids it covers (ADR-0037 §2.3's closing rule) — this one is not among them.

### 2.5 `ALTER TABLE … SET UNLOGGED` — destructive, limb B, and it is a **Q1** answer that is missing

The table remains, `information_schema` still says `BASE TABLE`, every constraint and trigger still fires — and the rows do not survive a crash, do not reach a physical standby, and are not in a PITR restore. For `catalog.operator_audit_event` and `catalog.erasure_replay_log` that is ADR-0028 §2's invariant deleted by a statement that mentions no trigger. The catalog column is `pg_class.relpersistence` and its values are `p` / `u` / `t`: a textbook Q1 row that the enumeration does not have.

> **Ruling: `ALTER TABLE … SET UNLOGGED` is destructive, limb B. `ALTER TABLE … SET LOGGED` is clean** — `p` is the top of the durability lattice, so from any prior state it is a widening or a no-op. It is the exact analogue of ADR-0037 §2.4's `ENABLE ALWAYS` row, and for the same reason.

**The accompanying positive rule, which nothing enforces:** `CREATE UNLOGGED TABLE` for an audit trail is a trail *born* weak. It narrows nothing that existed, so it is not in the destructive set — the same shape and the same present tense as ADR-0037 §2.6's guard born at `tgenabled = 'O'`.

### 2.6 `ALTER TABLE … OWNER TO` — **limb C**, because stretching limb B to cover it would be dishonest

`OWNER TO` neither removes nor suppresses. At the instant it runs, every invariant is exactly as enforced as it was. ADR-0037 §2.1 as written rules it **clean**, and P7 shows why that is the wrong answer: after one such statement the application role can disable an `ENABLE ALWAYS` guard, and `tgenabled` went from `A` to `D` in the next statement.

The honest move is to extend the rule rather than to stretch limb B into covering something it does not describe:

> **Limb C — delegation. A statement is destructive when it enlarges the set of principals that may issue a limb-A or limb-B statement against an object a declared invariant depends on.**

Under limb C:

| Statement | Ruling |
|---|---|
| `ALTER TABLE / SEQUENCE / FUNCTION / SCHEMA … OWNER TO` | **Destructive.** The owner may `DISABLE TRIGGER`, `DROP TRIGGER`, `CREATE RULE`, `CREATE POLICY`, `SET UNLOGGED` and `CREATE OR REPLACE` the guard's function. Executed: P7 |
| `GRANT TRIGGER ON …` | **Destructive.** The grantee may create the §2.4 trigger |
| `GRANT SET ON PARAMETER session_replication_role` (and `GRANT … ON PARAMETER` of any `SUSET` parameter that suppresses) | **Destructive.** `AppendOnlyTrails`'s own comment names this as the residual it could not close: *"one `GRANT SET ON PARAMETER` away"* |
| `GRANT <role> TO <role>`; `ALTER ROLE … SUPERUSER` / `BYPASSRLS` / `REPLICATION` | **Destructive.** Role membership and role attributes are delegation by another spelling |
| `GRANT SELECT / INSERT / UPDATE / DELETE / USAGE` | **Clean.** A data privilege does not let the grantee issue DDL. The two existing catalog migrations contain only these and `ALTER DEFAULT PRIVILEGES … REVOKE`, and both stay clean |

**Population check before ruling, because a rule that reddens a merged migration has cost this project two rounds already.** Every `GRANT`/`REVOKE`/`OWNER` statement in the repository today: `GRANT USAGE ON SCHEMA catalog`, five `GRANT SELECT`/`GRANT SELECT, INSERT`/`GRANT SELECT, INSERT, UPDATE` on catalog tables, and one `ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC`. **Limb C reddens none of them**, and `OWNER TO` appears nowhere.

**What limb C deliberately does not rule.** `REVOKE` of a data privilege narrows what the schema offers a reader and would arguably be limb A; ruling on it would change the category of a *merged* migration (`InitialCatalog`'s `ALTER DEFAULT PRIVILEGES … REVOKE`), it is not what either question asked, and deciding it here would be deciding a merged artefact's category from outside the review that accepted it. **Undecided, and it waits on** whoever next revises ADR-0007 §7.2 rule 2's treatment of privilege statements as a group.

### 2.7 Where the chain stops, per ruling

| Ruling | The chain | It stops at |
|---|---|---|
| `CREATE RULE` destructive | token stream names `CREATE RULE` → the destructive set contains it → the migration needs a `Contract` | The token stream. A rule created from a `DO $$ … $$` body or by dynamic SQL is not `CREATE RULE` in the batch text; ADR-0037 §3.2's rule that an unattributed command is judged on its text alone applies |
| `CREATE POLICY` / `ENABLE ROW LEVEL SECURITY` destructive | same | same |
| `SET UNLOGGED` destructive | token stream → `pg_class.relpersistence` → durability of the trail | The token stream, and the fact that **nothing checks `relpersistence` at runtime**. Unlike `tgenabled`, no existing guard test reads it |
| `OWNER TO` destructive (limb C) | token stream → PostgreSQL's ownership model → who may issue a limb-B statement. Executed end to end at P7 | The token stream, and the assumption that the roles named in a migration are the roles deployed. No deployment exists to check that against |
| `CREATE TRIGGER` **not** in the destructive set | — | Deliberately: the decision is passed to the runtime guard check (§2.4) |

---

## 3. Decision 2 (`ARCH-Q-OR-REPLACE-ESCAPE`) — neither relaxation; the pressure case is a dilemma with both horns closed

ADR-0037 §2.5 confirms the strict reading and §2.6 gives the truthful path. The question is only about the residual pressure: the *only* way to comply while **keeping** `OR REPLACE` is to declare `Contract`, which MIG1 then forces to name an `Expand` that, for a brand-new function, does not exist — a false statement MIG1 cannot detect, feeding the link MIG3 will be built on. That reading is correct.

### 3.1 Relaxation (a) — an explicit `Replaces = "<object>"` accepted in an `Expand` — is refused

The annotation would be an **acknowledgement**, and the property that would make the replacement safe is not one the annotator can check. ADR-0037 §6.2 accepts an annotation whose chain *"stops at `Reason`, which no machine reads"*, and that is acceptable there because the question behind it — *is `catalog.database_cluster` small enough to index in a transaction?* — is one a reviewer can answer by looking. The question behind `Replaces` is *does any code that will still be running at schema N−1 depend on the old body?* That is ADR-0037 §4's widening-versus-readers hole in a different costume, and §4 already says plainly that nothing enforces it and the gate cannot see which release starts producing the new behaviour. An annotation that legalises a destructive statement by declaring a fact nobody can establish is worse than no annotation, because it reads like one that was checked.

### 3.2 Relaxation (b) — narrow to "`OR REPLACE` of an object this migration population has already created" — is refused, and this is the interesting one

The proposal's appeal is real and it is stated correctly in the question: **it is a link the scanner can compute and does not**, since `MigrationPopulation` holds every earlier migration's generated SQL. Under ADR-0037's own principle, deciding by effect against state the migration controls beats deciding by acknowledgement.

It is refused because **the link it computes is not the link the conclusion rests on**, and the two are anti-correlated.

ADR-0037 §2.5's ruling turns on two properties. Neither is about who created the object.

1. **Invisibility.** *"The trigger still exists. It still names the same function… `pg_trigger` is byte-identical. Only `pg_proc.prosrc` changed, and nothing that reads names can see it."* Authorship of the object changes none of that. A body replaced by the same population is exactly as invisible as one replaced by a stranger.
2. **Coexistence.** §2.6's whole argument for the Expand/Contract path is that *"the old and the new body coexist for one release, so code running against schema N is never surprised."* `OR REPLACE` destroys coexistence — and it destroys it **hardest in precisely the case (b) would permit**, because the population having created the object is *evidence that something in the population already points at it*. The `AppendOnlyTrails` migration is the concrete instance: it creates `catalog.refuse_append_only_change()` and, four statements later, four triggers that name it. A later `CREATE OR REPLACE` of that function is (b)'s central case and is the most dangerous statement in the whole class.

So (b) is not merely insufficient; it selects for the hazard. Recorded in those words because it is a cheap-to-compute link that *looks* like a safety property, and the general form is worth keeping: **ADR-0037 §3.1 refuses a link nothing enforces; this refuses a link nothing connects.** A scanner being able to follow a link is not evidence that the link carries the conclusion.

### 3.3 The resumability case, which is the only real pressure — and it is a dilemma with both horns closed

The argument: once MIG4 lands, a `suppressTransaction: true` migration is not rolled back on failure; `CREATE OR REPLACE FUNCTION` is PostgreSQL's only idempotent create for a routine (there is no `CREATE FUNCTION IF NOT EXISTS`); so such a migration must choose between resumability, which `CLAUDE.md` requires, and an honest category.

Both horns close.

**Horn 1 — a migration whose only schema change is a routine creation has no reason to suppress its transaction.** ADR-0037 §6.1 requires suppression for exactly one thing: `CREATE INDEX CONCURRENTLY`, which cannot run inside a transaction block. Nothing else in this project's DDL requires it. A routine creation on its own runs inside the migration's transaction and is rolled back on failure, which is what makes it re-runnable — no idempotence needed.

**Horn 2 — a migration that suppresses its transaction for another statement is not made resumable by one of its statements being idempotent.** Re-running it re-runs the others, and the statement that forced the suppression is itself not re-runnable: ADR-0037 §6.1 records that *"a failed concurrent build leaves an index PostgreSQL marks invalid and ignores for querying, to be found and dropped out of band"*, so the re-run fails on the existing name. `OR REPLACE` would buy the *appearance* of resumability on one line of a migration that is not resumable. That is `CLAUDE.md`'s "a mechanism that cannot fail is not a check" in its constructive form: a partial idempotence that makes the whole read as idempotent.

**Executed, so the horns are not hypothetical about this repository:** `grep -rn "suppressTransaction\|CONCURRENTLY\|CreatedConcurrently" src/ --include=*.cs` returns **nothing**. There are zero transaction-suppressed commands in the repository, and every function it creates is spelled `CREATE FUNCTION` (ADR-0037 §2.5's own count, re-checked: the only `OR REPLACE` string in `AppendOnlyTrails.cs` is inside a comment describing the attack, not in SQL).

> **Ruling: ADR-0037 §2.5 stands unrelaxed. `CREATE OR REPLACE` gets no escape, because it needs none:** the escape for an accidental one is deleting two words, and the truthful path for a deliberate one is §2.6's Expand/Contract pair, where step 1 *is* a real `Expand` and step 2's `ContractOf` names it — no false statement anywhere, and MIG1/MIG3 read a true link.

### 3.4 The accompanying rule this creates, and what I did not verify

Horn 2 is an argument about a shape no mechanism enforces, so it names one:

> **A migration that contains a transaction-suppressed command performs no second schema change.**

Population today: **zero** (executed above), which is why this is decided now rather than under pressure from the first branch that wants it — and why it costs nothing to decide.

**What I did not verify, in the present tense:** whether EF Core's own `__EFMigrationsHistory` insert appears in `MigrationPopulation`'s command list, and therefore whether "no second schema change" must be phrased to exclude it. The row that builds this rule must check that and report the shape it found, rather than inheriting my phrasing. I did not run it.

---

## 4. Work this creates

No backlog row and no `docs/architecture/` file owned by another in-flight branch is edited here. Two hand-overs:

- **`docs/architecture/postgres-invariant-suppression.md` needs rows**, and that file belongs to `task/ARCH-MIGRATION-IDENTIFIERS` (PR #19) until it merges. The rows, for its owner to assign ids to: **Q2/interposition — `pg_rewrite` (a `DO INSTEAD` rule, executed P4), `pg_policy` + `pg_class.relrowsecurity` (a policy, asserted), `pg_trigger` (a `BEFORE … FOR EACH ROW` trigger returning `NULL`, asserted, and §2.4 routes it to the runtime check). Q1 — `pg_class.relpersistence`, values `p`/`u`/`t`, written by `ALTER TABLE … SET UNLOGGED` (executed P6). Limb C — PostgreSQL's ownership model, written by `ALTER … OWNER TO` (executed P7).** The refuted hypothesis (P5: PostgreSQL 17 refuses `ON SELECT` rules on tables) belongs there too, as a row that is **not** a hazard, because the next person will have the same idea.
- **MIG2 gains `CREATE RULE`, `CREATE POLICY`, `ENABLE`/`FORCE ROW LEVEL SECURITY`, `SET UNLOGGED` and limb C's statements**; MIG-something gains §3.4's rule. Row text is in the handback.

Until those land, every ruling above is a **specification and nothing enforces it**. Present tense, on purpose.

---

## 5. Consequences and revisit when

**Positive**

- The `CREATE`-versus-suppression seam is closed with a rule rather than an instance, and the audit method that missed it gains the second question that would have found it.
- Limb C names a real category that limb A and limb B genuinely do not cover, rather than widening limb B until it covers everything and means nothing.
- Every ruling was checked against the existing migration population before it was made; **none of them reddens a merged migration**, which is the third avoidance of a collision this project has paid for twice.
- `CREATE OR REPLACE`'s last argument — resumability — is answered with two horns instead of a preference, so it does not come back.

**Negative, and owned**

- **`CREATE TRIGGER` interposition is left to a runtime check that covers the catalog trails and nothing else.** A tenant-database guard has no equivalent today. This is the largest open hole in §2 and it is stated rather than papered over.
- **Limb C's chain stops at "the roles named in a migration are the roles deployed"**, and no deployment exists to check that against.
- **`SET UNLOGGED` has no runtime counterpart**: `tgenabled` is compared after the fact by B-19's tests, `relpersistence` is compared by nothing.
- **`REVOKE` of a data privilege is deliberately undecided** (§2.6), so the destructive set's treatment of privilege statements is complete in one direction only.
- **Three rulings rest on a population of zero.** That is what makes them cheap, and it also means none of them has ever fired. The first time one does is the first evidence it is implemented correctly.

**Revisit when**

- **A module wants row-level security for a non-tenancy reason.** §2.2 rules `CREATE POLICY` destructive against a population of zero; a real requirement is the trigger to decide whether `Contract` is the right price.
- **A tenant-database migration creates a guard trigger.** §2.4's hole becomes live there, and the runtime guard check has no tenant-side equivalent.
- **A migration genuinely needs both a concurrent index build and another schema change.** §3.4 forbids it; that request is the evidence the rule is wrong, and it should arrive with the reason.
- **`ALTER … OWNER TO` is proposed for any reason at all.** Limb C makes it destructive; if the reason is good, it is a `Contract` and a Full-tier review, which is the intended outcome, not an obstacle to remove.
- **`ARCH-Q-CREATE-RULE`'s `REVOKE` neighbour is picked up** by whoever next revises ADR-0007 §7.2 rule 2 for privilege statements as a group.
