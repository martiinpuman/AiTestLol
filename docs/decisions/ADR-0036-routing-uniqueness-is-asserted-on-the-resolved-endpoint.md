# ADR-0036 — The routing-uniqueness criterion is asserted on the resolved endpoint, and a cluster host is stored lower-case

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Amends:** **ADR-0034 §3.3** (the acceptance criterion, replaced in §2.3 below), **ADR-0034 §2's variant 3 row** (one false statement of fact, corrected in §2.2), and **ADR-0034 §5's scope table** (a destroy-path row, §4 of this record). Adds **ADR-0034 §3.4** (§3 of this record). Everything else in ADR-0034 stands, including §3.1's index, §3.2's enumeration and the whole of §4's stamp argument.
- **Superseded by:** —
- **Related:** ADR-0007 §3.5, §4.3, ADR-0034 §2, §3.1–§3.3, §9, `../architecture/modules.md` §4
- **Raised by:** B-20's developer, through the orchestrator, while implementing ADR-0034 §3.3 verbatim

> ADR-0034 exists to say that a routing criterion must be the property and not the mechanism. Its own criterion was a mechanism — comparison of a composed string — and that comparison cannot fail. This record replaces it with the property it was reaching for.

---

## 1. Context

ADR-0034 §3.3 states the acceptance criterion for routing uniqueness as:

> *"No two non-deleted `catalog.tenant` rows produce the same resolved connection string, computed by running the real `ITenantConnectionResolver` over the real catalog rows and comparing the composed strings."*

B-20's developer implemented exactly that and found it vacuous. `TenantConnectionStringComposer.Compose` sets

```csharp
ApplicationName = settings.ApplicationNameFor(tenantKey),   // "aurora-web:<tenant key>"
```

(`src/platform/Aurora.Platform.Tenancy/Routing/TenantConnectionStringComposer.cs`), and `ux_tenant_key` makes the tenant key unique across `catalog.tenant`. **Two distinct tenants therefore cannot produce equal connection strings, whatever the routing does.** The criterion is satisfied by a fleet in which every tenant resolves to one another tenant's database.

This is the shape ADR-0034 was written to prevent, one level up: §3.3's own opening sentence is *"The assertion is the property, not the index"*, and then it named a comparand that is a mechanism — the serialised output of the composer, which carries a discriminator that the property is not about. It was quoted verbatim into a task brief as *the* acceptance criterion and approved by a reviewer. An implementer who followed the specification exactly would have written a check that reports clean on a broken fleet, and would have been right to believe they had done what was asked.

The developer instead compared the **physical endpoint** parsed back out of the resolved string, and that version bites: it found ADR-0034 variant 3 live before the migration, naming both tenants and the shared endpoint, and reports `pairs compared: 120; collisions: 0` afterwards.

Two further findings came with it, both in §3's territory and both settled here:

- `catalog.database_cluster.host` has no lower-case check, although `tenant_host` has `ck_tenant_host_lower_case`. Two rows differing only in case are two endpoints to `ux_database_cluster_host_port` and one server in reality.
- B-20's migration builds `ux_database_cluster_host_port` **without** `CONCURRENTLY`. That collides with ADR-0007 §7.2 rule 2 and is decided in [ADR-0037](ADR-0037-what-the-migration-sql-scanner-may-conclude.md) §5, not here, because it is a migration-safety question rather than a routing one.

---

## 2. Decision 1 — the comparand is the resolved physical endpoint

### 2.1 Why the composed string is the wrong comparand, stated so it is not simplified back

