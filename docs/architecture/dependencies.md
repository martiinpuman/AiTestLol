# Third-party dependencies

Status: accepted **v2** · Author: architect · Date: 2026-09-11
Companion: `solution-layout.md` §5.2 stage 4 (`scripts/check-dependencies.sh`), `dependency-closure.md` (generated, §7), `../decisions/ADR-0026-dependency-supply-chain-gate.md`, `../decisions/ADR-0020-test-tooling.md`

**Changes in v2** (2026-09-11, from the B-01 peer review): §1 rule 2 rewritten — the name check is now two-tier and the licence check covers the whole closure (finding **S-1**, decided in ADR-0026); §3 records the two version pins B-01 resolved (finding **m-1**); §7 added — the specification of `dependency-closure.md`.

---

## 1. The rule

`CLAUDE.md`: **a new third-party dependency needs a permissive licence (MIT, Apache-2.0, BSD) and an entry in this file.** Enforced, not requested:

1. `Directory.Packages.props` holds every version; `packages.lock.json` is committed; `dotnet restore --locked-mode` runs in `verify.sh` stage 1. Nothing below is trustworthy without this: the gate reads the restored graph, so the graph has to be reproducible.
2. **`verify.sh` stage 4 governs the package graph in two tiers** (ADR-0026). The two tiers exist because a package we *chose* and a package that merely *arrived* carry different amounts of information:
   - **Direct** — any package with a `PackageReference` in a project. It must appear in §2 or §3 of this file, with a purpose, an owning ADR and a verification date. A direct package that is not listed **fails the gate**.
   - **Transitive** — arrives only because a direct package depends on it. It must appear in **`dependency-closure.md`**, an exact, generated, licence-verified allowlist (§7). A transitive package that is not listed, or a listed package that is no longer in the graph, **fails the gate**.
3. **The licence check applies to both tiers, and reads the resolved package's own `.nuspec`** — never the version string this document remembers. That is what catches a licence changing under a package we already ship, which is the failure this gate mainly exists for.
4. **Stage 4 fails when any of these is true** (ADR-0026):
   1. a direct package is absent from §2/§3;
   2. a transitive package is absent from `dependency-closure.md`;
   3. `dependency-closure.md` lists a package that is no longer in the graph — the allowlist is exact, not a superset, because a stale row silently pre-approves;
   4. a resolved licence differs from the licence recorded for that package in either file;
   5. a resolved licence is not on the accepted SPDX list below;
   6. a package id from the rejection list in §5 appears anywhere in the graph, at any version, direct or transitive.

   Stage 4 also fails if `dependency-closure.md` is missing or unparseable, and it **never writes to either file**: regeneration is a separate explicit command (§7.3). A gate must not silently fix what it is measuring.
5. Stage 5 fails on any High or Critical advisory.
6. **Licences are re-verified at every milestone health check.** 2025–2026 proved that a licence is not a property you check once: AutoMapper, MediatR, MassTransit and FluentAssertions all moved to commercial terms during that window.

> **Why two lists, and not one** (correction of the v1 rule, B-01 review finding S-1, 2026-09-11).
> The v1 rule required *every* package including transitive to be listed here. Measured against the real graph, B-01's nine lock files hold **66 distinct packages: 7 referenced directly, 59 present only transitively**, of which **57 were absent from this file**. All 57 arrive through approved direct packages (Testcontainers, bUnit, Shouldly, the test SDK) and are permissively licensed. The gate would therefore have been **red on its first run against a correct codebase**, and the only way to green it would have been to hand-write 57 rows describing decisions nobody made.
> That is not merely inconvenient. A gate that fails for a reason nobody believes in gets weakened, filtered or skipped — and then it is no longer there for the case that costs money: a dependency quietly moving to a commercial licence. Six of the nine packages in §5 were rejected on exactly that ground.
> Narrowing the rule to direct references only was the obvious fix and was rejected: it would let a **new** transitive package enter silently, and an unnoticed new dependency is how supply-chain risk usually starts. The two-tier form keeps the human attention on the packages we actually chose, while every addition, removal or licence change anywhere in the closure still shows up as a reviewable diff in the pull request that caused it. Options and reasoning: **ADR-0026**.

