# ADR-0035 — Two tenancy kernel types: `InstalledPackages` defined, `TenantDatabaseHandle` confirmed, relocated and given an owner

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** **ADR-0027 §1's placement of `TenantDatabaseHandle`** (*"In `Aurora.Platform.Tenancy.Contracts`: … `sealed class TenantDatabaseHandle : TenantAccess`"*) and **ADR-0027 §1's signature `ITenantMigrationContextFactory<TContext>.CreateAsync(TenantDatabaseHandle, ct)`**. Both are corrected in §3 below. **The decision ADR-0027 made — two proof types off one `TenantAccess` base, so the §7.5 gate has no bypass branch — is unchanged and is reaffirmed.** Every other clause of ADR-0027 stands, including §2's `ITenantAdminConnectionFactory`, §3's single migration executor, §4's ownership of the skew check and §5's upgrade planner.
- **Amends:**
  - **ADR-0008 §4** — which describes what a package contributes but never defines the set a `TenantScope` carries. §2 supplies the definition.
  - **ADR-0007 §3.4** — which names `InstalledPackages` in the `TenantScope` sketch and leaves it undefined. Same.
- **Superseded by:** —
- **Related:** ADR-0007 §3.4, §9.2, ADR-0008 §4.1, §5.1, §5.3, §6.1, ADR-0027 §1–§3, ADR-0032 §4.1.3 (the `SalesSchemaMigrator` worked example) and §5 (the rule summary), ADR-0033 (what a package can do once loaded), `../architecture/modules.md` §4
- **Raised by:** two reviewers of PR #13 (`task/B-06.1a`), and the project-manager's flag on B-06.1a's backlog row

> Both types were named by an ADR and defined by nobody. A developer had to invent one and skip the other, and flagged both rather than guessing quietly. This record is the answer to each.

---

## 1. Context

`task/B-06.1a` built the tenancy kernel types. Two questions came back that the implementation could not settle on its own:

1. **`InstalledPackages` had no definition anywhere.** ADR-0007 §3.4 lists it as a property of `TenantScope` with the note "(ADR-0008 §4)", and ADR-0008 §4 is about how a package contributes *data to a tenant database* — it defines package-owned schemas, side tables and three-way merge, and says nothing about the set a scope carries. The branch shipped the smallest immutable set it could justify. Two reviewers asked for a ruling.
2. **`TenantDatabaseHandle` has no owning backlog row.** ADR-0027 §1 defines it; nothing builds it. B-04's fitness rule T6 governs a type that does not exist, `RuleInventoryTests` pins its absence, and B-06.1a's inertness guard expires the day somebody builds it.

Answering (2) surfaced two contradictions in ADR-0027 §1 that would have been discovered mid-implementation, in the same way ADR-0027 itself was written to resolve contradictions discovered by reading ADR-0007 literally. They are in §3.

---

## 2. Decision 1 — `InstalledPackages`: confirmed as built, with one correction

### 2.1 Confirmed

`task/B-06.1a`'s shape **is** the definition, and ADR-0008 §4 and ADR-0007 §3.4 now point here for it:

- **Immutable, and a copy taken at scope open.** A scope is handed to every application-service call for a unit of work (ADR-0007 §4.5); the package set it carries must be the set read when the scope opened, not a live view an installer running in another request can change underneath it.
- **One entry per package id, or the whole set is refused.** `catalog.installed_package` has primary key `(tenant_id, package_id)`, so two entries for one id can only mean a reader that joined wrongly. Picking either would hide that.
- **`None` is the only empty value**, and `TenantScope` refuses a null set, so "this tenant has no packages" has exactly one representation.
- **Ordered by package id, ordinally**, so a lookup is exact and the order is stable rather than incidentally whatever the query returned.
- **`InstalledPackageEntry` is (`PackageId`, `Version`, `State`)** with `InstalledPackageState` — the enum B-05 already declared alongside the catalog row, whose five values (`Installing`, `Active`, `Deactivated`, `UpgradePending`, `Failed`) are the ones `ck_installed_package_state` constrains.

### 2.2 Text, not the Country Package contract types — confirmed, and the reason is a rule rather than an accident

The id and version are carried as the catalog's text. This is correct and is not a placeholder:

- `Aurora.Platform.Tenancy.Contracts` may reference `Aurora.SharedKernel` and `Aurora.Documents.Canonical` and nothing else (fitness rule L2, `solution-layout.md` §2). `PackageId` and `PackageVersion` live in `Aurora.Countries.Contracts`.
- And it **must not**, independently of the rule. Tenancy contracts are tier-1 for every module. The Country Package contract is a versioned public API on its own SemVer clock (ADR-0008 §3.1). Binding the shape of a `TenantScope` to it would make a package-contract MAJOR bump a tenancy change, and every module would recompile for a jurisdiction release.
- The catalog column is `text`. Text is what was read; text is what is carried.

