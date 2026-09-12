# Testing strategy

Status: accepted **v2** · Author: architect · Date: 2026-09-11
Companion: `solution-layout.md` §5 (`verify.sh`), `modules.md` §6 (the dependency matrix these tests enforce), `../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §12

**Changes in v2** (2026-09-11, B-03 peer review): §3 — a property test is only as arbitrary as its narrowest generator; the rule and the defect that taught it are recorded there.

---

## 1. Principles

1. **TDD is the process**: failing test, then code, then refactor (`CLAUDE.md`). Every acceptance criterion in a `SPEC-###` maps to at least one named test.
2. **A test asserts behaviour, not implementation.** If a refactor that changes no behaviour breaks a test, that test was wrong.
3. **Rules that matter are tests, not documents.** Every "must" in `modules.md`, ADR-0007 and ADR-0008 that can be checked mechanically has a fitness test. A rule enforced only by review is a rule that will be broken within three months of the team changing.
4. **Every fitness test ships with a deliberately-violating fixture** proving it actually fails. A green rule that cannot fail is worse than no rule, because it is trusted.
5. **A flaky test is a failing test.** Quarantine requires a backlog ID, a named owner and a seven-day limit. There is no long-term quarantine list.
6. **Tests are deterministic.** No `DateTime.Now`, no `Random` without a fixed seed, no dependence on test execution order, no network beyond the local container.

---

## 2. The pyramid

| Layer | Share | Per-test budget | Suite budget | What it covers |
|---|---|---|---|---|
| **Domain unit** | ~65% | < 20 ms | < 30 s | Aggregate invariants, value objects, money and rounding, state machines. No I/O of any kind |
| **Application unit** | ~10% | < 50 ms | included above | Handler orchestration with in-memory fakes for other modules' contracts |
| **Architecture fitness** | ~5% | — | < 30 s | §5. No database |
| **Integration** | ~15% | < 2 s | < 5 min | Real PostgreSQL via Testcontainers: mapping, migrations, concurrency, transactions, outbox, tenant isolation, Country Package install |
| **UI component** | ~4% | < 100 ms | < 60 s | bUnit: render, bind, validate, localize, accessibility attributes |
| **End-to-end** | ~1% | — | — | Only the walking skeleton and one order-to-cash happy path. Deliberately tiny: e2e tests are the slowest and least specific way to learn a thing is broken |

The shape is deliberate. In an ERP the expensive defects are in **domain invariants** (an unbalanced posting, a wrong rounding residual, a tax rate resolved as-of the wrong date) and in **boundaries** (tenant isolation, module leakage). Those are the two layers we over-invest in.

---

## 3. Domain unit tests

- One test class per aggregate or value object; test names read as sentences: `Posting_an_unbalanced_entry_is_rejected`.
- **Every invariant gets a test, including the ones that feel obvious.** `Money` addition across currencies throws. A `JournalEntry` whose debits and credits differ is rejected. A posting into a closed period is rejected. A `SalesOrder` line cannot be invoiced beyond its delivered quantity.
- **Property-based tests** (FsCheck) for the arithmetic that must hold universally: for any set of lines, the sum of tax amounts equals the document tax total plus the rounding residual; a reversal of a posting always nets to zero; FIFO layer consumption never produces a negative layer. Financial correctness is quality attribute #1 and example-based tests systematically miss the boundary cases that matter in money arithmetic. **A property test is only as arbitrary as its narrowest generator, and the test must say which dimensions are genuinely arbitrary and which are a fixed list.** B-03's allocation property drew weights from ten short literals, so every product was exact and the law it asserted could never break; the arithmetic was wrong for one split in five and the test stayed green. Where a law can fail because of *precision* (ADR-0021 Consequences), the generator must produce values that exhaust it — weights from `decimal` division, mixed scale and magnitude — and you demonstrate the test failing against the reintroduced defect rather than asserting that it would.
- **Time is injected.** `TimeProvider` everywhere, `FakeTimeProvider` in tests. A fitness test bans `DateTime.Now`/`UtcNow` in domain and application code (§5, rule S3).
- Test data comes from **builders with sane defaults** (`ASalesOrder.WithLine(...).Build()`), never from shared mutable fixtures.