**Accepted SPDX identifiers:** `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `PostgreSQL` (a permissive BSD-style licence; spelled out here because it is not one of the three names in `CLAUDE.md` and would otherwise be re-flagged at every review).

**Not accepted:** GPL, LGPL, AGPL, SSPL, RSAL, "source-available", dual-licence schemes with a commercial trigger, and anything requiring a per-seat or per-deployment payment.

All entries verified **2026-09-11** against nuget.org unless noted.

---

## 2. Runtime dependencies

### 2.1 Framework and data

| Package | Version | Licence | Used for | ADR |
|---|---|---|---|---|
| `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore.Design` | 10.0.12 | MIT | ORM, migrations | ADR-0003 |
| `Npgsql` | 10.0.3 | **PostgreSQL** | PostgreSQL driver, data sources, pooling | ADR-0004 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | **PostgreSQL** | EF Core provider | ADR-0003 |
| `System.Reflection.MetadataLoadContext` | 10.0.12 | MIT | Reading a Country Package manifest **without executing its code** | ADR-0008 §3.2 |
| `NuGet.Versioning` | 7.9.0 | Apache-2.0 | Parsing `coreContractRange` version ranges | ADR-0008 §3.2 |

### 2.2 Web, API and UI

| Package | Version | Licence | Used for | ADR |
|---|---|---|---|---|
| `Microsoft.AspNetCore.App` (shared framework) | 10.0.12 | MIT | Blazor Server, SignalR, minimal APIs, rate limiting, health checks | ADR-0005 |
| `Microsoft.AspNetCore.Components.QuickGrid` | 10.0.12 | MIT | The grid engine under our own grid chrome | ADR-0024 |
| `Microsoft.AspNetCore.Components.QuickGrid.EntityFrameworkAdapter` | 10.0.12 | MIT | Async EF paging for QuickGrid | ADR-0024 |
| `Microsoft.AspNetCore.OpenApi` | 10.0.12 | MIT | OpenAPI document generation | ADR-0013 |
| `Asp.Versioning.Http` | 10.2.3 | MIT | URL-path API versioning | ADR-0013 |
| `Asp.Versioning.Mvc.ApiExplorer` | 10.2.1 | MIT | Versioned OpenAPI documents | ADR-0013 |

### 2.3 Identity and authorization

| Package | Version | Licence | Used for | ADR |
|---|---|---|---|---|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.12 | MIT | Credential storage, hashing, lockout, TOTP MFA | ADR-0009 |
| `OpenIddict.Server.AspNetCore` | 7.7.0 | Apache-2.0 | OAuth 2.0 / OIDC token issuance for the public API | ADR-0009 |
| `OpenIddict.Validation.AspNetCore` | 7.7.0 | Apache-2.0 | Token validation | ADR-0009 |
| `OpenIddict.EntityFrameworkCore` | 7.7.0 | Apache-2.0 | OpenIddict stores in the catalog database | ADR-0009 |

**Nothing in this table is needed for the bootstrap identity and authorization path.** Interactive sign-in, cookie authentication, password hashing, lockout, the Identity store interfaces, `RevalidatingServerAuthenticationStateProvider` and `CircuitHandler` all ship in the `Microsoft.AspNetCore.App` 10.0.12 shared framework — verified in this environment on 2026-09-11 (`Microsoft.Extensions.Identity.Core.dll`, `Microsoft.Extensions.Identity.Stores.dll`, `Microsoft.AspNetCore.Identity.dll`, `Microsoft.AspNetCore.Authentication.Cookies.dll`, `Microsoft.AspNetCore.Components.Server.dll`). `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is listed here for the OpenIddict/public-API work and is **deliberately not referenced by `Aurora.Platform.Identity`**: it exists to provide `IdentityDbContext` over ASP.NET Core Identity's own schema, which `catalog.identity_user` + `catalog.identity_credential` deliberately is not (ADR-0029 §2). The three OpenIddict rows are for the public-API token path, which is not bootstrap scope.

### 2.4 Platform services