**Where the spelling is enforced, link by link** — because "the catalog row's to enforce" is a claim that needs its mechanism named:

`InstalledPackageEntry` refuses only null, blank and whitespace → the sole writer is `catalog.installed_package` → `InstalledPackage.Begin` validates `^[a-z][a-z0-9_]*$` in the domain → `ck_installed_package_id_well_formed` enforces the same pattern in the database, for every principal including one writing raw SQL → therefore any entry read out of the catalog has the spelling ADR-0008 §4.1 R1 needs for a `pkg_<id>` schema name. **The link the entry type stops at is deliberate and the next link is a database check constraint, not a convention.** Anyone who later composes SQL from `PackageId` should re-read this paragraph and confirm the chain is still intact rather than assume it.

### 2.3 The one correction: a scope must not make "installed" and "usable" the same question

`TryGet` and `Entries` return a package in any state. A capability resolver that asks "does this tenant have package `nz`?", gets a `Deactivated`, `Failed`, `Installing` or `UpgradePending` entry and uses it has broken ADR-0008 §5.3 — deactivation would switch nothing off — and ADR-0008 §6.1, where a package fills a core slot. Relying on every future consumer to remember the filter is exactly the convention `CLAUDE.md` says not to rely on.

**`InstalledPackages` gains a second view:**

- `Entries` and `TryGet` keep their current meaning: **everything the catalog holds**, for install, upgrade, operator views and support.
- `Active` and `TryGetActive` expose only `State == Active`: **what the tenant may actually use.**
- The rule, stated once here: **capability and slot resolution reads `Active`. Everything else reads `Entries`.**

*What `task/B-06.1a` must change:* add those two members and their tests (a set containing one entry in each of the five states, asserting `Active` holds exactly the one), and the sentence above in the type's own documentation. Nothing already shipped is removed or renamed.

