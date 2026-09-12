# Solution layout and the `scripts/verify.sh` spec

Status: accepted **v8** · Author: architect · Date: 2026-09-12
Companion: `modules.md`, `testing-strategy.md`, `dependencies.md`, `../decisions/ADR-0007-...`, `../decisions/ADR-0008-...`

**Changes in v8** (2026-09-12): merges two branches that both called their edit **v5**. `task/ARCH-IDENTITY` used v5-v7 for the identity/authorization rows (§6.2, §6.3); the ADR-0028/0030/0031 branch used v5 for §6.4. Both are present below and **each changelog entry is named by its content, not only its number**, because two documents called v5 is precisely the ambiguity that made the `B-16` dependency point at the wrong row (ADR-0029 A2.6). From here the history is linear again.

**Changes in v7** (2026-09-11, second security review of ADR-0029 on PR #5): **ADR-0029 Amendment 2** closes the five remaining high findings, three of which Amendment 1's own fixes introduced. §6.1 is **reconciled to `../BACKLOG.md`'s `B-16` numbering** (the contradiction that pointed two rows at an empty table); §6.2 gains `B-18.9`, `B-18.10` and `B-18.11` from splitting `B-18.1` and `B-18.7`, which were over the size rule; §6.3 is updated. **Dependencies below now name what a row ships, with the number in parentheses** — a bare number is a reference that a legitimate size split silently inverts, which is what happened to `B-16`.

**Changes in v6** (2026-09-11, security review of ADR-0029 — `../reviews/ADR-0029.md`, 2 blockers and 9 high): **§6.2 and §6.3 are replaced.** The nine v5 rows become thirteen; six of the nine would not have been dispatched as written. The decisions are in **ADR-0029 Amendment 1**; §6.2 opens with a v5→v6 mapping table.

**Changes in v5 — identity rows** (2026-09-11, closing the identity/authorization gap the project-manager flagged twice): new **§6.2** specifies the nine bootstrap rows that build ADR-0009's sign-in stack and ADR-0010's permission evaluation, and new **§6.3** records the dependency edges that change as a result. Both follow **ADR-0029**. §6 and §6.1 are unchanged; §6.3 overrides their **Depends on** column where it says so.
**Changes in v5 — ADR-0028/0030/0031** (2026-09-11, answering three review escalations): §6.4 records the acceptance criteria that change because **ADR-0028 Amendment 1** replaced §2 of that ADR, and the new rows that follow from it and from **ADR-0030** and **ADR-0031**. §6 and §6.1 are unchanged except where §6.4 says it overrides them.

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
| B-07.4 | B-07.3, B-13.2 | **B-07.3, B-13.2, B-16.2, B-06.3** | SPEC-001 BR-7/AC-6 needs the tenant-side audit **writer** — the row that ships `IAuditWriter` and the hash chain, which is **B-16.2** in `../BACKLOG.md` (see the reconciliation note below; this cell said B-16.1 under the older two-row split); AC-5's minimal two-tenant read needs the application path (B-06.3), which was previously implied through B-07.1 |
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

B-15.1 gains the **`[Auditable]` interceptor row** as a dependency — **B-16.3** in `../BACKLOG.md` — because its "audit annotation" acceptance criterion has nothing to annotate against until the interceptor exists.

> **The append-only half of the B-16.1 row above is overridden by §6.4 item 1** (ADR-0028 Amendment 1). The statement it quotes — `ALTER DEFAULT PRIVILEGES … REVOKE UPDATE, DELETE` — is a no-op on PostgreSQL 17.11, and the probe it asks for passes against that no-op. Do not implement the row as written here or as transcribed in `../BACKLOG.md`.

**Reconciliation note (v7, ADR-0029 A2.6).** The two rows in the table above were split again by the project-manager on size, and `../BACKLOG.md` — **the file the orchestrator dispatches from** — is authoritative:

| This section's older row | `../BACKLOG.md` | Ships |
|---|---|---|
| B-16.1 (store **and** writer) | **B-16.1** | Schema `audit`, partitioning, three-way append-only enforcement |
| " | **B-16.2** | `IAuditWriter` and the hash chain |
| B-16.2 (interceptor) | **B-16.3** | The `[Auditable]` `SaveChangesInterceptor` and the raw-write ban |

Everywhere this document names an audit dependency it now names **what the row ships**, with the backlog number in parentheses. ADR-0029 A1.4's "depend on B-16.1, not B-16.2" was right about the substance — neither row uses the interceptor — and backwards about the number, which pointed two rows at an empty table.

**Still open after v4, closed in §6.2/§6.3 below:** nothing here builds sign-in or permission evaluation, and `B-07.3` seeds an `access` schema no row creates. See §6.2.

---

### 6.2 The identity and authorization rows (v8, 2026-09-12)

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
| **B-17.4** | Access: `[RequiresPermission]`, the enforcement pipeline, fitness rule S1 | Platform/Access | Full | B-17.3, B-04, **B-16.2** (the `IAuditWriter` row) | B-18.7, B-18.8 |
| **B-18.1** | Identity: catalog identity schema and the credential privilege boundary | Platform/Identity | Full | **B-05** | Everything in the B-06/B-07 spine |
| **B-18.9** | Identity: the platform authentication trail | Platform/Identity | Full | B-18.1 | B-18.2 — but **no** other row may add a `CatalogDbContext` migration while it is open |
| **B-18.2** | Identity: stores, password and lockout policy, the `IdentitySnapshot` cache | Platform/Identity | Full | B-18.1 | B-18.9, B-06.\*, B-07.\*, B-16.\* |
| **B-18.3** | Identity: platform user resolution and invitation issue (invite vs join) | Platform/Identity | Full | B-18.2, B-18.9 | B-06.\*, B-07.1/.2, B-16.\* |
| **B-18.4** | Identity: invitation and join redemption | Platform/Identity + Aurora.Web | Full | B-18.3, B-18.5 | B-17.3, B-17.4 |
| **B-18.5** | Web: tenant resolution and the tenant-neutral endpoint allow-list | Aurora.Web | Full | B-06.3, B-18.9 | B-17.2, B-17.3, B-18.2, B-18.3 |
| **B-18.6** | Web: the cookie validation event — stamp validation, `tid` carry-over, tenant cross-check | Aurora.Web / Platform/Identity | Full | B-18.2, B-18.5 | B-17.3 |
| **B-18.7** | Identity: sign-in, the cookie and the `tid` mint | Platform/Identity + Aurora.Web | Full | B-18.2, B-18.6, **B-16.2** (the `IAuditWriter` row) | B-17.4 |
| **B-18.10** | Identity: sign-out, security-stamp rotation and session termination | Platform/Identity + Aurora.Web | Full | B-18.7 | B-18.11 |
| **B-18.11** | Identity: sign-in rate limiting and the bounded authentication-trail write | Platform/Identity + Aurora.Web | Full | B-18.7, B-18.9 | B-18.10 — but see the stacking note below |
| **B-18.8** | Blazor: circuit pinning, reconnect identity check, revalidation | Aurora.Web | Full | B-18.7 | B-17.4 |

Every row is **Full** tier. The numbering does not imply dispatch order — `B-17.2` and `B-18.3` land **before** `B-07.3` — and the **Depends on** column is authoritative.

**Stacking note for the orchestrator (v7).** `B-18.7` ships sign-in and `B-18.11` ships its rate limiter; they stack, and **the integration branch must not carry sign-in without its limiter** (ADR-0029 A1.2 H-8: unbounded unauthenticated writes into the shared catalog, plus fail-closed on that write, is a fleet-wide sign-in outage). Merge them in one wave, or hold `B-18.7`.

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
4. `access` joins the per-tenant migration order, asserted **relatively** — `access` after `platform`, and after `audit` — read from the order actually executed against a migrated tenant database. A relative assertion is required because B-16.1 asserts the same list from another branch, and two absolute assertions on one ordered list is a merge that silently drops a schema. **The assertion must report the number of schemas observed and fail below a floor, and must assert that `platform`, `audit` and `access` were each actually present** — "after `audit` if `audit` is present" passes vacuously on a database where `audit` never migrated, which is the one failure this criterion exists to catch.
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

1. `AccessSubject` is sealed with an internal constructor and exactly two factories, and `EvaluateAsync` takes one — never a `ClaimsPrincipal`. `FromPrincipal(ClaimsPrincipal, TenantScope)` **performs the tenancy comparison as the price of construction**: `tid` present, parsable and equal to `scope.TenantId`, and security stamp, user status and Tenant Membership (`Active` only) valid against the 60 s `IdentitySnapshot`. `ForSystemJob(TenantScope, SystemPrincipalId)` **takes no permission set** — a test asserts the type exposes no overload that accepts one, because an absent overload is the only version of this rule that cannot be worked around — looks the set up from the code-declared `SystemPrincipal` registry, and **throws unless `scope.Reason` is `Job` or `Outbox`**, so a request-path scope cannot be laundered into a system subject (ADR-0029 A2.2). Tests: a system subject is denied a permission outside its declared set; construction from a `Request`-reason scope throws; `system-principals.approved.txt` is asserted by rule S9 with its **count reported**, and `ForSystemJob`'s call sites are allow-listed to `Aurora.Platform.Jobs`, the outbox dispatcher and their tests.
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

ADR-0029 §2, A1.3 M-7, **A2.7**; ADR-0007 §9.2, §9.3; `solution-layout.md` §6.4 item 5 (the ACL-comparison probe and the writer field).

1. One additive EF migration on `CatalogDbContext` (expand-only — B-09's categories) adds `catalog.identity_credential` and `catalog.identity_invitation` (tenant, user, token hash, kind `Invite|Join`, expires_at, redeemed_at), and points `catalog.identity_user.credential_ref` at `identity_credential`.
2. **Credential privilege boundary:** a new `aurora_identity` login owns access to `catalog.identity_credential`, and `aurora_app` holds **nothing** on it. Probed in the shape §6.4 item 5 criterion 2 fixed — compare `aclexplode(pg_class.relacl)` **plus** `pg_attribute.attacl`, for `aurora_app` **and** `PUBLIC`, against `CatalogSchemaAllowlist.AppRolePrivileges`, **not** `has_table_privilege` (blind to a column-level grant, and short by `MAINTAIN` on PostgreSQL 17) — then, connected **as `aurora_app`**, execute `SELECT` and expect SQLSTATE `42501`. Each new entry names its **writer** (§6.4 item 5 criterion 3): `identity_credential` → the identity store on `aurora_identity`; `identity_invitation` → `B-18.3`'s issue path and `B-18.4`'s redemption. Probes report how many relations and ACL entries they compared and fail on zero.
3. `catalog.user_tenant_membership.state` is enumerated `Invited | Active | Suspended | Revoked` (ADR-0029 A2.3), with `Invited` as the state provisioning creates; the column's check constraint names the four values so a fifth cannot arrive by string.
4. `AuroraClaimTypes` (`tid`, `tkey`) lives in `Aurora.Platform.Identity.Contracts` as the single definition used by the mint, the carry-over and the cross-check.
5. An integration test against a real PostgreSQL container applies the migration to a catalog created by B-05 and re-runs it with no effect.
6. The identity data source's connection string is resolved from configuration and never from a literal; a test asserts no credential-shaped literal exists in the assembly (`docs/architecture/automation.md`'s hook covers the write, this covers the build).

*This row and `B-18.9` own the catalog migration chain **in that order**; no third row may add a `CatalogDbContext` migration while either is open.*

#### B-18.2 — Identity: stores, password and lockout policy, the `IdentitySnapshot` cache

ADR-0009 rule 1; ADR-0029 §2, A1.1 B-1, A1.3 M-5; ADR-0012 §3 as amended.

1. `IUserStore`, `IUserPasswordStore`, `IUserEmailStore`, `IUserSecurityStampStore` and `IUserLockoutStore` over `CatalogDbContext`, wired with `AddIdentityCore<…>()`; hashing is the framework's `PasswordHasher<…>` and a test asserts no type in the solution implements `IPasswordHasher<>`.
2. `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is **not** referenced and `IdentityDbContext` is not used, asserted over the project's references so a later `dotnet add package` under deadline fails the build rather than quietly reshaping the schema.
3. Configured and asserted by a test that reads the live options: `PasswordOptions` ≥ 12 characters with **no** composition rules (the framework default of 6 plus character classes fails ASVS L2); `LockoutOptions` 10 attempts, 15-minute lockout, enabled for new users. Lockout is driven by real failed attempts in the test, never by setting the flag.
4. **`IdentitySnapshot(userId, securityStamp, userStatus, IReadOnlyDictionary<TenantId, MembershipState>)`** is cached 60 s under `CatalogCacheKey.IdentitySnapshot(userId)` and invalidated on membership change, stamp rotation and status change. **The tenant is a parameter of the lookup, never of the key.** `CatalogCacheKey.For(kind, …)` is defined as the `c:`-prefix sibling of `TenantCacheKey.For`, its kinds are a committed enum, and ADR-0012 rule 1's fitness test is extended to accept exactly these two helpers and nothing else.
5. **The test that closes the blocker:** user U is an active member of tenant A and not of tenant B; ask `ITenantMembership` for `(U, A)`, then immediately for `(U, B)` **with no time advanced**; the second answer is `false`. A key that omits the tenant fails this and passes the old "revoke and re-read" test.
6. **The normalizer is named, not implied:** `EmailAddress.Normalize` trims, applies Unicode NFKC and then `ToUpperInvariant` (never `ToUpper`, which is culture-dependent), and is the single method used by the store, the uniqueness index and B-18.9's HMAC — a test asserts all three agree on the same input, including a Turkish-locale case and a full-width-character case. **`MembershipState` is enumerated and its meaning asserted per value:** only `Active` counts as membership for the mint and for `AccessSubject`; `Invited`, `Suspended` and `Revoked` each have their own test asserting refusal (ADR-0029 A2.3). A user holding memberships in two tenants is a normal result covered by a test, so SPEC-001 BR-6's external-accountant case is not designed out.

#### B-18.3 — Identity: platform user resolution and invitation issue (invite vs join)

ADR-0029 A1.1 B-2; SPEC-001 BR-2, BR-3, BR-4; ADR-0010 rule 8. **Consumed by `B-07.3` (saga step 6).**

1. `IPlatformUserProvisioning.ResolveForTenantAdministratorAsync(email, tenantId)` is the **single owner** of find-or-create on `catalog.identity_user` and returns one of two outcomes: **`Invite`** — the identity has no credential **and** no Tenant Membership in any other tenant — or **`Join`** — an established identity, for which it creates the Tenant Membership only. **On both paths the Membership is created `Invited`, never `Active`** (ADR-0029 A2.3): issue grants nothing, redemption is the only transition to `Active`, and a test asserts that immediately after issue the invited person can neither sign in to that tenant nor have an `AccessSubject` constructed for it.
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

1. Everything runs inside `CookieAuthenticationOptions.Events.OnValidatePrincipal`, in this order: security-stamp validation against the 60 s `IdentitySnapshot`, then the tenant cross-check. It runs wherever the cookie is read, so it can be neither mis-ordered against `UseAuthentication` nor path-exempted by accident. **The no-tenant-resolved outcome is stated, not left to the implementer:** on a request that resolves to no tenant because the endpoint is on B-18.5's tenant-neutral allow-list, the cross-check **does not compare** — there is nothing to compare against — the principal stays authenticated but *tenant-unusable*, and no `TenantScope` may be opened (asserted with a counted zero). On a request that resolves to no tenant and is **not** on that list, the outcome is B-18.5 criterion 2's `404`. A principal carrying `tid = A` reaching `/sign-out` therefore succeeds, which is what keeps the sign-out escape hatch real.
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
5. A successful sign-in writes one tenant-side `audit.audit_event` through `IAuditWriter` (the `B-16.2` row); every failed sign-in writes `catalog.authentication_event` and **nothing** tenant-side, with row counts asserted in **both** stores for each case. A forced failure of the authentication-event write fails the sign-in.
6. Sign-in requires an `Active` Tenant Membership, so an `Invited` one is refused (ADR-0029 A2.3) — the test redeems first and then signs in, which is the sequence SPEC-001 AC-5 actually describes.

*`B-18.10` (sign-out) and `B-18.11` (rate limiting) were split out of this row; see the stacking note above — the integration branch must not carry this row without `B-18.11`.*

#### B-18.8 — Blazor: circuit pinning, reconnect identity check, revalidation

ADR-0005 rule 2, rule 6; ADR-0007 §3.3; ADR-0009 rule 3; ADR-0029 §4 checkpoints 2–3, A1.2 H-7, H-9.

1. A `CircuitHandler` pins `(tid, sub, securityStamp)` at circuit creation and re-checks **all three** at creation and on every reconnect of a persisted circuit; a mismatch on any aborts the circuit rather than downgrading it. **A reconnect presenting a valid cookie for a different user of the same tenant is refused** — that is horizontal escalation inside a tenant and it passed the design as written.
2. The pinned tuple lives **server-side only, keyed by circuit id, never serialised to the client**; persisted circuit state is keyed to the authenticated subject. Deliberately-violating fixture: round-trip the pinned tenant through client state and assert the rule fails.
3. **The circuit pins the `TenantId`, not a `TenantScope`** (ADR-0029 A2.5): a scope is opened per unit of work — one per component event handler or application-service invocation — and disposed with it, so the tenant-state gate runs on every unit of work rather than once at circuit creation. **The falsifying assertion is a count:** over a circuit's life the number of scope opens is **greater than one**; a circuit-lifetime scope makes it exactly 1. Paired with it: suspend the tenant mid-circuit and assert the next component action fails within 60 s — the `SchemaBlocked`-keeps-writing case the gate was raised for. No `TenantScope` field exists on a `CircuitHandler` or a component — fitness rule **T11**. `RevalidatingServerAuthenticationStateProvider` at ADR-0009 rule 3's 30-minute interval checks security stamp, Tenant Membership, tenant state and `tid` against the pinned tenant; the row's README states what each interval bounds: 30 minutes for "still the right person on an idle circuit", 60 seconds for anything that opens a scope or runs a guarded command.
4. The failure path **forces a navigation to the sign-in page** rather than `ForceSignOut`'s stock anonymous-but-connected render — the criterion asserts the previously rendered tenant data is gone, not merely that the principal is anonymous. That is the opposite of stock behaviour and is the reason this criterion exists.
5. Revocation mid-session: with the circuit live, revoke the Tenant Membership through the real revocation path, advance `FakeTimeProvider` past the interval, assert invalidation — and the same scenario at 29 minutes asserts the principal is **still** valid, so the interval is a claim that can fail. The fault is injected inside the system under test; no `sleep`.
6. The pinned tuple is added to the assertions behind fitness rules T5/T6, and a component rendered after the creating HTTP request has completed still sees the pinned tenant without `IHttpContextAccessor`.

#### B-18.9 — Identity: the platform authentication trail

ADR-0029 §7, A1.2 H-8, A1.3 M-4, **A2.1**; **ADR-0028 Amendment 1** and §6.4 items 1 and 5 (not ADR-0028 §2, withdrawn); ADR-0028 §3. *Split out of B-18.1, which was over the size rule.* **Depends additionally on §6.4 item 5's row** (the catalog's two append-only tables — the project-manager assigns its id), which establishes `CatalogSchemaAllowlist.AppRolePrivileges` and the writer field this row extends; building the pattern twice is how the two copies drift.

1. A second additive migration on `CatalogDbContext` adds `catalog.authentication_event`, **monthly RANGE-partitioned** on `occurred_at` with a `DEFAULT` partition (the shape of `audit.audit_event`, ADR-0028 §3). **Append-only follows ADR-0028 Amendment 1 and §6.4 items 1 and 5 — not the withdrawn ADR-0028 §2**, whose `ALTER DEFAULT PRIVILEGES … REVOKE` is a no-op on PostgreSQL 17.11 and whose `has_table_privilege` probe is blind to a column-level grant: (a) `aurora_app` holds **exactly** `SELECT, INSERT`, compared by `aclexplode(pg_class.relacl)` plus `pg_attribute.attacl` for `aurora_app` **and** `PUBLIC` against `CatalogSchemaAllowlist.AppRolePrivileges`, whose entry names its writer (B-18.11's limiter and B-18.5/B-18.6/B-18.7's refusal paths), then connected **as `aurora_app`** an `INSERT` that **succeeds** and `UPDATE`/`DELETE` that return `42501`; (b) a `BEFORE UPDATE OR DELETE … FOR EACH ROW` trigger on the **partitioned parent**, probed **as `aurora_migrator`** through the parent, directly against a partition, and against a partition created *after* the trigger exists; (c) a `BEFORE TRUNCATE … FOR EACH STATEMENT` trigger on the parent **and attached to every partition the partition job creates**, because truncate triggers are **not** cloned (verified on 17.11, §6.4 item 1 criterion 5). Every probe reports how many relations, roles and ACL entries it compared and fails on zero. **`ALTER DEFAULT PRIVILEGES` is not used in schema `catalog`** — §6.4 item 5 criterion 5 asserts `pg_default_acl` holds zero rows for it, and this row must keep that assertion true.
2. The bounded-write shape of ADR-0029 A2.1: columns `dedupe_key text NOT NULL`, `granularity ('Instant'|'Window')`, `window_seconds`; index **`UNIQUE (occurred_at, dedupe_key)`**, which contains the partition key. For a `Window` row `occurred_at` **is the window start** and `dedupe_key` is `{event_type}|{source_hash}|{email_hash}`; for an `Instant` row `occurred_at` is the instant and `dedupe_key` is a fresh UUID. Every write is `ON CONFLICT DO NOTHING`.
3. **Three tests, and the third is the one that matters.** Three `Window` writes in one window produce **one** row; a write in the next window produces a second; **two `Instant` rows written in the same instant produce two rows**. The third fails under the repair that adds `occurred_at` to a per-attempt key and calls it deduplication — which compiles, runs green and deduplicates nothing (verified against `postgres:17-alpine`, ADR-0029 A2.1). A fourth asserts the retained row is the **first**, so attacker-chosen `detail` from a later attempt in the window is discarded rather than merged.
4. `attempted_email_hash` is **HMAC-SHA-256 with a key from the secret store** (ADR-0011), not a bare SHA-256: an unsalted hash over an enumerable address space is reversible, so it would put recoverable addresses of non-users in the catalog. Tests: the row contains the address in no form; two different keys produce different digests for one address; and the digest agrees with `EmailAddress.Normalize` (B-18.2).
5. `IAuthenticationEventSink.WriteAsync(…)` writes exactly one row per call for `Instant` events and at most one per window for `Window` events; a forced write failure **surfaces** rather than being swallowed, because ADR-0029 §7's "a sign-in that cannot be recorded does not happen" is enforced from here.
6. `IAuthenticationEventRetention.PruneAsync(cutoff)` detaches and drops whole partitions older than 180 days — retention on an append-only table is a partition operation or it is nothing, since the trigger stops `DELETE` for the owner too. Scheduling is a named follow-up (no scheduler exists in bootstrap; ADR-0028 §3's partition pre-creation job has the same gap).

#### B-18.10 — Identity: sign-out, security-stamp rotation and session termination

ADR-0029 A1.2 H-7. *Split out of B-18.7.*

1. `POST /sign-out` deletes the cookie, calls `UserManager.UpdateSecurityStampAsync` and invalidates the user's `IdentitySnapshot` entry; it is on B-18.5's tenant-neutral allow-list so it is reachable while holding a cookie for a suspended or deleted tenant.
2. A password change performs the same three steps — one method, two callers, asserted by a test that exercises both and compares the resulting state.
3. After sign-out, another HTTP session for the same user is refused on its **next request** (the 60 s stamp validation of B-18.6), and a guarded command from a live circuit is refused within 60 s by `AccessSubject.FromPrincipal`. Both with `FakeTimeProvider`; the revocation is applied through the real sign-out path, not by writing the stamp directly.
4. The counterpart at 59 seconds still succeeds, so the 60 s bound is a claim that can fail.
5. Sign-out writes one tenant-side `audit.audit_event` through `IAuditWriter` (the `B-16.2` row); a sign-out on a tenant-neutral endpoint where no tenant is resolved writes the platform-side event instead, and a test asserts exactly one row in exactly one of the two stores for each case.

#### B-18.11 — Identity: sign-in rate limiting and the bounded authentication-trail write

ADR-0029 A1.2 H-8, **A2.1**. *Split out of B-18.7. Stacks on it — see the stacking note above.*

1. `Microsoft.AspNetCore.RateLimiting` (in-box, no new dependency) applies fixed-window limits per source IP and per email hash to `/sign-in` and to invitation redeem.
2. The limiter rejects **before the credential check and before any catalog write** — asserted by a counting probe on both the password hasher and the sink, each reporting **0** invocations for a rejected request. A rejected request costs no KDF and no catalog row beyond criterion 3's single bounded row.
3. Rejections and failed sign-ins are written as `Window` rows through B-18.9's sink: N rejected attempts in one window produce **one** row, the next window produces a second, and the rejection does not depend on the write succeeding.
4. The limiter's own failure mode is closed: if the limiter cannot evaluate (its store faulted by a hook inside the limiter's data path), the request is **rejected**, not admitted.
5. Limits are configuration with stated defaults, asserted from the live options rather than from a constant, so a deployment that loosens them does so visibly.
6. A test drives the limiter to rejection **from two distinct source identities in parallel** and asserts one is rejected while the other is not — so a per-IP limiter that has silently become global fails.

---

### 6.3 Dependency edges that change (v7)

These override the **Depends on** column in §6, §6.1 and `../BACKLOG.md` for the rows named. Reasons are in ADR-0029 and its Amendment 1.

| Row | Was | Is | Why |
|---|---|---|---|
| **B-06.3** | B-06.2 | **B-06.2, B-03.1** *(+ scope addition)* | `ITenantDbContextFactory<TContext>` gains `CreateAsync(TenantScope, CompanyScope, ct)`, and the single-argument overload **throws** when the context's EF model contains an `ICompanyScoped` entity type (ADR-0029 A1.2 H-2). Model metadata is the mechanism that makes the company boundary structural rather than a convention. **Flag for the orchestrator's pre-dispatch size check** — this stacks on an already-large row |
| **B-08.3** | B-06.3 | B-06.3 *(+ scope addition, retitle)* | Retitle: *Tenancy: schema-version and tenant-state gates at scope open, **and the reason binding***. `OpenAsync` refuses a tenant whose `catalog.tenant.state` is not in the allow-list for the scope's `TenantAccessReason` (ADR-0029 A1.2 H-5) — **and takes no reason parameter**: the reason is bound by which factory the composition root registered, `Migration` and `OperatorSupport` have no binding in bootstrap, and a test asserts an operator-support scope cannot be obtained from any composition root (A2.4). A reason the caller can type is a gate the caller can skip, which is ADR-0027's own argument about the `Migration` reason applied to this one |
| **B-07.3** | B-07.2 | **B-07.2, B-17.2, B-18.3** | Step 6 seeds into schema `access` with a seeder no row wrote (B-17.2 — note this is the **seeder** row, not B-17.1's schema row), and SPEC-001 BR-3's credential path is B-18.3's invitation. It calls `IAccessSeeder.SeedAsync` and `IPlatformUserProvisioning`/`IssueAsync`; it re-implements neither. It does **not** need redemption (B-18.4) |
| **B-07.4** | B-07.3, B-13.2, B-16.2, B-06.3 | **+ B-18.7** | SPEC-001 AC-5's "*when* U signs in" needs a sign-in. B-07.4 exercises AC-5 at the HTTP level; BR-6's circuit half is B-18.8's and the exhaustive isolation proof stays B-10's. It does **not** gain B-17.4: BR-1's "authorized caller" is an operator, and ADR-0010 rule 8 puts operator capability outside the tenant permission model — the follow-up named in ADR-0029 A1.4 |
| **B-10** | B-07.4, B-06.3 | unchanged *(+ scope note)* | The instrumented counting resolver ships in `Aurora.TestKit` from **B-18.5**; B-10 consumes it and must not re-implement it |
| **B-15.1** | B-06.3, B-10, B-13.2, B-14, B-16.3 | **+ B-17.4, + B-08.3** | SPEC-002 BR-3/BR-4 are the enforcement pipeline plus `CompanyScope`; and the first module that reads tenant data should have the tenant-state gate in place, not acquire it later |
| **B-15.2** | B-15.1 | **+ B-18.7** | AC-1/AC-2/AC-9/AC-10 all begin with a signed-in caller |
| **B-15.3** | B-15.2, DESIGN-001 | **+ B-18.8** | The UI path runs inside a circuit |
| **B-16.1** (schema) / **B-16.2** (`IAuditWriter`) | B-07.2 / B-16.1 | unchanged | **B-17.1 is sequenced after B-16.1** rather than parallel with it: both touch the per-tenant migration order, and two branches asserting one ordered list is a merge that silently drops a schema (B-17.1 criterion 4 is relative, and non-vacuous, for the same reason). **B-17.4 and B-18.7 depend on B-16.2** — the row that ships `IAuditWriter` and the hash chain in `../BACKLOG.md`'s numbering — not on B-16.1's empty table and not on B-16.3's interceptor, which neither row uses (ADR-0029 A2.6) |

**What this does to the graph.** `B-18.1` still depends on **B-05 alone** and should be dispatched the moment B-05 merges; `B-18.9` follows it and then `B-18.2`/`B-18.3`, and that chain is what `B-07.3` waits for — run alongside `B-06.2`/`B-06.3`/`B-07.1`/`B-07.2` it costs `B-07.3` nothing, but it is one row longer than in v6, so start it early. `B-18.9 ∥ B-18.2` is real parallelism once `B-18.1` lands. `B-03.1` is unblocked now and must land before `B-06.3`. The Access chain is `B-07.2 → B-16.1 → B-17.1 → B-17.2` into `B-07.3`, with `B-17.3`/`B-17.4` joining later at `B-15.1`. Three rows touch the audit chain: `B-17.1` (after **B-16.1**, the schema, for the migration order) and `B-17.4`/`B-18.7` (on **B-16.2**, the `IAuditWriter` row). None of them uses **B-16.3**, the `[Auditable]` interceptor.


---

### 6.4 Corrections and new rows (v6, 2026-09-12)

These follow **ADR-0028 Amendment 1**, **ADR-0030** and **ADR-0031**, all written the same day in response to the B-04, B-05 security and B-12 reviews. **v6 (2026-09-12) changes items 1 and 5 only**, following **ADR-0028 Amendment 2**: item 1's criteria 4, 5 and 6 (both guards reach every partition, `DEFAULT` included, created by the statement that creates the partition rather than by a job that does not exist, enabled `ALWAYS`, and **asserted by executing the forbidden statement in a rolled-back transaction rather than by checking that a trigger exists**), its criterion 2 and its closing limitation; and item 5's criteria 2, 3 and 4 (a partition's expected ACL is recorded as **empty**, which is the catalog's shape and not `audit`'s). Items 2, 3 and 4 are untouched. Where an item overrides §6, §6.1 or a `../BACKLOG.md` row it says so. The architect does not edit the backlog; the project-manager transcribes these. **Row numbers here follow `../BACKLOG.md`'s split of the architect's single B-16 row — B-16.1 schema and append-only enforcement, B-16.2 the writer and hash chain, B-16.3 the `[Auditable]` interceptor — not §6.1's two-row form above.**

#### 1. B-16.1 — the append-only acceptance criteria, re-specified

**Overrides** the append-only half of §6.1's B-16.1 row and of `../BACKLOG.md`'s B-16.1 row. The partitioning half of both is unchanged. Reason: ADR-0028 §2 named a statement that does nothing, and the criterion built on it could not fail. ADR-0028 Amendment 1 carries the executed evidence; this is the row form. **Criteria 4, 5 and 6 were re-specified again on 2026-09-12 by ADR-0028 Amendment 2**, which carries the executed evidence in turn — the earlier form guarded the parent and left every partition truncatable by the owner, and Amendment 2's own first draft then checked that a guard *existed* rather than that it *refused*, which the PR #9 security review broke with one `CREATE OR REPLACE FUNCTION`.

| # | Criterion | What makes it a real check |
|---|---|---|
| 1 | Schema `audit`; `audit.audit_event` RANGE-partitioned monthly on `occurred_at`; a `DEFAULT` partition; every primary key and unique constraint includes `occurred_at`; the next two months pre-created by the platform job; a health check asserts the `DEFAULT` partition is empty | Unchanged from §6.1 |
| 2 | `aurora_app` holds **exactly** `SELECT, INSERT` on `audit.audit_event`, **and on every partition of it** — ADR-0028 §2 mechanism 1 always said *every relation in schema `audit`, partitions included*, and this row had dropped the phrase. In `audit` a partition created by `aurora_migrator` inherits `{SELECT, INSERT}` from criterion 3's default grant (executed on 17.11); that is **not** the catalog's shape, and item 5 criterion 3 is where the difference is recorded | Compare the **ACL** — `aclexplode(pg_class.relacl)` plus `pg_attribute.attacl`, entries for `aurora_app` **and** `PUBLIC` — against the recorded decision, **not** `has_table_privilege`, which is blind to a column-level grant and short by `MAINTAIN` on PostgreSQL 17 (`../reviews/security-B-05.md` H-1). Then, **connected as `aurora_app`**, execute `UPDATE` and `DELETE` and expect SQLSTATE `42501` |
| 3 | `ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit GRANT SELECT, INSERT ON TABLES TO aurora_app` — a **positive grant**, replacing the `REVOKE` the old row quoted | Two assertions, both required: (a) `pg_default_acl` holds exactly one row for (`aurora_migrator`, `audit`, object type `r`) with ACL `{aurora_app=ar/aurora_migrator}`; (b) a table created in `audit` **after** the migration gives `aurora_app` exactly `{SELECT, INSERT}` — the `INSERT` **succeeds** and the `UPDATE`/`DELETE` return `42501`. **The `INSERT` half is not optional:** a probe that only checks "`UPDATE` is refused" passes against the no-op, because a new table grants `aurora_app` nothing at all |
| 4 | `BEFORE UPDATE OR DELETE … FOR EACH ROW` guard raising `42501`, on the parent **and created on every partition by the same migration step that creates or attaches it** — not left to cloning — and enabled **`ALWAYS`** (**ADR-0028 Amendment 2**) | Probed **as `aurora_migrator`**, the owner whom privileges do not restrain, **inside a transaction that is rolled back**: through the parent, directly against a partition, and against a partition created *after* the guard exists. **Why not the clone:** `ALTER TABLE … DETACH PARTITION` **deletes cloned triggers** — executed, the cloned row guard vanished from the detached table, `DELETE` returned `DELETE 1`, and `ATTACH` re-cloned it, so the round trip left the enumeration printing `leaf 2 / guarded 2 / parents 1` throughout with a row gone. With the guard created on the partition, the same `DELETE` on the same detached table returns `42501` and the rows survive; the attacker must `DROP TRIGGER` first. It also stops sanctioned retention work, which detaches partitions, from unprotecting one for the duration. **Why rolled back:** the run that finds a broken guard is the run in which the `DELETE` succeeds |
| 5 | `BEFORE TRUNCATE … FOR EACH STATEMENT` guard on the parent **and on every partition, the `DEFAULT` partition included, created on the partition by the same migration step that creates or attaches it**, enabled **`ALWAYS`** — **ADR-0028 Amendment 2 (2026-09-12)**, replacing this row's earlier *“attached by the partition-creation job to every partition it creates”*. **What is asserted is refusal, not presence:** as `aurora_migrator`, inside a rolled-back transaction, execute `TRUNCATE` against **every** relation in `pg_partition_tree('audit.audit_event')` and require SQLSTATE **`42501`** from each; the probe reports how many refused and **names any that were silent**. The metadata enumeration — a non-internal trigger with `tgtype & 32 <> 0` (TRUNCATE), `tgtype & 2 <> 0` (BEFORE) and **`tgenabled = 'A'`** — stays as the **locator** that says *which* relation and *why*, and is not the guarantee | The old form guarded the parent and nothing else, and hung the rest on a job that does not exist (`FOLLOWUP-031`) which would never have created the `DEFAULT` partition anyway. Truncate triggers are **not** cloned, by `PARTITION OF` or by `ATTACH PARTITION`. **Executed on 17.11, guard on the parent only:** `TRUNCATE audit.audit_event` refused, then `TRUNCATE audit.audit_event_default` and `TRUNCATE audit.audit_event_2026_09` each returned `TRUNCATE TABLE` and took a three-row trail to **zero**. **Working:** truncate of the parent, a month partition, the `DEFAULT` partition and both partitions in one statement — four `42501`s, `count(*)` unchanged; the effect probe prints `relations probed 3, refused 3, silent (none)`. **Goes red:** a partition created with a bare `CREATE TABLE … PARTITION OF` that skips the helper — the enumeration goes `leaf 2 / guarded 2` → `leaf 3 / guarded 2` **and** the owner's `TRUNCATE` on it succeeds. **Four ways to implement this wrong and still be green, each executed:** (a) checking presence instead of effect — `CREATE OR REPLACE FUNCTION … RETURN NULL` is one statement, no table DDL, leaves `tgtype 34` / `tgenabled O` / `leaf 2 / guarded 2 / parents 1` **byte-identical** while `TRUNCATE` on the leaf and the parent both succeed and the trail goes to zero; (b) probing as **`aurora_app`** — an **unguarded** partition returns `42501: permission denied for table` from the privilege check, so the probe passes with no trigger present, which is why it runs as the owner, who holds `TRUNCATE` and whose privilege path therefore cannot produce `42501`; (c) asserting *“an exception was raised”* rather than the SQLSTATE — any replacement body that raises at all passes; (d) `tgenabled IN ('O','A')` or `<> 'D'` — `ENABLE REPLICA TRIGGER` leaves `'R'`, and `session_replication_role = 'replica'` suppresses `'O'` with `tgenabled` unchanged, both letting the `TRUNCATE` through. `ENABLE ALWAYS` on the **parent** reaches cloned triggers only: a partition's own truncate guard stays `'O'` (executed). Omitting the rollback also goes red — and empties the trail while doing it |
| 6 | Every privilege and trigger probe reports **how many** relations, roles and ACL entries it compared and fails on zero; the effect probe of criteria 4 and 5 reports **relations probed / refused with `42501` / silent**, naming the silent ones; the metadata enumeration prints `leaf_partitions` and `guarded_leaves` and fails when `leaf_partitions < 2` | `CLAUDE.md` self-check #2. A privilege test that found no relations must not print PASS, and a guard enumeration over an empty partition tree must not print a clean `0 of 0`: the floor is the initial month partition plus `DEFAULT`. The floor is on the **population**, not on the guarded count — a floor asserted on `guarded_leaves` passes when both are zero |
| 7 | **No part of this row touches the `catalog` schema** | The old row's "the same three-way retrofit is applied to `catalog.operator_audit_event` and `catalog.erasure_replay_log`" is **withdrawn**: neither table exists, and B-05 did not create them. **§6.4 item 5 below is the row that creates them**, carrying criteria 2 and 4 and recording the privilege decision in `CatalogSchemaAllowlist.AppRolePrivileges` with the writer named. Criterion 3 is **not** applied to `catalog`, whose tables legitimately need `UPDATE` — item 5 asserts that as a zero-row check on `pg_default_acl` |

**Two additions to B-16.2** (the writer and hash chain), for the same reason: its tamper test gains (a) disable the trigger as the owner, edit a row, re-enable, and assert `VerifyAsync` reports the break; and (b) drop a partition and assert the same. These are the cases the trigger cannot prevent, so the chain is what covers them.

**One limitation that needs a row of its own, not a sentence:** the hash chain does not detect truncation of the **tail** — removing the most recent rows leaves an internally consistent chain, and an **emptied** trail is the degenerate case of it, since zero rows verify clean and so does a trail restarted from a first row carrying ADR-0028 §5's 32 zero bytes. Criterion 5 above now closes the half of this that needed no DDL; what is left is a role that can `DROP`/`DISABLE` the guard or detach and drop a partition, and **nothing detects that** (ADR-0028 Amendment 2 states it as a limitation rather than claiming cover). The cover is ADR-0018's daily job writing the chain head to `catalog.operator_audit_event`, **outside the tenant database** — a head kept inside it is rewritten by the same role in the same session. It depends on **§6.4 item 5** (the row that creates `catalog.operator_audit_event`) and on **`FOLLOWUP-031`** (which ships `Aurora.Platform.Jobs` and the three jobs that assume a scheduler, the chain-head job among them). Carried in `../BACKLOG.md` as **`FOLLOWUP-026`**.

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

#### 5. New row — the catalog's two append-only tables (ADR-0028 Amendment 1, criterion 7) — **Full** tier

Item 1 criterion 7 withdrew the claim that B-16.1 retrofits `catalog.operator_audit_event` and `catalog.erasure_replay_log`, because neither table exists. This is the row that creates them, so that the withdrawal does not leave two ADR-0007 tables and one follow-up depending on work nobody owns. **Nothing here is new design** — the content is criterion 7's, and the mechanisms are item 1's criteria 2 and 4 applied to two unpartitioned tables. **v6 (2026-09-12) adds one expectation that is genuinely new in this item:** criterion 3's *empty* partition ACL, recorded here because this is where the allow-list is specified, and criterion 4's pointer to ADR-0028 Amendment 2 for a partitioned table that joins `catalog` later.

**Scope boundary:** this row creates the tables, the grants and the triggers. It does **not** build a writer. The operator trail is written by the provisioning saga (SPEC-001 BR-7: B-07.1 records failed and retried attempts, B-07.4 the successful platform-side event) and by ADR-0010 rule 8's support-access grant; the replay log is written by ADR-0007 §11.5's erasure path. Today **no row creates the tables those rows write to** — that is the gap this item closes.

| # | Criterion | What makes it a real check |
|---|---|---|
| 1 | `catalog.operator_audit_event` (ADR-0007 §9.2) and `catalog.erasure_replay_log` (ADR-0007 §11.5, ADR-0018 §5) exist in the catalog database with the columns those sections name | Asserted against the **migrated database** (`information_schema`), not against the EF model. At least one of these tables carries a trigger and is created by raw SQL, and `../reviews/security-B-05.md` M-1 demonstrated that a raw-SQL catalog table is invisible to the model-agreement test — it landed a table with a blanket grant and the gate still reported `RESULT: PASS` |
| 2 | `aurora_app` holds **exactly** `SELECT, INSERT` on each of the two, and the comparison runs over **every relation in schema `catalog`, partitions included** (criterion 3) | Item 1's criterion 2 verbatim: compare `aclexplode(pg_class.relacl)` plus `pg_attribute.attacl` for `aurora_app` **and** `PUBLIC` against `CatalogSchemaAllowlist.AppRolePrivileges`, then **connected as `aurora_app`** execute `INSERT` (must succeed — the saga has to be able to write the trail) and `UPDATE`/`DELETE` (must return `42501`). Fails in both directions, and B-05's integration suite already fires on a catalog table with no recorded decision (`security-B-05.md` probe 5) |
| 3 | Each entry in `AppRolePrivileges` names its **writer** — `operator_audit_event`: the provisioning saga (B-07.1, B-07.4) and ADR-0010 rule 8's support-access path; `erasure_replay_log`: ADR-0007 §11.5's erasure path. **And — new in v6 — an entry for a partitioned table records the expected privilege set of its partitions as *empty*, not as the parent's.** | A test asserts **every** entry in the allowlist names a non-empty writer, so the next catalog table cannot record a privilege without saying who needs it (`security-B-05.md` H-2: a decision that cannot name its writer has not been made). If B-05's rework has not already introduced the writer field, this row introduces it and fills it for the five existing entries. **On the partition expectation:** executed on 17.11, a partition of a `catalog` table comes out with `relacl` **null** and stays unreachable — a routed `INSERT` through the parent succeeds, because a routed insert is checked against the **parent's** ACL, while a direct `INSERT`, `SELECT` or `TRUNCATE` on the partition as `aurora_app` returns `42501`. That is **tighter** than `audit`, where item 1 criterion 3's schema-wide default grant gives every partition `{SELECT, INSERT}` — so unless this allow-list says *empty* out loud, `audit`'s expectation is what gets copied across and someone grants on a partition. The entry must be **present and empty**, not absent: absent and empty are indistinguishable to a comparison that enumerates only what the list mentions, which is how the `audit` shape arrives unnoticed. **Demonstrated failing** by granting `SELECT, INSERT` on one partition — that relation goes from `0` recorded privileges to `2` and becomes directly insertable (executed) |
| 4 | A `BEFORE UPDATE OR DELETE … FOR EACH ROW` guard raising `42501` on both tables, created in the same migration as the table and enabled **`ALWAYS`** (ADR-0028 Amendment 2) | Probed **as `aurora_migrator`** — the owner, whom privileges do not restrain — **inside a transaction that is rolled back**, requiring SQLSTATE `42501` and not merely *an* exception. Remove or disable the guard and the probe gets `UPDATE 1` instead of `42501`; replace the guard **function's body** with `CREATE OR REPLACE FUNCTION … RETURN NULL` and every presence check still reports green while the `UPDATE` succeeds — which is why the probe executes the statement, and why it rolls back. Neither table is partitioned, so item 1's clone and truncate-trigger questions do not arise here; say that in the migration rather than leaving a reader to wonder why criterion 5 is absent. **When a partitioned append-only table does join `catalog` — `catalog.authentication_event` is one, created by `B-18.9`'s own additive migration, which is how the project-manager answered ADR-0029 A2.8's ownership question when it assigned `B-19` to this item — ADR-0028 Amendment 2's clause applies to it unchanged:** the `BEFORE TRUNCATE` guard on the parent **and on every partition including `DEFAULT`**, created by the statement that creates or attaches the partition, with item 1 criterion 5's effect probe over `pg_partition_tree` and its `tgenabled = 'A'` locator. This is not hypothetical: executed on 17.11 in a `catalog` schema with no default privileges, `TRUNCATE catalog.authentication_event_default` as `aurora_migrator` emptied the partition past the parent's guard — the third ADR-0029 security review's finding, reproduced |
| 5 | **`pg_default_acl` holds zero rows for schema `catalog`** | Item 1's criterion 3 must **not** be copied to `catalog`, whose tables legitimately need `UPDATE`; this asserts that as an executable check. **What it catches:** the `GRANT` direction. `ALTER DEFAULT PRIVILEGES … IN SCHEMA catalog … GRANT … TO aurora_app` takes the count from 0 to 1 — B-05's M-1 blanket-grant shape, and what a well-meaning author produces by copying criterion 3 across. **What it does not catch, verified on 17.11:** the `REVOKE` direction writes **no row**. Three shapes were tried — `REVOKE UPDATE, DELETE … FROM aurora_app`, `REVOKE ALL … FROM PUBLIC`, and the form without `FOR ROLE` — and all three left the count at 0, because no role holds a default privilege on a new table for a `REVOKE` to remove; the only row a `REVOKE` can affect is one an earlier `GRANT` created, which it deletes (count back to 0). So **the no-op `ALTER DEFAULT PRIVILEGES … REVOKE` that ADR-0028 Amendment 1 withdrew from B-16.1 is invisible to this criterion, and to every other criterion in this item.** That is survivable rather than a hole because criterion 2 asserts the resulting ACL of every catalog relation and never which statement produced it: a decorative `REVOKE` above a correct grant changes nothing, and a wrong grant fails criterion 2 whether or not one sits above it |
| 6 | Every probe reports **how many** catalog relations and ACL entries it compared, and fails on zero | `CLAUDE.md` self-check #2, same as item 1 criterion 6 |

**Depends on B-05** (the catalog database, its migrations and `CatalogSchemaAllowlist`). **B-07.1 and B-07.4 depend on this row** — SPEC-001 BR-7 has them writing to a table nothing creates — as does the tail-truncation follow-up above. Area **Platform/Tenancy**, not Platform/Audit: this is a catalog migration and the catalog belongs to `Aurora.Platform.Tenancy`.