---

## 4. Application and module integration tests

Each module has an integration test project that exercises its handlers against a **real PostgreSQL database**, through the real tenant-scoped factory, with real migrations applied. Fakes are used only for *other modules'* contracts and for external systems.

Minimum coverage per module:

- Each command: happy path, each rejection, and **idempotency** where the command carries an idempotency key (financial documents — ADR-0013).
- Each query: correct results, and a bounded result set (no unbounded materialization).
- Mapping: every aggregate round-trips, including owned `Money` values landing in `numeric(19,4)` + `char(3)`.
- Concurrency: an optimistic-concurrency conflict is detected and surfaced, not silently last-write-wins.
- Outbox: the event row and the aggregate change commit in the **same transaction**, and nothing is published when the transaction rolls back.
- **Tenant isolation** (§7) — mandatory, not optional.

---

## 5. Architecture fitness tests

One project, `tests/Aurora.Architecture.Tests`. Rules whose subject is **compiled code** read IL metadata with the in-box `System.Reflection.Metadata`; rules whose subject is the **project graph** parse `.csproj` and `packages.lock.json` directly, because an *unused* forbidden reference exists only there. **ADR-0030** records that mechanism and withdraws ArchUnitNET, which ADR-0020 had chosen and which has no consumer. Choosing a compiler API for a future source-level rule (§5.2 M4, or a `.razor` rule) is a decision for the ADR that introduces that rule.

Budget: under 30 seconds, and no database, so it is cheap enough to run on every save. Two conventions come with the mechanism and apply to every rule below:

- **Every rule reports what it examined** (`SubjectsExamined` and `SubjectKind`) and is asserted against a floor, so a rule cannot pass having inspected nothing — the difference between "all good" and "nothing ran".
- **A rule that is inert because its subject does not exist yet is listed in the inventory** with the task that brings its subject, and carries a guard that fails when the subject appears. An inert rule that nobody wakes up is a rule that was never written.

### 5.1 Layer rules

| Id | Rule |
|---|---|
| L1 | `Aurora.Modules.*.Domain` and `Aurora.SharedKernel` reference only the BCL (and `SharedKernel` respectively). No EF, ASP.NET, Npgsql, logging, JSON or DI types |
| L2 | `*.Contracts` references only `Aurora.SharedKernel` and `Aurora.Documents.Canonical` |
| L3 | `*.Application` references no Npgsql type and no ASP.NET type |
| L4 | `*.Infrastructure` is referenced only by `Aurora.Composition` and its own test projects |
| L5 | `Aurora.Web` and `Aurora.Worker` reference no `*.Domain` or `*.Infrastructure` assembly (ADR-0005 rule 1) |
| L6 | No type in a module's `Application` or `Infrastructure` is `public` unless it is in `Contracts` — modules expose contracts, not classes |

### 5.2 Module boundary rules

| Id | Rule |
|---|---|
| M1 | The dependency matrix in `modules.md` §6 is encoded as data in the test and asserted for every project reference. Adding a reference requires editing the matrix, which requires a reviewer to see it |
| M2 | Same-tier modules have **no** project reference between them (events only) |
| M3 | No module's `DbContext` maps an entity to a schema other than its own |
| M4 | No SQL string in any module names a schema other than its own (Roslyn scan over string literals reaching `FromSql*`/`ExecuteSql*`) |
| M5 | Every module has exactly one `DbContext`, with `MigrationsHistoryTable` in its own schema |

### 5.3 Tenancy rules — the structural guarantee, tested

