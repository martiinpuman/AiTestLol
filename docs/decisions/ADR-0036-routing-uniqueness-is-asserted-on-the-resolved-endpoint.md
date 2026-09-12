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
> **The host is normalised before comparison, and the normalisation is part of the criterion rather than an implementation detail:** fold case (DNS is case-insensitive), then **strip every trailing `.`** (the DNS root label — `pg.internal.` and `pg.internal` are the same name; `pg.internal..` is not a name the catalog can hold, and stripping all of them merges it anyway). Each normalisation step may only ever *merge* spellings, never split them: the comparator is looking for collisions, so a step that merges can produce a false positive, which is loud, and a step that splits produces a false negative, which is the shape this criterion was written to stop being.
>
> **A value that is not one endpoint is refused, not compared.** A multi-host `Host=` (`pg-1.internal,pg-2.internal`) has no `(host, port, database)` projection — it is two endpoints — so `ResolvedEndpoint.Parse` **throws** on it rather than returning a triple. A comparator that treated it as one opaque host string would report `collisions: 0` across three spellings of one endpoint, which is exactly how it was broken.
>
> The assertion reports **how many tenants were resolved** and **how many pairs were compared**, and fails on zero of either.

Three things about that wording are load-bearing and are spelled out rather than left to be inferred:

1. **"parsed out of the string the resolver produced."** Reading `(host, port, database_name)` from the catalog rows instead would make the assertion a property of the *input* and would pass over a resolver that ignores its input entirely. The whole point is to measure what the resolver emits.
2. **`host` normalised, not compared raw.** This is deliberately *more* aggressive than the database's own uniqueness index, which is byte-exact. The assertion may therefore go red on a fleet the index accepted — that is correct, and §3 removes the gap from the other side.

   **The trailing dot is why this clause exists in this form.** The first draft of this record compared hosts case-insensitively and nothing else, and a reviewer reconstructed ADR-0034 variant 3 straight through it: `pg.internal.` beside `pg.internal` is one server in two spellings. Both store — executed on `postgres:17-alpine`, `check (host = lower(host))` accepts both, two rows inserted — so they are two rows to `ux_database_cluster_host_port`, two rows past §3's lower-case constraint, and two distinct hosts to a case-fold-only comparator, which then reports `collisions: 0`. **Every control in this record and in ADR-0034 §3 passed a live variant 3.**

   **And the division into "syntactic" and "resolutive" had a third member the first two drafts missed.** `pg-1.internal,pg-2.internal` is a **multi-host list**. It is syntactic by this record's own test — you split on a comma, no DNS — so it sits in the half this record claims to close, and it was open: the character deny-list refused only whitespace, control characters and `/ : = ;`, both §3 constraints admitted it, the byte-exact index saw two rows, Npgsql connected to the first host, and the criterion reported `collisions: 0` across three spellings of one endpoint. **Variant 3 reconstructed, for the second review running.** §3 is the answer, and §8 no longer lists it as a future condition — a revisit trigger phrased *"when X becomes allowed"* can never fire if X is allowed.

   **The honest generalisation.** "Two spellings of one endpoint" is an open class: case, a trailing dot, a multi-host list, a CNAME, a short name beside its FQDN, a failover alias, an IP literal beside a name, `localhost` beside `127.0.0.1`. Normalisation and §3's grammar close the members that are *syntactic*; the *resolutive* ones cannot be closed here, because closing them means resolving DNS, which a test must not do and a catalog cannot record. ADR-0034 §4's stamp is what covers the rest — subject to §2.4's note that it is asserted on no path today, and to §4 below, which finds one path where it cannot be asserted at all.
3. **The counts.** `CLAUDE.md`'s self-check #2: a stage that reports success without a count cannot distinguish "all good" from "nothing ran". Zero tenants resolved, or zero pairs compared, is a failure, not a pass.

### 2.4 What must demonstrate it, and which link each demonstration stops at

The comparison and the fleet scan are **two separable pieces**, and separating them is what keeps the demonstration alive after the defect is fixed.

