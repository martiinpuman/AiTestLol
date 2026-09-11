# ADR-0002 — Backend platform: .NET 10 (LTS) and C#

- **Status:** Accepted (2026-09-10) — **locked by the product owner**, see `docs/HUMAN_INBOX.md`
- **Deciders:** product owner (choice), architect (consequences)

## Context

Aurora ERP is long-lived business software: financial correctness, a decade of maintenance, a hiring pool that will still exist in 2036, and a need for strong static typing around money, tax and ledger types. The runtime must support a Blazor Server UI, EF Core against PostgreSQL, background workers and a versioned REST API in one solution.

The environment is already verified: SDK 10.0.401, ASP.NET Core runtime 10.0.12.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **.NET 10 (LTS), C#** *(chosen)* | LTS with support to **2028-11-10**; first-class EF Core, Blazor, DI, configuration, health checks, OpenTelemetry and OpenAPI in the box; `decimal` is a native 128-bit base-10 type, which matters more for an ERP than it does for most software; strong nullable-reference and analyzer story; large hiring pool; single vendor with a predictable annual cadence (even = LTS, 3 years) | Microsoft-controlled roadmap; annual major upgrades are non-optional over a decade; Blazor Server's constraints come attached (ADR-0005) |
| JVM (Java 21 LTS / Kotlin + Spring Boot) | Comparable maturity and hiring pool; longer vendor-neutral LTS options; excellent Postgres ecosystem | `BigDecimal` is more awkward than `decimal` for pervasive money arithmetic; no Blazor equivalent, so the locked UI choice would be impossible; team and tooling here are .NET |
| TypeScript / Node | Fastest iteration; one language across UI and server | Numeric type is IEEE-754 double by default — actively hostile to a general ledger; weaker long-lived-codebase ergonomics; the locked UI choice is impossible |
| Go | Excellent operational profile, tiny containers | No mature ORM comparable to EF Core; no `decimal` in the standard library; generics ergonomics for a rich domain model are still weak |

## Decision

Build on **.NET 10 (LTS)** with **C#**, targeting `net10.0`. Language features required project-wide: nullable reference types enabled, `TreatWarningsAsErrors`, implicit usings off (explicit is clearer for a large team), file-scoped namespaces, and records for value objects.

## Consequences

- **The architect agrees with this choice.** No disagreement to record.
- Support for .NET 10 ends **2028-11-10**. The upgrade to the next LTS (.NET 12, expected November 2027, supported to 2030) must be scheduled as a milestone, not discovered as an incident. `Directory.Build.props` holds the TFM in exactly one place so the upgrade is a one-line change plus a test run.
- Central package management (`Directory.Packages.props`) plus committed `packages.lock.json` files are mandatory so that `dotnet restore --locked-mode` in `verify.sh` makes the dependency graph reproducible and auditable — this is what makes the licence gate in `dependencies.md` enforceable rather than aspirational.
- `decimal` is available everywhere, so ADR-0021's money rules cost nothing to enforce. An architecture test bans `double` and `float` in domain and application projects.
- We inherit Microsoft's annual breaking-change cadence in ASP.NET Core and EF Core. Mitigation: pin exact versions, upgrade deliberately once per LTS, and keep the domain layer free of framework types so the blast radius of a framework upgrade is the outer rings only.

## Revisit when

Superseded by the next LTS upgrade ADR (expected 2027–2028), or if Microsoft changes the LTS cadence or licensing of the runtime.