| Id | Rule |
|---|---|
| T1 | No tenant `DbContext` has a `public` or `protected` constructor (ADR-0007 §4.1) |
| T2 | **The `AddDbContext` family is called only in `Aurora.Platform.Tenancy`, and only for `Aurora.Platform.Tenancy.Catalog.CatalogDbContext`.** Keys on the **call site's assembly**, not on the generic argument, so a generic helper whose argument is a type parameter (`!!0`) is a violation rather than a silence (ADR-0007 §4.2 as amended by **ADR-0032 §4.1**) |
| T3 | `ITenantDbContextFactory<>` is implemented only in `Aurora.Platform.Tenancy` — allow-list matched by **exact** assembly name, never a prefix (ADR-0032 §4.4) |
| T4 | `IHttpContextAccessor` appears only in `Aurora.Web`'s tenant-resolution middleware (§3.3) |
| T5 | No type registered as a **singleton** has a `TenantScope` field or property |
| T6 | `TenantScope` appears in no serializable payload: not in an integration event, not in a job payload, not in a cache entry (§10.4) |
| T7 | Every module integration-test assembly contains exactly one subclass of `TenantIsolationContract<>` |
| T8 | No `IPlatformJob` implementation references a module's `.Domain`, `.Application` or `.Infrastructure` (§10.1) |
| T9 | A `DbContext` mapping any `ICompanyScoped` entity type has a `CompanyScope` constructor parameter, and is never obtained through the single-argument `CreateAsync(TenantScope, ct)` overload (ADR-0029 A1.2 H-2). The company boundary is the one horizontal-escalation boundary inside a tenant and gets the same structural treatment as the tenant boundary. **Also:** `ITenantScopeFactory` exposes no `TenantAccessReason` parameter and no call site outside `Aurora.Platform.Tenancy` names a `TenantAccessReason` value (ADR-0029 A2.4) — a reason the caller can type is a state gate the caller can skip |
| T10 | Every `HybridCache` key is produced by `TenantCacheKey.For` or `CatalogCacheKey.For` and by nothing else; each `CatalogCacheKey` kind is a member of the committed kinds enum (ADR-0012 rules 1-2 as amended by ADR-0029 A1.1). A cross-tenant cache entry is a reviewed edit, not a string literal |
| T11 | No `TenantScope` field or property on a `CircuitHandler`, a Blazor component or any type whose lifetime is the circuit. The circuit pins the `TenantId`; a scope is opened per unit of work and disposed with it (ADR-0029 A2.5). Paired with B-18.8's counted assertion that a circuit opens more than one scope over its life, which is what a circuit-lifetime scope fails |
| T15 | No **container-facing** call or method names a tenant `DbContext`, **and no container-facing call registers a type the rule cannot resolve**. Container-facing, in exactly three limbs (ADR-0032 §4.1.1, which is the single definition): a **call** whose declaring type is under the namespace `Microsoft.Extensions.DependencyInjection` or `Microsoft.Extensions.Hosting` (segment-bounded prefix); a **call whose called member's signature** names one of five exact types (`IServiceCollection`, `ServiceDescriptor`, `IServiceProvider`, `IHostApplicationBuilder`, `WebApplicationBuilder`) in any position, parameter or return, in a framework member as readily as a project one; or a **method whose own signature or locals** name one of those five. Floor on the merged tree: **5 call sites and 2 methods**, measured — 3 is met by the `Microsoft.Extensions.Hosting` calls alone and would not detect the loss of the DI limb; 4 is met without `WebApplication.CreateBuilder` and would not detect the loss of the called-member-signature limb. A tenant context named by the call's **declaring type's** generic arguments counts, which is what catches `TypeHelper<SalesDbContext>.Register(services)` and needs the scanner extension in ADR-0032 §4.1.1a. Keys on the container surface, never on a method name (ADR-0032 §4.1) |
| T16 | A tenant `DbContext` type is **named** only inside its owning assemblies — the assembly that declares it plus, when that is a `<module>.Application`, the sibling `<module>.Infrastructure`. Exact names. **Must not ship before the scanner extension in ADR-0032 §4.1.1a**: without it the declaring type of an instruction reaches the rules with its generic arguments trimmed, so `TypeHelper<SalesDbContext>.Register(services)` mentions the context nowhere the rule can see and T16 reports nothing on the shape it exists to catch, in green (ADR-0032 §4.2) |
| T17 | No **externally reachable** member (member accessibility *and* enclosing-type visibility) returns or exposes a tenant `DbContext` — return type, field, property, or a **`ref`/`out` parameter**, which yields one (`bool TryOpen(out SalesDbContext)`). A *by-value* parameter does not, and a member typed `ITenantDbContextFactory<TContext>` does not (ADR-0032 §4.2) |
| T18 | A tenant `DbContext` is **constructed** (`newobj`) only in `Aurora.Platform.Tenancy` — the clause that closes the door inside the owning assembly, where T17 is silent by design (ADR-0032 §4.2) |
| T19 | The catalog exemption is the one exact pair (`Aurora.Platform.Tenancy.Catalog.CatalogDbContext`, assembly `Aurora.Platform.Tenancy`); any other type carrying that simple name is a violation, and **if the population contains any type deriving from `DbContext`, the pair must be present exactly once** — self-triggering, so it needs no promotion step and must not cite the `No_tenant_DbContext_exists_yet…` guard, which cannot fire on it. The exact-pair exemption itself is **built** (`TenancyNames.NonTenantContext`); the presence clause and the named rule are the remainder (ADR-0032 §4.3) |
| T20 | Every allow-list entry in every tenancy rule names an assembly that exists in the production population. **Live, floor 2** since B-05 merged `Aurora.Platform.Tenancy` and `Aurora.Platform.Tenancy.Contracts` — both on T3's and T6's exact allow-lists, the second because it *declares* `TenantScope`, `TenantAccess`, `TenantDatabaseHandle` and both factory interfaces. The exact lists are not built yet: the rules carry a documented interim segment-bounded prefix pending this decision (ADR-0032 §4.4) |