| Package | Version | Licence | Used for | ADR |
|---|---|---|---|---|
| `Quartz.Extensions.Hosting` | 4.0.1 | Apache-2.0 | Scheduling; clustered ADO job store in the catalog. 4.0 released 2026-09-03, targets .NET 10 | ADR-0014 |
| `Quartz.Serialization.SystemTextJson` | 4.0.1 | Apache-2.0 | Job-data serialization | ADR-0014 |
| `Microsoft.Extensions.Caching.Hybrid` | 10.10.0 | MIT | L1 cache with stampede protection; L2 seam | ADR-0012 |
| `Microsoft.FeatureManagement` | 4.7.0 | MIT | Feature flags with a catalog-backed definition provider | ADR-0011 |
| `Microsoft.Extensions.Http.Resilience` | 10.10.0 | MIT | Retry, timeout and circuit breaker on outbound HTTP (webhooks, tax authority, Peppol) | ADR-0013 |
| `FluentValidation` | 12.1.1 | Apache-2.0 | Boundary validation. Note: v12 removed `FluentValidation.AspNetCore` auto-validation; we wire a pipeline behaviour explicitly | ADR-0017 |
| `FluentValidation.DependencyInjectionExtensions` | 12.1.1 | Apache-2.0 | Validator discovery | ADR-0017 |
| `Riok.Mapperly` | 4.3.1 | Apache-2.0 | Compile-time source-generated mapping | ADR-0019 |

### 2.5 Observability

| Package | Version | Licence | Used for | ADR |
|---|---|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.18.0 | Apache-2.0 | Traces and metrics wiring | ADR-0016 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.18.0 | Apache-2.0 | OTLP export | ADR-0016 |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.18.0 | Apache-2.0 | HTTP server spans | ADR-0016 |
| `OpenTelemetry.Instrumentation.Http` | 1.18.0 | Apache-2.0 | HTTP client spans | ADR-0016 |
| `OpenTelemetry.Instrumentation.Runtime` | 1.18.0 | Apache-2.0 | GC, thread pool, exception metrics | ADR-0016 |
| `Npgsql.OpenTelemetry` | 10.0.3 | **PostgreSQL** | Database spans with command text redacted | ADR-0016 |
| `Serilog.AspNetCore` | 10.0.0 | Apache-2.0 | Structured logging provider behind `ILogger<T>` | ADR-0016 |
| `Serilog.Sinks.Console` | 6.1.1 | Apache-2.0 | Local development sink | ADR-0016 |
| `Serilog.Sinks.OpenTelemetry` | 4.2.0 | Apache-2.0 | Log export over OTLP | ADR-0016 |
| `Microsoft.Extensions.Compliance.Redaction` | 10.10.0 | MIT | Redaction of `[PersonalData]` before a sink sees it | ADR-0016 §3, ADR-0018 |

---

## 3. Test dependencies

| Package | Version | Licence | Used for | ADR |
|---|---|---|---|---|
| `xunit` | 2.9.3 | Apache-2.0 | Test framework (version verified in this environment). xUnit v3 (`xunit.v3` 4.0.0, Apache-2.0) is the forward path — backlog item | ADR-0020 |
| `xunit.runner.visualstudio` | **3.1.4** | Apache-2.0 | Test runner. 3.x runs both xUnit v2 and v3 test projects, so it does **not** track the framework major — see the note below | ADR-0020 |
| `Microsoft.NET.Test.Sdk` | 18.10.0 | MIT | Test host | ADR-0020 |
| `Shouldly` | 4.3.0 | BSD-3-Clause | Assertions | ADR-0020 |
| `NSubstitute` | 6.2.0 | BSD-3-Clause | Test doubles | ADR-0020 |
| `Testcontainers.PostgreSql` | 4.15.0 | MIT | Real PostgreSQL in integration tests (`postgres:17-alpine`) | ADR-0020 |
| `FsCheck` | 3.4.0 | BSD-3-Clause | Property-based tests for money, rounding, allocation, FIFO layers | ADR-0020, ADR-0021 |
| `Verify.Xunit` | 31.12.5 | MIT | Snapshot tests: e-invoice XML, statutory reports, OpenAPI, migration SQL | ADR-0020 |
| `bunit` | 2.10.3 | MIT | Blazor component tests | ADR-0020 |
| `Microsoft.Extensions.TimeProvider.Testing` | 10.10.0 | MIT | `FakeTimeProvider` for effective-dated and period logic | ADR-0020 |
| `coverlet.collector` | 10.0.1 | MIT | Coverage collection | ADR-0020 |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | **5.6.0** | MIT | Approved-API snapshot for `Aurora.Countries.Contracts` and event contracts | ADR-0008 §3.1 |
| `Bogus` | 35.6.5 | MIT — **confirm at adoption**: NuGet exposes a licence *file*, not an SPDX expression. Read `LICENSE` in the package and record the exact text here before first use. This is a **class B** package in the sense of §7.2 | Volume test data for performance guard rails | ADR-0020 |

