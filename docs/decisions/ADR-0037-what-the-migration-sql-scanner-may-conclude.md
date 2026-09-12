# ADR-0037 — What the migration SQL scanner may conclude: effect over spelling, constraint drops, widening, the release manifest and transactional index builds

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Amends:** **ADR-0007 §7.2** — §2 below states the rule that decides what belongs in rule 2's destructive set at all; rule 2's list is narrowed in §5 (a provably widening `ALTER COLUMN … TYPE`) and §3 (a `DROP CONSTRAINT` correlated to a typed check-constraint operation) and widened in §2.4 (suppression statements); rule 3's "migration manifest" is defined in §6; rule 4's `CREATE INDEX CONCURRENTLY` requirement gains a declared per-index exemption in §7. **The decision ADR-0007 §7.2 made — expand and contract are separated, and the separation is enforced by a test rather than by discipline — is unchanged and reaffirmed.**
- **Superseded by:** —
- **Related:** ADR-0003 rule 6, ADR-0004, ADR-0007 §7.1–§7.5, ADR-0008 §4.1 (package migrations are scanned too), ADR-0027 §3–§4, ADR-0028 §2, ADR-0034 §3.1, ADR-0036 §3, `../architecture/postgres-invariant-suppression.md`, `../architecture/testing-strategy.md` MIG1–MIG4
- **Raised by:** all three reviews of PR #16 (`task/B-09`) and B-20's developer, through the orchestrator

> Six questions from three reviews, and they are one question: what may a static scanner *conclude* from the SQL in front of it, and which link does each conclusion stop at. §2 is the rule; §3–§7 are it applied.

---

## 1. Context

`task/B-09` builds the migration safety scanner ADR-0007 §7.2 rule 2 asks for. It generates each migration's `Up` SQL through Npgsql's own generator, tokenises it the way PostgreSQL's lexer does, and names what a category must justify. Six questions came out of its reviews and out of B-20, and none can be settled inside the branch.

1. **Bare `DROP CONSTRAINT` was excluded from the destructive set**, on the reasoning that replacing a `CHECK` is a widening with no Expand to name. The reviewer showed that holds for `CHECK` and for nothing else, executed: `DROP CONSTRAINT pk_invoice`, `DROP CONSTRAINT uq_… CASCADE` — which reaches foreign keys in tables the migration never names — and `DROP CONSTRAINT ex_…` all scanned clean. **Is a `CHECK` replacement an Expand, and may the scanner read the `ck_` / `pk_` / `uq_` / `ex_` naming convention to tell the cases apart?**
2. **Is a trigger's firing mode a Contract-only change?** The second reviewer executed the family and found the destructive set matching on the *spelling* of a switch rather than its *effect*: `ENABLE REPLICA TRIGGER`, `ENABLE REPLICA RULE` and `SET session_replication_role = 'replica'` all scanned clean. `ENABLE REPLICA` sets `pg_trigger.tgenabled = 'R'`, so the trigger fires **only** in a replica session and never for an ordinary write — for a guard, indistinguishable from `DISABLE TRIGGER`. It specifically reverses work B-19 paid for: four `ALTER TABLE … ENABLE ALWAYS TRIGGER` statements bought precisely so its append-only guards survive replica mode.
3. **Is `CREATE OR REPLACE FUNCTION` an Expand when the function already exists?** B-19's own review named a hollowed-out guard body as a one-statement bypass that leaves every `pg_trigger` column byte-identical while the `TRUNCATE` lands. B-19 closed the runtime half by comparing `pg_get_functiondef`; a *migration* shipping that replacement is a different surface and currently scans as an ordinary additive statement.
4. **Every `ALTER COLUMN … TYPE` is treated as needing a `Contract`**, without judging widening from narrowing.
5. **B-09.3 needs a release manifest** so rule 3's gate can refuse an Expand and its Contract shipping together — and a manifest nobody generates is a gate that never fires.
6. **B-20's migration creates `ux_database_cluster_host_port` without `CONCURRENTLY`**, transactionally and on purpose. Rule 4 as written forbids that.

**This is the second time the `tgenabled` fact has cost a round.** PR #9's review met it from the other side: the natural predicate `tgenabled <> 'D'` counts a replica-mode trigger as live *while the `TRUNCATE` succeeds*. Two branches, two mechanisms, one underlying truth — **disabled is not the only way to stop a trigger firing** — rediscovered twice at review cost. A fact that expensive should not live only in two review threads, which is why §2.3 gives it a file.

While this record was being written, B-09 decided the strict reading rather than wait, and wrote it into `tests/Aurora.Architecture.Tests/README.md` §7 so it could be confirmed or relaxed. §2.4 does that, item by item.

---

## 2. Decision 1 — the destructive set classifies by effect, and the effect has two limbs

### 2.1 The rule

> **A statement is destructive when, from some prior state the migration does not control, it either narrows what the schema offers to a reader, or reduces the set of executions in which a declared invariant is enforced.**
>
> **Spelling is evidence of effect and never a substitute for it.** Two spellings with one effect are one rule. One spelling with two effects is two rules, and where the scanner cannot tell which, it is the destructive one.

Two limbs follow from it, and keeping them apart is what makes the rule usable — they behave oppositely on widening.

**Limb A — removal.** The statement takes an object, a column, a row or data away. It is **visible**: something that was in the catalog is no longer there, and anyone who looks sees the absence. Limb A is destructive when it **narrows what the schema offers** — dropping a column, a table, a key, an index, or data a reader depends on. It is **not** destructive when it widens what the schema permits **and no writer depended on the thing removed**: `DROP NOT NULL`, dropping a `CHECK`, widening a `varchar`. No stored row becomes invalid.