**Id allocation, and a collision that is not yet resolved.** The rows above are `T15`-`T20`, the range this branch was allocated; `T9`-`T14` belong to other in-flight branches (`task/ARCH-IDENTITY` claims `T9` for a company-scope rule and `T10` for `HybridCache` keys) and are not renumbered here. The implemented `T6` in `tests/Aurora.Architecture.Tests` is
ADR-0027 §1's `TenantDatabaseHandle` allow-list, not the serializable-payload rule in the row above,
and the rows `T7`/`T8` are unimplemented. Whoever implements the
serializable-payload rule takes a fresh id and corrects this table in the same change; until then the
row above describes an intent, not a mechanism.

**None of the tenancy rules above is the structural guarantee.** The guarantee is ADR-0007 §4.1
layer 1 — one `internal` constructor taking a `TenantAccess` only `Aurora.Platform.Tenancy` can
construct. These rules are defence in depth over it, each with a stated blind spot in ADR-0032 §7.
Do not cite this table as coverage.

**Every live rule in this section carries a non-zero population floor and a `SubjectKind` in
`RuleInventoryTests`; every inert one carries the task that wakes it and a guard that expires on a
fact rather than a date** (ADR-0030). A tenancy rule reporting "no violations" without saying how
many subjects it examined is treated as a finding, not as a pass — and so is an inert rule whose
wake-up cannot fire.

### 5.4 Financial-correctness rules

| Id | Rule |
|---|---|
| F1 | No `double` or `float` **anywhere in the assemblies built from `src/`** — not as a field, property, parameter or return type, and not as a local, a cast or a floating-point instruction inside a method body. The body half is not decoration: `../reviews/B-03.md` m-1 was a `decimal`-in/`decimal`-out method that computed through a `double` local, which a signature-level rule cannot see. The rule's exact population is stated in the project's README |
| F2 | Every `Money`-typed property maps to `numeric(19,4)` + `char(3)`; unit prices to `numeric(19,6)`; exchange rates to `numeric(19,10)` (asserted over the built EF model) |
| F3 | `JournalEntry` and `JournalEntryLine` expose no public setter and no delete path; the EF model marks them append-only |
| F4 | Every monetary arithmetic result in domain code is produced by `Money` operators, never by raw `decimal` arithmetic on an amount extracted from a `Money` |

### 5.5 Country-agnosticism rules

