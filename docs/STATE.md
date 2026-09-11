# State

**Phase:** build · **Milestone:** M1 walking skeleton (B-01 … B-15)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap` · draft PR #1 tracks it
**Last iteration:** 6 (2026-09-12)

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

## Done and merged (five tasks)
- **B-01** solution skeleton — 9 projects, Release clean at 0 warnings, lock file per project.
- **B-02** `scripts/verify.sh` — the quality gate, plus `verify-selftest.sh`, which injects a defect,
  asserts the gate fails naming the right stage, and reverts. 21/21.
- **B-03** `Aurora.SharedKernel` — Money, Currency, Quantity, Percentage, DateRange, typed ids, Result.
- **B-12** Country Package contracts and hosting (PR #4) — ten extension points, signed manifest,
  collectible load context. Approved after a rework closing seven findings.
- **B-05** catalog schema, roles and privileges (PR #3) — approved by a **third** security reviewer
  after two reworks; all nineteen attack shapes from the earlier rounds now return `42501`.
- **B-04** solution-wide architecture fitness rules (PR #8) — approved by a **third** reviewer after
  two reworks and four executed bypasses. T2 went live on the merge, because B-05 landing made its
  inertness guard expire exactly as designed.
- **ADR corrections** (PR #6) — ADR-0030 (fitness rule mechanism, ArchUnitNET withdrawn), ADR-0031
  (five Country Package contract questions), ADR-0028 Amendment 1 (append-only enforcement that
  actually works; ten PostgreSQL behaviours reproduced against 17.11).
- Architecture: **31 ADRs**. Design: system, WCAG audit, three screen specs and HTML prototypes.
- Automation: two hooks (39-case selftest), `dev-test.sh`, `dev-build.sh`, `file-claims.sh`,
  `project-health.sh`, five skills.

**Integration branch green at 736 unit tests**, 54 integration. Floor 730, re-rounded at every merge.

## In flight (two branches, both architecture)
- **ADR-0032** (PR #7) — what the tenancy fitness rules may bind to. Three reviews; the last found one
  line: T15's floor was 3 where the measured population is 4+2, so the rule could have shipped without
  the limb that catches `AddScoped<SalesDbContext>()` and passed its own floor green. Fixed, plus a
  twice-deferred definitional minor closed by widening. Awaiting confirmation, then merges.
- **ADR-0029** (PR #5) — sign-in, the `tid` claim, fail-closed permission evaluation. Both original
  blockers closed. **Five highs open, three introduced by the fixes themselves** — including a bounded
  audit write that PostgreSQL refuses to create on a partitioned table, whose accepted repair silently
  stops deduplicating. Gates thirteen rows; the longest pole in the milestone.

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
