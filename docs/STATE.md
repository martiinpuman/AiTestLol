# State

**Phase:** build · **Milestone:** M1 walking skeleton (B-01 … B-15)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap` · draft PR #1 tracks it
**Last iteration:** 7 (2026-09-12)

## Product in one line
A multi-tenant SaaS ERP for SMBs with a country-agnostic core, where every jurisdiction-specific rule ships as an installable **Country Package**.

## Locked by the human (agents may not revisit — `docs/HUMAN_INBOX.md`)
.NET 10 LTS · C# · EF Core · PostgreSQL · Blazor Server (`InteractiveServer`) ·
**database per tenant** + one shared catalog database · country-agnostic core.
Also decided 2026-09-11: **hardening-first ordering kept** (no business module before B-15 is green);
**reviews tiered by risk** (`CLAUDE.md`); **Fable for development**, Opus for review and architecture.
Decided 2026-09-12: **gitflow, strictly**, and **every code review is posted on the task's GitHub pull
request**. Merge authority is the orchestrator's; no branch waits on a human.

## Verified environment (do not re-verify)
.NET SDK 10.0.401 · Npgsql EF Core 10.0.3 · Testcontainers.PostgreSql 4.15.0 · postgres:17-alpine ·
xunit 2.9.3 with runner 3.1.4 (the runner major need not match the framework major from 3.0 on).
`source scripts/dev-env.sh` before any `dotnet`; `scripts/bootstrap-env.sh` rebuilds a fresh container.

## Done and merged (nine tasks)
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
- **B-03.1** `CompanyScope` and `ICompanyScoped` (PR #10) — approved first pass, after the reviewer
  attacked the empty-scope property from thirteen directions and found no route through.
- **ADR-0032** (PR #7) — what the tenancy fitness rules bind to. Four rounds; the last caught a
  widened definition whose new limb the architect claimed added nothing, measured to add exactly one.
- **ADR-0029** (PR #5) — sign-in, the `tid` claim, fail-closed permission evaluation. Both original
  blockers and all five later highs closed. It gated thirteen backlog rows, which are now unblocked.
- **ADR corrections** (PR #6) — ADR-0030 (fitness rule mechanism, ArchUnitNET withdrawn), ADR-0031
  (five Country Package contract questions), ADR-0028 Amendment 1.
- **PM-SPLIT-B061** (PR #11) — the tenancy core row was two subsystems; split into `B-06.1a` (types)
  and `B-06.1` (resolver), both depending only on merged B-05, so both run in parallel.
- Architecture: **32 ADRs**. Design: system, WCAG audit, three screen specs and HTML prototypes.
- Automation: two hooks (39-case selftest), `dev-test.sh`, `dev-build.sh`, `file-claims.sh`,
  `project-health.sh`, five skills.

**Integration branch green at 761 unit tests**, 54 integration. Floor 760, re-rounded at every merge
and **never inherited** — three in-flight branches each re-round it and the third to merge re-measures.

## In flight — four pull requests, all in review
| PR | Branch | Tier | Where it stands |
|---|---|---|---|
| #9 | `task/ARCH-AUDIT-TRUNCATE` | Full | ADR-0028 Amendment 2. **Rework 5.** Fifth review found the pinned side of a comparison unconstrained (round 3's blocker, reintroduced by its own fix) and a completeness guard scoped so narrowly it refuses to run on a correct system. |
| #12 | `task/B-19` | Full | Catalog append-only trails. **Rework 3** at `cabf7c6`, 75 integration tests. With the third reviewer, which Full tier requires after a rework. |
| #13 | `task/B-06.1a` | Full | Tenancy kernel types and `TenantIdentityStamp`. First review. |
| #14 | `task/B-06.1` | Full | `ITenantConnectionResolver`, its 60 s cache, the §5.2 pool settings. First review. |

## Routings recorded but not yet actioned (transcribe before merging, never after)
- **`TenantDatabaseHandle` has no owning backlog row.** B-04's inertness guard asserts it is absent;
  something must eventually build it. → architect, then project-manager.
- **`InstalledPackages` shape is unconfirmed.** B-06.1a built it because no such type existed and
  ADR-0008 §4 does not define one. Text id and version, because the tenancy contracts assembly may
  not reference the Country Package contracts (layering rule L2). → architect.
- **ADR-0007 §3.5 places `ITenantConnectionResolver` in `.Contracts`;** B-06.1 shipped it `internal`
  in `Aurora.Platform.Tenancy` because every named consumer lives there and the partition forbade
  Contracts writes. One-file move when an outside consumer exists (B-10/B-18.5 is the first).
  → architect: confirm the ADR stands or amend it.
- **ADR-0029 A2.7 point 3 wording is stale.** `FOLLOWUP-032` is being narrowed to that point only.

## Known risks
1. **Usage limits kill agents mid-task, repeatedly** (five times so far). Mitigation works: developers
   commit as soon as work compiles, and nothing has been lost. **A completion notice can badly
   understate what an agent did** — B-05's showed one sentence while its branch held 24 commits.
   Always check the branch before concluding nothing happened.
2. **Fable has its own, tighter quota.** Three concurrent Fable agents exhausted it in under a minute.
   Prefer one or two; fall back to Opus on a rate limit rather than leaving the graph idle.
3. **B-11 must implement ADR-0026's two-tier dependency gate**, not the original §1 rule — the
   original would fail on ~57 legitimately-acquired transitive packages.
4. **Every integration test is outside the merge gate.** `verify.sh` stages 4, 5, 7, 8, 9 and 10 do not
   exist, so the 54+ integration tests — including every tenant-isolation proof — run only under
   `dev-test.sh`. Demonstrated: a tampered migration gives 11 integration failures while the gate still
   returns `RESULT: PASS`. This is B-11's row and it is the largest standing hole in the process.
5. **Database-per-tenant is operationally heavy.** ADR-0007 caps the design at 1 000 tenants per
   cluster, ~25 000 total, with `ITenantConnectionResolver` as the seam for a cheaper tier later.
6. **The Country Package model is still unproven** — B-12 and B-13 are the first real test.

## Exact next action
1. **Integrate the four in-flight PRs as their reviews return**, in the order they turn green. Three of
   them re-round the `verify.sh` floor; the second and third to merge re-measure rather than inherit.
2. Then **B-06.2** and **B-06.3** — the structural guarantee that no `DbContext` can be obtained
   without a resolved tenant, and the lease/disposal semantics that flip `IsActive` false on reuse.
   B-06.3 owns the scope factory that B-06.1a's sanctioned-doors list is waiting to name. Full tier.
3. **B-07 provisioning is the highest-risk task in the milestone.** The architect found its old
   acceptance row was satisfiable by a saga that, on replay, silently adopts another tenant's
   database — cross-tenant exposure through a green row. The row is now widened; its reviewer should
   try hardest to break step 2's adoption rule and §8's guard that `DROP DATABASE` is never automatic.
4. **B-11 (the gate's missing stages)** should be pulled forward against risk 4 above.

## Open questions for the human (none blocking)
Q5 tenant scale · Q6 one entity filing in two jurisdictions · **Q7 attempt the NZ IRD sandbox
registration** (the only one needing real-world action) · Q8 24-hour single-tenant recovery point ·
Q9 how much of the data grid is v1 · Q10 ownership of a generated file in an architect-owned folder.
Each already has a decision recorded, chosen to be the cheapest to reverse.
