# Solution layout and the `scripts/verify.sh` spec

Status: accepted **v3** · Author: architect · Date: 2026-09-11
Companion: `modules.md`, `testing-strategy.md`, `dependencies.md`, `../decisions/ADR-0007-...`, `../decisions/ADR-0008-...`

**Changes in v5** (2026-09-11, answering three review escalations): §6.4 records the acceptance criteria that change because **ADR-0028 Amendment 1** replaced §2 of that ADR, and two new rows that follow from **ADR-0030** and **ADR-0031**. §6 and §6.1 are unchanged except where §6.4 says it overrides them.

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
  Aurora.Countries.TestKit/                     # CountryPackageContractTests<TPackage> (ADR-0031 §5, §6.4 item 3)
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

> **The append-only half of the B-16.1 row above is overridden by §6.4 item 1** (ADR-0028 Amendment 1). The statement it quotes — `ALTER DEFAULT PRIVILEGES … REVOKE UPDATE, DELETE` — is a no-op on PostgreSQL 17.11, and the probe it asks for passes against that no-op. Do not implement the row as written here or as transcribed in `../BACKLOG.md`.


---

### 6.4 Corrections and new rows (v5, 2026-09-11)

These follow **ADR-0028 Amendment 1**, **ADR-0030** and **ADR-0031**, all written the same day in response to the B-04, B-05 security and B-12 reviews. Where an item overrides §6, §6.1 or a `../BACKLOG.md` row it says so. The architect does not edit the backlog; the project-manager transcribes these. **Row numbers here follow `../BACKLOG.md`'s split of the architect's single B-16 row — B-16.1 schema and append-only enforcement, B-16.2 the writer and hash chain, B-16.3 the `[Auditable]` interceptor — not §6.1's two-row form above.**

#### 1. B-16.1 — the append-only acceptance criteria, re-specified

**Overrides** the append-only half of §6.1's B-16.1 row and of `../BACKLOG.md`'s B-16.1 row. The partitioning half of both is unchanged. Reason: ADR-0028 §2 named a statement that does nothing, and the criterion built on it could not fail. ADR-0028 Amendment 1 carries the executed evidence; this is the row form.

| # | Criterion | What makes it a real check |
|---|---|---|
| 1 | Schema `audit`; `audit.audit_event` RANGE-partitioned monthly on `occurred_at`; a `DEFAULT` partition; every primary key and unique constraint includes `occurred_at`; the next two months pre-created by the platform job; a health check asserts the `DEFAULT` partition is empty | Unchanged from §6.1 |
| 2 | `aurora_app` holds **exactly** `SELECT, INSERT` on `audit.audit_event` | Compare the **ACL** — `aclexplode(pg_class.relacl)` plus `pg_attribute.attacl`, entries for `aurora_app` **and** `PUBLIC` — against the recorded decision, **not** `has_table_privilege`, which is blind to a column-level grant and short by `MAINTAIN` on PostgreSQL 17 (`../reviews/security-B-05.md` H-1). Then, **connected as `aurora_app`**, execute `UPDATE` and `DELETE` and expect SQLSTATE `42501` |
| 3 | `ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit GRANT SELECT, INSERT ON TABLES TO aurora_app` — a **positive grant**, replacing the `REVOKE` the old row quoted | Two assertions, both required: (a) `pg_default_acl` holds exactly one row for (`aurora_migrator`, `audit`, object type `r`) with ACL `{aurora_app=ar/aurora_migrator}`; (b) a table created in `audit` **after** the migration gives `aurora_app` exactly `{SELECT, INSERT}` — the `INSERT` **succeeds** and the `UPDATE`/`DELETE` return `42501`. **The `INSERT` half is not optional:** a probe that only checks "`UPDATE` is refused" passes against the no-op, because a new table grants `aurora_app` nothing at all |
| 4 | `BEFORE UPDATE OR DELETE … FOR EACH ROW` trigger on the **partitioned parent**, raising `42501` | Probed **as `aurora_migrator`**, the owner whom privileges do not restrain: through the parent **and** directly against a partition. Plus: create a partition *after* the trigger exists and probe it, because the guarantee for next month's partition is that the trigger is cloned on creation |
| 5 | `BEFORE TRUNCATE … FOR EACH STATEMENT` on the parent, **and attached by the partition-creation job to every partition it creates** | Truncate triggers are **not** cloned to partitions — verified on 17.11: `TRUNCATE audit.audit_event` was refused while `TRUNCATE audit.audit_event_2026_09` succeeded and emptied the table. The test truncates a **job-created** partition as the owner and expects `42501`. Without the job half, criterion 5 is a claim about the parent only |
| 6 | Every privilege and trigger probe reports **how many** relations, roles and ACL entries it compared, and fails on zero | `CLAUDE.md` self-check #2. A privilege test that found no relations must not print PASS |
| 7 | **No part of this row touches the `catalog` schema** | The old row's "the same three-way retrofit is applied to `catalog.operator_audit_event` and `catalog.erasure_replay_log`" is **withdrawn**: neither table exists, no task creates them, and B-05 did not. When the row that creates them is written, it carries criteria 2 and 4 and records the privilege decision in `CatalogSchemaAllowlist.AppRolePrivileges` with the writer named. Criterion 3 is **not** applied to `catalog`, whose tables legitimately need `UPDATE` |