| Id | Rule |
|---|---|
| C1 | No comparison against an ISO-3166 country literal, and no `CountryCode` switch, in any business module's `Domain` or `Application`. This is the mechanical form of *"the core must never contain `if (country == "SE")`"* |
| C2 | No account-number-shaped literal (a bare 3–8 digit string assigned to an account-typed member) in core code — accounts are resolved by `AccountRole` (ADR-0008 §6.1) |
| C3 | A Country Package assembly references only `Aurora.Countries.Contracts` and `Aurora.Documents.Canonical` |
| C4 | The public surface of `Aurora.Countries.Contracts` matches its approved-API file; any change forces an explicit SemVer decision (ADR-0008 §3.1) |
| C5 | No tax or rate lookup signature exists without an explicit `asOf` date parameter (ADR-0008 §6.2) |

### 5.6 Security and correctness rules

| Id | Rule |
|---|---|
| S1 | Every application-service command type carries a permission declaration (ADR-0010). The rule **reports how many types it asserted** and fails below a floor, and has a runtime counterpart: the enforcement pipeline refuses a request type with no declaration (ADR-0029 §5, §8) |
| S2 | Every REST endpoint has an authorization attribute; anonymous access requires an explicit, reviewed `[AllowAnonymous]` on a short allow-list |
| S3 | No `DateTime.Now`, `DateTime.UtcNow` or `DateTimeOffset.UtcNow` in `*.Domain` or `*.Application` — time comes from `TimeProvider` |
| S4 | No interpolated or concatenated string reaches `FromSqlRaw`/`ExecuteSqlRaw` (Roslyn rule); parameterized APIs only |
| S5 | No `[PersonalData]`-annotated property on a type that does not implement `IPseudonymisable`, unless listed in the reviewed exception file (ADR-0007 §11.5) |
| S6 | The set of claim types minted at sign-in equals the committed approved-claims file, and no claim type in it is a permission or a role — permissions are read at evaluation time, never carried in a cookie or a token (ADR-0029 §6). Reports the number of claim types asserted |
| S7 | **The tenant claims are written in exactly two places.** The rule inspects the constants `AuroraClaimTypes.TenantId` **and `TenantKey`**, the string literals `"tid"` and `"tkey"` anywhere in the solution, and every `ClaimsIdentity`/`ClaimsPrincipal` construction site; the allow-list is the sign-in mint, the `OnRefreshingPrincipal` carry-over and the cross-check, plus their test assemblies (ADR-0029 §4, A1.2 H-6). Named for what it inspects: a rule over one constant's references does not see `new Claim("tid", …)` |
| S8 | Every endpoint is either tenant-resolved or named in the committed tenant-neutral allow-list with a justification (ADR-0029 A1.2 H-4). Reports the count of each; no `TenantScope` may be opened on a tenant-neutral endpoint |
| S9 | Every declared `Permission` constant appears in **exactly one** of `administrator-permissions.approved.txt` and `not-administrator.approved.txt`, and no constant uses the reserved `operator.` or `platform.` prefix (ADR-0029 A1.2 H-3). Reports both counts, so a permission can be neither silently granted fleet-wide nor silently unreachable. **Also:** every declared `SystemPrincipal` appears in `system-principals.approved.txt` with its permission set and a reason, count reported, and `AccessSubject.ForSystemJob` is referenced only by `Aurora.Platform.Jobs`, the outbox dispatcher and their test assemblies (ADR-0029 A2.2) |
| Q1 | No `ToListAsync`/`ToArrayAsync` on an `IQueryable` without a preceding `Take` (Roslyn heuristic; suppressions require a justification string and are reviewed) |
| Q2 | Every module `DbContext` sets `QueryTrackingBehavior.NoTrackingWithIdentityResolution` as its default (ADR-0003 rule 4) |

### 5.7 Migration safety rules

