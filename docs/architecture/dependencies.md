# Third-party dependencies

Status: accepted v1 · Author: architect · Date: 2026-09-11
Companion: `solution-layout.md` §5.2 stage 4 (`scripts/check-dependencies.sh`), `../decisions/ADR-0020-test-tooling.md`

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
| `Microsoft.EntityFrameworkCore` (+ `.Relational`, `.Design`) | 10.0.12 | MIT | ORM, migrations | ADR-0003 |
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
| `xunit.runner.visualstudio` | matching the framework major | Apache-2.0 | Test runner | ADR-0020 |
| `Microsoft.NET.Test.Sdk` | 18.10.0 | MIT | Test host | ADR-0020 |
| `Shouldly` | 4.3.0 | BSD-3-Clause | Assertions | ADR-0020 |
| `NSubstitute` | 6.2.0 | BSD-3-Clause | Test doubles | ADR-0020 |
| `Testcontainers.PostgreSql` | 4.15.0 | MIT | Real PostgreSQL in integration tests (`postgres:17-alpine`) | ADR-0020 |
| `TngTech.ArchUnitNET` + `.xUnit` | 0.13.4 | Apache-2.0 | Architecture fitness tests | ADR-0020 |
| `FsCheck` | 3.4.0 | BSD-3-Clause | Property-based tests for money, rounding, allocation, FIFO layers | ADR-0020, ADR-0021 |
| `Verify.Xunit` | 31.12.5 | MIT | Snapshot tests: e-invoice XML, statutory reports, OpenAPI, migration SQL | ADR-0020 |
| `bunit` | 2.10.3 | MIT | Blazor component tests | ADR-0020 |
| `Microsoft.Extensions.TimeProvider.Testing` | 10.10.0 | MIT | `FakeTimeProvider` for effective-dated and period logic | ADR-0020 |
| `coverlet.collector` | 10.0.1 | MIT | Coverage collection | ADR-0020 |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | latest stable | MIT | Approved-API snapshot for `Aurora.Countries.Contracts` and event contracts | ADR-0008 §3.1 |
| `Bogus` | 35.6.5 | MIT — **confirm at adoption**: NuGet exposes a licence *file*, not an SPDX expression. Read `LICENSE` in the package and record the exact text here before first use | Volume test data for performance guard rails | ADR-0020 |

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
| **NetArchTest.Rules** | MIT, but last published 2021 — unmaintained | `TngTech.ArchUnitNET` (Apache-2.0) — ADR-0020. Rejected on maintenance, not licence |
| Commercial Blazor suites (Telerik, Syncfusion, DevExpress) | Commercial, per developer | Own component library over QuickGrid — ADR-0024 |

---

## 6. Adding a dependency — the checklist

1. Does something already in this list do the job? Prefer one library used well over two used partly.
2. Verify the **SPDX licence on nuget.org** and, for anything ambiguous (a licence *file* rather than an expression), read the file. Record the date.
3. Check it is **actively maintained**: a release within the last 12 months, and an issue tracker with responses.
4. Check the transitive graph — `CentralPackageTransitivePinningEnabled` means a transitive package is a decision too, and stage 4 will fail on it.
5. Add the version to `Directory.Packages.props`, commit the updated `packages.lock.json`, add a row here with version, SPDX licence, purpose, ADR and verification date.
6. If it is architecturally significant (hard to reverse, constrains other teams, or introduces a runtime), it needs an ADR as well as a row.