**Two additions to B-16.2** (the writer and hash chain), for the same reason: its tamper test gains (a) disable the trigger as the owner, edit a row, re-enable, and assert `VerifyAsync` reports the break; and (b) drop a partition and assert the same. These are the cases the trigger cannot prevent, so the chain is what covers them.

**One limitation that needs a row of its own, not a sentence:** the hash chain does not detect truncation of the **tail** — removing the most recent rows leaves an internally consistent chain. ADR-0018's daily job writing the chain head to `catalog.operator_audit_event` is the answer, and nothing schedules it. Carry it as a follow-up (the project-manager assigns the id), depending on the row that creates `catalog.operator_audit_event`.

#### 2. Remove the ArchUnitNET package pins (ADR-0030) — **Light** tier

A developer task, small and mechanical:

- delete the `TngTech.ArchUnitNET` and `TngTech.ArchUnitNET.xUnit` `PackageVersion` lines from `Directory.Packages.props`;
- in `tests/Aurora.Architecture.Tests/Aurora.Architecture.Tests.csproj`, update the comment that cites ADR-0020's ArchUnitNET choice so it cites **ADR-0030** instead — the deviation is no longer a deviation;
- no other code change, and no rule changes.

Acceptance: `scripts/verify.sh` green with its stage-6 count unchanged, and a search for `ArchUnit` across `src/`, `tests/` and `Directory.Packages.props` returns nothing. **Sequence it after B-04 merges** — the rework owns that `.csproj` right now.

#### 3. New row — `Aurora.Countries.TestKit` and `CountryPackageContractTests<TPackage>` (ADR-0031 §5) — **Full** tier

| Task | Acceptance criteria | Depends on |
|---|---|---|
| `tests/Aurora.Countries.TestKit`: the package contract test base | The ten cases of ADR-0008 §10 as a `CountryPackageContractTests<TPackage>` base class in its own assembly — **not** in `Aurora.TestKit`, which every module's integration tests reference and which must not acquire the package host. Built whole: the three install cases (`Install_does_not_alter_core_ddl`, `Install_uninstall_round_trip`, `Install_into_a_live_tenant_with_existing_data`) are what give the base class its value, so the metadata-only half is not shipped alone. The first subclass is the first reference package; until one exists, the base class is exercised by the B-12 fixture package. Plus a fitness rule asserting every package assembly under `src/packages/` has exactly one subclass — **inert** until a package project exists, so it is registered in the `Inert` table of `tests/Aurora.Architecture.Tests` with this row's id and an inertness guard that fails when a package assembly appears without a subclass | B-13.2, B-12 |

#### 4. The B-12 rework carries the ADR-0031 contract edits — no new row

Six items, all inside the branch already in rework. Listed here because two of them enlarge it and the orchestrator's size check should see them:

1. §3.1's allowlist sentence in `src/Aurora.Countries.Contracts/README.md` and its `.csproj` comment restated to the three-assembly form, **plus** the derived-set test (the allowlist equals the contract's own `Aurora.*` references plus the contract itself).
2. A `coreContractRange` lacking either bound makes the manifest **invalid**, enforced where the manifest is constructed; `CoreContractGate` keeps the same check with the same message; `CoreContractGateTests`' `("1.0.0", "1.4.0")` row becomes a refusal case, joined by `"[1.0.0, )"` and `"(, )"`.
3. `IStatutoryReportDefinition.VersionAsOf(CompanyId, TaxRegistrationId, DateOnly)`, with `PublicAPI.Shipped.txt` moved with it. **Enlarges the rework.**
4. The registration-keyed slot registry, its written exception list (`ITaxCategoryMapping.DefaultCodeFor` goes in it, with the follow-up id), and a fixture interface written to break the rule. **Enlarges the rework.**
5. A second fixture package that **uses a type from** an `Aurora.*` assembly not on the allowlist, and a test watching the loader refuse it before loading. It must *use* the type: an unused `ProjectReference` is not emitted into the assembly, so a fixture that merely declares one would pass and prove nothing (ADR-0031 §2).
6. A second fixture whose `ICountryPackage.Manifest` disagrees with its embedded resource in one field, so the ADR-0008 §3.2 agreement check can fail (`../reviews/B-12.md` m2).