**No package provides the architecture fitness tests.** `tests/Aurora.Architecture.Tests` reads IL metadata with `System.Reflection.Metadata`, which ships in the .NET 10 shared framework — the project declares no `PackageReference` for it and builds. `TngTech.ArchUnitNET` was ADR-0020's choice, was never referenced by any project, and is **withdrawn by ADR-0030**; see §5.1.

**Two pins recorded 2026-09-11** (B-01 review finding m-1). Both were previously written here as a rule of thumb rather than a version, which §6 step 5 forbids. B-01 resolved them; the licences were verified the same day by reading the published `.nuspec` from `api.nuget.org/v3-flatcontainer`, which is the same artifact stage 4 reads (§7.2):

- `xunit.runner.visualstudio` **3.1.4** — `<license type="expression">Apache-2.0</license>`, published 2025-08-16, repository `github.com/xunit/visualstudio.xunit`.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` **5.6.0** — `<license type="expression">MIT</license>`, published 2026-07-02, repository `github.com/dotnet/roslyn`. 5.11.0 exists only as a prerelease; `AllowPrerelease` is false, so 5.6.0 is the current stable.

**Correction to a note in ADR-0020.** ADR-0020's options table says *"the test-runner package major must match the framework major"*. That was true of `xunit.runner.visualstudio` 2.x and is **not** true from 3.0 onward: the 3.x runner runs xUnit v2 and v3 test projects alike (it advertises support back to 1.9.2). So `xunit.runner.visualstudio` **3.1.4** alongside `xunit` **2.9.3** is correct, not a mismatch, and must not be "fixed" by downgrading the runner. ADR-0020's decision — use xUnit with this runner — is unchanged, so it is not superseded; only the version-selection heuristic in its supporting text is wrong, and the correction is recorded here rather than by editing an accepted ADR.

---

## 4. Container images

| Image | Tag | Licence / terms | Used for |
|---|---|---|---|
| `postgres` | `17-alpine` | PostgreSQL licence | Integration tests (pinned; `PostgreSqlBuilder`'s parameterless constructor is obsolete in Testcontainers 4.15.0), and local `docker compose` |
| `mcr.microsoft.com/dotnet/aspnet` | `10.0-noble-chiseled` (**ICU-bearing variant**) | Microsoft container licence terms (redistributable) | Runtime image. `InvariantGlobalization=false` requires ICU — a non-ICU base is not an option (ADR-0022, ADR-0025) |
| `mcr.microsoft.com/dotnet/sdk` | `10.0` | Microsoft container licence terms | Build stage only; never shipped |
| `pgbouncer` (official or distribution build) | ≥ 1.21 | ISC | Connection pooling from stage S2. Version ≥ 1.21 matters if prepared statements are ever enabled (`scalability.md` §4.3) |

---

## 5. Rejected on licensing — do not propose these again

The whole point of this section is that a future developer reaching for the familiar package finds the answer here instead of re-discovering it in review.

| Package | Licence status (verified 2026-09-11) | What we use instead |
|---|---|---|
| **AutoMapper** | Commercial since 2025 | `Riok.Mapperly` (Apache-2.0) — ADR-0019 |
| **MediatR** | Commercial since 2025 | A ~100-line in-process dispatcher we own, over module contract interfaces — ADR-0006, ADR-0015 |
| **MassTransit** (v9+) | Commercial; v8 security patches through 2026 | Our own outbox and dispatcher; no broker at bootstrap — ADR-0015 |
| **FluentAssertions** (v8+) | Commercial (Xceed partnership, January 2025; ~USD 130 per seat). v7.x stays Apache-2.0 but is a dead end | `Shouldly` (BSD-3-Clause) — ADR-0020 |
| **Hangfire** | LGPLv3 — copyleft, not permissive | `Quartz.NET` (Apache-2.0) + our own per-tenant queue — ADR-0014 |
| **Duende IdentityServer** | Commercial; tiers reported at USD 5,750 / 12,500 / 24,900 per year (2026) | `OpenIddict` (Apache-2.0) — ADR-0009 |
| **Redis** (server, 7.4+) | RSALv2 / SSPL — not permissive | **Valkey** (BSD-3-Clause) if and when an L2 cache is needed — ADR-0012 |
| **Moq** | MIT, but the 4.20 SponsorLink episode showed data-collecting behaviour shipped in a patch release | `NSubstitute` (BSD-3-Clause) — ADR-0020. Rejected on maintainer predictability, not licence text |
| **NetArchTest.Rules** | MIT, but last published 2021 — unmaintained | Nothing: architecture rules read IL metadata with the in-box `System.Reflection.Metadata` — **ADR-0030**. Rejected on maintenance, not licence |
| Commercial Blazor suites (Telerik, Syncfusion, DevExpress) | Commercial, per developer | Own component library over QuickGrid — ADR-0024 |

**Machine-readable rejection list.** `check-dependencies.sh` fails if any of these ids appears anywhere in the graph, at any version, direct or transitive (§1 rule 4.6). A trailing `*` is a prefix match. Keep this block in sync with **the table above and §5.1** — the list is "must not reach the dependency graph", which covers both the licence rejections and the withdrawals; the script reads this block, because the tables' prose cells are for humans.

```
AutoMapper*
MediatR
MediatR.Extensions.*
MassTransit*
FluentAssertions*
Hangfire*
Duende.IdentityServer*
Moq
NetArchTest.*
TngTech.ArchUnitNET*
Telerik.*
Syncfusion.*
DevExpress.*
```

Three deliberate precisions. `Duende.IdentityServer*` is matched rather than `Duende.*`, because `Duende.IdentityModel` — the renamed, Apache-2.0 `IdentityModel` library — is not commercial; it is simply not adopted, and would need a §2 row before use. **Redis** is rejected as a *server* (RSALv2/SSPL); its .NET clients (`StackExchange.Redis`, `Microsoft.Extensions.Caching.StackExchangeRedis`) are MIT and are **not** rejected — they also speak to Valkey, which is the replacement in ADR-0012. And `FluentValidation` is unrelated to `FluentAssertions` despite the name: it is Apache-2.0, approved, and listed in §2.4.

### 5.1 Withdrawn after adoption — removed, do not reintroduce without an ADR

Not a licence problem. These were approved in an ADR, never acquired a consumer, and are now removed, because an approved dependency with no consumer is an invitation in the repository's only memory: the next developer reads the ADR, adds the reference, and the solution carries two mechanisms for one job.

| Package | Approved by | Withdrawn by | Why | What does the job instead |
|---|---|---|---|---|
| `TngTech.ArchUnitNET`, `TngTech.ArchUnitNET.xUnit` (0.13.4, Apache-2.0) | ADR-0020 | **ADR-0030** (2026-09-11) | B-04 built the rule set on `System.Reflection.Metadata` and no project ever referenced these. The `PackageVersion` pins stayed in `Directory.Packages.props` with no `PackageReference`, which restores nothing — the cost was documentation drift, and the exposure would have started the moment somebody followed the stale ADR row | `tests/Aurora.Architecture.Tests`, reading IL metadata and project files (ADR-0030) |

**What enforces this, and what does not yet.** The ids are in the machine-readable block above, which `scripts/check-dependencies.sh` reads at `verify.sh` stage 4 — a script and a stage that **do not exist yet** (owned by B-11). Until they do, nothing mechanically stops a reintroduction, and what stands in the way is documentation alone. The ADR row is gone; **both `PackageVersion` pins are still in `Directory.Packages.props`** and are removed by the Light-tier task in `solution-layout.md` §6.4 item 2, sequenced after B-04 merges. Until that task runs, a pin with no `PackageReference` restores nothing — but it is still the invitation this section exists to withdraw, and this table is the only thing that says so.

---

## 6. Adding a dependency — the checklist

1. Does something already in this list do the job? Prefer one library used well over two used partly.
2. Verify the **SPDX licence on nuget.org** and, for anything ambiguous (a licence *file* rather than an expression), read the file. Record the date.
3. Check it is **actively maintained**: a release within the last 12 months, and an issue tracker with responses.
4. Check what it **drags in**. Run `scripts/check-dependencies.sh --update-closure` after restoring and read the added rows in `dependency-closure.md`: those are packages you are adopting too, even though you did not choose them. A direct package with a large or surprising fan-out is itself an argument against the package.
5. Add the version to `Directory.Packages.props`, commit the updated `packages.lock.json`, add a row here with version, SPDX licence, purpose, ADR and verification date — and commit the regenerated `dependency-closure.md` in the same change. Reviewers look at the closure diff, not just at your new row.
6. If it is architecturally significant (hard to reverse, constrains other teams, or introduces a runtime), it needs an ADR as well as a row.

---

## 7. `dependency-closure.md` — the transitive allowlist

Specified here, implemented in **B-11**. Decision and alternatives: **ADR-0026**.

### 7.1 What it is

`docs/architecture/dependency-closure.md` is a **generated** file holding one row per `(package id, resolved version)` in the transitive tier. It is not a decision record — decisions live in §2 and §3 — it is *evidence*: these packages are in the product, this is the licence each carried at the last restore, and this is the direct package that brought it in. Its purpose is to make any change to the closure visible in the pull request that caused it.

Row format, sorted by id then version, case-insensitive, one flat table (grouping invites merge conflicts):

| Package | Version | SPDX | Source | Arrives via | Verified |
|---|---|---|---|---|---|
| `AngleSharp` | 1.8.0 | MIT | `nuspec-expression` | `bunit` | 2026-09-11 |
| `xunit.abstractions` | 2.0.3 | Apache-2.0 | `human:licenseUrl` | `xunit` | 2026-09-11 |

The key is `(id, version)`, not id. A solution may legitimately resolve one package at two versions; B-01's graph already does, twice — `Microsoft.Extensions.DependencyInjection.Abstractions` at **8.0.2 and 10.0.10**, and `Microsoft.Extensions.Logging.Abstractions` at **8.0.3 and 10.0.10**. A one-row-per-id format would have been wrong on its first day.

*Arrives via* names the direct-tier package(s) whose graph contains the row, comma-separated and sorted. It is there so a reviewer can judge the fan-out: a direct package that drags in fifteen others is an argument against that package.

### 7.2 Two licence classes

| Class | Nuspec shape | Handling |
|---|---|---|
| **A** | `<license type="expression">SPDX</license>` | The script resolves it. No human involved. `Source` = `nuspec-expression` |
| **B** | `<license type="file">…</license>`, only a legacy `<licenseUrl>`, or no licence element at all | The script **cannot** decide. A human reads the licence text and records the SPDX id, with `Source` = `human:file:<path>` or `human:licenseUrl`, and the date in `Verified`. **Stage 4 fails if a class B row's version is not the resolved version** — a new version can ship a different licence file, and the earlier human verification does not carry over |

The two classes apply to **both tiers** — the licence of a direct package is read from its nuspec too. For a direct class B package the human-verified SPDX id and date go in its §2/§3 row instead of in the closure file; `Bogus` (§3) is the example waiting to happen.

Class B is real but rare, and the manual surface is bounded. Measured on B-01's restored package cache (2026-09-11): **207 of 208** nuspecs carry an SPDX expression; the one exception is `xunit.abstractions` 2.0.3, which has only `<licenseUrl>https://raw.githubusercontent.com/xunit/xunit/master/license.txt</licenseUrl>` (the Apache-2.0 text).