- **D1 — the comparator, on synthetic input, as a unit test.** A pure function from a list of resolved connection strings to a list of colliding pairs. Fed: two strings that differ only in `Application Name`; two that differ only in `Host` case; two that differ only in a **trailing dot on `Host`**; two that differ only in `Password`; two that differ only in `Database` case; and one carrying a **multi-host `Host`**, which must make the parse *throw* rather than yield a triple. The first four must be reported as collisions; the fifth must not; the sixth must raise. **This test can fail, and it stays able to fail forever**, because it never touches the catalog and therefore is not disarmed by the constraints that stop such rows from being stored.
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
>   add constraint ck_database_cluster_host_lower_case    check (host = lower(host)),
>   add constraint ck_database_cluster_host_no_root_label check (host not like '%.'),
>   add constraint ck_database_cluster_host_well_formed   check (host ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$');
> ```
>
> **A grammar, not a deny-list, and that is the decision.** Three characters were refused one review at a time — `/`, then a dot in the wrong place, then `,`, the multi-host separator that reconstructed variant 3 a second time. **A deny-list grows by one character per finding; it is a list of the attacks somebody has already seen.** An allow-list grammar excludes by construction everything it does not name: a multi-host list, a Unix-socket directory, a scheme, a port suffix, a path, a stray or doubled dot, a hyphen at either end of a label, and every upper-case and non-ASCII character. **This is the same correction ADR-0037 §2.1 makes for `ALTER COLUMN … DROP <x>` — invert the default so an unnamed spelling is refused rather than admitted — reached independently on the same branch in the same two rounds, which is why it is stated as a rule and not as a patch.**
>
> The grammar is RFC 1123 host-name labels: lower-case ASCII letters, digits and inner hyphens, 1 to 63 characters per label, joined by single dots, with `varchar(253)` bounding the whole. An IPv4 literal is the digit-only special case.
>
> **The first two constraints are redundant under the third and are kept anyway.** A host that breaks several should be reported by the most specific constraint rather than by whichever the planner reached first, because `ConstraintName` is what a test asserts on and what an operator reads. Redundant constraints on one column are cheap; an unnamed refusal is not.
>
> **The multi-host list was live, not prospective.** Until this grammar, `IsWellFormedHost` refused only whitespace, control characters and `/ : = ;`, so `pg-1.internal,pg-2.internal` was admitted by every control this record and ADR-0034 §3 describe. `FOLLOWUP-059` owns the implementation; `CanonicalHost` with `ck_database_cluster_host_well_formed` is the shape it takes, the entity's grammar and the constraint's proven equal by executing both over one list of cases.
>
> **IPv6 is outside the grammar, and that is a decision rather than an omission.** The column has never held one, and admitting one is not a spelling rule: a single IPv6 address has many textual forms (`::1`, `0:0:0:0:0:0:0:1`, `::0001`) — the "two spellings of one endpoint" class this record has now failed on three times. **IPv6 is admitted only together with a canonical-form rule — RFC 5952 — enforced by the grammar, so one address has one spelling in the catalog.** Until a deployment needs it, it stays out.
>
> §3.1's index is byte-exact, so without this a host written `DB1.example.com` and a host written `db1.example.com` are two rows, two endpoints to the index, and one server. This is part of the §3.1 invariant rather than a separate tidiness rule: the index's job is to make `cluster_id → (host, port)` injective **on the endpoint**, and it can only do that if one endpoint has one spelling. `ck_tenant_host_lower_case` already does the same job for `catalog.tenant_host`, so this is the existing rule applied to the column §3.1 depends on, not a new kind of rule.
>
> **What `lower()` actually does here, and what the losslessness claim rests on — both smaller than the first draft of this record said.**
>
> - **`lower()` is not lossless on arbitrary text, and the column is `text`.** Folding is lossless for a DNS name (case-insensitive by definition) and for an IPv4 literal (no letters). Nothing *in the database* restricts `host` to those: the restriction is `DatabaseCluster.IsWellFormedHost`, a C# check in the write path. **This record's own §6.2 refuses write-path-only rules as evidence for the constraint, and the losslessness argument then leans on exactly such a rule.** Stated rather than hidden: the constraint is sound, and the argument that it loses nothing stops at a C# method.
> - **The IPv6 sentence in the first draft was irrelevant.** No IPv6 literal can be stored in this column — the deny-list refused `:`, and §3's grammar admits no `:` either. Reasoning about hexadecimal group case described a value the system does not accept. §3 now states the condition on which IPv6 could be admitted: a canonical-form rule (RFC 5952) in the grammar, so one address has one spelling.
> - **Non-ASCII folding is collation-dependent, and the deployment does not pin the collation.** Executed on `postgres:17-alpine`, 2026-09-12: `lower('Ü' collate "C")` returns `'Ü'` unchanged, while `lower('Ü')` under the database's `en_US.utf8` default returns `'ü'`. **So `host = lower(host)` does not mean the same thing on two catalog databases created with different `lc_ctype`.** A reviewer could not reproduce an earlier "folds ASCII only" witness, and this is why: neither "ASCII only" nor "full Unicode" is true of the constraint — the behaviour is a property of the database it was created in. Recorded because it is the sort of fact that is rediscovered as a bug.
>
>   **§3's grammar removes the dependence for this column**, admitting no upper-case and no non-ASCII character at all, so `lower()` never meets one here whatever the collation. Worth naming, because it means the lower-case constraint is now redundant twice over rather than load-bearing.
>
>   **The general fact survives and is not discharged by that.** `ck_tenant_host_lower_case` governs a different column under the same collation-dependent `lower()`, and any future case-folding constraint inherits the problem. **The catalog's collation is an input to a constraint's meaning and must be pinned at `createdb` time.** This is deferred to **a backlog row of its own, not to a role**: at the time of writing `docs/BACKLOG.md` contains **zero** occurrences of "collation" or "lc_ctype", so the earlier hand-off to B-07.1 deferred it to a row that does not carry it. Until `docs/BACKLOG.md` holds a row saying *pin the catalog database's `LC_CTYPE` and `LC_COLLATE` at creation and assert them*, **nothing carries this**, in the present tense.
> - It does **not** generalise to a path — a Unix-socket directory is case-sensitive, and if `database_cluster.host` is ever allowed to carry one, this constraint is wrong and §9 brings whoever changes it back here.

**What demonstrates it:** integration tests in the shape `CatalogConstraintTests` already uses for eleven other constraints, one per case, each asserting `SqlState == "23514"` **and** `ConstraintName` — `DB1.example.com` → `ck_database_cluster_host_lower_case`; `pg.internal.` → `ck_database_cluster_host_no_root_label`; `pg-1.internal,pg-2.internal`, `/var/run/postgresql`, `host:5432`, `-pg.internal` and `pg..internal` → `ck_database_cluster_host_well_formed`. With three constraints on one column, asserting the name stops being a nicety.

**And the grammar is asserted equal in both places it lives.** The C# predicate and the SQL in the constraint are two implementations of one rule, so one test runs **both** over a single list of cases and requires identical verdicts. Two copies of a grammar that nothing compares is the drift this project has paid for elsewhere; the comparison is what makes "the entity and the constraint agree" a measured claim rather than a stated one.

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
>
> **That value is a runtime string composed into a DDL identifier, so [ADR-0038](ADR-0038-contract-carried-text-names-its-sink.md) §2.3 governs it and there is no exception here.** `DROP DATABASE` cannot be parameterised, so the composer must take the value, **validate** it against `PostgresIdentifier.IsWellFormed` and **refuse** rather than sanitise, **quote** it, and be the **one** place in the solution that composes a database name into DDL. The first draft of this section mandated a query result flowing into DDL with no quoting rule at all — in the same pull request whose ADR-0038 §2.3 answers *"may a composer interpolate?"* with **"No. Ever."** The rule is not weaker because the value came from PostgreSQL rather than from a user: `current_database()` returns whatever the database is called, and what it is called was set by whoever created it.

**What covers that window is weaker than the first draft of this section said, and the difference is the point.**

- **The 30-day reversible rename covers the rename. It does not cover the `DROP`.** They are different operations at opposite ends of ADR-0007 §11.4's state machine, and offering the reversibility of the first as the mitigation for the second points a compensating control at the wrong act.
- **The drop's only compensating control is backup retention** — ADR-0007 §11.4's *"backups expire on their own schedule within 35 days"*. It is a recovery path measured in hours-to-days for a whole database, not a routing control, and §4's first draft did not mention it at all.
- **The deletion certificate is written *after* the irreversible act and only on the path that succeeded.** So the case it is offered for — a drop that hit the wrong database — is exactly the case that leaves no record. **The certificate must be written in two parts: an *intent* record naming the asserted tenant, the stamped identity and the exact database name, committed to `catalog.operator_audit_event` before the statement runs; and a *completion* record after.** An intent with no completion is the signal that something irreversible was attempted and did not report back, and on the failure path it is the only signal there is.

**It is detection, not prevention** — the same boundary ADR-0028 §2 draws — and this record does not close it. What it does is stop the detection being claimed where it does not reach.

**This needs ADR-0007 §11.4 amended too**, to state that the rename and the drop name the database by `current_database()` from a stamped connection. That edit is not made here: §11.4 is offboarding's section and the tenancy-scope questions in that area are being decided on another branch. It is handed back as a row so the two records cannot drift apart silently.

## 5. Where each rule in this record stops

| Rule | Last link it follows | What is on the other side, unchecked |
|---|---|---|
| §2.3 the endpoint triple | The resolver's own output, parsed | **What the catalog was told.** A CNAME, a failover alias, a short name beside its FQDN, `localhost` beside `127.0.0.1` — all resolutive, none recordable |
| §2.3 host normalisation | Syntactic spellings only: case and the root label | **Resolution.** Closing more would mean a test doing DNS lookups, which makes the test a function of the network |
| §2.4 D2 the fleet scan | What the catalog will accept | **Its own ability to fail**, which §3's constraints remove. D1 is the one that keeps it |
| §3 the host grammar | `ck_database_cluster_host_well_formed`, evaluated by PostgreSQL for every writer including raw SQL, with the C# copy asserted equal case-by-case | **What the grammar does not name is refused**, so this link does not depend on anyone having thought of an attack. What it does stop at: the grammar is a decision about which hosts are expressible, and widening it re-opens the class |
| §3 `lower()`'s meaning | The database's collation | **Not pinned anywhere.** Neutralised for this column by the grammar; live for `ck_tenant_host_lower_case`, and carried by no backlog row today |
| §4 the destroy path | A stamped connection's `current_database()`, validated, quoted and composed in one place (ADR-0038 §2.3) | **The window between the assertion and the statement.** For the rename, a 30-day reversal; for the **drop**, only 35-day backup retention. The certificate covers it only if written intent-before and completion-after — otherwise the failure path leaves no record |
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
| E. A deny-list of characters in the constraint (what existed before §3) | Nothing to design; each finding costs one character | **Refuted by execution, twice.** `/`, then a misplaced dot, then `,` — each admitted until a reviewer found it, and the last reconstructed a tenant takeover. A deny-list enumerates the attacks already seen; the grammar refuses by construction what it does not name |
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
- **ADR-0034 needed amending three hours after it merged, and it was approved first pass.** The lesson: an acceptance criterion phrased over a *serialised* value is suspect by default, because serialisation adds fields the property is not about.
- **Variant 3 has now been reconstructed through this record twice** — a trailing dot, then a multi-host list — and both times the defect was an **allow-by-default** surface: a deny-list of characters, and a comparator that normalised only what someone had thought of. ADR-0037 §2.1 reached the same correction independently, on the same branch, in the same two rounds. **Where a rule decides what is admitted, the unnamed case must be refused, not admitted.** Added to the conventions note.
- **§3's constraints may surface a `23514` to an operator** before a normalising write path exists. Accepted; the alternative stores two spellings of one machine.
- **The grammar forbids hosts some deployments will want** — an IPv6 literal, an underscore in a label, a Unix socket. Each becomes a deliberate decision with a canonical-form rule attached. That is the cost of refusing by construction, and it is the right cost: the alternative was found admitting a takeover vector in two consecutive reviews.

---

## 8. Revisit when

- **`database_cluster` grows a second endpoint pair** for PgBouncer (ADR-0034 §3.2). The triple becomes two triples; the comparator must then compare the endpoint the *app path* resolves to, and the assertion must run once per path rather than once. Both §2.3 and §3 are re-read then.
- **§3's grammar is widened for any reason** — an IPv6 literal, a Unix-socket directory, an underscore in a label, a longer label. Each re-opens a member of the "two spellings of one endpoint" class. The first two drafts listed a multi-host list here *as a future condition while it was already admitted*: **a revisit trigger phrased "when X becomes allowed" is worthless if X is allowed, so check that before writing one.**
- **A shared-tier option appears** (ADR-0007 §3.5's escape hatch). Then many tenants deliberately share one endpoint, §2.3's criterion is false by design, and ADR-0034 §3 is rewritten rather than patched — as ADR-0034 §9 already says.
- **Any field is added to the composed connection string.** Not because it affects the triple, but as the trigger to re-read §2.1 and confirm nobody has reintroduced a whole-string comparison somewhere else.
