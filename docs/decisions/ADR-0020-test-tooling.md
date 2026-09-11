# ADR-0020 — Test tooling

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** `../architecture/testing-strategy.md`, `../architecture/dependencies.md`

## Context

The test stack is a decade-long commitment touching every project, and 2025–2026 saw several widely-used .NET testing libraries move to commercial or restrictive licences. `CLAUDE.md` permits only MIT, Apache-2.0 and BSD. Every choice below was verified on **2026-09-11**.

## Options considered and decided

| Need | Chosen | Licence & version (verified 2026-09-11) | Alternatives considered and why not |
|---|---|---|---|
| Test framework | **xUnit** | Apache-2.0, **2.9.3** (the version verified in this environment) | NUnit (MIT) and MSTest (MIT) are both credible; xUnit's collection fixtures are exactly the mechanism the one-container-per-collection strategy needs (`testing-strategy.md` §6), and its per-test class isolation suits parallel integration tests. **xUnit v3 (4.0.0, Apache-2.0, released 2026-08-15) is the forward path**; the environment is verified on 2.9.3, so bootstrap uses 2.9.3 and a backlog item covers the v3 evaluation. The test-runner package major must match the framework major |
| Assertions | **Shouldly 4.3.0** | **BSD-3-Clause** | **FluentAssertions ≥ 8 is commercial** (Xceed partnership, January 2025; ~USD 130 per seat) — **rejected on licensing**. FluentAssertions 7.x remains Apache-2.0 but is a dead end. `AwesomeAssertions`, the community fork of the pre-licence code, is a plausible drop-in but inherits a fork's maintenance risk. Shouldly is BSD-3, independent, long-lived and has no licence cloud over it |
| Mocking | **NSubstitute 6.2.0** | **BSD-3-Clause** | Moq is MIT, but the 4.20 SponsorLink episode showed a maintainer willing to add data-collecting behaviour in a patch release. For a dependency we will carry for a decade, predictability of the maintainer matters as much as the licence. NSubstitute's syntax is also terser, which matters because most of our mocks are one-method module contracts |
| Integration database | **Testcontainers.PostgreSql 4.15.0** | MIT | An externally managed test database makes `verify.sh` non-hermetic and unreproducible on a fresh machine. Note: `PostgreSqlBuilder`'s parameterless constructor is obsolete in 4.15.0, so the image is pinned explicitly |
| Architecture tests | **TngTech.ArchUnitNET 0.13.4** | **Apache-2.0**, last published 2026-08-20 | `NetArchTest.Rules` (1.3.2) was last published in **2021** — unmaintained, rejected. ArchUnitNET's fluent rule model expresses the module dependency matrix directly. Source-level rules (no interpolated SQL, no `DateTime.Now`, no literal strings in `.razor` parameters) are Roslyn analyzers/tests, because reflection cannot see them |
| Property-based tests | **FsCheck 3.4.0** | BSD-3-Clause | The money, rounding, allocation and FIFO-layer invariants are universally quantified statements; example-based tests systematically miss their boundaries. Used narrowly, not everywhere |
| Snapshot tests | **Verify.Xunit 31.12.5** | MIT | For generated artefacts whose exact text matters: e-invoice XML, statutory report output, the OpenAPI document, generated migration SQL |
| Blazor components | **bUnit 2.10.3** | MIT | The only credible option for testing Blazor components in-process |
| Deterministic time | **Microsoft.Extensions.TimeProvider.Testing 10.10.0** | MIT | First-party `FakeTimeProvider`; effective dating (ADR-0008 §6.2) and period logic are untestable without controllable time |
| Coverage | **coverlet.collector 10.0.1** | MIT | Standard, integrates with `dotnet test` |
| Test data | Hand-written builders, plus **Bogus** where bulk realistic data is needed | Bogus 35.6.5 — MIT (NuGet exposes a licence file rather than an SPDX expression; confirm the file at adoption and record it in `dependencies.md`) | Builders with sane defaults are clearer for domain tests. Bogus earns its place only for seeding volume data in performance guard-rail tests |
| Database reset | **Not adopted.** Template-database cloning (`testing-strategy.md` §6.3) | — | Respawn 7.0.0 (Apache-2.0) was considered and rejected: cloning from a template is faster than truncating, and it also exercises the real provisioning saga on every run. One fewer dependency |

## Decision

Adopt the table above as the standard test stack. Two standing rules:

1. **No test dependency enters the solution without an entry in `../architecture/dependencies.md` with a verified SPDX licence and a verification date.** `verify.sh` stage 4 fails otherwise.
2. **Licence status is re-verified at every milestone health check.** The 2025–2026 wave — AutoMapper, MediatR, MassTransit, FluentAssertions — showed that a dependency's licence is not a property you check once.

## Consequences

- Positive: every test dependency is MIT, Apache-2.0 or BSD, with a verified version and date; no licence carries a commercial tripwire.
- Positive: the choices are driven by the strategy (collection fixtures, template cloning, property tests for money) rather than by habit.
- Negative: Shouldly instead of FluentAssertions means a different assertion vocabulary; anyone arriving from a FluentAssertions codebase pays a small tax. That is the cost of a licence we can rely on.
- Negative: pinning xUnit 2.9.3 at bootstrap means a migration to v3 later. Contained: it is mostly package references plus the runner, and it happens once, deliberately.
- Negative: ArchUnitNET is at 0.13.x — a pre-1.0 version number for a load-bearing part of the gate. It is actively maintained and Apache-2.0; the mitigation is that our rules are expressed as a thin layer over it, so a replacement would be a rewrite of that layer only, not of the rules.

## Revisit when

Any package above changes licence or stops being maintained, xUnit v3 is adopted, or ArchUnitNET reaches 1.0 (upgrade deliberately and re-run the deliberately-violating fixtures).
