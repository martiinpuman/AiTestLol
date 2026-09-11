# Solution layout and the `scripts/verify.sh` spec

Status: accepted **v6** · Author: architect · Date: 2026-09-11
Companion: `modules.md`, `testing-strategy.md`, `dependencies.md`, `../decisions/ADR-0007-...`, `../decisions/ADR-0008-...`

**Changes in v6** (2026-09-11, security review of ADR-0029 — `../reviews/ADR-0029.md`, 2 blockers and 9 high): **§6.2 and §6.3 are replaced.** The nine v5 rows become thirteen; six of the nine would not have been dispatched as written. The decisions are in **ADR-0029 Amendment 1**; §6.2 opens with a v5→v6 mapping table.

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

### 6.2 The identity and authorization rows (v6, 2026-09-11)

`ADR-0029` closes the gap the project-manager flagged twice: **nothing in `B-01` … `B-15` builds sign-in or permission evaluation**, while SPEC-001 AC-5, SPEC-001 BR-1, SPEC-002 BR-3/BR-4 and `B-15.2`'s endpoint authorization all presuppose both — and `B-07.3` seeds roles, the permission catalogue and the administrator Membership into an `access` schema **no row created**, for a reader that did not exist.

**v6 supersedes the v5 rows.** The security review of ADR-0029 (`../reviews/ADR-0029.md`, 2 blockers and 9 high) returned CHANGES_REQUESTED and would not have dispatched six of the nine v5 rows. ADR-0029 **Amendment 1** carries the decisions; the rows below carry the criteria. Read the ADR **including Amendment 1** before implementing any row — as everywhere in §6, the criteria are a floor and the ADR is the contract.

| v5 row | v6 | Why it moved |
|---|---|---|
| B-17.1 | **B-17.1 + B-17.2** | Split on the reviewer's and my own size flag; the wildcard grant (H-3) is isolated into the seeder row |
| B-17.2 / B-17.3 | **B-17.3 / B-17.4** | Renumbered by the split above |
| B-18.3 | **B-18.3 + B-18.4** | Issue and redemption are two subsystems once B-2's invite/join decision exists |
| B-18.4 | **B-18.5 + B-18.6** | Resolution and the cookie validation event are two subsystems once the cross-check moves into `OnValidatePrincipal` (H-4) |
| B-18.5 / B-18.6 | **B-18.7 / B-18.8** | Renumbered |
| — | **B-03.1** (new) | `CompanyScope` must exist in `Aurora.SharedKernel` before `B-06.3` can take it as a factory parameter (H-2) |

| Row | Title | Module | Tier | Depends on | Parallel-safe with |
|---|---|---|---|---|---|
| **B-03.1** | SharedKernel: `CompanyScope` and `ICompanyScoped` | SharedKernel (tier 0) | Full | B-03 | Everything — tier 0, no dependents until B-06.3 |
| **B-17.1** | Access: schema `access` and the model | Platform/Access | Full | B-07.2, **B-16.1** | B-13.1, B-08.1 — **not** B-16.1 (both assert the per-tenant migration order) |
| **B-17.2** | Access: permission catalogue, the approved administrator list, the per-tenant seeder | Platform/Access | Full | B-17.1 | B-18.\*, B-13.\*, B-08.\* |
| **B-17.3** | Access: `AccessSubject` and permission evaluation, fail closed | Platform/Access | Full | B-17.2, B-06.3, B-18.2 | B-18.5, B-18.6 |
| **B-17.4** | Access: `[RequiresPermission]`, the enforcement pipeline, fitness rule S1 | Platform/Access | Full | B-17.3, B-04, **B-16.1** | B-18.7, B-18.8 |
| **B-18.1** | Identity: catalog identity schema, the credential privilege boundary, the platform authentication trail | Platform/Identity | Full | **B-05** | Everything in the B-06/B-07 spine |
| **B-18.2** | Identity: stores, password and lockout policy, the `IdentitySnapshot` cache | Platform/Identity | Full | B-18.1 | B-06.\*, B-07.\*, B-16.\* |
| **B-18.3** | Identity: platform user resolution and invitation issue (invite vs join) | Platform/Identity | Full | B-18.2 | B-06.\*, B-07.1/.2, B-16.\* |
| **B-18.4** | Identity: invitation and join redemption | Platform/Identity + Aurora.Web | Full | B-18.3, B-18.5 | B-17.3, B-17.4 |
| **B-18.5** | Web: tenant resolution and the tenant-neutral endpoint allow-list | Aurora.Web | Full | B-06.3, B-18.1 | B-17.2, B-17.3, B-18.2, B-18.3 |
| **B-18.6** | Web: the cookie validation event — stamp validation, `tid` carry-over, tenant cross-check | Aurora.Web / Platform/Identity | Full | B-18.2, B-18.5 | B-17.3 |
| **B-18.7** | Identity: sign-in, sign-out, the `tid` mint, rate limiting | Platform/Identity + Aurora.Web | Full | B-18.2, B-18.6, **B-16.1** | B-17.4 |
| **B-18.8** | Blazor: circuit pinning, reconnect identity check, revalidation | Aurora.Web | Full | B-18.7 | B-17.4 |

