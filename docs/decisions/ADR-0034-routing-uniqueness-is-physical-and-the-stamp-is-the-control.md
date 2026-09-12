# ADR-0034 — Tenant routing: uniqueness is defined over the physical endpoint, `TenantIdentityStamp` is the control, and the resolver's types stay internal

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** **ADR-0007 §3.5's placement sentence** — *"`TenantScope` is a sealed class in `Aurora.Platform.Tenancy.Contracts`"* is unaffected, but §3.5's own code block, which declares `ITenantConnectionResolver` and `TenantConnection` in that same contracts assembly, is superseded by §6 below: both are `internal` to `Aurora.Platform.Tenancy`. Every other statement in §3.5 — that it is the *only* place that knows how a tenant maps to physical storage, the 60-second cache, the stage-1/stage-2 credential plan — stands unchanged and is in fact strengthened.
- **Amends:**
  - **ADR-0007 §9.2** — the catalog tables gain one uniqueness constraint (§3). No column is removed and no table is redefined.
  - **ADR-0007 §4** — §4 calls layers 3 and 4 "defence in depth, because a guarantee nobody can observe failing is a guarantee nobody trusts". For the **routing-identity** question that word is wrong and §4.3 is the control (§4 below). For "no `DbContext` without a tenant", §4's ordering is unchanged.
- **Superseded by:** —
- **Related:** ADR-0004 (PostgreSQL), ADR-0007 §3.2, §3.5, §4.3, §8, §9.2, §11.3, ADR-0027 §2 (`ITenantAdminConnectionFactory`, `StampAssertion`), ADR-0033 (what the stamp is *not* a control against), `../architecture/modules.md` §4
- **Raised by:** the security review of PR #14 (`task/B-06.1`), third of three variants of one finding

> The same defect has now been executed three times against three different columns. This ADR decides the axis, not the instance, so that the fourth variant has nowhere to live.

---

## 1. Context: three variants, one shape

| # | What was executed | What the constraint was defined over |
|---|---|---|
| 1 | A second `catalog.tenant` row copying another tenant's `database_name` on the same cluster | Nothing — closed afterwards by `ux_tenant_cluster_id_database_name` |
| 2 | A `catalog.tenant_host` row for a hostname the tenant does not own, marked verified by the party that benefits | `tenant_host.host` as a primary key — a *name*, verified by a claim |
| 3 | Two `catalog.database_cluster` rows pointing at one server, one tenant each, the attacker's `database_name` copied. Both tenants resolve to `127.0.0.1:32905/aurora_t_t_82b6f49207bc` — byte-identical connection strings | `(cluster_id, database_name)` — a *logical* pair |

Variant 3 is the sharpest illustration: the single-cluster shape is correctly refused with SQLSTATE `23505`, and the second cluster row walks straight around the index. `database_cluster` has a primary key on `id` and an alternate key on `(id, region)`. **Nothing makes `(host, port)` unique.**

Each time, a constraint was written over the identifier the catalog *organises by* while the thing that must be unique is the resource the system *reaches*. Fixing the third instance and leaving the axis undecided buys one round.

---

## 2. The invariant, stated once

> **No two tenants may resolve to the same physical database.**
>
> **And the catalog may never be asked to prove which physical database is which.** A catalog row holds a name. Two names can denote one machine — a CNAME, a second DNS record, a failover alias, a proxy endpoint, an IP literal beside a hostname. A constraint over a name narrows the claims that can be stored; it cannot establish identity. Identity comes from inside the data.

Two mechanisms follow from those two sentences, and the division of labour between them is the decision this ADR exists to make:

- §3 is a **narrowing**: it removes the shapes the catalog *can* see are wrong.
- §4 is the **control**: it is what actually decides, and it is immune to how the database was addressed.

Anyone who reads §3 as the fix has read half of this ADR.

---

## 3. Decision 1 — the catalog constrains the resolved endpoint, not the logical one

### 3.1 One unique index, and why it is enough to close variants 1 and 3

Add, to `catalog.database_cluster`:

```sql
create unique index ux_database_cluster_host_port on catalog.database_cluster (host, port);
```

With it, the composition is a proof rather than a hope:

1. `ux_database_cluster_host_port` makes `cluster_id → (host, port)` **injective**.
2. `fk_tenant_cluster_in_region` makes it **total** for every non-tombstoned tenant (`ck_tenant_routing_present_unless_deleted` already requires `cluster_id` to be non-null unless the row is a `Deleted` tombstone).
3. `ux_tenant_cluster_id_database_name` makes `(cluster_id, database_name)` unique.

Therefore `(host, port, database_name)` — the triple `ITenantConnectionResolver` composes into `Host=…;Port=…;Database=…` — is unique across `catalog.tenant`. Variant 3's second cluster row is refused by (1); variant 1's copied database name is refused by (3).

**Why an index on the cluster rather than the endpoint denormalised onto the tenant.** Copying `host` and `port` onto `catalog.tenant` and indexing the triple there would work too, and it is worse: it is a second copy of an operational fact, it needs a composite foreign key with a cascade to keep the copies honest, and re-addressing a cluster becomes a fleet-wide update rather than one row. One index on the table that owns the endpoint gives the same property with none of that.

### 3.2 What this forbids, enumerated rather than discovered later

- **Two logical clusters on one `host:port` — forbidden, and that is correct.** A `database_cluster` row is an endpoint plus a region, three secret references, a cap and a state. Role *names* are global (ADR-0004 rule 2), so a second row on one endpoint is one of: a residency lie (two regions, one machine), a duplicate, or a test convenience. None is a shape to keep.
- **Test fixtures.** A fixture that wants two clusters uses two containers — Testcontainers maps a distinct host port per container — or one cluster row with two tenants. **A fixture that needs two cluster rows on one endpoint is constructing variant 3**, and after this index it will fail to insert, which is the point.
- **Read replicas — not affected, because a replica is not a row.** ADR-0007 §3.5 places replica selection *inside* the resolver: "introducing a read replica … is a change inside this one implementation". Nothing today creates a `database_cluster` row for a replica. If that ever changes, this index is wrong as written and §9 brings whoever changes it back here first.
- **PgBouncer — a second endpoint on one row, never a second row.** ADR-0007 §5/§6 put PgBouncer in front of the app path at roughly 1 000 tenants, while §7.3 has the migrator connect directly. When that arrives, `database_cluster` grows an app endpoint beside the direct one, and **each endpoint pair gets its own unique index**. Written here, now, so that the shape change does not quietly re-open the hole it is about to widen. This is a rule for that future change, not a mechanism that exists.
- **Two DNS names for one server, a CNAME, a failover alias, an IP literal beside a hostname — not covered, and not coverable.** The catalog stores what it was told. This is precisely why §4 exists.

### 3.3 The assertion is the property, not the index

An acceptance criterion that says "the index exists" is a check on a name. The criterion is:

> **No two non-deleted `catalog.tenant` rows produce the same resolved connection string**, computed by running the real `ITenantConnectionResolver` over the real catalog rows and comparing the composed strings — reporting how many tenants were resolved and how many pairs were compared, and failing on zero of either.

That assertion would have caught all three variants, it does not depend on which index happens to be present, and it survives the PgBouncer change in §3.2. It is also the one to write **first**, against a catalog seeded with variant 3's two-cluster shape, and watched to fail before the index is added.

**A near miss worth naming:** a tenant `database_name` equal to some cluster's `maintenance_database` on the same endpoint is not caught by any of the above — `maintenance_database` is not in the tenant uniqueness axis. What covers it is the `aurora_t_` naming convention plus B-07.1's safe-adoption rule (adopt only a database owned by `aurora_migrator` that is either empty or already stamped for this tenant). Recorded so nobody later mistakes §3.1 for complete coverage of "which database could a tenant end up on".

---

## 4. Decision 2 — `TenantIdentityStamp` is the control, and what that obliges

ADR-0007 §4.3 already gives the reason, in its own words: the identity *"travels inside the data, not in its name"*, which is why it is stronger than comparing `current_database()`. Everything in §2's second paragraph is the same argument applied to the catalog. So:

> **`TenantIdentityStamp` is the control for routing identity.** §3's index is a narrowing that removes storable mistakes; the stamp is what decides, on every physical connection, whether the database reached is the database intended. ADR-0007 §4's phrase "defence in depth" is amended for this question: for routing identity, layer 3 *is* the depth, and layers 1–2 do not address routing at all.

Two limits belong beside that sentence so it is not over-read:

- The stamp is a **routing** check, not an **authorization** check. Against a caller that chose the tenant it names, the stamp agrees. ADR-0033 §2 is the record of that, and it is not a gap in this decision — it is a different threat with a different control.
- The stamp is only in effect where something calls it. That is the whole of §5.

## 5. Decision 3 — a row that writes routing rows may not merge before the check is in effect on that path

Because §4 makes the stamp the control rather than a backstop, its absence is not a missing nicety, it is the control being off. The reviewer's grep is the evidence: `TenantIdentityStamp` exists as a type on `task/B-06.1a` and is wired nowhere. **Today the control is in effect on no path.**

The general rule, so it does not have to be re-derived per row:

> **A row that writes tenant routing rows must not merge before the stamp assertion is in effect on the path it writes for.** Two paths, two wirings, and neither covers the other.

| Path | Where the stamp is asserted | Owned by |
|---|---|---|
| **DDL** — provisioning, migration, package install | `ITenantAdminConnectionFactory.OpenAsMigratorAsync(..., StampAssertion, ct)` calling `TenantIdentityStamp.AssertAsync` (ADR-0027 §2). ADR-0007 §4.3's per-physical-connection initializer belongs to the *app* data source and does **not** protect this path | **B-07.1** builds the factory; **B-06.1a** ships the stamp |
| **App** — every request, job, outbox dispatch | The `NpgsqlDataSourceBuilder` physical-connection initializer of ADR-0007 §4.3 | **B-06.2** |

Three orderings follow, and they are architectural, not scheduling preferences:

1. **B-06.1a before B-07.1.** B-07.1's compensation guard and its step-2 adoption rule both re-read the stamp; without the type there is nothing to call. (Already on B-07.1's row. This ADR makes it non-negotiable.)
2. **B-06.2 before B-06.3.** B-06.3 is the first row that opens a `TenantScope` and therefore the first that can serve a request. A scope opened over a data source with no identity initializer is a request with the control off. (Already a dependency. Same status.)
3. **The §3.1 migration before B-07.1.** B-07.1 is the row that writes `database_cluster` rows, so it is the row that would create variant 3's second cluster.

`AllowUnstampedDuringProvisioning` (ADR-0027 §2) is unchanged and is not an exception to any of this: it covers the window in which a database genuinely cannot prove anything yet, it is legal in three named call sites, and ADR-0027 already records it as a hole bounded by a fitness test.

### 5.1 Where the migration lands in the catalog chain

`docs/BACKLOG.md` B-18.1 establishes that the `CatalogDbContext` migration chain has one order and that no third row may add a migration while B-18.1 or B-18.9 is open; B-19 carries its own and must therefore run before B-18.1 starts or after B-18.9 merges.

**§3.1's index lands as its own additive, expand-only `CatalogDbContext` migration, immediately after B-19 merges and before B-18.1 is dispatched.** Reasoning: B-07.1 is held on this decision and B-07.1 gates the entire provisioning and migration spine; B-18.1 has not started, so the slot is free; placing it after B-18.9 would hold the spine behind the whole identity chain for no benefit. It is one `CREATE UNIQUE INDEX` against a table B-05 already created, so it does not interact with anything B-19, B-18.1 or B-18.9 touch.

**Expand/contract honesty.** Creating a unique index fails if existing rows violate it. That failure is the correct behaviour and the row must prove it happens: a test seeds two cluster rows on one endpoint, runs the migration, and asserts it fails loudly with the duplicate named — rather than the far worse outcome of a migration that silently succeeds because the duplicate was created concurrently. No production catalog exists yet, so no de-duplication procedure is required; if that changes before the row lands, one is.