*The demonstration that does not exist yet, recorded as open:* no capability resolver exists, so nothing today can read the wrong view. The first row that resolves a package capability (B-13's installer path, and the first reference package) must carry the test that a `Deactivated` package resolves no capability. **Until then §2.3's rule is a specification and nothing enforces it** — stated in the present tense on purpose.

---

## 3. Decision 2 — `TenantDatabaseHandle` is still in the design, moves out of `.Contracts`, and belongs to B-07.1

### 3.1 It stays

ADR-0027's option A was chosen over option B because a single proof type forced the §7.5 schema-version gate to grow a bypass branch named `Migration` — *"exactly what an attacker or a careless developer would choose"* — and forced a scope to carry a `SchemaVersion` that the run was about to invalidate. Nothing about that has changed. Removing `TenantDatabaseHandle` re-opens option B by default.

**Verdict: the type is part of the design. The two parked rules are finished, not deleted.**

### 3.2 It lives in `Aurora.Platform.Tenancy`, not `Aurora.Platform.Tenancy.Contracts`

ADR-0027 §1 puts it in the contracts assembly and gives it *"the open `NpgsqlConnection`"*. Those two sentences cannot both be true: a `*.Contracts` project references `Aurora.SharedKernel` and `Aurora.Documents.Canonical` and nothing else — no EF, no Npgsql, no DI (fitness rule L2, and it is stated in the project file itself). ADR-0027 §1 specified a type that could not compile where it specified it.

The correction is the one that also makes the guarantee stronger:

> **`TenantDatabaseHandle` is declared in `Aurora.Platform.Tenancy`.** It still derives from `TenantAccess`, which stays in `.Contracts` with its `internal` constructor — the existing `[InternalsVisibleTo("Aurora.Platform.Tenancy")]` grant is exactly what lets the derived type chain to it, and that grant is already asserted as an exact set.

Nothing is lost, because nothing outside that assembly was ever allowed to name it: T6's allow-list is `Aurora.Platform.Tenancy` and its dotted descendants, which is the same set. Two things are gained:

1. **Layering enforces it before T6 does.** Modules, hosts and Country Packages may reference `Aurora.Platform.*.Contracts` only (rules L1–L5, live today over declared `ProjectReference`s). A type in `Aurora.Platform.Tenancy` is therefore not merely forbidden to them by a name-matching rule — it is unreachable by project reference. T6 becomes the second line rather than the only one, which is what a rule keyed on a simple name should always be (ADR-0032 §5 made that argument about a different rule).
2. **`.Contracts` keeps its promise.** The assembly every module compiles against acquires no database driver and exposes no `NpgsqlConnection`.

### 3.3 No `.Contracts` type mentions the handle — and what that settles

It follows from §3.2 that **no type in any `.Contracts` assembly may name `TenantDatabaseHandle`.** That resolves a second contradiction ADR-0027 §1 left:

- ADR-0027 §1 gives the DDL-path factory the signature `ITenantMigrationContextFactory<TContext>.CreateAsync(TenantDatabaseHandle, ct)` and says it is *"consumed only by `IModuleSchemaMigrator` implementations"*.
- ADR-0032 §4.1.3's worked example has `SalesSchemaMigrator : IModuleSchemaMigrator` take `ITenantMigrationContextFactory<SalesDbContext>` in its constructor — a module type, in `Aurora.Modules.Sales.Infrastructure`.
- A module can only see `.Contracts`. So either the factory is not module-facing (contradicting ADR-0032 §4.1.3), or its signature does not name the handle (contradicting ADR-0027 §1). **The conflict is resolved in favour of T6 and of ADR-0032 §4.1.3: the signature gives.**

> **`ITenantMigrationContextFactory<TContext>` does not name `TenantDatabaseHandle`.** A module's `IModuleSchemaMigrator` never holds a DDL-capable proof. The handle-to-context binding happens inside `Aurora.Platform.Tenancy`, which is the only assembly that can name both.

**The shape I recommend, and its status.** `ITenantSchemaMigrator` (ADR-0027 §3) opens one DI scope per tenant run, registers the run's handle in it — a registration only `Aurora.Platform.Tenancy` can write, because only it can name the type — and resolves the module migrators from that scope, so `TenantMigrationContextFactory<T>` receives the handle by ordinary constructor injection and `CreateAsync(CancellationToken)` is all a migrator ever calls. That keeps ADR-0032 §4.1.3's example verbatim and adds no third proof type. **It is a recommendation, not a mechanism that exists**; B-07.2 owns `ITenantMigrationContextFactory<T>` and `ITenantSchemaMigrator` and must name the shape it adopts and prove the invariant above, whichever it picks.

### 3.4 Which row owns it: **B-07.1**

`ITenantAdminConnectionFactory`'s two methods are the only things that can produce a handle, and B-07.1 already owns that factory and its `StampAssertion` opt-out (ADR-0027 §2, applied to B-07.1's row). A type that one factory produces and that factory's callers consume belongs with the factory; giving it a row of its own would insert another dependency in front of the provisioning spine for a sealed class with an internal constructor and four properties.

**What B-07.1 must do with it**, beyond declaring it:

1. Declare `TenantDatabaseHandle : TenantAccess` in `Aurora.Platform.Tenancy`, sealed, internal constructor, carrying `ClusterId`, `DatabaseName`, `Role` (`Admin | Migrator`), the open `NpgsqlConnection` and `IdentityVerified` (ADR-0027 §1).
2. **Retire B-06.1a's inertness guard by resolving it, not by deleting it.** `RuleInventoryTests.The_tenant_access_types_T5_bites_on_are_the_two_real_ones_and_T6s_handle_does_not_exist_yet` asserts an exact `(full name, assembly)` set of two. It goes red the moment the handle lands, which is the design. B-07.1 updates that set to three, renames the test to match, and states the new assembly.
3. **Re-point T6's fixtures at the real type.** `Fixtures/Violations/TenancyViolations.cs` carries a stand-in `TenantDatabaseHandle` because the rules key on simple names. With a real type, the deliberately-violating fixtures must exercise the real one, and `tests/Aurora.Architecture.Tests/README.md`'s live/inert table must move T6 out of "live, nothing to find".
4. **Raise T6's floor and say what it measured.** `RuleAssert.Holds(..., minimumSubjects: 10)` is a population floor, not a finding floor; the row must additionally assert that T6 examined at least one type use that *names* the handle, or the rule is still reporting clean on a population that contains no instance of what it looks for.
5. **Prove the confinement with a deliberately-violating assembly that is not on the allow-list**, as ADR-0027 §1 already requires ("a named allow-list, asserted, with a deliberately-violating fixture") — and, per §3.2, additionally assert that no `src/` project outside `Aurora.Platform.Tenancy*` declares a `ProjectReference` to `Aurora.Platform.Tenancy`, which is the layering half of the same guarantee.

### 3.5 What this does *not* change

