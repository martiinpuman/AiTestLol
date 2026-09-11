# State

**Phase:** build (bootstrap B1–B4 complete)
**Milestone:** M1 — walking skeleton (B-01 … B-15)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap` · draft PR #1 tracks it
**Last iteration:** 3 (2026-09-11)

## Product in one line
A multi-tenant SaaS ERP for SMBs with a country-agnostic core, where every jurisdiction-specific rule ships as an installable **Country Package**.

## Locked technical parameters
Decided by the human; agents may not revisit these (`docs/HUMAN_INBOX.md`).
.NET 10 LTS · C# · EF Core · PostgreSQL · Blazor Server (`InteractiveServer`) ·
**database per tenant** + one shared catalog database · country-agnostic core.

## Verified environment (do not re-verify)
.NET SDK 10.0.401 at `/usr/share/dotnet` · Npgsql EF Core 10.0.3 · Testcontainers.PostgreSql 4.15.0 ·
postgres:17-alpine · xunit 2.9.3 with `xunit.runner.visualstudio` **3.1.4** (the runner major need not
match the framework major from 3.0 onward — verified by running a passing and a failing test).
Run `source scripts/dev-env.sh` before any `dotnet` command; `scripts/bootstrap-env.sh` rebuilds a
fresh container.

## Done
- **B1–B4 bootstrap:** research (9 files), design system (tokens with a computed WCAG audit, prototypes),
  architecture (**26 ADRs**, module map, solution layout, testing strategy, scalability, dependencies),
  planning (roadmap, glossary, SPEC-001, SPEC-002, 29-task backlog).
- **B-01 solution skeleton — merged.** 9 projects, Release build clean at 0 warnings, a
  `packages.lock.json` per project. Reviewed twice: CHANGES_REQUESTED for a solution-wide red
  `dotnet test`, then APPROVE.

## In flight
- **B-02 `verify.sh`** — `task/B-02`, in rework after CHANGES_REQUESTED (`docs/reviews/B-02.md`).
  Stages 0–3, 6, 11 work and a self-test harness proves each one fails when it should. The gating fix:
  stage 6 reports PASS when zero tests execute and has no way to notice. Also fixing m-1 (no self-test
  for the stage 11 tree guard), m-2 (the tree guard never runs when an earlier stage fails), m-3 (the
  offline case can now cover the whole gate).
- **B-03 `Aurora.SharedKernel`** — `task/B-03`, 14 commits, **203 tests passing**, 0 warnings, no
  `double`/`float`. Under peer review (the first reviewer was killed by a usage limit mid-review).

## Known risks
1. **Usage limits kill agents mid-task**, repeatedly. Mitigation is working: developers commit as soon
   as work compiles, and files already written always survive. Always read what exists and resume from
   it — never discard sound partial work, and never re-run a task from scratch.
2. **Database-per-tenant is operationally heavy.** ADR-0007 caps the design at 1 000 tenants per
   cluster and ~25 000 total, and names `ITenantConnectionResolver` as the single seam for a cheaper
   tier later.
3. **Stage 4 of `verify.sh` is unimplemented and its spec was wrong** — it would have failed on ~57
   legitimately-acquired transitive packages. ADR-0026 replaced it with a two-tier gate. **B-11 must
   implement that**, not the original rule.
4. **The Country Package extension model is still unproven** — B-12/B-13 are the first real test.

## Exact next action
1. When the B-02 rework returns, re-review it, merge, and mark B-02 done.
2. When the B-03 review returns, act on its verdict; merge only on APPROVE. B-03 is the financial core,
   so a CHANGES_REQUESTED there is worth taking slowly.
3. Then **B-04** (architecture fitness tests) and **B-05** (catalog database — the first task that
   touches PostgreSQL for real). They are in different projects and parallel-safe.
4. Architect follow-ups outstanding: reconcile `solution-layout.md` §6 so stage 6 belongs to B-02 and
   B-11 owns 4, 5, 7–10 (review S-1); and resolve `modules.md` §3 saying "IClock over TimeProvider"
   against ADR-0020 and `testing-strategy.md`, which both say `TimeProvider` directly.

## Open questions for the human (none blocking)
Q5 tenant scale · Q6 one entity filing in two jurisdictions · **Q7 attempt the NZ IRD sandbox
registration** (the only one needing action in the real world) · Q8 24-hour single-tenant recovery
point · Q9 how much of the data grid is v1 · Q10 ownership of a generated file in an architect-owned
folder. Each already has a decision recorded, chosen to be the cheapest to reverse.