---

## 6. Decision 4 — `ITenantConnectionResolver` and `TenantConnection` are internal to `Aurora.Platform.Tenancy`

ADR-0007 §3.5 declares both in `Aurora.Platform.Tenancy.Contracts`. `task/B-06.1` shipped them `internal` in `Aurora.Platform.Tenancy` instead, on the grounds that the task partition forbade Contracts writes and every named consumer lives in that assembly. **The ADR moves; the code stays.**

The reason is not the task partition — that is an accident of scheduling and would be a poor basis for a decision that outlives it. The reason is what the type carries:

> `TenantConnection.ConnectionString` is *"fully formed, including credentials from the secret store"* — ADR-0007 §3.5's own words. A type in a `.Contracts` assembly is **nameable by every assembly that references it**: every module's `.Application` and `.Infrastructure`, `Aurora.Web`, every job (fitness rule L2/L3 permits exactly `Aurora.Platform.*.Contracts` from all of them). `internal` in the implementation assembly means the credential-carrying type cannot be *named* outside the one assembly that composes it.

That is a stronger form of §3.5's own stated goal — *"Nothing else in the system learns what a connection string looks like"* — than the placement §3.5 chose. §3.5 wrote the goal and then put the type where the goal could only be upheld by a fitness rule; `internal` upholds it by construction. Every other sentence of §3.5 is unaffected, including the one that matters most: this is still the single seam through which a cluster move, a read replica or a future shared-tier connection is one implementation change and zero callers.

**Placement follows who must hold the value.** `TenantScope` is in `.Contracts` because every application-service method takes one as a parameter (§4.5). `TenantConnection` is the opposite: nothing outside tenancy should ever hold one. The two placements are not inconsistent; they are the same rule applied to opposite answers.

### 6.1 The first outside consumer: `[InternalsVisibleTo]`, not promotion

`Aurora.TestKit`'s instrumented counting resolver (`solution-layout.md` §6.4 item 6 — it ships from B-18.5 and B-10 consumes it) is the first consumer outside `Aurora.Platform.Tenancy`, and it is the thing that will force this question. It gets a grant, under three conditions:

1. **The grant list on `Aurora.Platform.Tenancy` is asserted as an exact set**, in the same shape `task/B-06.1a` already uses on the contracts assembly, so a fourth name is a red test and not a quiet widening.
2. **The grantee is test-only**: no project under `src/` references it. Fitness rules L1–L5 read declared `ProjectReference`s of `src/` projects and classify an unrecognised target as a violation, so this holds today — but the row that adds the grant must assert it explicitly rather than rely on a rule that was written for a different purpose.
3. **A *production* consumer outside `Aurora.Platform.Tenancy` is the trigger to revisit**, not a reason to grant. There is none today. The answer then is a narrower published abstraction that does not carry a connection string — not promotion of `TenantConnection`.

A test double is a legitimate reason to see internals. It is not a legitimate reason to publish a credential type to every module in the solution.

---

## 7. Options considered

**For the uniqueness axis (§3):**

| Option | Pros | Cons |
|---|---|---|
| **A. Unique index on `database_cluster (host, port)`, composed with the existing tenant index** *(chosen)* | One declarative index on the table that owns the endpoint; the composition in §3.1 is a proof, not an inspection; no second copy of an operational fact; re-addressing a cluster stays one row | Forbids two cluster rows on one endpoint, which some test fixture will want (§3.2) |
| B. Denormalise `host`/`port` onto `catalog.tenant` and index the triple there | The constraint sits on exactly the tuple that must be unique, with no composition argument to get wrong | A second copy of the endpoint, kept honest by a composite foreign key with a cascade; re-addressing a cluster becomes a fleet-wide `UPDATE`; two places to read when debugging a routing question |
| C. A trigger or an exclusion constraint spanning both tables | Expresses the cross-table rule directly | This project has had eight executed bypasses of trigger-shaped mechanisms; a trigger's presence, its parent, its body and its `WHEN` clause are each a link something must read. A unique index has no body to read |
| D. Nothing in the catalog; rely on the stamp alone | Honest about where identity comes from; one mechanism instead of two | Leaves storable, detectable mistakes storable. The stamp fails the request *at connection time*, in production, on a tenant that was provisioned wrong hours earlier — a catalog `23505` at write time is the same defect caught where it is cheap. §4 is the control precisely *because* it catches what §3 cannot; that is not an argument for deleting §3 |