Every row is **Full** tier. The numbering does not imply dispatch order — `B-17.2` and `B-18.3` land **before** `B-07.3` — and the **Depends on** column is authoritative.

#### B-03.1 — SharedKernel: `CompanyScope` and `ICompanyScoped`

ADR-0029 A1.2 H-2; ADR-0010 rules 4, 6.

1. `CompanyScope` is a value object in `Aurora.SharedKernel` with exactly two constructible forms: `AllCompaniesInTenant`, and `Of(IEnumerable<CompanyId>)` which **throws on an empty or default-valued collection** — the empty set cannot be constructed, so it cannot decay to `WHERE 1=1`.
2. `ICompanyScoped` exposes `CompanyId CompanyId { get; }`. An aggregate that *is* a company implements it by returning its own id, so the rule is uniform and `Company` needs no special case.
3. `CompanyScope` is immutable, value-equal and safe to compare; `Of` deduplicates and orders so that two scopes over the same ids are equal.
4. Unit tests cover every construction path including the empty, duplicate, default-id and single-id cases; the assembly still references only the BCL (B-03's fitness rule must still pass).
5. The row raises the §5.3 stage-6 executed-test floor by the count it lands, as B-03 did.

*Small by design. It exists as its own row because `B-06.3` takes `CompanyScope` as a parameter and `B-03` is already merged.*

#### B-17.1 — Access: schema `access` and the model

ADR-0010 rules 1–4; ADR-0027 §1; ADR-0028 (migration order); ADR-0029 §1.

1. `Aurora.Platform.Access.Contracts` and `Aurora.Platform.Access` exist. `Permission` is a value object over a `module.resource.action` string with a validating constructor; a malformed name cannot be constructed, and the reserved prefixes `operator.` and `platform.` are refused (ADR-0029 A1.2 H-3: rule 8's capabilities do not live in the tenant catalogue).
2. Schema `access`: `access.permission`, `access.role` (with `is_system`), `access.role_permission`, `access.role_assignment(user_id, role_id, company_id NULL)`. `AccessDbContext` sets `HasDefaultSchema("access")` with its migrations history table in `access`, has exactly one `internal` constructor taking a `TenantAccess` (ADR-0027 §1), and is never registered in DI.
3. `role_assignment.company_id` is a `CompanyId` with **no foreign key** to `organization.company` or any other module's schema (`modules.md` §1.1); a test asserts the model maps no entity outside schema `access` (fitness rule M3).
4. `access` joins the per-tenant migration order, asserted **relatively** — `access` after `platform` always, and after `audit` when `audit` is present — read from the order actually executed against a migrated tenant database. A relative assertion is required because B-16.1 asserts the same list from another branch; two absolute assertions on one ordered list is a merge that silently drops a schema.
5. An integration test against a real PostgreSQL container migrates a provisioned tenant and re-runs the migration with no effect.
6. `access` tables are `UPDATE`/`DELETE`-able by `aurora_app` (they are mutable business configuration, unlike `audit`) — stated and probed, so nobody copies B-16.1's append-only policy here by pattern-matching.

#### B-17.2 — Access: permission catalogue, the approved administrator list, the per-tenant seeder

ADR-0010 rules 1–3; ADR-0029 §5 and A1.2 H-3. **Consumed by `B-07.3` (saga step 6).**

1. Permission constants are `static readonly Permission` fields grouped per module, each carrying the description the role editor will render.
2. Two committed files: `administrator-permissions.approved.txt` and `not-administrator.approved.txt` (the latter with a reason per entry). A fitness rule asserts **every** declared constant appears in **exactly one** of them, and **reports both counts** — so a permission can be neither silently granted to every administrator in the fleet nor silently unreachable.
3. `IAccessSeeder.SeedAsync(TenantAccess, DbContext, CancellationToken)` is idempotent on natural keys: it upserts every declared permission into `access.permission`, seeds the non-editable built-in roles, and grants the built-in administrator role **exactly the approved list** — never "everything in the catalogue" (that was a wildcard, and it made criterion 3 of B-17.3 unpassable).
4. Running the seeder twice leaves every row count identical; a permission added to the approved list between two runs reaches the administrator role on the second, which is what lets SPEC-002's `organization.company.manage` reach an administrator provisioned before the Organization module existed.
5. A permission in `not-administrator.approved.txt` is seeded into the catalogue and granted to **no** role — this is the row that makes B-17.3 criterion 3's no-superuser test constructible.
6. The catalogue assertion reports how many declared constants it checked against the seeded catalogue and fails below a floor, so a run that declared nothing cannot report success.

#### B-17.3 — Access: `AccessSubject` and permission evaluation, fail closed

ADR-0029 §5, §6 and A1.2 H-1, H-2, A1.3 M-3; ADR-0010 rules 2, 4, 6, 7; ADR-0012 §3.

1. `AccessSubject` is sealed with an internal constructor and exactly two factories. `FromPrincipal(ClaimsPrincipal, TenantScope)` **performs the tenancy comparison as the price of construction** — `tid` present, parsable and equal to `scope.TenantId`, and the security stamp, user status and Tenant Membership valid against the 60 s `IdentitySnapshot` — and `ForSystemJob(TenantScope, SystemPrincipalId, IReadOnlySet<Permission>)` carries no `tid` at all (ADR-0010 rule 9). `EvaluateAsync` takes an `AccessSubject`, never a `ClaimsPrincipal`.
2. Three distinct failure outcomes, each with a test that constructs the condition **directly, not through HTTP**: `tid` absent/unparsable/mismatched → **no decision**, throws, `500`, counter `authz_tenant_mismatch_total`; stale stamp / disabled user / revoked membership → **`401`**; empty catalogue or `access` schema behind the skew gate → **no decision**, `503`. A single absent constant in a populated catalogue stays `Denied` + `Error` log.
3. **No hidden superuser.** Using B-17.2's `not-administrator` permission — seeded into the catalogue, granted to no role — assert the **built-in administrator is denied**. A second assertion proves no role-name literal appears in the evaluator (ADR-0010 rule 2 applies to the evaluator first of all).
4. `PermissionDecision` has exactly two constructible outcomes, `Granted(CompanyScope)` and `Denied(Reason)`; no nullable return, no third state. The `null` company scope resolves per ADR-0010 rule 4 with one test each for: target matched by an explicit assignment; target matched only through a `null`-scoped assignment; target matched by neither (`Denied`); no target plus a `null`-scoped assignment (`AllCompaniesInTenant`); no target plus explicit assignments (`Of(ids)`); assignments resolving to zero ids (`Denied`).
5. The permission set is cached 60 s per `(tenant, user)` under `TenantCacheKey.For(scope, …)` (ADR-0012 rule 1), with three tests: revoking through the real role-admin path denies on the **next** call with no time advanced; a direct database revocation plus `FakeTimeProvider` +61 s denies; the same at **+59 s still grants** — the third is what makes the 60 s claim falsifiable. With the cache empty and the store faulted by a hook **inside the evaluator's own data path** (not by stopping a container), it throws rather than returning `Denied`, and never serves an expired entry.
6. A two-tenant test gives one user an assignment in tenant A only and asserts `FromPrincipal` **refuses to construct** a subject for that principal with tenant B's scope — the escalation is unreachable, not merely denied.

#### B-17.4 — Access: `[RequiresPermission]`, the enforcement pipeline, fitness rule S1

ADR-0010 rules 5, 10; ADR-0029 §5, §8, A1.3 M-6, A1.4; ADR-0013 rule 3; ADR-0028 §4.

1. `[RequiresPermission("…")]` on application-service request types, evaluated by a pipeline behaviour **before the handler and before validation** — a request failing both authorization and validation returns the authorization outcome, so an unauthorized caller cannot probe through validation messages.
2. A request type reaching the pipeline **with no declaration** is refused at runtime, not passed: the behaviour throws and the response names no permission. The build gate (criterion 3) and this runtime gate fail in different ways on purpose.
3. Fitness rule **S1** extended: every application-service request type carries a declaration, the rule **reports the number of types asserted** and fails below a floor, and a deliberately-undeclared fixture proves it fails when it should.
4. Surface mapping, one test each with a distinct stable `type` URI (ADR-0013 rule 3): `Denied` → `403` naming the missing permission (ADR-0010 rule 10); anonymous → `401`; invalid session → `401`; no-decision → `500` naming no permission; store unavailable or tenant unseeded → `503` with `Retry-After`.
5. A denial writes its audit row in a transaction **the pipeline opens itself** on `AccessDbContext` (via `ITenantDbContextFactory` with the current scope) — there is no caller transaction, because the handler never ran, and `IAuditWriter` requires one (ADR-0028 §4). At most **one** row per `(actor, permission, request type)` per 5-minute window, deduplicated in the tenant cache, with the exact count carried by the metric. Tests: N identical denials in the window produce exactly one row; a denial after the window produces a second; a forced failure of the audit write still returns the denial and is logged at `Error` — a caller is never granted because auditing failed. A bare `IPermissionEvaluator` probe used to hide a UI control writes **none**.
6. One implementation covers both paths: the same guarded command invoked directly through its application service and through a minimal API endpoint produces identical outcomes (ADR-0010 rule 5).

#### B-18.1 — Identity: catalog identity schema, the credential privilege boundary, the platform authentication trail

ADR-0029 §2, §7 and A1.3 M-4, M-7, A1.2 H-8; ADR-0007 §9.2, §9.3; ADR-0028 §2, §3.

1. One additive EF migration on `CatalogDbContext` (expand-only — B-09's categories) adds `catalog.identity_credential`, `catalog.identity_invitation` (tenant, user, token hash, kind `Invite|Join`, expires_at, redeemed_at) and `catalog.authentication_event`, and points `catalog.identity_user.credential_ref` at `identity_credential`.
2. **Credential privilege boundary:** a new `aurora_identity` login owns access to `catalog.identity_credential`; `REVOKE ALL … FROM aurora_app`, probed by connecting **as `aurora_app`** and asserting `SELECT` fails — the same probe shape as ADR-0028 §2, because B-05 finding M-1 already showed this project shipping an over-broad catalog grant once. The identity store's data source uses the `aurora_identity` credential.
3. `catalog.authentication_event` is **monthly RANGE-partitioned** on `occurred_at` with a `DEFAULT` partition (the shape of `audit.audit_event`, ADR-0028 §3) and append-only by all three mechanisms of ADR-0028 §2 applied **per table** — the `catalog` schema also holds mutable tables — probed as `aurora_app` **and** as the owner with `has_table_privilege` plus an executed `UPDATE` and `DELETE` that both fail. A unique key on `(source_hash, email_hash, window_start, event_type)` makes the bounded-write path of B-18.7 an `ON CONFLICT DO NOTHING` insert rather than a counter `UPDATE`, which append-only would forbid.
4. `attempted_email_hash` is **HMAC-SHA-256 with a key from the secret store** (ADR-0011), not a bare SHA-256: an unsalted hash over an enumerable address space is reversible, so it would have put recoverable addresses of non-users in the catalog. A test asserts the row contains the address in no form and that two different keys produce different digests for one address.
5. `IAuthenticationEventRetention.PruneAsync(cutoff)` detaches and drops whole partitions older than 180 days — retention on an append-only table is a partition operation or it is nothing. Scheduling is a named follow-up (no scheduler exists in bootstrap; ADR-0028 §3's partition pre-creation job has the same gap).
6. `IAuthenticationEventSink.WriteAsync(…)` writes exactly one row per call and a forced write failure **surfaces** rather than being swallowed; `AuroraClaimTypes` (`tid`, `tkey`) lives in `Aurora.Platform.Identity.Contracts` as the single definition used by the mint, the carry-over and the cross-check.

*This row owns the single catalog migration for all three tables; no other row may add a `CatalogDbContext` migration concurrently.*

#### B-18.2 — Identity: stores, password and lockout policy, the `IdentitySnapshot` cache

ADR-0009 rule 1; ADR-0029 §2, A1.1 B-1, A1.3 M-5; ADR-0012 §3 as amended.

1. `IUserStore`, `IUserPasswordStore`, `IUserEmailStore`, `IUserSecurityStampStore` and `IUserLockoutStore` over `CatalogDbContext`, wired with `AddIdentityCore<…>()`; hashing is the framework's `PasswordHasher<…>` and a test asserts no type in the solution implements `IPasswordHasher<>`.
2. `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is **not** referenced and `IdentityDbContext` is not used, asserted over the project's references so a later `dotnet add package` under deadline fails the build rather than quietly reshaping the schema.
3. Configured and asserted by a test that reads the live options: `PasswordOptions` ≥ 12 characters with **no** composition rules (the framework default of 6 plus character classes fails ASVS L2); `LockoutOptions` 10 attempts, 15-minute lockout, enabled for new users. Lockout is driven by real failed attempts in the test, never by setting the flag.
4. **`IdentitySnapshot(userId, securityStamp, userStatus, IReadOnlyDictionary<TenantId, MembershipState>)`** is cached 60 s under `CatalogCacheKey.IdentitySnapshot(userId)` and invalidated on membership change, stamp rotation and status change. **The tenant is a parameter of the lookup, never of the key.** `CatalogCacheKey.For(kind, …)` is defined as the `c:`-prefix sibling of `TenantCacheKey.For`, its kinds are a committed enum, and ADR-0012 rule 1's fitness test is extended to accept exactly these two helpers and nothing else.
5. **The test that closes the blocker:** user U is an active member of tenant A and not of tenant B; ask `ITenantMembership` for `(U, A)`, then immediately for `(U, B)` **with no time advanced**; the second answer is `false`. A key that omits the tenant fails this and passes the old "revoke and re-read" test.
6. Email is normalized in exactly one place and is globally unique (two users differing only by case or surrounding whitespace cannot both exist); a user holding memberships in two tenants is a normal result covered by a test, so SPEC-001 BR-6's external-accountant case is not designed out.

#### B-18.3 — Identity: platform user resolution and invitation issue (invite vs join)

ADR-0029 A1.1 B-2; SPEC-001 BR-2, BR-3, BR-4; ADR-0010 rule 8. **Consumed by `B-07.3` (saga step 6).**

1. `IPlatformUserProvisioning.ResolveForTenantAdministratorAsync(email, tenantId)` is the **single owner** of find-or-create on `catalog.identity_user` and returns one of two outcomes: **`Invite`** — the identity has no credential **and** no Tenant Membership in any other tenant — or **`Join`** — an established identity, for which it creates the Tenant Membership only.
2. **A `Join` never produces a password-set token.** The criterion that fails today: provisioning a tenant naming an email that already has a credential produces no password-set capability of any kind, asserted over both the returned handle and the `catalog.identity_invitation` row.
3. Issue is idempotent per `(tenant, user)`: a saga replay returns the existing unredeemed invitation and creates no second row. The token comes from `RandomNumberGenerator`, is ≥ 256 bits, is stored only as a hash, and expires in 72 hours.
4. **The secret never returns to the caller.** `IssueAsync` returns an opaque `InvitationHandle` (id, expiry, outcome); the token leaves the process only through `IInvitationDelivery`. A test serialises the full provisioning response and asserts no substring equals the token.
5. `Aurora.Composition` registers an `IInvitationDelivery` that **throws** when no transport is configured, so provisioning fails loudly instead of handing the secret back; the test harness registers a capturing sink.
6. Aurora staff never learn the credential (SPEC-001 BR-3): the log-capture test across a full issue cycle fails if the token value appears in any log, metric or audit row. **Keep this test verbatim** — the reviewer named it as correct.

#### B-18.4 — Identity: invitation and join redemption

ADR-0029 A1.1 B-2 (3), A1.3 L-3; ADR-0029 §4 (tenant never from the request body).

1. The redeem endpoint lives on the **tenant's own host**, resolves its tenant by host or path, and refuses unless `invitation.tenant_id` equals the resolved tenant — never a tenant read from the form, query string or a cookie.
2. Redeeming an **`Invite`** sets the password through `UserManager`; redeeming a **`Join`** requires authenticating as that user with their existing credential and sets no password. Two tests, and a third asserting a `Join` token presented as an `Invite` is refused.
3. Redemption is single-use and time-boxed against `TimeProvider`: redeem-twice, redeem-after-expiry and redeem-with-a-wrong-token are each refused with the **same generic outcome** and each recorded in `catalog.authentication_event`.
4. Redemption and the "mark redeemed" write happen in one transaction; a forced failure of either leaves neither applied.
5. The token is never placed in a URL: it is POSTed into the redeem form, so it does not land in browser history, referrer headers or proxy logs.
6. The endpoint is on fitness rule S2's reviewed `[AllowAnonymous]` allow-list and on B-18.5's tenant-neutral list only if it genuinely needs to be, with the justification recorded in the file rather than in a comment.

#### B-18.5 — Web: tenant resolution and the tenant-neutral endpoint allow-list

ADR-0007 §3.2; ADR-0029 §4, A1.2 H-4, A1.3 M-1.

1. Resolution uses ADR-0007 §3.2 strategies 1 and 2 **only** — host header against `catalog.tenant_host`, then the `/t/{tenantKey}` path segment — through the existing catalog read path. `IHttpContextAccessor` appears here and in no other assembly (fitness rule T4).
2. **The tenant-existence oracle is closed:** an unknown tenant key and a known-but-not-mine tenant key produce responses that are **byte-identical apart from the correlation id** — same `404`, same Problem Details `type`. Asserted by comparing the two responses, not by checking each against an expected code.
3. A committed **tenant-neutral endpoint allow-list** (`/sign-in`, `/sign-out`, `/_blazor`, `/health*`, static assets, the deferred tenant picker) with a justification per entry, on the same footing as S2's file. A fitness rule fails the build for any endpoint that is neither tenant-resolved nor listed, and **reports the count of each**.
4. No `TenantScope` can be opened on a tenant-neutral endpoint — asserted, so an unchecked principal there can do nothing tenantful — and `/sign-out` is always reachable, so a user holding a cookie for a suspended or deleted tenant can sign out in-product rather than being told to clear cookies.
5. An anonymous request to a valid tenant host resolves the tenant and opens no `TenantScope`; `tid` is never consulted as a resolution source (it is a constraint, checked in B-18.6).
6. The **instrumented counting resolver** — the one that makes "zero connections to the other tenant" assertable — ships in `Aurora.TestKit` here, and `B-10` consumes it rather than re-implementing it.

#### B-18.6 — Web: the cookie validation event — stamp validation, `tid` carry-over, tenant cross-check

ADR-0029 §4, A1.2 H-4, H-6, A1.3 M-2, L-1, L-2; ADR-0009 rules 3, 4.

1. Everything runs inside `CookieAuthenticationOptions.Events.OnValidatePrincipal`, in this order: security-stamp validation against the 60 s `IdentitySnapshot`, then the tenant cross-check. It runs wherever the cookie is read, so it can be neither mis-ordered against `UseAuthentication` nor path-exempted by accident.
2. **Checkpoint 4 (`OnRefreshingPrincipal`) re-runs the mint preconditions** — Tenant Membership `Active` for the existing `tid`, tenant state `Active`, host-resolved tenant equal to the existing `tid` — and carries `tid`/`tkey` over unchanged if all pass, **rejecting** if any fails. Tests: the framework rebuilding the principal never drops the claim, and never carries it over when a precondition fails.
3. On failure: `RejectPrincipal()`, `ShouldRenew = false`, reason recorded in `HttpContext.Items`, **no `SignOutAsync`** (the cookie is not deleted). A middleware turns the reason into the Problem Details response. **Deliberately-violating fixture:** remove or mis-register that middleware and assert the replay still refuses — degraded to an anonymous `401`, never to a pass. That asymmetry is the criterion.
4. **Counted zero:** the mismatch test asserts, through B-18.5's instrumented resolver, that the other tenant's database was resolved or connected exactly **0** times. A status-code assertion alone does not satisfy this.
5. A mismatch leaves `catalog.tenant.state` unchanged — `TenantClaimMismatchException` is not `TenantRoutingViolationException`, or a stale cookie becomes a denial-of-service against a tenant — and the exception carries tenant ids in **structured properties only**, never in `Message`. The refusal response carries **no `Set-Cookie` header**, asserted by parsing it.
6. Fitness rule **S7 rewritten to what it inspects**: the constants `AuroraClaimTypes.TenantId` **and `TenantKey`**, the string literals `"tid"` and `"tkey"`, and every `ClaimsIdentity`/`ClaimsPrincipal` construction site, allow-listed to the mint, the carry-over and the cross-check plus their tests, with a deliberately-violating fixture that writes `new Claim("tid", …)` and must fail. The counter distinguishes "no `tid`" from "`tid` mismatch".

#### B-18.7 — Identity: sign-in, sign-out, the `tid` mint, rate limiting

ADR-0009 rules 1–4; ADR-0029 §3, §4, §7, §8, A1.1 B-1, A1.2 H-7, H-8, A1.3 L-4.

1. `POST /sign-in` and `POST /sign-out` over real HTTP requests, antiforgery-protected with the antiforgery token **regenerated at sign-in** alongside the auth cookie; `/sign-in` on S2's reviewed `[AllowAnonymous]` allow-list.
2. The cookie is `__Host-aurora.auth`: `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, **no `Domain`**, sliding 8 h and absolute 12 h — asserted by parsing the emitted `Set-Cookie` header, not by reading back the options object.
3. `tid` is minted only after credentials verify **and** the Tenant Membership for the host-resolved tenant is `Active` **and** `catalog.tenant.state` is `Active`; its value is the host-resolved `TenantId`. One refusal test per condition, **plus the blocker test**: sign in successfully at A, then attempt sign-in at B with correct credentials and no membership in B **inside the 60 s TTL** — refused, and **zero** rows in B's `audit.audit_event`.
4. **The replay test.** A cookie minted at tenant A's host is replayed verbatim at tenant B's host in a harness where both hosts **share one data-protection key ring**, and the harness asserts the ring is genuinely shared — with separate rings the cookie merely fails to decrypt and the test passes for the wrong reason. Assert the refusal and a counted **0** connections to tenant B. A second test replays against `/t/{b}/…` on a **single** host, the case the `__Host-` prefix does not cover.
5. **Rate limiting ships with sign-in, not after it:** fixed-window limits per source IP and per email hash on `/sign-in` and invitation redeem, rejecting **before** the credential check and **before** any catalog write; a rejected request writes at most one row per `(source, email-hash, window, event type)` via `ON CONFLICT DO NOTHING`. Test: N rejected attempts produce one row, and the rejection does not depend on the write succeeding.
6. Sign-out deletes the cookie, calls `UpdateSecurityStampAsync` and invalidates the `IdentitySnapshot`; a password change does the same. Successful sign-in and sign-out each write one tenant-side `audit.audit_event` through `IAuditWriter`; every failed sign-in writes `catalog.authentication_event` and **nothing** tenant-side, with row counts asserted in **both** stores; a forced failure of the authentication-event write fails the sign-in. Tests: after sign-out, another session's next request is refused within 60 s, and a password change invalidates other sessions.

#### B-18.8 — Blazor: circuit pinning, reconnect identity check, revalidation

ADR-0005 rule 2, rule 6; ADR-0007 §3.3; ADR-0009 rule 3; ADR-0029 §4 checkpoints 2–3, A1.2 H-7, H-9.

1. A `CircuitHandler` pins `(tid, sub, securityStamp)` at circuit creation and re-checks **all three** at creation and on every reconnect of a persisted circuit; a mismatch on any aborts the circuit rather than downgrading it. **A reconnect presenting a valid cookie for a different user of the same tenant is refused** — that is horizontal escalation inside a tenant and it passed the design as written.
2. The pinned tuple lives **server-side only, keyed by circuit id, never serialised to the client**; persisted circuit state is keyed to the authenticated subject. Deliberately-violating fixture: round-trip the pinned tenant through client state and assert the rule fails.
3. `RevalidatingServerAuthenticationStateProvider` at the 30-minute interval of ADR-0009 rule 3 checks security stamp, Tenant Membership, tenant state and `tid` against the pinned tenant. The row states in its README what each interval bounds: 30 minutes for "still the right person on an idle circuit", 60 seconds for anything that opens a scope or runs a guarded command.
4. The failure path **forces a navigation to the sign-in page** rather than `ForceSignOut`'s stock anonymous-but-connected render — the criterion asserts the previously rendered tenant data is gone, not merely that the principal is anonymous. That is the opposite of stock behaviour and is the reason this criterion exists.
5. Revocation mid-session: with the circuit live, revoke the Tenant Membership through the real revocation path, advance `FakeTimeProvider` past the interval, assert invalidation — and the same scenario at 29 minutes asserts the principal is **still** valid, so the interval is a claim that can fail. The fault is injected inside the system under test; no `sleep`.
6. The pinned tuple is added to the assertions behind fitness rules T5/T6, and a component rendered after the creating HTTP request has completed still sees the pinned tenant without `IHttpContextAccessor`.

---

### 6.3 Dependency edges that change (v6)

These override the **Depends on** column in §6, §6.1 and `../BACKLOG.md` for the rows named. Reasons are in ADR-0029 and its Amendment 1.

| Row | Was | Is | Why |
|---|---|---|---|
| **B-06.3** | B-06.2 | **B-06.2, B-03.1** *(+ scope addition)* | `ITenantDbContextFactory<TContext>` gains `CreateAsync(TenantScope, CompanyScope, ct)`, and the single-argument overload **throws** when the context's EF model contains an `ICompanyScoped` entity type (ADR-0029 A1.2 H-2). Model metadata is the mechanism that makes the company boundary structural rather than a convention. **Flag for the orchestrator's pre-dispatch size check** — this stacks on an already-large row |
| **B-08.3** | B-06.3 | B-06.3 *(+ scope addition, retitle)* | Retitle: *Tenancy: schema-version **and tenant-state** gates at scope open*. `OpenAsync` refuses a tenant whose `catalog.tenant.state` is not in the allow-list for the requested `TenantAccessReason` (ADR-0029 A1.2 H-5's table). One choke point bounds tenant-state staleness to 60 s for every path including jobs and the outbox, instead of five separate checks and an unbounded HTTP path |
| **B-07.3** | B-07.2 | **B-07.2, B-17.2, B-18.3** | Step 6 seeds into schema `access` with a seeder no row wrote (B-17.2 — note this is the **seeder** row, not B-17.1's schema row), and SPEC-001 BR-3's credential path is B-18.3's invitation. It calls `IAccessSeeder.SeedAsync` and `IPlatformUserProvisioning`/`IssueAsync`; it re-implements neither. It does **not** need redemption (B-18.4) |
| **B-07.4** | B-07.3, B-13.2, B-16.2, B-06.3 | **+ B-18.7** | SPEC-001 AC-5's "*when* U signs in" needs a sign-in. B-07.4 exercises AC-5 at the HTTP level; BR-6's circuit half is B-18.8's and the exhaustive isolation proof stays B-10's. It does **not** gain B-17.4: BR-1's "authorized caller" is an operator, and ADR-0010 rule 8 puts operator capability outside the tenant permission model — the follow-up named in ADR-0029 A1.4 |
| **B-10** | B-07.4, B-06.3 | unchanged *(+ scope note)* | The instrumented counting resolver ships in `Aurora.TestKit` from **B-18.5**; B-10 consumes it and must not re-implement it |
| **B-15.1** | B-06.3, B-10, B-13.2, B-14, B-16.3 | **+ B-17.4, + B-08.3** | SPEC-002 BR-3/BR-4 are the enforcement pipeline plus `CompanyScope`; and the first module that reads tenant data should have the tenant-state gate in place, not acquire it later |
| **B-15.2** | B-15.1 | **+ B-18.7** | AC-1/AC-2/AC-9/AC-10 all begin with a signed-in caller |
| **B-15.3** | B-15.2, DESIGN-001 | **+ B-18.8** | The UI path runs inside a circuit |
| **B-16.1** | B-07.2 | unchanged | But it is now depended on by **B-17.1, B-17.4 and B-18.7**, and **B-17.1 is sequenced after it** rather than parallel with it: both touch the per-tenant migration order, and two branches asserting one ordered list is a merge that silently drops a schema. B-17.1 criterion 4 is also written as a *relative* assertion for the same reason |

**What this does to the graph.** `B-18.1` still depends on **B-05 alone** and should be dispatched the moment B-05 merges: the chain `B-18.1 → B-18.2 → B-18.3` is what `B-07.3` now waits for, and run alongside `B-06.2`/`B-06.3`/`B-07.1`/`B-07.2` it costs `B-07.3` nothing. `B-03.1` is unblocked now and must land before `B-06.3`. The Access chain is `B-07.2 → B-16.1 → B-17.1 → B-17.2` into `B-07.3`, with `B-17.3`/`B-17.4` joining later at `B-15.1`. Only **three** rows touch the audit chain — `B-17.4`, `B-18.7` and (through B-16.1) `B-17.1` — and they depend on **B-16.1**, the store and `IAuditWriter`, **not B-16.2**, the `[Auditable]` interceptor, which none of them uses.
