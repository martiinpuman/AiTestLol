# State

**Phase:** build · **Milestone:** M1 walking skeleton (B-01 … B-15)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap` · draft PR #1 tracks it
**Last iteration:** 4 (2026-09-11)

## Product in one line
A multi-tenant SaaS ERP for SMBs with a country-agnostic core, where every jurisdiction-specific rule ships as an installable **Country Package**.

## Locked by the human (agents may not revisit — `docs/HUMAN_INBOX.md`)
.NET 10 LTS · C# · EF Core · PostgreSQL · Blazor Server (`InteractiveServer`) ·
**database per tenant** + one shared catalog database · country-agnostic core.
Also decided 2026-09-11: **hardening-first ordering kept** (no business module before B-15 is green);
**reviews tiered by risk** (`CLAUDE.md`); **Fable for development**, Opus for review and architecture.

## Verified environment (do not re-verify)
.NET SDK 10.0.401 · Npgsql EF Core 10.0.3 · Testcontainers.PostgreSql 4.15.0 · postgres:17-alpine ·
xunit 2.9.3 with runner 3.1.4 (the runner major need not match the framework major from 3.0 on).
`source scripts/dev-env.sh` before any `dotnet`; `scripts/bootstrap-env.sh` rebuilds a fresh container.

## Done and merged
- **B-01 solution skeleton** — 9 projects, Release clean at 0 warnings, lock file per project.
- **B-02 `scripts/verify.sh`** — the quality gate: stages 0–3, 6, 11, plus `verify-selftest.sh`, a
  16-case harness that injects a defect, asserts the gate fails naming the right stage, and reverts.
  Stage 6 reports its executed-test count and fails below a floor, so PASS can never again mean
  "measured nothing".
- **B-03 `Aurora.SharedKernel`** — Money, Currency, Quantity, Percentage, DateRange, typed ids,
  Result. **208 tests.** Raised the stage-6 floor from 0 to 200.
- Architecture: **26 ADRs**, module map, solution layout, testing strategy, scalability, dependencies.
- Research (9 files), design system with a computed WCAG audit and HTML prototypes, roadmap,
  glossary, SPEC-001/002, 29-task backlog.

## In flight (four developers)
| Task | What | Model | Notes |
|---|---|---|---|
| **B-05** | Catalog database, `CatalogDbContext`, the tenant registry | opus | Resuming 24 recovered commits. 3 build errors to close: `CatalogDbContext` is `internal` and the test fixture exposes it publicly — a deliberate decision, not a quick fix |
| **B-04** | Solution-wide architecture fitness rules | opus | Every rule must be proven to fail; some are inert until B-06 brings `TenantScope` |
| **B-12** | Country Package contracts + hosting | opus | The extension model's first real test |
| **B-02-FU** | Quality-gate follow-ups (m-6, n-7, n-8, n-6) | fable | Two were flagged "before B-11 starts" |

## Known risks
1. **Usage limits kill agents mid-task, repeatedly** (five times so far). Mitigation works: developers
   commit as soon as work compiles, and nothing has been lost. **A completion notice can badly
   understate what an agent did** — B-05's showed one sentence while its branch held 24 commits.
   Always check the branch before concluding nothing happened.
2. **Fable has its own, tighter quota.** Three concurrent Fable agents exhausted it in under a minute.
   Prefer one or two; fall back to Opus on a rate limit rather than leaving the graph idle.
3. **B-11 must implement ADR-0026's two-tier dependency gate**, not the original §1 rule — the
   original would fail on ~57 legitimately-acquired transitive packages.
4. **Database-per-tenant is operationally heavy.** ADR-0007 caps the design at 1 000 tenants per
   cluster, ~25 000 total, with `ITenantConnectionResolver` as the seam for a cheaper tier later.
5. **The Country Package model is still unproven** — B-12 and B-13 are the first real test.

## Exact next action
1. Integrate the four in-flight tasks as they return. B-04, B-05 and B-12 are **Full** tier
   (fitness rules, tenancy, the package boundary); B-02-FU is **Light**.
2. Then **B-06** (tenancy core — `TenantScope`, `ITenantConnectionResolver`, the structural guarantee
   that no `DbContext` exists without a resolved tenant) once B-05 merges. Full tier.
3. **B-07 provisioning is the highest-risk task in the milestone.** The architect found its old
   acceptance row was satisfiable by a saga that, on replay, silently adopts another tenant's
   database — cross-tenant exposure through a green row. The row is now widened; its reviewer should
   try hardest to break step 2's adoption rule and §8's guard that `DROP DATABASE` is never automatic.

## Open questions for the human (none blocking)
Q5 tenant scale · Q6 one entity filing in two jurisdictions · **Q7 attempt the NZ IRD sandbox
registration** (the only one needing real-world action) · Q8 24-hour single-tenant recovery point ·
Q9 how much of the data grid is v1 · Q10 ownership of a generated file in an architect-owned folder.
Each already has a decision recorded, chosen to be the cheapest to reverse.