**For the resolver's placement (§6):**

| Option | Pros | Cons |
|---|---|---|
| **A. `internal` to `Aurora.Platform.Tenancy`; `[InternalsVisibleTo]` for the test double** *(chosen)* | The credential-carrying type is not nameable outside the assembly that composes it — a guarantee rather than a rule; matches what `task/B-06.1` already shipped | A friend list, which grows; a test-support assembly sees internals |
| B. Promote both to `.Contracts` as ADR-0007 §3.5 wrote it | No friend list; the interface is where a reader of §3.5 looks for it | Publishes a type whose property is a live credential to every module, host and job; §3.5's own goal would then need a fitness rule to hold, and a rule keyed on a type name is defeated by a wrapper |
| C. Promote `ITenantConnectionResolver` only, keep `TenantConnection` internal | Interface visible, credential not | Not expressible: the interface returns the record |
| D. Publish a narrower abstraction that hands back an open `DbConnection` rather than a string | Callers never see a credential at all | Nobody needs it today; it would be designed against no requirement. Recorded as the answer *if* §6.1 condition 3 ever fires |

---

## 8. Consequences

**Positive**

- The axis is decided rather than the instance. §2's two sentences answer the fourth variant before it is written, and §3.3's assertion tests the property rather than the index.
- Variant 3 is closed by one expand-only index with a proof (§3.1) rather than by inspection.
- The stamp stops being "defence in depth" and becomes the named control, which changes what it is acceptable to merge without it (§5) — a consequence that was previously invisible because the stamp sounded optional.
- The credential type is unnameable outside one assembly, which is a guarantee rather than a rule, and it costs nothing because nothing outside that assembly wants it.

**Negative, and owned**

- **§3.1 forbids two cluster rows on one endpoint, and some test fixture will want exactly that.** It is stated in §3.2 so that the fixture is rewritten rather than the index dropped.
- **§3 does not close the aliasing case and never will.** Everything therefore rests on §4, which rests on §5's orderings holding. An ordering is a weaker mechanism than a constraint, and §5's general rule is prose until a row carries it.
- **Two paths must each wire the stamp** (§5's table), and neither covers the other. ADR-0027 already called this out as a negative; §5 raises its severity from "each DDL task proves it" to "no such row merges without it".
- **§6 means the tenancy implementation assembly grows a friend list.** Three conditions bound it; a friend list is still a list, and lists grow.
- **§5.1 inserts a row into a migration chain that already has a stated single order**, which is exactly the coordination cost B-18.1's rule exists to make visible. It is taken deliberately, and the slot is the one that is free.

---

## 9. Revisit when

- A `database_cluster` row is created for something that is not a primary writable endpoint — a read replica, a pooler, a failover alias. §3.2 says why that breaks §3.1, and this ADR must be re-read before the row is added.
- PgBouncer arrives (ADR-0007 §6's ~1 000-tenant threshold), which is when `database_cluster` grows a second endpoint and §3.2's rule about one index per endpoint pair must actually be applied.
- A shared-tier option appears (ADR-0007 §3.5's escape hatch: one connection string plus an RLS session variable). Then many tenants deliberately share a physical database, §3.1's composition no longer holds, and the whole of §3 must be rewritten — not patched.
- A production consumer outside `Aurora.Platform.Tenancy` needs to resolve a tenant connection (§6.1 condition 3).
- Tenant host registration gains real ownership proof — variant 2's control, which is a domain-verification design and is deliberately **not** decided here. §2's second paragraph is the pattern it must satisfy: `verified_at` set by the party that benefits is a claim, and a claim is not a proof.