**That second condition is not decoration, and the first draft of this record got it wrong by omitting it.** `DROP DEFAULT` was written here as an example of a safe widening. It is not one, and the counter-example was executed on `postgres:17-alpine`:

```
create table t(id int primary key, n int not null default 7);
insert into t(id) values (1);                    -- INSERT 0 1
alter table t alter column n drop default;       -- ALTER TABLE
insert into t(id) values (2);
-- ERROR:  null value in column "n" of relation "t" violates not-null constraint  (23502)
```

An insert that succeeded before the migration fails after it. Expand migrations deploy **ahead** of the code (ADR-0007 §7.5), so code N−1 is the writer that breaks, on every tenant in the fleet, from a migration a widening-blind MIG2 would pass as an `Expand`. The general form: **a limb-A removal widens the set of permitted *states* and may narrow the set of permitted *statements*.** Those are different sets, and only the first is safe by construction.

**So limb A's widening exception carries a reader-and-writer obligation, discharged in the migration, not deferred to §4.** A migration claiming the exception states in `[MigrationSafety(Reason = …)]` which writers and readers depended on the removed thing and why none of them is version N−1. For `DROP NOT NULL` that is usually trivial and still has to be said; for `DROP DEFAULT` it is almost never dischargeable, because the whole purpose of a default is that writers omit the column. **`DROP DEFAULT` is therefore treated as destructive unless the migration discharges the obligation explicitly** — the opposite of where this record started.

**Limb B — suppression.** The object remains, its name remains, its definition remains, and something *outside* its definition decides it does not take effect. Limb B is **always destructive, with no widening exception**, and the reason is not the size of the change but its **invisibility**: after a limb-B statement the catalog still shows the guard, `information_schema` still shows the guard, a reviewer reading the migration that created it still sees a guard — and it does not fire. B-19 spent four statements closing one limb-B hole; one limb-B statement reopens it and nothing that reads names can tell.

**This is why `DROP NOT NULL` is clean and `ENABLE REPLICA TRIGGER` is not**, although both reduce enforcement. The first removes a claim; the second keeps the claim and removes the enforcement. A schema that no longer claims something is honest. A schema that claims something it no longer does is the failure mode this project has met more than any other.

### 2.2 What this does *not* license

The rule decides membership of the destructive set. It does not decide that a destructive statement is forbidden — a `Contract` migration may contain one, annotated and reviewed at Full tier. And it does not license the scanner to *infer* effect from anything it cannot follow: §3 refuses a naming convention for exactly that reason. Effect is established from the token stream, from the EF operation that generated the statement, or from the migration population — never from a habit.

### 2.3 What enumerates the effects: the catalog columns that record "in force"

Limb A enumerates itself; a removal names what it removes. **Limb B does not**, and that is why it has been rediscovered twice. The method that would have found `tgenabled = 'R'` the first time is systematic and is adopted here:

> **For every invariant the schema can declare, find the catalog column that records whether it is in force, enumerate that column's values, and then enumerate the statements that write it.** `tgenabled` has four values and only one of them is `D`. Reasoning from the statement (`DISABLE TRIGGER`) finds one. Reasoning from the column finds all four.

The enumeration lives in **`docs/architecture/postgres-invariant-suppression.md`**, not in this ADR and not in a review thread, because three different mechanisms need it — MIG2's destructive set, B-19's runtime guard assertions, and whatever checks a tenant database's guards after a restore — and a fact three mechanisms need belongs where all three can find it. Each row carries a stable id (`S1`, `S2`, …), the catalog column, the values that weaken the invariant, and the statements that write it.

**The link this stops at, stated plainly: the table's completeness rests on human knowledge of PostgreSQL, and it is incomplete today.** Naming the audit method is the mitigation, not a fix — it converts "did we think of everything?" into "which catalog columns have we walked?", which is a question with a finite answer someone can be held to. Any check claiming to cover suppression **lists the row ids it covers and asserts that its declared set equals its implemented set**; that makes a check's coverage measurable without pretending the table behind it is complete.

### 2.4 B-09's three calls, ruled