### 7.3 How `check-dependencies.sh` reads the graph — offline, no extra dependency

Both inputs are already on disk after `verify.sh` stage 1, so stage 4 needs **no network and no third-party tool**:

1. **Every project's committed `packages.lock.json`** gives id, resolved version and `"type"`:
   - `"Direct"` → direct tier, checked against §2/§3.
   - `"Transitive"` and `"CentralTransitive"` → transitive tier, checked against `dependency-closure.md`. `CentralTransitive` means *we pinned the version*, not that we chose the dependency, so it belongs in the closure like any other transitive package.
   - `"Project"` → a project reference. Skip.
   - Union across every project and every target framework.
   - **Trap:** `UseArtifactsOutput` copies a project's lock file into `artifacts/bin/<project>/<config>/`, so a naive `**/packages.lock.json` glob reads stale duplicates. Enumerate the projects in `Aurora.sln`, or exclude `artifacts/` explicitly. This was observed on B-01.
2. **`${NUGET_PACKAGES:-$HOME/.nuget/packages}/<id-lower>/<version-lower>/<id-lower>.nuspec`** gives the licence element. Restore has already extracted every package. If a nuspec is missing, fail with "restore first" — never reach for the network to answer a licence question inside the gate.

`dotnet list package --include-transitive` is deliberately **not** the input: it requires an evaluation pass, carries no licence data, and classifies less precisely than the lock file.

