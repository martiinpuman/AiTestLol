# State

**Phase:** build · **Milestone:** M1 walking skeleton (B-01 … B-15)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap` · draft PR #1 tracks it
**Last iteration:** 9 (2026-09-12)

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

**Integration branch green at 764 unit tests**, 77 integration. Floor 760, re-measured at every merge
and **never inherited** — three in-flight branches each re-round it (826, 840, 860) and each measures
the merged result rather than trusting the incoming line. Git cannot see that conflict, which is why
the rule exists.

## In flight
| What | Tier | Where it stands |
|---|---|---|
| PR #13 `task/B-06.1a` | Full | **Rework 3.** Third review found `BindingFlags.Public` is not the externally reachable surface — `protected` on a public unsealed type is reachable by anyone who derives from it, and a non-friend assembly compiled against three such doors. Also a second unguarded `DROP DATABASE` site, and two wrong numbers in `verify.sh`'s own comment. |
| PR #16 `task/B-09` | Full | **Rework 2**, queued behind the rate limit. `ENABLE REPLICA TRIGGER` scans clean and is *pinned clean as a safe look-alike* — it disarms B-19's guards exactly as `DISABLE TRIGGER` does. And adjacent string literals are one body to PostgreSQL, so half of a concatenated body is never read. |
| PR #18 `task/B-20` | Full | First review. The branch found **ADR-0034 §3.3's acceptance criterion cannot fail as written** — `Application Name` carries the tenant key, so composed strings never collide. |
| `task/ARCH-MIGRATION-IDENTIFIERS` | Full | Architect, queued. Six questions: the `pkg_<id>` identifier rule, CHECK-replacement-as-Expand and whether a naming convention may be a link, `varchar` widening and the release manifest, ADR-0033's Development reading, **§3.3's vacuous criterion**, and whether the destructive set classifies by spelling or by effect. |

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