| B-09's call | Ruling | Limb, and the reason |
|---|---|---|
| `ENABLE REPLICA TRIGGER`, `ENABLE REPLICA RULE` destructive | **Confirmed** | **B.** `tgenabled = 'R'` / `ev_enabled = 'R'`: fires only under `session_replication_role = 'replica'`, never for an ordinary write, while `pg_trigger` still shows a trigger on the table |
| `SET session_replication_role` destructive in every spelling — `SET`, `SET LOCAL`, `ALTER ROLE … SET`, `set_config(…)` | **Confirmed, and extended to `ALTER DATABASE … SET`** | **B.** It suppresses *every* origin-mode trigger and rule in reach for the session, naming none of them. It is the broadest limb-B statement PostgreSQL has, and the one whose blast radius the scanner can least bound |
| `ENABLE ALWAYS TRIGGER` clean | **Confirmed** | `A` is the top of the lattice: from any prior state this is a widening or a no-op. It is the **only** firing-mode statement that cannot reduce |
| `ENABLE TRIGGER` (plain) clean | **Relaxed call overturned — it is destructive** | **B.** `ENABLE TRIGGER` sets `O`. From `A` that is a reduction: the trigger stops firing in replica sessions. The scanner does not know the prior state, so it must assume the one where the statement does harm. This is exactly B-19's four statements undone by a word that reads like an enabling |
| `CREATE OR REPLACE` of anything destructive | **Confirmed, with a different reason** | **B.** See §2.5 |
| `SET search_path`, plain `CREATE FUNCTION` clean | **Confirmed** | Neither removes nor suppresses. (`search_path` *can* change what an identifier in a function body resolves to — but a function's own `SET search_path` is part of its definition, which `pg_get_functiondef` renders and B-19's runtime check compares) |
| `DROP NOT NULL`, `ATTACH PARTITION`, `DROP CONSTRAINT … RESTRICT` clean | **Confirmed**, with §2.1's obligation stated in `Reason` | **A, widening.** `DROP CONSTRAINT` is refined by §3, not by this row |
| `DROP DEFAULT` clean | **Overturned — destructive** unless the migration discharges §2.1's obligation | **A, and not a widening.** Executed: an insert omitting the column succeeds before and fails `23502` after. A default exists so writers can omit the column; removing it narrows the set of permitted statements |
| `CREATE … IF NOT EXISTS` clean | **Confirmed**, and see §2.5's *proves too much* test | It cannot displace an existing object, so it is neither limb. `CREATE EXTENSION IF NOT EXISTS "btree_gist"` is emitted by Npgsql from `CatalogDbContext.cs:99`'s `HasPostgresExtension` and this repository cannot spell it otherwise |

### 2.5 `CREATE OR REPLACE` — confirmed, and why the reason matters

B-09 justified it by capability: *"a body replaced is one the scanner cannot compare with the one it displaces."* That is true and it is the weaker argument, because it invites the answer "then teach the scanner to compare". The effect argument does not:

**`CREATE OR REPLACE FUNCTION` over an existing guard is the purest limb-B statement available.** The trigger still exists. It still names the same function. The function still exists with the same name, the same signature, the same owner. `pg_trigger` is byte-identical. Only `pg_proc.prosrc` changed, and nothing that reads names can see it. That is what B-19's review found, and it is why B-19 had to descend to `pg_get_functiondef` to close it.

