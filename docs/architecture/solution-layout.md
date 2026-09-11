# Solution layout and the `scripts/verify.sh` spec

Status: accepted **v5** · Author: architect · Date: 2026-09-11
Companion: `modules.md`, `testing-strategy.md`, `dependencies.md`, `../decisions/ADR-0007-...`, `../decisions/ADR-0008-...`

**Changes in v5** (2026-09-11, closing the identity/authorization gap the project-manager flagged twice): new **§6.2** specifies the nine bootstrap rows that build ADR-0009's sign-in stack and ADR-0010's permission evaluation, and new **§6.3** records the dependency edges that change as a result. Both follow **ADR-0029**. §6 and §6.1 are unchanged; §6.3 overrides their **Depends on** column where it says so.

**Changes in v4** (2026-09-11, answering the project-manager's four questions on the B-06/B-07/B-08/B-13 split): §6.1 below records the dependency corrections that follow from **ADR-0027** (the DDL path is separate from the application data path) and the two new bootstrap rows that follow from **ADR-0028** (the tenant audit store). The §6 table rows themselves are unchanged; §6.1 overrides their **Depends on** column where it says so.

**Changes in v3** (2026-09-11, B-02 and B-03 peer review findings **S-1**/**S-2**): stage 6 now belongs to **B-02** (stages 0–3, 6, 11) and B-11 owns stages 4, 5, 7–10; §5.2 stage 6 and §5.3 record the executed-test floor and `AURORA_MIN_UNIT_TESTS`; §6 gains a standing note that an acceptance-criteria row is a floor and its ADR is the contract, and the **B-03, B-04, B-05, B-06, B-07, B-08** rows are widened to the ADR sections they implement.

**Changes in v2** (2026-09-11, B-01 peer review finding **S-1**): §5.2 stage 4 and the §6 **B-11** acceptance criteria now describe the two-tier dependency gate decided in `../decisions/ADR-0026-dependency-supply-chain-gate.md`.

This document is written as **bootstrap work for senior developers**. The architect writes no code; §6 lists the tasks to hand to the project-manager.

---

## 1. Repository layout

```
Aurora.sln
Directory.Build.props          # TFM, nullable, warnings-as-errors, analyzers — one place
Directory.Packages.props       # central package management; every version lives here
.editorconfig                  # style rules, enforced in build
global.json                    # pins SDK 10.0.401 with rollForward: latestPatch
nuget.config

src/
  Aurora.SharedKernel/                          # tier 0
  Aurora.Documents.Canonical/                   # tier 0 — EN 16931-shaped semantic model
  Aurora.Countries.Contracts/                   # tier 0 — the ten extension points
  Aurora.Countries.Hosting/                     # manifest, catalogue, signature, ALC loader, installer

  platform/
    Aurora.Platform.Tenancy.Contracts/          Aurora.Platform.Tenancy/
    Aurora.Platform.Identity.Contracts/         Aurora.Platform.Identity/
    Aurora.Platform.Access.Contracts/           Aurora.Platform.Access/
    Aurora.Platform.Audit.Contracts/            Aurora.Platform.Audit/
    Aurora.Platform.Messaging.Contracts/        Aurora.Platform.Messaging/
    Aurora.Platform.Jobs.Contracts/             Aurora.Platform.Jobs/
    Aurora.Platform.Localization.Contracts/     Aurora.Platform.Localization/
    Aurora.Platform.Configuration.Contracts/    Aurora.Platform.Configuration/

  modules/
    Aurora.Modules.Organization.{Contracts,Domain,Application,Infrastructure}/
    Aurora.Modules.Parties.{...}/   Aurora.Modules.Products.{...}/
    Aurora.Modules.Ledger.{...}/    Aurora.Modules.Tax.{...}/
    Aurora.Modules.Inventory.{...}/
    Aurora.Modules.Sales.{...}/     Aurora.Modules.Purchasing.{...}/  Aurora.Modules.Payments.{...}/
    Aurora.Modules.DocumentExchange.{...}/      Aurora.Modules.Reporting.{...}/

  packages/
    Aurora.Countries.NewZealand/                # the first reference Country Package

  hosts/
    Aurora.Web/                                 # Blazor Server + REST API + webhooks
    Aurora.Worker/                              # scheduler, outbox dispatcher, migration runner, provisioner
    Aurora.Composition/                         # the only project that wires every module into DI

tests/
  Aurora.TestKit/                               # fixtures, builders, the two-tenant fixture
  Aurora.Architecture.Tests/                    # fitness tests — no database, fast
  unit/         Aurora.Modules.<M>.UnitTests/
  integration/  Aurora.Modules.<M>.IntegrationTests/
                Aurora.Platform.Tenancy.IntegrationTests/
                Aurora.Countries.NewZealand.Tests/
  ui/           Aurora.Web.ComponentTests/      # bUnit

scripts/
  dev-env.sh      # exists: PATH for /usr/share/dotnet, starts Docker if needed
  verify.sh       # the single quality gate (§5)
  check-dependencies.sh   # stage 4; also `--update-closure` (dependencies.md §7.3)
  new-module.sh   # scaffolds the 4 projects + schema + isolation test, from templates

artifacts/        # git-ignored: build output, TRX, coverage, verify logs
```

**The `Modules.` and `Platform.` namespace segments are load-bearing.** Every architecture fitness test in `testing-strategy.md` is expressed as a rule over these prefixes. Do not flatten them for brevity.

---

## 2. The four projects of a module, and what each may reference

| Project | Contains | May reference |
|---|---|---|
| `<M>.Contracts` | DTOs, application-service interfaces, integration event records, strongly-typed ids, query interfaces | `Aurora.SharedKernel`, `Aurora.Documents.Canonical`. **Nothing else.** |
| `<M>.Domain` | Aggregates, entities, value objects, domain events, domain services, invariants | `Aurora.SharedKernel` **only**. No EF, no ASP.NET, no Npgsql, no `System.Data`, no logging framework |
| `<M>.Application` | Use-case handlers, the module `DbContext`, EF entity configurations, query implementations, validators | `<M>.Domain`, `<M>.Contracts`, other modules' `.Contracts` **per the matrix in `modules.md` §6**, `Aurora.Platform.*.Contracts`, `Microsoft.EntityFrameworkCore` (provider-agnostic) |
| `<M>.Infrastructure` | Npgsql specifics, **migrations**, compiled model, external gateways, the module's `AddXModule()` DI extension | `<M>.Application`, `Npgsql.EntityFrameworkCore.PostgreSQL` |

### 2.1 Why the `DbContext` lives in Application, not Infrastructure

This deviates from the textbook and a reviewer will challenge it, so the reasoning is recorded here.

ADR-0007 §4 requires handlers to receive their `DbContext` from `ITenantDbContextFactory<TContext>` — that is the mechanism that makes the tenant guarantee compile-time. Putting the context behind hand-written repository interfaces would mean the guarantee lives in Infrastructure, where a developer can construct a repository without a scope, and would add a layer whose only product is purity. EF Core's `DbSet<T>` is already a repository and `DbContext` is already a unit of work; wrapping them buys nothing an ERP needs.

What `CLAUDE.md` actually requires is that **the domain layer has no references to frameworks, databases or UI**. That is preserved exactly, and it is the rule the fitness tests enforce:

```
Aurora.Modules.*.Domain may reference only Aurora.SharedKernel and the BCL.
```

`Application` referencing provider-agnostic EF Core is a deliberate, bounded concession; `Infrastructure` still owns everything Npgsql- and migration-specific, so swapping providers remains a one-project change.

### 2.2 Schema ownership

Each module's `DbContext` sets `HasDefaultSchema("<module>")` and its migrations history table lives in that schema:

```csharp
options.UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "sales"));
```

A fitness test asserts that every entity type mapped by a module's context has that module's schema, and only that schema.

---

## 3. Hosts and composition

- **`Aurora.Composition`** is the only project allowed to reference every module's `.Infrastructure`. Both hosts reference `Aurora.Composition` and nothing else from `src/modules`. This keeps "which modules exist" a single, reviewable file, and makes it impossible for `Aurora.Web` to sneak a reference to `Sales.Domain`.
- **`Aurora.Web`** — Blazor Server (`InteractiveServer`), the versioned REST API (ADR-0013) and webhooks. May reference every `.Contracts` and `Aurora.Composition`. A fitness test asserts it references no `.Domain` or `.Infrastructure` assembly directly (ADR-0005 rule 1).
- **`Aurora.Worker`** — Quartz host, outbox dispatcher, migration runner, provisioning saga runner, fan-out triggers. Same reference rules.
- Both hosts ship from **one container image with two entrypoints** so application code and schema expectations can never drift between them (`overview.md` §3).

---

## 4. Build-wide settings that are not negotiable

`Directory.Build.props`:

| Property | Value | Why |
|---|---|---|
| `TargetFramework` | `net10.0` | One place, so the next LTS upgrade is a one-line change (ADR-0002) |
| `Nullable` | `enable` | The tenant guarantee relies on non-nullable parameters |
| `TreatWarningsAsErrors` | `true` | |
| `ImplicitUsings` | `disable` | Explicit is clearer in a large, long-lived codebase (ADR-0002) |
| `EnforceCodeStyleInBuild` | `true` | `dotnet format --verify-no-changes` then has teeth |
| `AnalysisLevel` | `latest-recommended` | |
| `InvariantGlobalization` | **`false`** | ICU must be present. Localization is day one; an invariant-globalization container silently breaks every locale-specific format and is a very unpleasant bug to find in production |
| `Deterministic`, `ContinuousIntegrationBuild` | `true` in CI | Reproducible builds |
| `ManagePackageVersionsCentrally` | `true` | `Directory.Packages.props` is the single version list |
| `CentralPackageTransitivePinningEnabled` | `true` | A transitive package cannot smuggle in an unreviewed version |
| `RestorePackagesWithLockFile` | `true` | `packages.lock.json` committed; `--locked-mode` in `verify.sh` makes the licence gate in `dependencies.md` enforceable rather than aspirational |

`global.json` pins `10.0.401` with `rollForward: latestPatch`.

---

## 5. `scripts/verify.sh` — specification

One command, one gate. The integration branch must always pass it (`CLAUDE.md`).

### 5.1 Contract

- `#!/usr/bin/env bash` with `set -Eeuo pipefail`.
- **Runnable from any working directory**: resolves its own directory and `cd`s to the repository root.
- First action: `source "$REPO_ROOT/scripts/dev-env.sh"` (PATH for `/usr/share/dotnet`, starts Docker if needed).
- Exit code `0` = pass, non-zero = fail. **Fails on the first failing stage** (fail fast), and prints a per-stage timing summary at the end regardless.
- Writes logs, `.trx` and coverage to `artifacts/verify/`. Never writes anywhere else.
- Never mutates tracked files. `dotnet format` runs in `--verify-no-changes` mode only — a gate must not silently fix what it is measuring.
- No network access required beyond NuGet restore and pulling `postgres:17-alpine` once.

### 5.2 Stages, in order

| # | Stage | Command (essence) | Fails when |
|---|---|---|---|
| 0 | Preflight | `dotnet --version`; `docker info` (only if integration stages will run) | SDK missing or version mismatch with `global.json`; Docker unavailable when required |
| 1 | Restore | `dotnet restore --locked-mode` | Any `packages.lock.json` is stale — i.e. someone changed a dependency without committing the lock |
| 2 | Format & style | `dotnet format --verify-no-changes --severity warn` | Any formatting or style deviation |
| 3 | Build | `dotnet build -c Release --no-restore` | Any warning (warnings are errors) |
| 4 | Dependency licence gate | `scripts/check-dependencies.sh` — reads every project's `packages.lock.json` and the resolved `.nuspec` of each package (offline), cross-checked against `dependencies.md` §2/§3 for **direct** packages and `dependency-closure.md` for **transitive** ones | A **direct** package is absent from `dependencies.md`; a **transitive** package is absent from `dependency-closure.md`, or a closure row is no longer in the graph; a resolved licence differs from the recorded one or is not on the accepted SPDX list; a package from the `dependencies.md` §5 rejection list appears anywhere. Full list: `dependencies.md` §1 rule 4 and ADR-0026 |
| 5 | Vulnerability gate | `dotnet list package --vulnerable --include-transitive` | Any High or Critical advisory |
| 6 | Unit tests | `dotnet test --no-build -c Release --filter "Category!=Integration&Category!=Ui"` | Any failure, **or fewer tests execute than the floor in §5.3** (the stage must be able to report its own vacuity). **Must pass with Docker stopped** |
| 7 | Architecture fitness tests | `dotnet test --no-build -c Release tests/Aurora.Architecture.Tests` | Any layer, module-dependency, tenancy, money, migration-safety or country-branch rule is violated |
| 8 | Integration tests | `dotnet test --no-build -c Release --filter "Category=Integration"` | Any failure. Uses Testcontainers, one PostgreSQL container per collection |
| 9 | UI component tests | `dotnet test --no-build -c Release --filter "Category=Ui"` | Any failure (bUnit) |
| 10 | Coverage report | collect always; **enforce a floor of 80% line coverage on `*.Domain` assemblies from milestone M2 onward** | Domain coverage below the floor, once enabled |
| 11 | Summary | per-stage wall-clock table, total, pass/fail | — |

**Stage 4 writes nothing.** Regenerating the transitive allowlist is a separate, explicit developer action — `scripts/check-dependencies.sh --update-closure` — run as part of the change that moved a dependency, never by `verify.sh` and never in CI. The gate must not silently fix what it is measuring (§5.1). Format, licence classes, the offline read mechanism and the ownership carve-out for the generated file: `dependencies.md` §7.

### 5.3 Options

| Flag / env | Effect | Intended use |
|---|---|---|
| `--fast` / `AURORA_VERIFY_FAST=1` | Skips stages 8–10 | Inner loop only. **Never** a substitute for a full run before review |
| `--no-docker` | Skips stages 0's Docker check and 8 | Environments without Docker |
| `--filter <expr>` | Passed to stages 6–9 | Debugging one failure. Will fail stage 6 whenever the filter selects fewer tests than the floor below — that is the floor working, not a bug; set `AURORA_MIN_UNIT_TESTS=0` for the debugging run |
| `--stage <n>` | Runs a single stage | Debugging the script |
| `AURORA_MIN_UNIT_TESTS` | Overrides stage 6's minimum executed-test count. Stage 6 counts the tests it actually executed (TRX `Counters/@executed`, which excludes skipped tests), reports the number in the summary's Note column on every path, and **fails when the count is below the floor** | Debugging with `--filter`; never lowered in a committed default |

**The floor is what stops stage 6 reporting `PASS` while measuring nothing** — a dropped test project, a `Category` trait typo, a discovery failure or a suite that has been entirely `Skip`ped all present as a green stage otherwise. Its standing rule, which supersedes the "raise it when the first tests land" note that shipped with B-02:

> The committed default is **the number stage 6 actually executes, rounded down to the nearest ten, with a minimum of 10**. Not the exact count — that turns every added test into a `verify.sh` edit and gets deleted in frustration. **Re-round whenever a test project is added to or removed from `Aurora.sln`**, in the same commit.

The floor's own failure path is proved by a self-test case that sets `AURORA_MIN_UNIT_TESTS` explicitly against a filter matching nothing, so it keeps demonstrating the mechanism at any default.

### 5.4 Budget

Target **under 3 minutes** at bootstrap and **under 10 minutes** at milestone 3, on this machine. A PostgreSQL container costs ~9 s to start, so the integration suite must share one container per collection (`testing-strategy.md` §6) — this is the single biggest lever on the number.

**If a full run exceeds 10 minutes, split it**: `verify.sh` keeps stages 0–7 plus a representative integration subset, and `verify-full.sh` runs everything nightly. Record the split in an ADR when it happens; do not let the gate quietly become something developers skip.

---

## 6. Bootstrap tasks for the project-manager

Each is a task a senior developer can pick up. Acceptance criteria are given because "the spec is the test" (`CLAUDE.md`). Dependencies are noted; several run in parallel.

**An acceptance-criteria row is a floor, never a ceiling — the ADR it implements is the contract.** B-03 met all five of its original criteria on a branch that still lost a cent out of one invoice in five, because the law it broke lived in ADR-0021 §6 and the row did not name it. A reviewer checking only the row would have merged it. So: every row below names the ADR sections it implements, and **a reviewer reads those sections, not only the row**. If you find a row narrower than its ADR, say so in the review — that is an architecture finding, not a nit.

| # | Task | Acceptance criteria | Depends on |
|---|---|---|---|
| **B-01** | Solution skeleton and build-wide settings | `Aurora.sln` with the §1 folder structure (empty projects for tier 0 + hosts + test projects only); `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `global.json`, `nuget.config` per §4; `dotnet build -c Release` succeeds with zero warnings; `packages.lock.json` committed for every project | — |
| **B-02** | `scripts/verify.sh` stages 0–3, **6** and 11 | Runs from any cwd; sources `dev-env.sh`; fail-fast; timing summary; `artifacts/verify/` output; passes on B-01's skeleton; exits non-zero if a deliberately introduced format violation is present; stage 6 reports its executed-test count and fails below the §5.3 floor. Stage 6 was moved here from B-11 after the B-01 review found a solution-wide failure that no planned stage would have caught; implementing it with §5.2's own number, command and filter means B-11 inherits it without renumbering | B-01 |
| **B-03** | `Aurora.SharedKernel`: `Money`, `Quantity`, `Percentage`, `DateRange`, strongly-typed id primitives, `Result` | `Money` is `(decimal, Currency)`; arithmetic between different currencies throws; no `double`/`float` anywhere; unit tests for every invariant; assembly references only the BCL (asserted by a fitness test); **allocation satisfies ADR-0021 §6 — parts sum exactly to the total and every part is a whole number of minor units, for arbitrary weights, proven by a property test whose weight generator is genuinely arbitrary** (weights produced by `decimal` division, mixed magnitude and scale, single-weight splits — not a list of short literals) and by `Allocate` verifying that postcondition before it returns; raises the §5.3 stage-6 floor to the rounded-down count it lands | B-01 |
| **B-04** | `Aurora.Architecture.Tests` with the first rule set | Rules from `testing-strategy.md` §5: domain purity, module dependency matrix, no `double`/`float` in domain, no `DateTime.Now`/`UtcNow` in domain or application, no `IHttpContextAccessor` outside the resolution middleware, no `AddDbContext` of a tenant context, no public constructor on a tenant context, **no `ITenantDbContextFactory<>` implementation outside `Aurora.Platform.Tenancy`, no `TenantScope` field on a type registered as a singleton** (ADR-0007 §12.3 lists five rules; all five land here). Each rule has a deliberately-violating fixture proving the rule *fails* when it should. **A rule's name must not claim more than its mechanism checks:** the floating-point rule is solution-wide and must catch a `double` *local* or a `(double)` cast inside a method body — reflection over member signatures does not see those — or be renamed to what it actually inspects | B-01, B-03 |
| **B-05** | Catalog database and `CatalogDbContext` | Schema `catalog` per ADR-0007 §9.2, including the full `catalog.tenant.state` set (`Provisioning \| ProvisioningFailed \| Active \| Suspended \| SchemaBlocked \| Exporting \| PendingDeletion \| Deleted`) — later tasks depend on states this one creates; EF migrations; integration test against a real PostgreSQL container. **ADR-0007 §9.3 gets a mechanism, not just a sentence:** a test asserts the catalog model holds no tenant business data, with `catalog.identity_user`'s email as the one recorded exception, so the rule fails when a future entity breaks it rather than being rediscovered in review | B-01 |
| **B-06** | `Aurora.Platform.Tenancy`: `TenantScope`, `ITenantConnectionResolver`, `ITenantDbContextFactory`, the data-source cache | Structural guarantee of ADR-0007 §4 in place; `TenantScope` not constructible outside the tenancy assembly and `IsActive` false after disposal, so a reused scope throws (§3.4, §10.4); pool settings of ADR-0007 §5.2. **Identity check (§4.3):** runs in the `NpgsqlDataSourceBuilder` physical-connection initializer — once per *physical connection*, not per `DbContext`, so nothing using the data source can bypass it — and a mismatch throws `TenantRoutingViolationException`, fails the request, alerts **and marks the tenant `SchemaBlocked`**; proven by a test that deliberately mis-routes. **LRU cache (§5.1):** bounded (512 default, configurable) **and evicting disposes the data source asynchronously only after in-flight connections drain** — a test evicts under load and asserts no in-flight query is killed. **Resolver (§3.5):** the only code that builds a connection string; 60 s `HybridCache` entry under a tenant-prefixed key, **invalidated on tenant state change** — a test suspends a tenant and asserts the next resolve does not serve the cached row. `ITenantScopeAccessor.Current` returns a non-nullable scope and throws `NoTenantResolvedException`, and is registered in `Aurora.Web` only — a test asserts resolving it from the worker composition root fails (§4.2). Time comes from `TimeProvider`; there is no `IClock` (`modules.md` §3) | B-05 |
| **B-07** | Tenant provisioning saga | The nine steps of ADR-0007 §8, each idempotent; resumable after a kill at any step (test kills and resumes at each); provisions a usable tenant in under 60 s. **Step 2 never adopts a database it cannot prove is ours:** SQLSTATE `42P04` counts as success *only if* the existing database is owned by `aurora_migrator` **and** is either empty or already stamped with this `tenant_id` — a test points a replay at a database stamped for a *different* tenant and asserts the saga fails rather than adopting it. **Step 3 hardens:** `REVOKE ALL ON DATABASE … FROM PUBLIC`, `GRANT CONNECT` to `aurora_app` only, `DROP SCHEMA public`, `btree_gist` enabled — asserted by querying the resulting privileges, not by asserting the statements ran. **Step 4** stamps `platform.tenant_identity` and fails loudly if the existing row names another tenant. **Step 9** writes `TenantProvisioned` to the **catalog** outbox, dedupe key = tenant id. **Reaper:** re-leases expired non-terminal runs from `current_step`; after 5 attempts the tenant becomes `ProvisioningFailed` and pages. **Compensation is guarded:** `DROP DATABASE` is never automatic — operator console only, only for a tenant in `ProvisioningFailed`, and only after re-reading `platform.tenant_identity` and confirming it matches that tenant id; a test asserts a compensation request against an `Active` tenant, and one against a mismatched stamp, are both refused | B-06 |
| **B-08** | Migration runner | `catalog.migration_run*` tables; `FOR UPDATE SKIP LOCKED` claiming with leases; resumable; a failing tenant is quarantined — its `catalog.tenant.state` set to `SchemaBlocked` — and the run continues, retrying while `attempts < 3`; failure budget halts the run; waves; **within one tenant, `platform` migrates first, then module schemas in the `modules.md` dependency order, then `pkg_*` (§7.1)**; direct (non-PgBouncer) connection for the advisory lock; schema-version skew checks of ADR-0007 §7.5 | B-06 |
| **B-09** | Migration safety and release gate tests | Categories `Expand`/`Contract`/`DataOnly`; SQL scan for destructive statements; release gate failing when an Expand and its Contract ship together; `CREATE INDEX` without `CONCURRENTLY` rejected | B-04 |
| **B-10** | `Aurora.TestKit`: `TwoTenantDatabaseFixture` and `TenantIsolationContract<T>` | One PostgreSQL container per xUnit collection; two tenants provisioned through the **real** saga; the six mandatory tests of ADR-0007 §12.2 including the deliberate mis-route; a fitness test asserting every module integration-test assembly has a subclass | B-07 |
| **B-11** | `verify.sh` stages 4, 5, 7–10 and `scripts/check-dependencies.sh` (**stage 6 is B-02's**, already merged — extend its self-test harness, do not restart it) | Implements the two-tier gate of `dependencies.md` §1 rule 4 / §7 and ADR-0026. Generates the first `docs/architecture/dependency-closure.md` with `--update-closure` and commits it; its reviewer confirms every licence in it. Each of the six failure conditions has a test that proves the gate **fails** — including a fabricated new transitive package, a stale closure row, a licence flipped to a non-permissive value, and a package id from `dependencies.md` §5. Stage 4 runs offline, writes nothing, and reads project lock files (not `artifacts/` copies). Vulnerability gate fails on a seeded High advisory; unit stage passes with Docker stopped; full run inside the §5.4 budget | B-02, B-04, B-10 |
| **B-12** | `Aurora.Countries.Contracts` + `Aurora.Countries.Hosting` | The ten extension-point interfaces; manifest read via `MetadataLoadContext`; ECDSA P-256 signature verification; collectible ALC with the contracts assembly resolved from the default context; approved-API snapshot test on the contracts assembly; install refused against an out-of-range `coreContractRange` with both versions named | B-03 |
| **B-13** | Country Package installer | The nine install steps of ADR-0008 §5.1 including the core-DDL-hash check; install into a **live** tenant with existing data; deactivate and purge; three-way merge of §4.2 | B-07, B-12 |
| **B-14** | `scripts/new-module.sh` and the module template | Generates the four projects, the `DbContext` with `HasDefaultSchema`, the DI extension, the unit and integration test projects and a `TenantIsolationContract` subclass; the generated module passes `verify.sh` unchanged | B-10 |
| **B-15** | Walking skeleton | One trivial vertical slice (create a company, read it back) through Blazor Server and `/api/v1`, in two tenants, proving: tenant resolution pinned to the circuit, authorization, audit entry, outbox event, background job, and a Country Package installed — all covered by tests | B-06, B-10, B-13 |

**Do not start business modules before B-15 is green.** The walking skeleton is the go/no-go checkpoint the researcher asked for in `../research/01-feature-priority.md`.

---

### 6.1 Corrections to the bootstrap graph (v4, 2026-09-11)

These override the **Depends on** column above and in `../BACKLOG.md`. Reasons are in ADR-0027 and ADR-0028; the short version is that provisioning and migration never use the application data path, and nothing in B-01 … B-15 built an audit writer.

**Dependency corrections (ADR-0027).**

| Row | Was | Is | Why |
|---|---|---|---|
| B-07.1 | B-06.3 | **B-06.1** | Steps 1–3 are a catalog insert plus two `aurora_admin` connections. No `TenantScope`, no `ITenantDbContextFactory`, no data-source cache (ADR-0027 §1–§2) |
| B-08.1 | B-06.3 | **B-07.2** | Not the tenancy guarantee — the *single-tenant migration executor*, which is saga step 5. One component, not two (ADR-0027 §3). It also needs a provisioned, stamped tenant database to migrate |
| B-08.3 | B-08.1, B-06.3 | **B-06.3** | The §7.5 skew check is `ITenantScopeFactory` behaviour and belongs to Tenancy, not to the runner (ADR-0027 §4). Retitle: *Tenancy: schema-version skew gate at scope open*; module **Platform/Tenancy** |
| B-07.4 | B-07.3, B-13.2 | **B-07.3, B-13.2, B-16.1, B-06.3** | SPEC-001 BR-7/AC-6 needs the tenant-side audit writer (B-16.1); AC-5's minimal two-tenant read needs the application path (B-06.3), which was previously implied through B-07.1 |
| B-10 | B-07.4 | **B-07.4, B-06.3** | `TenantIsolationContract` is built on `TenantScope` and the §4.3 mis-route. B-06.3 used to arrive transitively; after the B-07.1 change it must be named |

**Scope corrections that go with them.**

- **B-06.1** additionally owns the tenancy kernel types (`TenantAccess`, `TenantScope`, `TenantAccessReason`, `SchemaVersion`, `CoreSchemaVersion`) and `TenantIdentityStamp` (the ADR-0007 §4.3 table definition plus `AssertAsync`). One implementation of that assertion, consumed by B-06.2's connection initializer, B-07.1's compensation guard, B-07.2's step 4 and B-08.1. The *guarantee* — factories, accessor, lease and disposal semantics, no DI registration — stays in B-06.3.
- **B-07.1** builds `ITenantAdminConnectionFactory` (ADR-0027 §2) including the `StampAssertion` opt-out and its mis-route test.
- **B-07.2** builds `ITenantSchemaMigrator` (advisory lock, stamp assertion, `platform` → `audit` → module schemas → `pkg_*` order) and `ITenantMigrationContextFactory<T>`. B-08.1 calls it and owns claiming, leases, heartbeats and resumability only.
- **B-13.1** additionally owns `IPackageUpgradePlanner` — a pure function, no database (ADR-0027 §5). The runner-side consumption (fleet compatibility report, per-tenant `Skipped | PackageIncompatible`) is not Milestone 1 work: no core upgrade happens before there is a version to upgrade from. Carry it as a follow-up depending on B-08.2 and B-13.1.

**Two new rows (ADR-0028).** `src/platform/` has no Audit projects and no task creates them; SPEC-001 BR-7/AC-6, SPEC-002 BR-5 and `CLAUDE.md`'s financial-audit rule all assume they exist.

| Row | Task | Acceptance criteria | Depends on |
|---|---|---|---|
| **B-16.1** | Tenant audit store and `IAuditWriter` | Schema `audit` with `audit.audit_event` per ADR-0018 §1, RANGE-partitioned monthly on `occurred_at` with a `DEFAULT` partition and every key including the partition key; append-only proven **three ways and probed as the runtime role** — `has_table_privilege` plus an executed `UPDATE`/`DELETE` as `aurora_app` **and** as `aurora_migrator` (the trigger must stop the owner); `ALTER DEFAULT PRIVILEGES … IN SCHEMA audit REVOKE UPDATE, DELETE` proven by creating a new table in the schema and re-probing; the same retrofit applied to `catalog.operator_audit_event` and `catalog.erasure_replay_log` (B-05 finding M-1); `IAuditWriter.WriteAsync(TenantAccess, DbContext, AuditEvent, ct)` with a test that a rolled-back business transaction leaves zero audit rows and that a forced audit failure rolls back the business change; hash chain with the pinned canonical form and `VerifyAsync`, proven by tampering with a row **as the owner** and watching verification fail. Tier **Full** | B-07.2 |
| **B-16.2** | `[Auditable]` interceptor and the raw-write ban | The `SaveChangesInterceptor` writing before/after for `[Auditable]` entities in the caller's transaction; fitness rule failing `ExecuteUpdate`/`ExecuteDelete` on an `[Auditable]` entity, with a deliberately-violating fixture; a counted assertion of how many entity types the interceptor actually covered, so "zero auditable entities" cannot report success. Tier **Full** | B-16.1, B-06.3 |

B-15.1 gains **B-16.2** as a dependency: its "audit annotation" acceptance criterion has nothing to annotate against until the interceptor exists.

**Still open after v4, closed in §6.2/§6.3 below:** nothing here builds sign-in or permission evaluation, and `B-07.3` seeds an `access` schema no row creates. See §6.2.

---

### 6.2 The identity and authorization rows (v5, 2026-09-11)

`ADR-0029` closes the gap the project-manager flagged twice: **nothing in `B-01` … `B-15` builds sign-in or permission evaluation**, while SPEC-001 AC-5, SPEC-001 BR-1, SPEC-002 BR-3/BR-4 and `B-15.2`'s endpoint authorization all presuppose both — and `B-07.3` seeds roles, the permission catalogue and the administrator Membership into an `access` schema **no row creates**, for a reader that does not exist. Read ADR-0029 before implementing any row below; as everywhere in §6, the criteria are a floor and the ADR is the contract.

**Format note.** These rows are written as subsections with numbered criteria rather than as table cells. Nine rows of six criteria do not fit a table legibly, and the project-manager needs to transcribe them one by one. The summary table is the row; the subsection is its Spec column.

| Row | Title | Module | Tier | Depends on | Parallel-safe with |
|---|---|---|---|---|---|
| **B-17.1** | Access: schema `access`, the permission catalogue and the per-tenant seeder | Platform/Access | Full | B-07.2 | B-16.1, B-13.1, B-08.1 — all four branch from B-07.2 into different project trees |
| **B-17.2** | Access: permission evaluation, fail closed | Platform/Access | Full | B-17.1, B-06.3 | B-18.4, B-18.5 |
| **B-17.3** | Access: `[RequiresPermission]`, the enforcement pipeline and fitness rule S1 | Platform/Access | Full | B-17.2, B-04, B-16.2 | B-18.6 |
| **B-18.1** | Identity: catalog identity schema and the platform authentication trail | Platform/Identity | Full | **B-05** | Everything in the B-06/B-07 spine — this row is unblocked the moment B-05 merges |
| **B-18.2** | Identity: ASP.NET Core Identity stores, hashing, lockout, Tenant Membership query | Platform/Identity | Full | B-18.1 | B-06.\*, B-07.\*, B-16.\* |
| **B-18.3** | Identity: administrator invitation — issue and redeem | Platform/Identity | Full | B-18.2 | B-06.\*, B-07.1/.2, B-16.\* |
| **B-18.4** | Web: tenant resolution pipeline and the `tid` cross-check | Aurora.Web / Tenancy | Full | B-06.3, B-18.1 | B-17.2, B-18.2, B-18.3 |
| **B-18.5** | Identity: sign-in, sign-out, cookie configuration and the `tid` mint | Platform/Identity + Aurora.Web | Full | B-18.2, B-18.4, B-16.2 | B-17.2 |
| **B-18.6** | Blazor: circuit tenant pinning and revalidation | Aurora.Web | Full | B-18.5 | B-17.3 |

Every row is **Full** tier: `CLAUDE.md`'s Full list names tenancy and isolation, authentication and authorization outright, and there is no Standard-tier meat to isolate out of any of them. The numbering does not imply dispatch order — `B-17.1` and `B-18.3` both land **before** `B-07.3`, and the **Depends on** column is authoritative, exactly as `../BACKLOG.md`'s own preamble says.

#### B-17.1 — Access: schema `access`, the permission catalogue and the per-tenant seeder

ADR-0010 rules 1–4, ADR-0029 §1, §5; ADR-0027 §1 (context constructor), ADR-0028 (migration order).

1. `Aurora.Platform.Access.Contracts` and `Aurora.Platform.Access` exist. `Permission` is a value object over a `module.resource.action` string with a validating constructor (a malformed name cannot be constructed); constants are `static readonly Permission` fields grouped per module, each carrying the description the role editor will render.
2. Schema `access` in the tenant database: `access.permission` (the catalogue), `access.role` (with an `is_system` flag — system roles are not editable, ADR-0010 rule 3), `access.role_permission`, `access.role_assignment(user_id, role_id, company_id NULL)`. `AccessDbContext` sets `HasDefaultSchema("access")` with its migrations history table in `access`, and has exactly one `internal` constructor taking a `TenantAccess` (ADR-0027 §1); it is never registered in DI.
3. `role_assignment.company_id` is a `CompanyId` with **no foreign key** to `organization.company` or to any other module's schema (`modules.md` §1.1), and a test asserts the `access` model maps no entity to any other schema (fitness rule M3).
4. `access` joins the per-tenant migration order as `platform` → `audit` → `access` → module schemas → `pkg_*`, extending ADR-0028's order. The test reads the order actually executed against a migrated tenant database; asserting a list constant does not satisfy this.
5. `IAccessSeeder.SeedAsync(TenantAccess, DbContext, CancellationToken)` is idempotent on natural keys: it upserts every code-declared permission into `access.permission`, seeds the built-in roles, and grants the built-in administrator role **every permission in the catalogue after the upsert** — so a permission declared after a tenant was provisioned reaches that tenant's administrator without a wildcard and without an `isAdministrator` branch (ADR-0029 §5). Tests: running it twice leaves every row count identical, and a permission added between two runs reaches the administrator role on the second.
6. The catalogue assertion **reports a count** — how many declared permission constants were checked against the seeded catalogue — and fails below a floor, so a run that declared nothing cannot report success.

*Size flag for the orchestrator's pre-dispatch check: projects, four entities, a migration, the seeder and its tests together sit near the ~400-line guideline. If it does not fit, split criterion 5 and 6 (the seeder) from criteria 1–4 (the schema).*

#### B-17.2 — Access: permission evaluation, fail closed

ADR-0029 §5, §6; ADR-0010 rules 2, 4, 6, 7; ADR-0012 §3.

1. `IPermissionEvaluator.EvaluateAsync(ClaimsPrincipal, TenantScope, Permission, CompanyId?, ct)` returns `PermissionDecision` with exactly two constructible outcomes — `Granted(CompanyScope)` and `Denied(Reason)`. No nullable return, no third state, and `CompanyScope.Of` **throws on an empty collection** so an empty filter can never become "no filter".
2. The `null` company scope of ADR-0010 rule 4 resolves per ADR-0029 §5, with one test each for: target matched by an explicit assignment; target matched only through a `null`-scoped assignment; target matched by neither (`Denied`); no target plus a `null`-scoped assignment (`Granted(AllCompaniesInTenant)`); no target plus explicit assignments (`Granted(Of(ids))`); assignments resolving to zero ids (`Denied`).
3. **No hidden superuser.** A test declares a permission, seeds it into the catalogue, grants it to no role, and asserts the **built-in administrator is denied**. A second assertion proves no role name literal appears in the evaluator (ADR-0010 rule 2 applies to the evaluator first of all).
4. The permission set is cached 60 s per `(tenant, user)` under a key produced by `TenantCacheKey.For(scope, …)` (ADR-0012 rule 1). Three tests: revoking through the real role-admin path denies on the **next** call with no time advanced; a revocation written directly to the database plus `FakeTimeProvider` +61 s denies; the same direct revocation at +59 s **still grants** — the third is what makes the 60 s claim falsifiable.
5. **No decision is not a denial.** With the cache empty and the store faulted by a hook inside the evaluator's own data path (not by stopping a container), `EvaluateAsync` throws `PermissionEvaluationUnavailableException`; it never returns `Denied`, never serves an expired cache entry and never falls back to last-known-good. A counter increments.
6. Evaluation reads no ambient state: a two-tenant test gives one user an assignment in tenant A only and asserts the same permission is denied when evaluated with tenant B's `TenantScope`, with the access context obtained through `ITenantDbContextFactory<AccessDbContext>` from the passed scope.

#### B-17.3 — Access: `[RequiresPermission]`, the enforcement pipeline and fitness rule S1

ADR-0010 rules 5, 10; ADR-0029 §5, §7, §8; ADR-0013 rule 3.

1. `[RequiresPermission("…")]` on application-service request types, evaluated by a pipeline behaviour against the resolved `TenantScope` and principal **before the handler and before validation** — a test whose request fails both authorization and validation asserts the authorization outcome is returned, so an unauthorized caller cannot probe through validation messages.
2. A request type reaching the pipeline **with no declaration** is refused at runtime, not passed: the behaviour throws and the response names no permission. The build gate (criterion 3) and this runtime gate fail in different ways on purpose.
3. Fitness rule **S1** extended: every application-service request type carries a declaration, the rule **reports the number of types asserted** and fails below a floor, and a deliberately-undeclared fixture proves the rule fails when it should.
4. Surface mapping, one test each with a distinct stable `type` URI (ADR-0013 rule 3): `Denied` → `403` naming the missing permission (ADR-0010 rule 10); anonymous → `401`; no-decision (undeclared request, no `TenantScope`) → `500` naming no permission; store unavailable → `503` with `Retry-After`.
5. A denial raised by the pipeline writes exactly **one** tenant-side `audit.audit_event` (`access.permission.denied`, with permission, request type, actor, correlation) through `IAuditWriter` in the caller's transaction (B-16.2); a bare `IPermissionEvaluator` probe used to hide a UI control writes **none**. Both proven by counting rows, not by observing a log.
6. One implementation covers both paths: the same guarded command invoked directly through its application service and through a minimal API endpoint produces identical outcomes (ADR-0010 rule 5 — "nowhere for the three to diverge" is a test, not a hope).

#### B-18.1 — Identity: catalog identity schema and the platform authentication trail

ADR-0029 §2, §7; ADR-0007 §9.2, §9.3; ADR-0028 §2.

1. One additive EF migration on `CatalogDbContext` (expand-only, no destructive step — B-09's categories) adds `catalog.identity_credential` (password hash, security stamp, concurrency stamp, lockout end, access-failed count), `catalog.identity_invitation` (tenant, user, token hash, expires_at, redeemed_at) and `catalog.authentication_event` (ADR-0029 §7's columns), and points `catalog.identity_user.credential_ref` at `identity_credential`.
2. `catalog.authentication_event` is append-only by all three mechanisms of ADR-0028 §2 applied **per table** — the `catalog` schema also holds mutable tables and cannot take a schema-wide policy — probed as `aurora_app` **and** as the owner role with `has_table_privilege` plus an executed `UPDATE` and an executed `DELETE` that both fail.
3. `attempted_email_hash` is SHA-256 of the normalized email and the address itself is never stored: a test records an attempt for an unknown address and asserts no column of the resulting row contains it in any form.
4. `IAuthenticationEventSink.WriteAsync(…)` writes exactly one row per call, and a forced write failure **surfaces** rather than being swallowed — ADR-0029 §7's "a sign-in that cannot be recorded does not happen" is enforced here and consumed in B-18.5.
5. `AuroraClaimTypes` (with `tid` and `tkey`) lives in `Aurora.Platform.Identity.Contracts` as the single definition referenced by both the mint (B-18.5) and the cross-check (B-18.4).
6. An integration test against a real PostgreSQL container applies the migration to a catalog created by B-05 and re-runs it with no effect.

*This row owns the single catalog migration for all three tables. No other row may add a `CatalogDbContext` migration concurrently — the EF migration chain is the file conflict.*

#### B-18.2 — Identity: ASP.NET Core Identity stores, hashing, lockout, Tenant Membership query

ADR-0009 rule 1; ADR-0029 §2; ADR-0012 §3 as extended.

1. `IUserStore`, `IUserPasswordStore`, `IUserEmailStore`, `IUserSecurityStampStore` and `IUserLockoutStore` over `CatalogDbContext`, wired with `AddIdentityCore<…>()`; hashing is the framework's `PasswordHasher<…>`, and a test asserts no type in the solution implements `IPasswordHasher<>` (ADR-0009: no custom password hashing, ever).
2. `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is **not** referenced and `IdentityDbContext` is not used (ADR-0029 §2) — asserted over the project's references, so a later `dotnet add package` under deadline fails the build rather than quietly reshaping the schema.
3. Email is normalized in exactly one place and is globally unique: two users differing only by case or surrounding whitespace cannot both be created.
4. Lockout is enforced through the framework's own sign-in path, driven by real failed attempts in the test rather than by setting the flag directly.
5. `ITenantMembership` answers "is this user an active member of this tenant" from `catalog.user_tenant_membership` joined to `catalog.tenant.state`, cached 60 s under `c:member:{userId}` (ADR-0012 §3 as extended by ADR-0029 §4) and invalidated on membership change — proven by revoking a membership and asserting the next call does not serve the cached row.
6. A user holding memberships in two tenants is a normal result, covered by a test, so SPEC-001 BR-6's external-accountant case is not designed out before it is scheduled.

#### B-18.3 — Identity: administrator invitation — issue and redeem

SPEC-001 BR-3, BR-4; ADR-0029 §7, §9. **Consumed by `B-07.3` (saga step 6).**

1. `IAdministratorInvitation.IssueAsync(TenantId, UserId, ct)` is idempotent per `(tenant, user)`: a saga replay returns the existing unredeemed invitation and creates no second row.
2. The token comes from `RandomNumberGenerator`, is at least 256 bits, is returned to the caller exactly once, and is stored only as a SHA-256 hash — a test asserts the stored value never equals the issued token.
3. Redemption is single-use and time-boxed against `TimeProvider`: redeem-twice, redeem-after-expiry and redeem-with-a-wrong-token are each refused with the same generic outcome and each recorded in `catalog.authentication_event`.
4. Redemption sets the password through `UserManager` and marks the invitation redeemed **in one transaction**; a forced failure of either leaves neither applied.
5. Aurora staff never learn the credential (SPEC-001 BR-3): the token is never written to a log, a metric or an audit row. The test captures log output across a full issue-and-redeem cycle and fails if the token value appears anywhere in it.
6. The redemption endpoint is on fitness rule S2's reviewed `[AllowAnonymous]` allow-list with its justification recorded in the allow-list file, not in a comment.

#### B-18.4 — Web: tenant resolution pipeline and the `tid` cross-check

ADR-0007 §3.2, §3.3; ADR-0029 §4. **This row is the tenancy control, not plumbing.**

1. Middleware resolves the tenant by ADR-0007 §3.2 strategies 1 and 2 **only** — host header against `catalog.tenant_host`, then the `/t/{tenantKey}` path segment — through the existing catalog read path. `IHttpContextAccessor` appears here and in no other assembly (fitness rule T4).
2. An authenticated principal's `tid` is compared with the resolved tenant **after** resolution and **before** `ITenantScopeFactory.OpenAsync`. A mismatch, **or an absent `tid`**, throws `TenantClaimMismatchException` and returns `403` Problem Details (`…/problems/tenant-mismatch`) naming neither tenant and not saying which side was wrong.
3. **Counted zero.** The mismatch test asserts, through an instrumented resolver/scope factory, that the other tenant's database was resolved or connected exactly **0** times. A status-code assertion alone does not satisfy this criterion.
4. `TenantClaimMismatchException` is **not** `TenantRoutingViolationException`: a mismatch must not mark a tenant `SchemaBlocked`, or a stale cookie becomes a denial-of-service against a tenant. The test reads `catalog.tenant.state` before and after and asserts it is unchanged.
5. `tid` is a constraint, never a source: an authenticated request to a host that maps to no tenant returns `404` without consulting the claim; an anonymous request to a valid tenant host resolves the tenant and opens no `TenantScope`.
6. Every refusal writes one `catalog.authentication_event` row and one `Warning` log carrying tenant and correlation ids and no personal data (ADR-0016), and nothing at all to any tenant's `audit.audit_event`.

#### B-18.5 — Identity: sign-in, sign-out, cookie configuration and the `tid` mint

ADR-0009 rules 1–4; ADR-0029 §3, §4, §6, §7, §8.

1. `POST /sign-in` and `POST /sign-out` over real HTTP requests (ADR-0009 rule 2), antiforgery-protected, with `/sign-in` on fitness rule S2's reviewed `[AllowAnonymous]` allow-list.
2. The cookie is `__Host-aurora.auth`: `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, **no `Domain` attribute**, sliding 8 h and absolute 12 h — asserted by parsing the emitted `Set-Cookie` header, not by reading back the options object.
3. `tid` is minted only after credentials verify **and** the Tenant Membership for the host-resolved tenant is `Active` **and** `catalog.tenant.state` is `Active`; its value is the host-resolved `TenantId` and never anything the client supplied. One refusal test per condition.
4. **The replay test.** A cookie minted at tenant A's host is replayed verbatim at tenant B's host in a harness where both hosts **share one data-protection key ring** — and the harness asserts the ring is genuinely shared, because with separate rings the cookie merely fails to decrypt and the test would pass for the wrong reason. Assert `403` and a counted **0** connections to tenant B. A second test replays the same cookie against `/t/{b}/…` on a **single** host — the case the `__Host-` prefix does not cover, and the one that proves the check rather than the browser.
5. Successful sign-in and sign-out each write one tenant-side `audit.audit_event` through `IAuditWriter` (B-16.2); every failed sign-in writes `catalog.authentication_event` and **nothing** tenant-side. Each case counts rows in **both** stores. A forced failure of the authentication-event write fails the sign-in (ADR-0029 §7).
6. Fitness rules **S6** (the minted claim set equals a committed approved-claims file, contains no permission or role claim, and the test reports how many claim types it asserted) and **S7** (`AuroraClaimTypes.TenantId` is referenced only by this row's mint, B-18.4's cross-check and their test assemblies), each with a deliberately-violating fixture.

#### B-18.6 — Blazor: circuit tenant pinning and revalidation

ADR-0005 rule 2; ADR-0007 §3.3; ADR-0009 rule 3; ADR-0029 §4 checkpoints 2 and 3.

1. A `CircuitHandler` pins the tenant resolved by the request that created the circuit and re-checks `tid` against it at creation **and** on every reconnect of a persisted circuit (ADR-0005's .NET 10 persisted circuit state). A mismatch aborts the circuit; it never downgrades it to anonymous-but-connected.
2. `RevalidatingServerAuthenticationStateProvider` with a 30-minute interval (ADR-0009 rule 3) checks, on every tick: security stamp still current, Tenant Membership still `Active`, `catalog.tenant.state` still `Active`, and `tid` still equal to the pinned tenant.
3. Revocation mid-session: with the circuit live, revoke the Tenant Membership through the real revocation path, advance `FakeTimeProvider` past the interval, and assert the circuit's principal is invalidated. The fault is injected inside the system under test — no `sleep` racing the revalidator.
4. The same scenario at 29 minutes asserts the principal is **still** valid, so the interval is a claim that can fail rather than one that cannot.
5. The pinned `TenantId` is added to the assertions behind fitness rules T5/T6 (nothing tenant-scoped captured in a singleton or in a serialized payload), with a deliberately-violating fixture.
6. A component rendered after the creating HTTP request has completed still sees the pinned tenant, obtained without `IHttpContextAccessor` — the ADR-0007 §3.3 failure mode, reproduced as a passing test rather than described in prose.

---

### 6.3 Dependency edges that change (v5)

These override the **Depends on** column in §6, §6.1 and `../BACKLOG.md` for the rows named. Reasons are in ADR-0029.

| Row | Was | Is | Why |
|---|---|---|---|
| **B-07.3** | B-07.2 | **B-07.2, B-17.1, B-18.3** | Saga step 6 seeds roles, the permission catalogue and the administrator Role Assignment into schema `access`, which no row created, using a seeder no row wrote (B-17.1); and SPEC-001 BR-3's "working sign-in credential path for the first administrator" is the invitation issued in B-18.3. Calls `IAccessSeeder.SeedAsync` and `IAdministratorInvitation.IssueAsync`; it does not re-implement either |
| **B-07.4** | B-07.3, B-13.2, B-16.2, B-06.3 | **+ B-18.5** | SPEC-001 AC-5's "*when* U signs in for the first time" needs a sign-in. B-07.4 exercises AC-5 at the HTTP level with a minimal two-tenant setup; BR-6's circuit-pinning half is proven in B-18.6 and the exhaustive isolation proof stays B-10's. B-07.4 does **not** gain B-17.3: BR-1's "authorized caller" is an operator, and ADR-0010 rule 8 puts operator access outside the tenant permission model — see the follow-up named in ADR-0029 §9 |
| **B-15.1** | B-06.3, B-10, B-13.2, B-14, B-16.3 | **+ B-17.3** | SPEC-002 BR-3/AC-2's permission-gated create and BR-4's scoped list are the enforcement pipeline plus `CompanyScope`; without B-17.3 the row has nothing to declare a permission to |
| **B-15.2** | B-15.1 | **+ B-18.5** | AC-1/AC-2/AC-9/AC-10 all begin with a signed-in caller |
| **B-15.3** | B-15.2, DESIGN-001 | **+ B-18.6** | The UI path runs inside a circuit; the pinned tenant and the revalidating provider are what make it the same authorization story as the API path |
| **B-04** | B-01, B-03 | unchanged | But note: S1's count, S6 and S7 are **extensions** of B-04's suite shipped by B-17.3 and B-18.5. B-04 must land before either; it already will |
| **B-10** | B-07.4, B-06.3 | unchanged | It acquires the identity rows transitively through B-07.4 and needs nothing named directly |

**What this does to the graph.** `B-18.1` depends on **B-05 alone** — it is the only new row with no dependency on the tenancy spine, and it opens the chain `B-18.1 → B-18.2 → B-18.3` that `B-07.3` now waits for. **Dispatch `B-18.1` as soon as `B-05` merges**: run cold, that three-row chain is the new critical path into `B-07.3`; run in parallel with `B-06.2`/`B-06.3`/`B-07.1`/`B-07.2`, it costs `B-07.3` nothing. `B-17.1` branches from `B-07.2` alongside `B-16.1`, `B-13.1` and `B-08.1`, so it joins the existing fan-out rather than lengthening it. `B-17.2` and `B-18.4` both open on `B-06.3` and are parallel with each other. Only **two** new rows touch the audit chain — `B-18.5` and `B-17.3`, both on `B-16.2` — because everything else in the set writes platform-side to `catalog.authentication_event` and never into a tenant's hash-chained log (ADR-0029 §7); that is what keeps seven of the nine rows free of the B-16 spine.