A connection string is a **serialisation of an intent**, not a description of a destination. It carries, beside the destination, at least: `Application Name` (per-tenant by construction, ADR-0016's requirement that a PostgreSQL session be attributable), `Username` and `Password` (which may differ per cluster), and every pool setting of ADR-0007 §5.2 (which differ between `TenantPoolSettings.Web` and `.Worker`). Every one of those is a field on which two strings can differ while addressing the same bytes on the same disk.

**Any discriminator anywhere in the string makes string equality a test that cannot fail.** `Application Name` is the one that does it today; removing it would not make string comparison correct, it would only make the next added field the one that breaks it. The comparison must therefore be over a projection that contains the destination and nothing else, chosen deliberately, rather than over whatever the composer happens to emit.

### 2.2 One correction of fact in ADR-0034 §2

ADR-0034 §2's variant 3 row reads *"Both tenants resolve to `127.0.0.1:32905/aurora_t_t_82b6f49207bc` — byte-identical connection strings"*. The endpoint claim is true; the phrase **"byte-identical connection strings" is false** and always was, for the reason in §2.1. It is corrected to **"one identical physical endpoint"**. This matters beyond tidiness: that phrase is the evidence §3.3's criterion rested on, and with it uncorrected a reader would reconstruct the vacuous criterion from §2.

### 2.3 ADR-0034 §3.3's criterion, replaced

ADR-0034 §3.3's block quote is replaced by the following. It keeps §3.3's surrounding argument — the assertion is the property, not the index — unchanged.

> **No two non-deleted `catalog.tenant` rows resolve to the same physical endpoint.** The endpoint of a tenant is the triple **`(host, port, database)` parsed out of the string the real `ITenantConnectionResolver` produced** — parsed with `NpgsqlConnectionStringBuilder`, from the resolver's own output, never read from the catalog row the resolver was given. Two endpoints are equal when `database` is equal ordinally (PostgreSQL database names are case-sensitive), `port` is equal, and **normalised hosts** are equal.
>
> **The host is normalised before comparison, and the normalisation is part of the criterion rather than an implementation detail:** fold case (DNS is case-insensitive), then **strip a single trailing `.`** (the DNS root label — `pg.internal.` and `pg.internal` are the same name, one written as a fully-qualified absolute form). Each normalisation step may only ever *merge* spellings, never split them: the comparator is looking for collisions, so a step that merges can produce a false positive, which is loud, and a step that splits produces a false negative, which is the shape this criterion was written to stop being.
>
> The assertion reports **how many tenants were resolved** and **how many pairs were compared**, and fails on zero of either.

Three things about that wording are load-bearing and are spelled out rather than left to be inferred:

1. **"parsed out of the string the resolver produced."** Reading `(host, port, database_name)` from the catalog rows instead would make the assertion a property of the *input* and would pass over a resolver that ignores its input entirely. The whole point is to measure what the resolver emits.
2. **`host` normalised, not compared raw.** This is deliberately *more* aggressive than the database's own uniqueness index, which is byte-exact. The assertion may therefore go red on a fleet the index accepted — that is correct, and §3 removes the gap from the other side.

   **The trailing dot is why this clause exists in this form.** The first draft of this record compared hosts case-insensitively and nothing else, and a reviewer reconstructed ADR-0034 variant 3 straight through it: `pg.internal.` beside `pg.internal` is one server in two spellings. Both store — executed on `postgres:17-alpine`, `check (host = lower(host))` accepts both, two rows inserted — so they are two rows to `ux_database_cluster_host_port`, two rows past §3's lower-case constraint, and two distinct hosts to a case-fold-only comparator, which then reports `collisions: 0`. **Every control in this record and in ADR-0034 §3 passed a live variant 3.**

   **The honest generalisation, which matters more than the dot.** "Two spellings of one endpoint" is an open class: a trailing dot, a CNAME, a short name beside its FQDN, a failover alias, an IP literal beside a name, `localhost` beside `127.0.0.1`. Normalisation closes the members that are *syntactic* — case and the root label — and cannot close the ones that are *resolutive*, because closing those means resolving DNS, which a test must not do and a catalog cannot record. ADR-0034 §4's stamp is what covers the rest — subject to §2.4's note that it is asserted on no path today, and to §4 below, which finds one path where it cannot be asserted at all.
3. **The counts.** `CLAUDE.md`'s self-check #2: a stage that reports success without a count cannot distinguish "all good" from "nothing ran". Zero tenants resolved, or zero pairs compared, is a failure, not a pass.

### 2.4 What must demonstrate it, and which link each demonstration stops at

The comparison and the fleet scan are **two separable pieces**, and separating them is what keeps the demonstration alive after the defect is fixed.

- **D1 — the comparator, on synthetic input, as a unit test.** A pure function from a list of resolved connection strings to a list of colliding pairs. Fed: two strings that differ only in `Application Name`; two that differ only in `Host` case; two that differ only in a **trailing dot on `Host`**; two that differ only in `Password`; two that differ only in `Database` case. The first four must be reported as collisions; the fifth must not. **This test can fail, and it stays able to fail forever**, because it never touches the catalog and therefore is not disarmed by the constraints that stop such rows from being stored.
- **D2 — the fleet scan, on a real catalog, as an integration test.** The same comparator over the real resolver's output for every non-deleted tenant, reporting both counts. **Its ability to fail is bounded by what the catalog will accept**: after §3.1's index and §3's check constraint are in place, no seed can construct a collision through the normal write path, so D2 is a regression guard and not a demonstration.
- **D3 — the one-shot demonstration, recorded on the pull request and not as a standing test.** D2 run against a catalog seeded with variant 3's two-cluster shape *before* `ux_database_cluster_host_port` exists, watched to go red, naming both tenants and the shared endpoint. B-20 has done this. It is written down here because **the fix removes the evidence**: once the index lands, nothing can reproduce D3, and a later reader who finds only a green D2 has no way to know D2 ever measured anything. That is this project's eighth recurring form — a fix that silently invalidates the evidence for the claim it was fixing — and the counter to it is to record the red run, with its output, where the claim lives.

**The link D1 and D2 stop at:** the triple is what the *catalog* was told the endpoint is. Two DNS names for one server, a CNAME, a failover alias, and an IP literal beside a hostname all resolve to one machine and to two different triples. ADR-0034 §3.2 enumerates this and §4 hands it to the `TenantIdentityStamp`.

> **That hand-off must be made in the present tense, and the first draft of this record did not.** Writing "the stamp is the control" reads as though a control is in effect. **It is not in effect on any path today.** `TenantIdentityStamp` ships with B-06.1a (PR #13, in rework); the app-path physical-connection initializer that asserts it is **B-06.2, not built**; the DDL-path factory that asserts it is **B-07.1, not built**. So the residual §3.2 enumerates is, right now, covered by nothing at all. That is acceptable only because no deployment exists, and it stops being acceptable the day one does.

---

## 3. Decision 2 — `catalog.database_cluster.host` is stored in one spelling (ADR-0034 §3.4)

The following is added to ADR-0034 §3 as a new §3.4.

> **§3.4 — A cluster host is stored in one spelling.**
>
> ```sql
> alter table catalog.database_cluster
>   add constraint ck_database_cluster_host_lower_case  check (host = lower(host)),
>   add constraint ck_database_cluster_host_no_root_label check (host not like '%.');
> ```
>
> **Two constraints, because there are two syntactic spellings of one endpoint and case is only one of them.** A trailing `.` is the DNS root label: `pg.internal.` and `pg.internal` name the same host and are two different strings to a byte-exact index. Refusing the absolute form rather than stripping it keeps the column's contents equal to what an operator typed, which is what every error message and metric label will show.
>
> §3.1's index is byte-exact, so without this a host written `DB1.example.com` and a host written `db1.example.com` are two rows, two endpoints to the index, and one server. This is part of the §3.1 invariant rather than a separate tidiness rule: the index's job is to make `cluster_id → (host, port)` injective **on the endpoint**, and it can only do that if one endpoint has one spelling. `ck_tenant_host_lower_case` already does the same job for `catalog.tenant_host`, so this is the existing rule applied to the column §3.1 depends on, not a new kind of rule.
>
> **What `lower()` actually does here, and what the losslessness claim rests on — both smaller than the first draft of this record said.**
>
> - **`lower()` is not lossless on arbitrary text, and the column is `text`.** Folding is lossless for a DNS name (case-insensitive by definition) and for an IPv4 literal (no letters). Nothing *in the database* restricts `host` to those: the restriction is `DatabaseCluster.IsWellFormedHost`, a C# check in the write path. **This record's own §6.2 refuses write-path-only rules as evidence for the constraint, and the losslessness argument then leans on exactly such a rule.** Stated rather than hidden: the constraint is sound, and the argument that it loses nothing stops at a C# method.
> - **The IPv6 sentence in the first draft was irrelevant.** `IsWellFormedHost` refuses `:` outright (`DatabaseCluster.cs:136-150`), so **no IPv6 literal can be stored in this column at all** — with or without brackets. Reasoning about hexadecimal group case described a value the system does not accept. The day IPv6 is allowed, the `:` refusal and this constraint are re-read together.
> - **Non-ASCII folding is collation-dependent, and the deployment does not pin the collation.** Executed on `postgres:17-alpine`, 2026-09-12: `lower('Ü' collate "C")` returns `'Ü'` unchanged, while `lower('Ü')` under the database's `en_US.utf8` default returns `'ü'`. **So `host = lower(host)` does not mean the same thing on two catalog databases created with different `lc_ctype`.** A reviewer could not reproduce an earlier "folds ASCII only" witness, and this is why: neither "ASCII only" nor "full Unicode" is true of the constraint — the behaviour is a property of the database it was created in. Recorded because it is the sort of fact that is rediscovered as a bug. It does not weaken the constraint for host names, which are ASCII; it means **the catalog's collation is an input to a constraint's meaning and should be pinned at `createdb` time**, which is B-07.1's territory and is not decided here.
> - It does **not** generalise to a path — a Unix-socket directory is case-sensitive, and if `database_cluster.host` is ever allowed to carry one, this constraint is wrong and §9 brings whoever changes it back here.

**What demonstrates it:** two integration tests in the shape `CatalogConstraintTests` already uses for eleven other constraints — insert a `database_cluster` row with `DB1.example.com` and assert `SqlState == "23514"` with `ConstraintName == "ck_database_cluster_host_lower_case"`; insert one with `pg.internal.` and assert the same `SqlState` with `ConstraintName == "ck_database_cluster_host_no_root_label"`. Asserting the constraint *name* is what makes each a test of its own rule rather than of whichever constraint happened to fire first — and with two constraints on one column that stops being a nicety.

**And the comparator test, which is the one that survives.** §2.4 D1 feeds the comparator two resolved strings differing only in a trailing dot and requires a collision. That test never touches the catalog, so unlike the two above it is not disarmed by the constraints that stop such rows existing — the §2.4 division applied to the very defect that motivated this paragraph.

**What it does not demonstrate, and the link it stops at:** it proves the database refuses the second spelling. It does not prove that the operator surface which creates cluster rows *normalises* rather than merely fails — the write path may still hand a mixed-case host straight through and surface a `23514` to an operator who typed a legitimate address. That is a usability consequence, it is owned by the row that builds the cluster-administration surface, and no such surface exists today.

**Migration category:** this constraint narrows the set of permitted rows, so it is not an Expand. See [ADR-0037](ADR-0037-what-the-migration-sql-scanner-may-conclude.md) §2.2 for the shape it must take (`NOT VALID` first, `VALIDATE CONSTRAINT` one schema version later) if `catalog.database_cluster` ever holds a row that violates it. **Today it holds none in any environment this project can observe**, because no deployment exists; the migration may therefore add it validated in one step, and that sentence is true only while it is true.

---

## 4. Decision 3 — ADR-0034 §5's scope table gains a destroy-path row, and it says the stamp cannot reach it

§2's link and §3's residuals both hand off to ADR-0034 §4's `TenantIdentityStamp`. §5 of that record tabulates where the stamp is asserted, and it has exactly two rows: **DDL** (provisioning, migration, package install) and **App** (requests, jobs, outbox). **There is no row for the destroy path, and the destroy path is the one where the stamp cannot be asserted at all.**

ADR-0007 §11.4 runs two irreversible operations:

- `PendingDeletion`: *"database renamed to `deleted_<key>_<date>`"*
- `Deleted`: *"`DROP DATABASE`"*

PostgreSQL cannot rename or drop a database from a session connected to it, so both run against the **maintenance database** — where `platform.tenant_identity` is not reachable and no stamp exists to assert. **The one control ADR-0034 §4 calls "the control" is structurally absent from the two operations that cannot be undone.** ADR-0034 §5's table is therefore added to:

> | **Destroy** — offboarding's rename and drop (ADR-0007 §11.4) | **No stamp can be asserted.** The statement runs against the cluster's maintenance database; the tenant's `platform.tenant_identity` is in the database being destroyed and is unreachable from there. See the rule below | **B-07.2** |

**What covers it instead, and it is weaker — say so.** The stamp is asserted at a *different time* from the operation it protects, which makes this a check-then-act with a window. The rule that shrinks the window as far as it goes:

> Before a rename or a drop, open a connection **to the tenant database**, assert the stamp, and read `current_database()` on that same connection. The `ALTER DATABASE … RENAME` or `DROP DATABASE` that follows names the database by **the value that connection returned**, never by the catalog's `database_name`. Then a stale or wrong catalog row cannot select the victim, and what remains is only the window between the assertion and the statement.

That window is covered by ADR-0007 §11.4's own design rather than by a mechanism: the rename is reversible for 30 days, and both operations write a deletion certificate to `catalog.operator_audit_event`. **It is detection, not prevention** — the same boundary ADR-0028 §2 draws — and this record does not close it.

**This needs ADR-0007 §11.4 amended too**, to state that the rename and the drop name the database by `current_database()` from a stamped connection. That edit is not made here: §11.4 is offboarding's section and the tenancy-scope questions in that area are being decided on another branch. It is handed back as a row so the two records cannot drift apart silently.

## 5. Where each rule in this record stops

| Rule | Last link it follows | What is on the other side, unchecked |
|---|---|---|
| §2.3 the endpoint triple | The resolver's own output, parsed | **What the catalog was told.** A CNAME, a failover alias, a short name beside its FQDN, `localhost` beside `127.0.0.1` — all resolutive, none recordable |
| §2.3 host normalisation | Syntactic spellings only: case and the root label | **Resolution.** Closing more would mean a test doing DNS lookups, which makes the test a function of the network |
| §2.4 D2 the fleet scan | What the catalog will accept | **Its own ability to fail**, which §3's constraints remove. D1 is the one that keeps it |
| §3 `lower()` losslessness | `DatabaseCluster.IsWellFormedHost`, a C# write-path check | **The column, which is `text`.** And `lower()`'s non-ASCII behaviour, which is a property of the database's collation and is not pinned |
| §4 the destroy path | A stamped connection's `current_database()`, read before the statement | **The window between the assertion and the statement.** Covered by a 30-day reversible rename and an audit certificate — detection, not prevention |
| The hand-off to the stamp (§2, §3) | ADR-0034 §4 | **Nothing, today.** B-06.2 and B-07.1 are not built, so the stamp is asserted on no path |

## 6. Options considered

### 6.1 For the comparand (§2)

| Option | Pros | Cons |
|---|---|---|
| **A. `(host, port, database)` parsed from the resolved string** *(chosen)* | It is the destination and nothing else; it is a projection of the resolver's own output, so it measures the resolver; it survives ADR-0034 §3.2's PgBouncer change, where a second endpoint appears and the triple is still the thing that must be unique | Needs a parse step, and the parse is itself code that can be wrong — mitigated by using `NpgsqlConnectionStringBuilder`, the same type that composed it |
| B. Compare whole composed strings (ADR-0034 §3.3 as written) | Nothing to choose, nothing to maintain | Vacuous. `Application Name` alone makes equality impossible between two tenants, and every future field added to the string has the same effect |
| C. Compare whole strings with `Application Name` excluded | Small edit to the existing criterion | Fixes today's discriminator and not the class. `Password`, `Username`, `Max Pool Size` and `Command Timeout` all differ legitimately between two tenants on the same endpoint, so the comparison is still wrong — and it is wrong in the silent direction |
| D. Compare `(cluster_id, database_name)` from the catalog rows | No resolver needed; fastest | It is the *logical* pair that ADR-0034 §2 exists to reject: variant 3 is two cluster ids on one endpoint, and this option cannot see it. It also never runs the resolver, so a resolver bug is invisible |

### 6.2 For the host spelling (§3)

| Option | Pros | Cons |
|---|---|---|
| **A. A check constraint, as part of the §3.1 invariant** *(chosen)* | One spelling per endpoint, enforced for every principal including one writing raw SQL; matches the existing `ck_tenant_host_lower_case`; makes §3.1's index mean what §3.1 claims | Refuses a mixed-case host an operator may reasonably type, until a normalising write path exists |
| B. Normalise in the write path only | No refusal reaches an operator | The catalog is reachable by `psql` and by the provisioning saga; a rule that lives only in one write path is a convention, and this project's standing rule is that a convention nothing enforces is not a link |
| C. A case-insensitive (functional `lower(host)`) unique index instead of a constraint | Also closes the gap, and permits mixed-case storage | Two spellings of one endpoint remain storable, so every *reader* — the resolver, an operator view, a metric label — now has to know to fold. Storing one spelling is the smaller surface |
| D. `citext` for the column | PostgreSQL solves it | An extension, a non-standard type, and a dependency for one column; the check constraint is free |

---

## 7. Consequences

**Positive**

- The routing criterion can now go red. Until this record it could not, in a document whose entire argument is that a criterion must be able to.
- Splitting the comparator (D1) from the fleet scan (D2) means the demonstration outlives the fix. That pattern is worth copying wherever a constraint is added to stop the very shape a test was written to detect.
- §3 removes the one residual §3.1's index could not see *and could have been made to see* cheaply, leaving the residuals that genuinely cannot be closed at this layer (aliases, CNAMEs, IP-versus-name) as §4's job, which is where ADR-0034 always said they belonged.

**Negative, and owned**

- **B-20's branch changes shape after review.** The criterion it was briefed on is not the criterion in this record; the endpoint comparison it wrote is. The branch is closer to correct than the brief was, which is the good direction, but its review round must re-read the acceptance criterion from here rather than from the brief.
- **ADR-0034 needed amending three hours after it merged, and it was approved first pass.** The lesson is specific and worth carrying: an acceptance criterion phrased over a *serialised* value is suspect by default, because serialisation adds fields the property is not about. Added to the conventions note.
- **§3's constraint may surface a `23514` to an operator** before a normalising write path exists. Accepted; the alternative stores two spellings of one machine.

---

## 8. Revisit when

- **`database_cluster` grows a second endpoint pair** for PgBouncer (ADR-0034 §3.2). The triple becomes two triples; the comparator must then compare the endpoint the *app path* resolves to, and the assertion must run once per path rather than once. Both §2.3 and §3 are re-read then.
- **`database_cluster.host` is allowed to carry a Unix-socket directory or a multi-host list.** Both break §3's lossless-folding argument, and the multi-host case breaks the triple as a projection.
- **A shared-tier option appears** (ADR-0007 §3.5's escape hatch). Then many tenants deliberately share one endpoint, §2.3's criterion is false by design, and ADR-0034 §3 is rewritten rather than patched — as ADR-0034 §9 already says.
- **Any field is added to the composed connection string.** Not because it affects the triple, but as the trigger to re-read §2.1 and confirm nobody has reintroduced a whole-string comparison somewhere else.