**Two modes, and only one of them writes:**

| Mode | Behaviour |
|---|---|
| default — what stage 4 runs | Compare; report; fail per §1 rule 4. **Writes nothing.** |
| `--update-closure` | Rewrite `dependency-closure.md` from the current graph, preserving class B rows whose version is unchanged. Run by a developer, deliberately, as part of the change that moved a dependency. Never run by `verify.sh`, never in CI |

A stage 4 failure must name the package, the version, the tier and the exact next step — for a new transitive package that is *"run `scripts/check-dependencies.sh --update-closure`, then review the added rows and their licences"*.

**Parsing rules for this file, so the tables stay machine-readable.** In §2 and §3:

- the *Package* cell holds one or more **full** package ids, each in backticks, comma-separated; version and licence apply to all ids in the cell. No shorthand such as `(+ .Relational)` — it was removed for exactly this reason;
- the *Licence* cell **begins** with the SPDX identifier (bold markers stripped); anything after an em dash is prose for humans;
- the *Version* cell holds a single exact version.

§4 (container images) is out of scope for the script — those are not NuGet packages. `Microsoft.AspNetCore.App` in §2.2 is a `FrameworkReference` and never appears in a lock file; it is listed for the reader, and the script must not expect it. The rejection check of §1 rule 4.6 reads the fenced id list at the end of §5, not the prose table.