| Id | Rule |
|---|---|
| MIG1 | Every migration is annotated `Expand`, `Contract` or `DataOnly` |
| MIG2 | Generated SQL containing a **destructive** statement appears only in a `Contract` migration. Destructive is decided by **effect, not spelling** (ADR-0037 §2), in three limbs, and every `ALTER TABLE` sub-action outside two stated clean lists is destructive by default (ADR-0037 §2.1.1): limb A, a removal that narrows what the schema offers — `DROP COLUMN`, `DROP TABLE`, `RENAME`, a non-widening `ALTER COLUMN … TYPE` (ADR-0037 §4), `ADD COLUMN … NOT NULL` without a default, a `DROP CONSTRAINT` not attributable to a `DropCheckConstraintOperation` (ADR-0037 §3.2); and limb B, a **suppression** that leaves the object named and unenforced — `DISABLE`/`ENABLE`/`ENABLE REPLICA TRIGGER` and `RULE`, `SET session_replication_role` in every spelling, `CREATE OR REPLACE` of any object, `DISABLE ROW LEVEL SECURITY`, `NO FORCE ROW LEVEL SECURITY`, `DETACH PARTITION`. limb C, a **silent data rewrite** that leaves the catalog identical — `SET EXPRESSION AS`, `TRUNCATE`, `DELETE FROM`. The enumerations are [`postgres-invariant-suppression.md`](postgres-invariant-suppression.md), which the rule names the row ids it covers from; limb C has no enumeration and is reached only by the sub-action default |
| MIG3 | A `Contract` migration and the `Expand` it contracts never ship in the same release (ADR-0007 §7.2). "Release" is the `SchemaVersion` declared on `[MigrationSafety]`; the Contract names its Expand through `Contracts` (the member B-09 ships) and must carry a strictly greater `SchemaVersion`, **which B-09.3 adds and which does not exist today** (ADR-0037 §5.3). Reports `Contract` migrations examined, which is **0** today — the rule carries an inertness guard until it stops being 0 |
| MIG4 | `CREATE INDEX` on an existing table uses `CONCURRENTLY` with `suppressTransaction: true`, **unless** the migration carries a `[TransactionalIndexBuild]` naming that exact index and saying why (ADR-0037 §6). Reports indexes created, built concurrently, and exempted by name |
| MIG5 | A package migration names no schema other than its own (ADR-0008 §4.1 R2) |

### 5.8 Localization rules

| Id | Rule |
|---|---|
| A1 | No literal string is passed to a component parameter named `Text`, `Label`, `Title`, `Placeholder`, `Header` or `Description` in a `.razor` file; values come from `IStringLocalizer` (Roslyn/source scan). Best-effort by nature — code review remains the backstop for prose in markup |
| A2 | Every `.resx` key used in code exists in the base `en` resource; unused keys are reported, not failed |

---

## 6. Integration test infrastructure

**Measured fact that shapes everything here: a `postgres:17-alpine` container costs about 9 seconds to start.** With one container per test class, a hundred integration test classes would spend fifteen minutes doing nothing but starting databases.

### 6.1 One container per xUnit collection, and few collections

- The container is an xUnit **collection fixture** (`ICollectionFixture<PostgresFixture>`), not a class fixture.
- **Hard rule: a test project has at most two collections.** More collections means more containers means a gate developers stop running.
- `PostgreSqlBuilder` must pin the image explicitly — `new PostgreSqlBuilder().WithImage("postgres:17-alpine")` — because the parameterless constructor is obsolete in Testcontainers 4.15.0.
- Leave the Testcontainers resource reaper enabled; orphaned containers on a developer machine are a support cost nobody budgets for.

### 6.2 Test-only tuning that is safe because it is test-only

```
--tmpfs /var/lib/postgresql/data
-c fsync=off -c full_page_writes=off -c synchronous_commit=off
-c max_connections=200
```

Durability is irrelevant in a container that is destroyed at the end of the run, and turning it off is the difference between a 5-minute and a 12-minute suite.

### 6.3 Template databases, not repeated migrations

Applying every module's migrations takes seconds. Doing it per test class is the second biggest cost after container startup. Instead:

1. **Once per collection**, provision `aurora_template` through the **real provisioning saga** (ADR-0007 §8) — so the saga is continuously tested by everything else.
2. **Per test class**, `CREATE DATABASE <unique> TEMPLATE aurora_template` (typically 100–300 ms) and re-stamp `platform.tenant_identity` with that class's tenant id.
3. **Within a class**, tests that do not manage their own transactions may roll back; tests that touch the outbox, provisioning or advisory locks get a fresh database from the template, because a rollback would hide exactly the behaviour under test.

No connection may be open to the template while cloning — the fixture closes and clears pools before each clone.

### 6.4 Parallelism

