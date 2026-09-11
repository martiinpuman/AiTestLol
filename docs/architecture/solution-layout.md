# Solution layout and the `scripts/verify.sh` spec

Status: accepted **v3** · Author: architect · Date: 2026-09-11
Companion: `modules.md`, `testing-strategy.md`, `dependencies.md`, `../decisions/ADR-0007-...`, `../decisions/ADR-0008-...`

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