`TenantScope` stays in `.Contracts`, for the reason ADR-0034 §6 states generally: placement follows who must hold the value. Every application-service method takes a scope; nobody outside tenancy may hold a handle. The `TenantAccess` base stays in `.Contracts` because the scope needs it there, and it carries nothing but `TenantId` and `TenantKey`.

---

## 4. Options considered

**For `InstalledPackages` (§2):**

| Option | Pros | Cons |
|---|---|---|
| **A. Confirm the shipped shape; add an `Active` view** *(chosen)* | The type is already built, reviewed and tested; the one gap is a bug shape rather than a design disagreement, and it costs two members while the branch is open | Asks a branch in rework for a change |
| B. Confirm the shipped shape unchanged | Nothing to do | Leaves "installed" and "usable" the same question, so every future consumer must remember a filter. ADR-0008 §5.3's deactivation would switch nothing off |
| C. Carry only `Active` packages in the scope | The bug shape is impossible | The install and upgrade paths need the full set, and they would then read the catalog a second time — two readers of one fact, which is how they drift |
| D. Carry typed `PackageId`/`PackageVersion` from `Aurora.Countries.Contracts` | One spelling of a package id across the system | Impossible under fitness rule L2, and undesirable independently: it puts a package-contract MAJOR bump on tenancy's critical path (§2.2) |

**For `TenantDatabaseHandle` (§3):**

| Option | Pros | Cons |
|---|---|---|
| **A. Keep it, move it to `Aurora.Platform.Tenancy`, own it in B-07.1** *(chosen)* | ADR-0027's decision is preserved intact; the L2 contradiction disappears; layering enforces confinement ahead of T6; the owning row is the one that already builds the only factory that can produce it | Adds items to the largest row in the split; B-06.1a's exact-set guard goes red when it lands, which is the design but is still a coordination cost |
| B. Keep it in `.Contracts` and add Npgsql there | ADR-0027 §1 unchanged | Breaks fitness rule L2 and puts a database driver on the surface every module compiles against. The rule would have to be weakened for one type, which is how allow-lists start |
| C. Give it its own kernel row, as B-06.1a was for the scope | Symmetry; the guard is resolved in the same pass that created it | A row for a sealed class with four properties, inserted in front of the provisioning spine, for a type with exactly one producer |
| D. Supersede it; go back to one proof type with a `Migration` reason | One type to learn | ADR-0027 rejected this for reasons that have not changed: the §7.5 gate grows a bypass branch whose name is what a careless developer would pick, and a scope must be manufactured carrying facts the run is about to invalidate |

---

## 5. Consequences

**Positive**

- Both types now have a definition a developer can implement without guessing, and the one that had no owner has one.
- §3.2 turns T6 from the only mechanism into the second one, behind a layering rule that is already live and that no rename can defeat.
- §3.3 removes a contradiction that would have been found by whoever implemented B-07.2, at the point where backing out is most expensive.
- §2.3 closes a bug shape — "installed" read as "usable" — while it costs one property on a type that is in rework, rather than after five consumers have each written the filter differently.

**Negative, and owned**

- **`task/B-06.1a` is asked for a change while in rework.** It is two members and their tests, and the alternative is a rule every future consumer must remember.
- **§2.3's rule has no enforcing mechanism yet** and says so. The first capability resolver must bring one; if it does not, the rule is prose.
- **§3.2 and §3.3 correct an accepted ADR's mechanism**, which is the second time ADR-0027 §1 has needed correction (ADR-0032 §4.2 was the first). ADR-0027's *decision* has held both times; its type-level detail was written before any of it existed, and that is the pattern to expect from the remaining unbuilt clauses.
- **§3.4 adds work to B-07.1**, already flagged as the largest and riskiest row in the B-06/B-07 split. Items 2–5 are test and fixture updates rather than new behaviour, but they are not free, and the pre-dispatch size check should be re-run with them counted.

---

## 6. Revisit when

- A capability resolver exists and §2.3's rule can finally be enforced — that is the moment to check it was, not to assume it.
- A second DDL-capable role appears (ADR-0027 §5 already names a read-only schema inspector), which turns `Role` from a two-value enum into something else and brings whoever does it back to ADR-0027 first.
- Migrations move to pre-generated idempotent SQL scripts (ADR-0027's option C), which removes `ITenantMigrationContextFactory` entirely and makes §3.3 moot.
- The Country Package contract needs to be visible to tenancy — for instance if a scope ever has to carry a typed capability set rather than a package list. That is the trigger to re-open §2.2, and the answer is likely to be a tenancy-owned projection rather than a reference across the seam.