**The two lists are asymmetric, on purpose:**

- §2/§3 is a **superset** — an approved-for-use list. It legitimately contains packages no project references yet (B-01 centrally pins about thirty for exactly this reason). An unused row is not a failure.
- `dependency-closure.md` is **exact** — every row must be in the graph and every transitive package must have a row. A stale row silently pre-approves a package nobody is using, which is the hole this file exists to close.

### 7.4 Ownership of a generated file in an architect-owned folder

`CLAUDE.md` gives `docs/architecture/` to the architect, and it does not anticipate a machine-generated file in a docs folder. The carve-out, decided in ADR-0026 and pending a `HUMAN_INBOX` answer:

- **Senior developers may regenerate `dependency-closure.md`** with `scripts/check-dependencies.sh --update-closure` as part of any task that changes a dependency, and must commit it with that change.
- **Nobody hand-edits it** — except to fill in the SPDX id of a class B row, which is the one field a machine cannot produce.
- The **architect owns the rule** (this file and ADR-0026) and re-reads the closure at every milestone health check.

It lives in `docs/architecture/` rather than beside the code because its readers are reviewers and the architect, and because `CLAUDE.md`'s premise is that the repository is the team's only memory: the licence evidence has to sit next to the licence rule.

### 7.5 First generation

`dependency-closure.md` does not exist yet, so stage 4 is expected to fail the first time it runs — with the message above, which is the correct behaviour for a missing allowlist. B-11 generates it and its reviewer performs the one review that matters: reading roughly **59 rows** and confirming that every licence is on the accepted list and that nothing on the §5 rejection list appears. After that first review the file maintains itself, one small diff at a time.