The same holds for `CREATE OR REPLACE VIEW` (readers see different rows under an unchanged name) and `CREATE OR REPLACE TRIGGER` (the trigger's whole definition is displaced under an unchanged name — and **whether it preserves `tgenabled` is unverified**; see §2.7).

**On the orchestrator's concern — that this makes a first-time `CREATE OR REPLACE FUNCTION` written out of habit need a `Contract`, and a rule that is worked around is worse than a narrower one.** The concern is right in general and does not apply here, for a reason worth stating so it is not re-litigated:

> **The workaround is deleting two words, and deleting them makes the migration better.** A migration runs exactly once per database, under a session-level advisory lock (ADR-0007 §7.3). `OR REPLACE` buys no idempotence it needs, and written as `CREATE FUNCTION` a migration that unexpectedly meets an existing object **fails loudly**, which is information.

**That paragraph is a statement about the cost, and it is explicitly not the rule.** The first draft of this record went further and argued that `OR REPLACE` is destructive because "the statement's meaning depends on state the migration does not control". **That argument proves too much, and the case that breaks it is in this repository.** `CatalogDbContext.cs:99` calls `HasPostgresExtension("btree_gist")`, from which Npgsql's own generator emits `CREATE EXTENSION IF NOT EXISTS "btree_gist"`. That statement's meaning also depends on uncontrolled state, it is emitted by the provider rather than written by a developer, and **the repository cannot spell it any other way.** A rule derived from the state-dependence argument would condemn a statement nobody can remove, which is how a gate stops being obeyed.

**The rule is limb B, and limb B is about displacement:**

| | Can it displace an existing object? | Verdict |
|---|---|---|
| `CREATE OR REPLACE …` | **Yes** — that is its entire purpose | Destructive |
| `CREATE … IF NOT EXISTS` | **No** — if the object exists it does nothing at all | Neither limb. Clean |

`IF NOT EXISTS` cannot leave an object named-but-changed, so there is nothing for a reader of names to be wrong about. That is the whole of limb B's concern, and it is the test to apply before extending this rule to any other spelling: **ask whether the statement can displace.** If the answer is no, the argument that reaches it is the wrong argument.

**It costs nothing today**: every function this repository creates is already spelled `CREATE FUNCTION` (`20260912020620_AppendOnlyTrails.cs:130`).

### 2.6 What a legitimate change to a guard function looks like

Since `CREATE OR REPLACE` is destructive and §6's gate requires a `Contract` to name the `Expand` it contracts, "annotate it `Contract`" is not an escape — there would be no Expand to name. The legitimate path is the expand/contract discipline ADR-0007 §7.2 already mandates for columns, applied to a routine:

1. **Expand release, schema version N.** `CREATE FUNCTION catalog.refuse_append_only_change_v2() …`. Nothing points at it yet. Purely additive; no destructive finding.
2. **Contract release, schema version N+1**, annotated `Contract` with `ContractOf` naming the migration from step 1. In one transaction: `DROP TRIGGER …`; `CREATE TRIGGER … EXECUTE FUNCTION catalog.refuse_append_only_change_v2()`; `ALTER TABLE … ENABLE ALWAYS TRIGGER …`; `DROP FUNCTION catalog.refuse_append_only_change()`.

Two properties this buys that `CREATE OR REPLACE` does not. The change is **visible to anything comparing names** — a new `pg_proc` entry, a re-pointed `tgfoid` — rather than hidden in a body. And the old and the new body **coexist for one release**, so code running against schema N is never surprised, which is the whole point of the discipline.

The `ENABLE ALWAYS` in step 2 is not optional and is not decoration: `CREATE TRIGGER` produces `tgenabled = 'O'`, and a guard left at `O` is suppressible by `session_replication_role`. **A migration that creates a guard trigger and does not follow it with `ENABLE ALWAYS` has built a weaker guard than the one it replaced**, and nothing in the destructive set catches that, because nothing was suppressed — the guard was merely born weak. Recorded in §2.3's file as the accompanying positive rule; no mechanism enforces it today.

### 2.7 One thing executed since this record was drafted, and one it still does not know

- **`CREATE OR REPLACE TRIGGER` resets `tgenabled` from `'A'` to `'O'` — executed, no longer a guess.** On `postgres:17-alpine`:

  ```
  create trigger tg before update on g for each row execute function gf();
  alter table g enable always trigger tg;              -- tgenabled = A
  create or replace trigger tg before update on g for each row execute function gf();
  -- tgenabled = O
  ```

  So `CREATE OR REPLACE TRIGGER` silently undoes an `ENABLE ALWAYS` in a statement that mentions no firing mode, and the guard becomes suppressible by `session_replication_role` again. It is limb B twice over — the definition is displaced *and* the firing mode is reset — and §2.5's ruling now rests on a witness rather than on plausibility. Recorded in §2.3's file as S1.
- **Whether `aurora_migrator` can set `session_replication_role` at all.** It is a `SUSET` parameter; if the migration role is not superuser and has no `GRANT SET`, the statement fails at runtime, which would be a second and stronger line than the scanner. **Nothing on this project has established which**, no deployment exists, and the scanner rule stands on its own either way. Confirming it belongs to whoever owns role provisioning, and the answer belongs in §2.3's file.

---

## 3. Decision 2 — a `DROP CONSTRAINT` is judged by the operation that produced it, never by the constraint's name

### 3.1 The naming convention is refused as a link, and the reason generalises

**The scanner may not read `ck_` / `pk_` / `uq_` / `ex_`.** Three reasons, in increasing order of how much each would still apply if the previous were fixed:

1. **Nothing enforces the convention.** No test asserts that every constraint this repository creates carries its kind's prefix. `CLAUDE.md`'s standing rule is that a check is only as good as the last link it follows, and a convention nothing enforces is not a link at all — it is a correlation that has held so far.
2. **Even perfectly enforced, it would be the wrong link.** The scanner would infer a constraint's *kind* from a *name chosen by whoever wrote the migration that created it* — possibly a different migration, in a different release, in a different assembly. Country Package migrations are scanned by the same mechanism (ADR-0008 §4.1), and a package author's naming habits have no authority over the core's safety gate.
3. **It fails in the silent direction and is trivially bypassed.** Naming a primary key `ck_invoice_id` is one keystroke, produces no warning anywhere, and turns a destructive drop into a clean scan. A gate whose bypass is "pick a different name" is not a gate.

The generalisation, worth carrying past this decision and quoting rather than re-arguing: **a scanner may follow a link that the compiler, the type system or the database enforces. It may not follow a link that only a habit enforces.** Naming conventions are excellent for humans reading a schema and are never evidence for a machine.

### 3.2 What B-09 does instead: correlate the statement to the EF operation that generated it

The link that *is* enforced is already in `MigrationPopulation`'s hands and is currently thrown away. It instantiates each migration, runs `Up()`, and hands `migration.UpOperations` to Npgsql's `IMigrationsSqlGenerator`. EF Core models a check-constraint drop as **`DropCheckConstraintOperation`**, a distinct CLR type from `DropPrimaryKeyOperation` and `DropUniqueConstraintOperation` — all three verified present in `Microsoft.EntityFrameworkCore.Relational` 10.0.x, the version this solution pins. The three generate identical SQL; they are not identical operations.

> A `DROP CONSTRAINT` statement is judged non-destructive **only** when the command containing it was generated by a `DropCheckConstraintOperation` whose `Schema`, `Table` and `Name` match the statement, **and** the constraint it names appears as a check constraint in the migration's **source model** — the `TargetModel` of the immediately preceding migration in the same assembly, read through `IEntityType.GetCheckConstraints()`. Every other `DROP CONSTRAINT`, including one in raw `migrationBuilder.Sql(…)`, one inside a procedural body, and one carrying `CASCADE`, is destructive and requires `Contract`.

Attribution — which generated command came from which operation — is obtained by generating **per operation** alongside the batch generation the scan already does, and matching command texts. Batch generation stays authoritative for *what is scanned*; per-operation generation answers only *which operation produced this command*. A command per-operation generation does not account for is unattributed, and unattributed is destructive.

**The chain, link by link, with the place it stops:** `DROP CONSTRAINT` in generated SQL → the `MigrationOperation` that generated it, obtained from EF rather than guessed from the text → its CLR type is `DropCheckConstraintOperation`, which only `migrationBuilder.DropCheckConstraint(…)` produces → the name appears among the *source* model's check constraints, which EF built from the previous migration's own snapshot → therefore the object dropped was a `CHECK` in the schema this migration starts from. **The chain stops at EF's model snapshot, not at the database.** A constraint created by raw SQL is not in the model, is never attributed, and is always destructive — the loud direction. Nothing consults a live database; nothing on this project can.

**What must demonstrate it, watched failing:** four fixture migrations, scanned, asserting the verdict on each — `DropCheckConstraint("ck_x")` clean; `DropPrimaryKey("pk_invoice")` **destructive**; `DropUniqueConstraint("uq_x")` **destructive**; `migrationBuilder.Sql("alter table s.t drop constraint ck_x")` **destructive**, though the name is the same one the first case allows. The third distinguishes this mechanism from a naming convention; the fourth distinguishes it from name-matching against the operation list. The rule reports **how many `DROP CONSTRAINT` statements it examined and how many it attributed**; zero examined is not a pass.

### 3.3 Is a `CHECK` replacement an Expand?

"Replacing a `CHECK`" is not one thing, and treating it as one is how the hole opened. It is two statements, each categorised by its effect on the set of rows the table permits.

| The replacement | Category | Why |
|---|---|---|
| **Strictly widening** — every row the old predicate permitted, the new one permits | **Expand** | Limb A, widening. No stored row becomes invalid, no writer that satisfied the old predicate starts failing, and nothing is left for a later Contract to remove, so rule 3 has nothing to separate |
| **Dropping without replacing** | **Expand** | The widest case of the above |
| **Strictly narrowing, or neither** (incomparable predicates) | **Contract**, and two-phase | The `ADD` is the risk, not the `DROP`: `ALTER TABLE … ADD CONSTRAINT … CHECK` scans the whole table and locks out other updates until it commits, and **fails outright on any tenant database holding a violating row**. A fleet migration that fails on tenant 4 217 of 9 000 is the partial failure ADR-0007 §7.4 is built around |

**The two-phase shape for a narrowing**, which is the part a developer needs written down:

1. **Expand release:** `ALTER TABLE … ADD CONSTRAINT … CHECK (…) NOT VALID`. PostgreSQL documents that with `NOT VALID` the command *"does not scan the table and can be committed immediately"*; it is enforced for new and updated rows from that moment and says nothing about rows already there. A `DataOnly` migration or a background repair fixes the existing rows.
2. **Contract release, one schema version later:** `ALTER TABLE … VALIDATE CONSTRAINT …`. PostgreSQL documents that validation *"acquires only a `SHARE UPDATE EXCLUSIVE` lock on the table being altered"*, because concurrent transactions already enforce the constraint themselves. The scan is the expensive half; it does not block writes.

EF Core's `migrationBuilder.AddCheckConstraint` does not emit `NOT VALID`, so phase 1 is raw `migrationBuilder.Sql(…)` — which the scanner reads as code, by design.

**A `NOT VALID` constraint is itself a limb-B state** (`pg_constraint.convalidated = false`: the constraint is listed and does not hold for pre-existing rows), which is why it appears in §2.3's file. It is *deliberate* limb B, for one release, and the thing that makes it acceptable is that phase 2 is scheduled. **Nothing enforces that phase 2 ever ships.** A `NOT VALID` constraint left unvalidated for ten releases is a constraint the schema claims and does not hold — present tense, and the natural home for a check is the same gate that reads the manifest in §6.

**Who decides "strictly widening", and the link that stops there: a human.** Deciding whether one SQL predicate implies another is undecidable in general and this project will not approximate it. The author states it in `[MigrationSafety(Reason = …)]` — naming the old predicate, the new one, and why every row satisfying the old satisfies the new — and a reviewer checks it at **Full** tier, which a migration already is. **No mechanism verifies the claim.** §3.2's mechanism guarantees only that the object dropped is a `CHECK` and not a key or an exclusion constraint. That is a smaller guarantee than "this replacement is safe", it is stated at its true size, and review fills the gap.

---

## 4. Decision 3 — a widening `ALTER COLUMN … TYPE` is an Expand, on an allowlist of two shapes

`ALTER COLUMN … TYPE` normally rewrites the whole table and its indexes under `ACCESS EXCLUSIVE`. PostgreSQL documents one exception: *"if the `USING` clause does not change the column contents and the old type is either binary coercible to the new type or an unconstrained domain over the new type, a table rewrite is not needed. However, indexes will still be rebuilt unless the system can verify that the new index would be logically equivalent"* — and it names the case that matters: *"in the absence of a collation change, a column can be changed from `text` to `varchar` (or vice versa) without rebuilding the indexes because these data types sort identically."*

| Shape | Expand? |
|---|---|
| `varchar(n)` → `varchar(m)` with `m > n` | **Yes** |
| `varchar(n)` or `varchar` → `text` | **Yes** |
| Everything else — every `numeric` precision or scale change, every narrowing, every change of base type, anything carrying `USING` or a collation change | **No — `Contract`** |

Three conditions attach to the two allowed shapes, all checkable on tokens the scanner already has: **no `USING`**, **no `COLLATE`**, and **no other action in the same `ALTER TABLE`** (a `SET NOT NULL` beside a type change is not a widening and stays destructive).

**Why `numeric` is excluded rather than reasoned about.** Money on this project is `numeric` (ADR-0021). Whether `numeric(12,2)` → `numeric(18,2)` is binary coercible and therefore rewrite-free is a question **I did not verify**, and the cost of being wrong on a ledger table across a fleet is a multi-hour `ACCESS EXCLUSIVE` lock per tenant. An allowlist containing only what has been checked against the documentation is worth more than one containing what sounds right. Adding `numeric` later is one table row and a paragraph of evidence; it is not done here because the evidence is not here.

**The residual:** even in the two allowed shapes, `ALTER TABLE` takes `ACCESS EXCLUSIVE` for the catalog update. It is brief — no scan, no rewrite, no index rebuild — but it queues behind every open transaction on that table and blocks everything behind it while it waits.

**Widening is safe for the database and is not automatically safe for readers.** ADR-0007 §7.5 has expand migrations deploying *ahead* of the code, so code version N−1 runs against schema N. A column widened from 50 to 100 characters does not break N−1 while nothing writes values longer than 50 — and only version N does. This is the **same obligation §2.1 attaches to every limb-A widening**, and it is discharged the same way: the migration's `Reason` names the readers and writers that depended on the old bound and why none of them is version N−1, and a reviewer checks it at Full tier. **No mechanism verifies the claim** — the gate reads migrations and cannot see which release starts producing the wider values. The rule stops at a human knowing which readers exist (§8), and the difference between this record's first draft and this one is that the obligation is now *stated in the migration* rather than left as a hole in a consequences section.

---

## 5. Decision 4 — the release manifest is the migration assemblies, read through two declarations on `[MigrationSafety]`

### 5.1 There is no manifest file, because a file is a thing somebody forgets to regenerate

ADR-0007 §7.2 rule 3 says the gate "reads the migration manifest". No such artefact exists, and the brief's warning is the right one: *a manifest nobody generates is a gate that never fires.* The answer is to make the manifest something the compiler already produces.

| Member of `MigrationSafetyAttribute` | On which migrations | Meaning |
|---|---|---|
| `SchemaVersion` (int) | **Every** migration | The schema version this migration belongs to. Many migrations share one; it is the release ordinal |
| `ContractOf` (string) | **Exactly** the `Contract` ones | The EF migration id of the `Expand` this migration contracts |

B-09 has already built `ContractOf`'s rule (*"must name the Expand it contracts, which must exist and be an Expand"*, README §7). This record confirms it and adds the version ordinal that makes rule 3's *separation in time* checkable rather than only its *existence*.

**Why the schema version and not a separate release number.** ADR-0007 §7.5 already gates every scope open on `CoreSchemaVersion.Current` and `MinimumSupported`, with `Current − MinimumSupported ≤ 1` — which *is* the statement "code runs against this release and the one before". Rule 3's "one release apart" and §7.5's "one version apart" are the same interval. Two numbers for one interval is two clocks that can disagree, silently. One number, and it is the one the runtime already fails closed on.

### 5.2 What the gate checks, and what it reports

| | Check |
|---|---|
| G1 | Every migration carries `[MigrationSafety]` — MIG1, built |
| G2 | `ContractOf` is set **if and only if** `Category == Contract` |
| G3 | `ContractOf` names a migration id present in the same assembly's population — built |
| G4 | The named migration's `Category` is `Expand` — built |
| G5 | `Contract.SchemaVersion > Expand.SchemaVersion` — this is rule 3, and it is the new one |
| G6 | Schema versions are non-decreasing in migration-id order |
| G7 | For the tenant-database population, `max(SchemaVersion) == CoreSchemaVersion.Current` |

It reports **migrations examined, `Contract` migrations examined, and Expand/Contract pairs checked.**

**The count is not decoration.** There are three migrations today, all `Expand` or `DataOnly`, and **zero** `Contract` migrations anywhere in the repository. A gate printing `PASS` having examined zero Contracts is indistinguishable from one that is broken — `CLAUDE.md`'s self-check #2 exactly. MIG3 must print `Contract migrations examined: 0` **and** carry the inertness guard this project already uses for a rule with an empty population (B-04's T2 shape, which expired the day B-05 gave it something to read): it pins the population size it expects to be zero and goes red when that stops being true without the rule having been exercised.

