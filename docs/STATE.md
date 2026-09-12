# State

**Phase:** build · **Milestone:** M1 walking skeleton (B-01 … B-15)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap` · draft PR #1 tracks it
**Last iteration:** 9 (2026-09-12) · four PRs in flight, none blocked on a human

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

## Done and merged (fourteen tasks)
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

**Integration branch green at 854 unit tests**, floor 850. The floor is re-measured at every merge and
**never inherited**, and it is now the greatest multiple of ten *strictly below* the count — B-06.1
changed the rule because rounding *down* made the floor an exact count whenever the count was itself a
multiple of ten, so deleting one obsolete test would red the gate on a branch that did nothing wrong.
The in-flight branches each re-round it against the merged result rather than trusting the incoming
line (924 on B-06.1a, 1032 on B-09, 859 on B-20). Git cannot see that conflict, which is why the rule
exists.

## In flight
| What | Tier | Where it stands |
|---|---|---|
| PR #13 `task/B-06.1a` | Full | **Rework 4.** The fourth review found the fourth link in the same rule: an **explicitly implemented member of a non-generic interface** is reachable from outside, is IL-private so `BindingFlags.Public` misses it, sits on a type that may be `sealed` so the derivability check is silent, and names no proof in its type arguments so the inheritance check misses it. Executed: a non-friend assembly compiled and printed a stolen scope. Exposure today is zero (all 64 explicit implementations on public types are BCL). Rework follows the link into `GetInterfaceMap` and adopts ADR-0038 §2.1–§2.2. |
| PR #16 `task/B-09` | Full | **Rework 3.** The third review found the **eighth** spelling of the body hole: `Bodies()` decides from `t[0]`, but `Split` cuts on `;`, so a nested statement's first token is `BEGIN`/`DECLARE`/`IF` — a new routine with a quoted body inside a `DO` block scans **0 violations**, and in the same position a dynamic `EXECUTE`, a `U&'…'` body and a user-function call all read clean where each should be `Unscannable`. Also adopting ADR-0037, which **overturns one call**: plain `ENABLE TRIGGER` is destructive (`'O'` is a reduction from `'A'`). |
| PR #18 `task/B-20` | Full | **Approved on the first review**, then reworked for three mediums, all landed: `ck_database_cluster_host_lower_case` proven loud (`23514`, constraint named), the criterion folded to compare host case-insensitively and made honest about what it does *not* establish, and every non-deleted tenant compared — `25 non-deleted, 16 resolved, 9 taken from the routing row the resolver reads, 300 pairs, collisions: 0`. Now adopting ADR-0036, which replaced its acceptance criterion. **Full tier after a rework needs a second reviewer.** |
| PR #19 `task/ARCH-MIGRATION-IDENTIFIERS` | Full | **First review running.** ADR-0036 (routing uniqueness is asserted on the resolved endpoint), ADR-0037 (the destructive set classifies by effect), ADR-0038 (contract-carried text names its sink), ADR-0039 (the admission floor holds in every environment), plus `postgres-invariant-suppression.md`. Docs-only. Corrects a **statement of fact** in merged ADR-0034 §2 — the variant-3 row claimed two tenants resolved to "byte-identical connection strings", which was never true and was the evidence the vacuous §3.3 criterion rested on. |

## Known risks
1. **Usage limits kill agents mid-task, repeatedly** — most recently **all four at once**, costing
   about three hours of wall clock. **Nothing was lost, again:** seven worktrees intact, every branch
   pushed, and all four resumable with their context. The mitigation is the discipline that developers
   commit as soon as work compiles and the orchestrator never removes a worktree whose agent it may
   want back.
   **What caused it is worth recording as an orchestrator error rather than bad luck:** five agents
   with four building, sustained. `LOOP_PROMPT.md` already said six-agent load had exhausted the limit
   once. Four building is the stated cap and it is a cap, not a target — the cost of exceeding it is
   paid in wall clock, not in work.
   **A completion notice can badly understate what an agent did** — B-05's showed one sentence while
   its branch held 24 commits. Always check the branch before concluding nothing happened. Equally:
   an agent reporting that it is *waiting on a run* may be telling the truth — check for a live
   process before treating it as a stall. That has now gone both ways once each.
2. **Fable has its own, tighter quota.** Three concurrent Fable agents exhausted it in under a minute.
   Prefer one or two; fall back to Opus on a rate limit rather than leaving the graph idle.
3. **B-11 must implement ADR-0026's two-tier dependency gate**, not the original §1 rule — the
   original would fail on ~57 legitimately-acquired transitive packages.
4. **Every integration test is outside the merge gate.** `verify.sh` stages 4, 5, 7, 8, 9 and 10 do not
   exist, so every integration test — 98 on B-06.1a, 93 on B-20, including every tenant-isolation proof —
   runs only under `dev-test.sh`. Any count written here is stale the moment a branch lands; what does not
   go stale is that the set is *all of them*. Demonstrated: a tampered migration gives 11 integration
   failures while the gate still returns `RESULT: PASS`. Measured a second way: a deterministic 2-in-5
   flake in the tenancy suite went entirely unseen by the gate, and its second casualty was a
   tenant-isolation test failing for a reason unrelated to isolation. This is B-11's row and it is the
   largest standing hole in the process.
5. **Database-per-tenant is operationally heavy.** ADR-0007 caps the design at 1 000 tenants per
   cluster, ~25 000 total, with `ITenantConnectionResolver` as the seam for a cheaper tier later.
6. **The Country Package model is still unproven** — B-12 and B-13 are the first real test.

## Exact next action
1. **Four PRs are in flight and none is waiting on me** — three reworks and one first review. Merge them
   as their verdicts return, in the order they turn green. Each re-rounds the `verify.sh` floor; whoever
   merges second re-measures rather than inherits.
2. **PR #19 is the one to watch**, because three of the four branches are implementing against ADRs that
   are themselves still in review. If the review changes ADR-0036 §2.3 or ADR-0037 §2, relay it to B-20
   and B-09 the same turn — they were told to implement now and that I would carry any change.
3. **PR #18 needs a second reviewer**, not a first. Full tier after a rework requires one, and its first
   reviewer has already approved.
4. Then **B-06.2** and **B-06.3** — the structural guarantee that no `DbContext` can be obtained without a
   resolved tenant, and the lease/disposal semantics that flip `IsActive` false on reuse. `ITenantScopeFactory`
   goes in `Aurora.Platform.Tenancy.Contracts`, **not** a new abstractions assembly: B-06.1a's link 2 asserts
   the friend set of that exact assembly, and a new one would be a boundary nothing asserts.
5. **B-07 provisioning is still the highest-risk row in the milestone.** Its old acceptance row was
   satisfiable by a saga that, on replay, silently adopts another tenant's database — cross-tenant exposure
   through a green row. The row is now widened; its reviewer should try hardest to break step 2's adoption
   rule and §8's guard that `DROP DATABASE` is never automatic.
6. **B-11 (the gate's missing stages)** should be pulled forward against risk 4 below. Every integration
   test on this project — 98 on B-06.1a, 93 on B-20 — runs outside the merge gate.

## Open questions for the human (none blocking)
Q5 tenant scale · Q6 one entity filing in two jurisdictions · **Q7 attempt the NZ IRD sandbox
registration** (the only one needing real-world action) · Q8 24-hour single-tenant recovery point ·
Q9 how much of the data grid is v1 · Q10 ownership of a generated file in an architect-owned folder.
Each already has a decision recorded, chosen to be the cheapest to reverse.