Collections run in parallel; `maxParallelThreads` is capped (start at 4) so parallel tests do not create a connection storm against a single container. Raise it only with a measured before/after.

---

## 7. The tenant isolation test pattern — mandatory

Defined normatively in ADR-0007 §12. Restated here as the developer's checklist, because this is the one pattern nobody may skip.

`TwoTenantDatabaseFixture` provisions **two** tenants, A and B, through the real saga. Each module inherits `TenantIsolationContract<TFixture>`, implements four small methods — seed, read-all, run-the-background-job, raise-the-integration-event — and inherits six tests:

1. Data written in A is not readable in B.
2. Data written in B is not readable in A.
3. A background job for A writes only to A.
4. An integration event raised in A is handled only in A.
5. Reusing a disposed `TenantScope` throws.
6. **A deliberate mis-route** — tenant A's scope handed a connection string pointing at tenant B's database — throws `TenantRoutingViolationException`.

Test 6 is the one a developer would never think to write and the one that matters most: it proves the §4.3 safety net still works. Without it, the identity check can silently rot into a no-op and nothing turns red.

**Fitness rule T7 fails the build for any module without a subclass.** Tests 3 and 4 exist because background jobs and integration events are where database-per-tenant leaks in practice — there is no HTTP request to carry the tenant, so the context has to be carried deliberately, and the test is what proves it was.

---

## 8. Country Package tests

Every package inherits `CountryPackageContractTests<TPackage>` (ADR-0008 §10): manifest matches assembly, migrations touch only the package schema, install does not alter core DDL, all declared capabilities resolve, effective-dated rows have no gaps or overlaps, the identifier validator matches the jurisdiction's published test vectors, e-invoice output validates against the published XSD, install/uninstall round-trips, **and install into a live tenant with existing data works**. A fitness test asserts every package assembly has a subclass.

---

## 9. UI component and accessibility tests

- **bUnit** for components: rendering, two-way binding, validation messages, and that every user-facing string comes from `IStringLocalizer` (a test renders under `en` and a pseudo-locale and asserts the output differs).
- Grid components are tested for **server-side paging**: a component given a 100 000-row provider must request at most one page (ADR-0005 rule 3).
- Accessibility: automated checks for the mechanical parts of WCAG 2.2 AA — labels associated with inputs, roles, focus order, colour-contrast tokens from `docs/design/tokens.json`. Automation catches perhaps half of AA; the design reviews in `docs/design/reviews/` are the other half and this document does not pretend otherwise.

---

## 10. Performance tests

Not a suite; three guard rails:

1. **Query-count assertions** on the heaviest read paths (order list, ledger enquiry, stock availability): the test fails if the number of SQL round trips exceeds a stated budget. This is how N+1 is caught the day it is written rather than the day a customer complains.
2. **Result-bound assertions**: a query over a seeded 50 000-row table must return a bounded page and must not materialise the table.
3. **Index presence**: a test asserts that every foreign key and every column used by a declared query filter has an index, by comparing the EF model against `pg_indexes`.

Load testing is a milestone activity against the numbers in `scalability.md`, not a per-commit gate.

---

## 11. What runs in `verify.sh`

See `solution-layout.md` §5.2 for the definitive stage list. In short: restore (locked), format, build (warnings as errors), dependency licence gate, vulnerability gate, **unit tests (which must pass with Docker stopped)**, architecture fitness tests, integration tests, UI component tests, coverage.

Two properties worth stating explicitly:

- **Stages 6 and 7 need no Docker.** A developer without a working Docker daemon still gets the domain and architecture feedback, which is most of the value.
- **The gate is the same locally and in CI.** There is no separate CI script that runs more or fewer checks. A gate that differs between the two teaches developers to distrust both.

---

## 12. Coverage

Collected on every run and reported; **not** a primary target — coverage measures execution, not assertion. One enforced floor: **80% line coverage on `*.Domain` assemblies, from milestone M2**. Domain code is pure, fast to test and holds the invariants that cost money when wrong; there is no honest excuse for uncovered domain code. No floor is set on Infrastructure, where coverage chasing produces tests of the ORM rather than of the system.