### 5.3 What is not built, in the present tense

- **`CoreSchemaVersion` is on `task/B-06.1a` (PR #13) and is not merged.** Until it merges, G7 has nothing to read and must not be written as though it does.
- **`CoreSchemaVersion.Current` is `0` and no tenant-database migration exists.** Every migration in the repository today is a *catalog* migration, and the catalog has **no** schema-version constant and no skew gate. For catalog migrations G7 does not apply and G6 is the only ordering check there is. A catalog skew gate would need its own constant and is a new decision, not an extension of this one.
- **G7's stronger form** — `CoreSchemaVersion.Current` equals the highest ordinal across registered `IModuleSchemaMigrator`s — **is B-08.3's** (ADR-0027 §4), and no migrator is registered. G7 as written checks declarations against the constant; it does not check the constant against reality.

---

## 6. Decision 5 — `CREATE INDEX` may be transactional where a named index says why

### 6.1 Rule 4 is narrowed, not exempted by database

ADR-0007 §7.2 rule 4 makes `CREATE INDEX CONCURRENTLY` "mandatory for any index on a table that a live tenant writes to". B-20 builds `ux_database_cluster_host_port` transactionally, and that is correct:

- `CREATE INDEX CONCURRENTLY` **cannot run inside a transaction block**, so a concurrent build sits outside the migration's transaction and its failure is not rolled back with the rest. ADR-0007 §7.4's resumability assumes a failed migration leaves no partial DDL.
- A failed concurrent build **leaves an index PostgreSQL marks invalid and ignores for querying**, to be found and dropped out of band. For `ux_database_cluster_host_port` specifically, the whole of ADR-0034 §3.1 rests on that index being present and enforced; an invalid one nobody notices is a link nothing is reading, inside the mechanism ADR-0034 exists to create. It is limb B by accident (`pg_index.indisvalid`), which is why §2.3's file carries it.
- `catalog.database_cluster` holds one row per cluster. The transactional build is milliseconds.

**A wholesale "catalog migrations are exempt" rule is refused.** `catalog.operator_audit_event` and `catalog.erasure_replay_log` are append-only trails (ADR-0028) that will be large and write-hot, and a blanket exemption would let a future index on one of them lock it for the duration. The exemption is per index, because each index build is its own decision.

### 6.2 The exemption is an annotation, because a documented exemption nothing reads is not an exemption

`Aurora.Platform.Tenancy.Contracts` gains a repeatable attribute:

```csharp
[TransactionalIndexBuild(
    "ux_database_cluster_host_port",
    Reason = "catalog.database_cluster holds one row per cluster, so the build is milliseconds. "
           + "CONCURRENTLY cannot run in the migration's transaction and a failed build leaves an "
           + "invalid index behind; ADR-0034 §3.1's argument depends on this index being enforced.")]
```

**MIG4's rule:** for every `CREATE [UNIQUE] INDEX` in a migration's generated SQL **without** `CONCURRENTLY`, the migration must carry a `TransactionalIndexBuild` naming exactly that index. An annotation naming an index the migration does not create is itself a failure — an exemption that outlives its statement is the shape that silently widens. MIG4 reports **indexes created, built concurrently, and exempted by name.**

**The chain, and where it stops:** the generated SQL names the index → the annotation names the same index → the exemption cannot be broadened to another index by accident or by editing one word. **It stops at `Reason`, which no machine reads.** Whether `catalog.database_cluster` really is small is checked by a reviewer at Full tier and by nobody else.

### 6.3 The ordering problem, stated before it bites

**MIG4 does not exist** (B-09.2) and **`TransactionalIndexBuildAttribute` does not exist.** B-20's migration lands first and will carry no annotation. When MIG4 goes live it will go red on that migration — precisely what happened once already, when MIG1 went red at merge on B-19's unannotated migration (PR #16, blocker 2).

**The row that builds MIG4 owns the attribute and owns annotating every migration that already exists.** It is not B-20's work and B-20's branch does not change for it. Written here so the second occurrence of this collision is planned rather than discovered at a merge.

---

## 7. Options considered

### 7.1 The basis for the destructive set (§2)

| Option | Pros | Cons |
|---|---|---|
| **A. Classify by effect, in two limbs, with limb B enumerated by catalog column** *(chosen)* | Covers spellings nobody has thought of yet, because the audit walks columns rather than statements; explains `DROP NOT NULL` clean and `ENABLE REPLICA` destructive with one rule; gives the twice-rediscovered fact a home three mechanisms can read | The enumeration's completeness still rests on human knowledge; naming the method is a mitigation, not a fix |
| B. Classify by spelling — a list of statements | Simple, exact, no judgement | It is the defect. Three reviews found three spellings of one effect, one at a time. The list is complete only against the imagination of whoever last edited it |
| C. Classify by effect but enumerate by *statement family* rather than catalog column | Closer to how the SQL reads | `tgenabled` is the counter-example: the `DISABLE`/`ENABLE` family reads as two states and has four. Enumerating statements finds what the statements say; enumerating columns finds what the database records |
| D. Read the live catalog and diff before/after | The only thing that actually measures effect | No deployment exists; a build-time gate that needs a database is not a build-time gate. It is, however, the right shape for an *install-time* check, which ADR-0008 §4.1 already does for package migrations |

### 7.2 Judging a `DROP CONSTRAINT` (§3)

| Option | Pros | Cons |
|---|---|---|
| **A. Correlate to the EF operation, corroborated by the source model** *(chosen)* | Follows links the compiler and EF's snapshot enforce; fails closed on raw SQL and procedural bodies; the bypass requires calling a different EF API, visible in review | Needs per-operation generation in `MigrationPopulation`; the source-model lookup is real work; still proves only the *kind*, not that the replacement widens |
| B. Read the `ck_` / `pk_` / `uq_` / `ex_` naming convention | Trivial; no new machinery | Nothing enforces it; package authors are not bound by it; the bypass is choosing a name. §3.1 |
| C. Enforce the convention with its own fitness rule, then read it | Makes the convention a real link | Two mechanisms where one suffices, and the second is still weaker — a rule constraining names cannot constrain what a name is attached to. It also binds every Country Package author to the core's naming |
| D. Keep every bare `DROP CONSTRAINT` destructive (B-09's current state) | Simplest; strictly safe; fails loudly | Every legitimate `CHECK` widening must be annotated `Contract`, which is a lie in the annotation, and §5's G5 then demands an Expand that does not exist. Annotations that must be falsified to get work done are how a gate loses its meaning |
| E. Read the live database | The only source that knows the kind | Same objection as 7.1 D |

### 7.3 The release manifest (§5)

| Option | Pros | Cons |
|---|---|---|
| **A. Two declarations on `[MigrationSafety]`, read from the assemblies** *(chosen)* | The compiler produces it; nothing to regenerate, nothing to forget; typed, reviewable in the diff beside the migration it describes, read by the population the scanner already builds | The version ordinal is hand-maintained, checked against `CoreSchemaVersion` for the tenant population and against nothing for the catalog |
| B. A committed `migrations.manifest.json` regenerated by a script | Explicit; an artefact a release process can publish | The failure named in the brief: generated by a step somebody omits, stale in exactly the way that makes the gate pass. It also duplicates facts already in the assemblies |
| C. Derive the release from git — the tag containing the commit that added the file | Nothing to maintain; reflects what shipped | No release tags exist; it breaks under a shallow clone, a squash or a rebase; and it makes a fitness test depend on repository history rather than on the code under test |
| D. Infer the Expand a Contract belongs to from table and column names | No declaration needed | Another naming convention, refused for §3.1's reasons |

---

## 8. Where each rule in this record stops

Every blocker found in this record's first draft was a sentence claiming more than the mechanism beneath it delivered. This table is the antidote and is meant to be extended rather than summarised in prose.

| Rule | Last link it follows | What is on the other side of that link, unchecked |
|---|---|---|
| §2.1 limb A, widening exception | The migration's `Reason`, read by a reviewer at Full tier | **A human knowing which readers and writers exist.** `DROP DEFAULT` is the executed proof that "widens the permitted states" and "breaks no writer" are different claims. No mechanism enumerates a column's writers |
| §2.1 limb B, suppression | The token stream, plus §2.3's table of catalog columns | **Human knowledge of PostgreSQL.** §2.3's table is incomplete by construction; the audit method is the mitigation |
| §2.5 `CREATE OR REPLACE` | Whether the statement *can displace* — a property of the spelling, decidable on tokens | Nothing. This one is closed, which is why `IF NOT EXISTS` falls out of it cleanly instead of needing an exemption |
| §3.2 `DROP CONSTRAINT` | EF's model snapshot for the preceding migration | **The live database.** A constraint created by raw SQL is not in the model, is never attributed, and stays destructive — the loud direction |
| §3.3 "is this `CHECK` replacement a widening" | The migration's `Reason`, read by a reviewer | **Predicate implication**, which is undecidable and which this project will not approximate |
| §3.3 `NOT VALID` phase 1 | Nothing schedules phase 2 | **Whether `VALIDATE CONSTRAINT` ever ships.** An unvalidated constraint is a claim the schema makes and does not hold |
| §4 `varchar` widening allowlist | PostgreSQL's documented binary-coercibility rule, cited | **`numeric`**, deliberately outside the allowlist because its rewrite behaviour was not verified here and money is `numeric` |
| §5 `SchemaVersion` | `CoreSchemaVersion.Current`, for the tenant population only | **The catalog population**, which has no schema-version constant and no skew gate; and **what was actually deployed**, which nothing on this project can observe |
| §6 `TransactionalIndexBuild` | The index name, matched against the generated SQL | **`Reason`**, which no machine reads. Whether the table is small is a reviewer's call |

## 9. Consequences

**Positive**

- The destructive set now has a *rule* rather than a list, so the next spelling of a known effect is caught by the mechanism instead of by a fourth review.
- The fact that cost two rounds — disabled is not the only way to stop a trigger firing — has a file, an id scheme, and an audit method that generalises to invariants nobody has thought about.
- `DROP CONSTRAINT pk_invoice` stops scanning clean, and what stops it cannot be defeated by renaming anything.
- A legitimate `CHECK` widening can be annotated truthfully as `Expand` and ship, which keeps category annotations honest. A gate people must lie to is worse than no gate.
- Rule 3 becomes implementable without a release process, which this project does not have and will not have soon.

**Negative, and owned**

- **`ENABLE TRIGGER` becoming destructive is stricter than it looks.** Re-enabling a trigger after legitimate maintenance now needs a `Contract`, and the honest alternative — `ENABLE ALWAYS` — is a *different* firing mode, not the same one. A migration that genuinely wants `O` has to justify it. Accepted: the population of migrations that want `O` on a guard table is, today, empty.
- **`MigrationPopulation` grows a second generation pass** (per operation, for attribution). More of the scanner's surface depends on EF internals — specifically on per-operation generation producing command texts that appear in the batch output. Where it does not, the command is unattributed and therefore destructive, so the failure is loud; the coupling is still real.
- **Four of these decisions stop at a human**, and §8 names each: whether a limb-A widening breaks a writer (§2.1), whether a `CHECK` replacement widens (§3.3), whether a transactionally built index is small enough (§6.2), and whether §2.3's table is complete. All four are written at that size rather than dressed as mechanisms.
- **The widening-versus-readers obligation is stated, not mechanised** (§2.1, §4). The gate reads schema and the obligation is on code, so it is discharged in a `Reason` and checked by a reviewer. §8 records that it stops at a human knowing which readers exist.
- **`DROP DEFAULT` moving to destructive will red a future migration that expected it to be free**, and the migration that wants it must argue for it. That is the intended cost of the correction, and the executed counter-example in §2.1 is why it is not negotiable.
- **A `NOT VALID` constraint that never gets validated is limb B with a reason**, and nothing schedules phase 2 (§3.3).
- **`[MigrationSafety]` grows two members** and every existing migration must supply `SchemaVersion`. Three today; the cost only rises.

---

## 10. Revisit when

- **§2.3's file gains a row from an incident rather than from an audit.** That is the signal the audit method is not being run, and the method is the whole value of the file.
- **A tenant-database migration exists** and `CoreSchemaVersion.Current` moves off `0`. G7 goes live that day and §5.3's first two bullets expire.
- **The catalog needs its own skew gate.** §5.3 says it has none.
- **A `numeric` widening is actually needed.** §4 excludes it for lack of verified evidence, not on principle.
- **A Country Package ships a migration with a constraint or a function created in raw SQL.** §3.2 makes every drop of that constraint destructive forever, which is correct and may become annoying enough to want a package-side declaration.
- **Anything proposes reading a naming convention as evidence again.** §3.1 is the general answer and should be quoted rather than re-argued.
