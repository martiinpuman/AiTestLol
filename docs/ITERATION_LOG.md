# Iteration log

Append-only. One entry per iteration, newest at the bottom.

---

## Iteration 1 — 2026-09-10 — Bootstrap B1 (scaffold)

**Done**
- Read the supplied agent bundle; installed the six role definitions into `.claude/agents/`.
- Put the four locked product decisions to the human and recorded the answers in `docs/HUMAN_INBOX.md`.
- Adapted `CLAUDE.md`: generic core + Country Packages, .NET 10 LTS, EF Core, PostgreSQL, Blazor Server, database-per-tenant.
- Provisioned the toolchain: .NET SDK 10.0.401 (LTS) at `/usr/share/dotnet`, Docker daemon running, `postgres:17-alpine` pulled for Testcontainers.
- Created the docs skeleton, `.gitignore` and `scripts/dev-env.sh`.

**verify.sh** — does not exist yet (arrives in B5).

**Problems** — none.

**Wasted effort** — none.

**Next** — B2: researcher (landscape pass) and ui-designer (design foundation) in parallel.

### Environment capability spike (orchestrator, iteration 1)

Before letting the architect commit ADRs to the locked stack, the orchestrator verified the stack
actually works in this container. A throwaway spike (built outside the repo, not committed as code)
provisioned two PostgreSQL databases through Testcontainers, pointed a separate EF Core `DbContext`
at each, wrote a row into tenant A and asserted tenant B could not see it.

**Result: PASSED in 9 s.** Verified versions:

| Component | Version |
|---|---|
| .NET SDK | 10.0.401 (LTS) |
| ASP.NET Core runtime | 10.0.12 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| Testcontainers.PostgreSql | 4.15.0 |
| PostgreSQL test image | postgres:17-alpine |
| xunit | 2.9.3 |

Notes for developers:
- `dotnet new blazor --interactivity Server --empty` produces the correct Blazor Server starting point.
- `PostgreSqlBuilder`'s parameterless constructor is **obsolete** in Testcontainers 4.15.0. Use
  `new PostgreSqlBuilder("postgres:17-alpine")` so the image is pinned explicitly.
- Creating a database requires a connection to a *different* database on the same server, so tenant
  provisioning needs an admin connection distinct from any tenant connection. This shapes the
  provisioning service's design.
- Roughly 9 s of container startup per integration-test class. Share one PostgreSQL container across
  a test collection rather than starting one per test, or `verify.sh` will get slow fast.

**Conclusion:** the locked stack is sound and database-per-tenant is demonstrably workable here.
The architect may design against these versions with confidence.

---

## Iteration 2 — 2026-09-11 — Bootstrap B3, B4 and the first code task

**Done**
- **B3 architecture complete.** 25 ADRs, module map, solution layout, testing strategy, scalability
  and dependencies. Resumed the previous session's interrupted architect rather than restarting it;
  its committed forward references (ADR-0007, 0008, 0013, 0018, 0021, 0023 and the `TenantScope` /
  `ITenantConnectionResolver` vocabulary) were honoured exactly.
- **Dependency licences verified, not assumed.** Rejected AutoMapper, MediatR, MassTransit
  (commercial since 2025), FluentAssertions v8, Hangfire (LGPL), Duende IdentityServer, Redis server
  (RSAL/SSPL), plus Moq and NetArchTest on maintainer-trust and maintenance grounds.
- **Orchestrator verification:** restored all 25 chosen packages at their stated versions in a
  scratch project. Every one resolved. The version numbers in `dependencies.md` are real.
- **B4 planning complete.** Roadmap, glossary, SPEC-001, SPEC-002 and a 29-row backlog
  (16 `ready`, 12 `draft`). The PM split B-15 into three rows because as specified it did not fit
  one session — correct application of its own sizing rule.
- **B-01 recovered and completed.** See below.

**verify.sh** — still does not exist; it is task B-02. The gate for B-01 is `dotnet build -c Release`.

**Problems**
1. **A session restart killed the B-01 developer before it committed.** Its worktree survived with
   the work intact and uncommitted. The orchestrator verified the build (Release, 0 warnings,
   0 errors, 9/9 lock files, all four acceptance criteria met) and committed it unchanged on
   `task/B-01`.
2. **The Docker daemon failed to start on resume** — the stale-pid fix added in iteration 1 caught
   it, retried and recovered. The fix worked exactly as intended on its first real exercise.

**Wasted effort** — none, but two near-misses. Both were recoveries of work that a less careful
handoff would have thrown away and redone.

**Lessons folded into the loop**
- A usage limit or a session restart can kill an agent at any moment, but **files already written
  survive**. Always read what exists and resume from it; never discard sound partial work.
- **Developers must commit as soon as their work first compiles**, not at the end. B-01 came within
  one reclaimed container of losing a complete, correct scaffold. The task brief template now
  requires an early WIP commit on the task branch.

### Orchestrator verification — the pinned test runner (iteration 2)

The architect corrected a factual claim in ADR-0020: the rule that the test-runner major must match
the framework major held for runner 2.x and is false from 3.0, so `xunit.runner.visualstudio` **3.1.4**
beside `xunit` **2.9.3** is correct and must not be "fixed" by downgrading.

Nothing had actually exercised that pairing — the solution has no tests yet, so `dotnet test` reporting
"No test is available" proves only that the run does not abort. If the claim were wrong, every test
project would be silently broken and the first symptom would arrive at B-03 with the first real test.

Verified directly: a scratch project at exactly those two pinned versions, with one passing and one
deliberately failing test.

**Result: 2 tests discovered, 1 passed, 1 failed, exit 1.**

The runner discovers and runs xunit 2.9.3 tests, and a failing test genuinely fails the run — which the
quality gate depends on. The architect's correction is confirmed; ADR-0020's decision stands unchanged.

---

## Iteration 3 — 2026-09-11 — B-01 merged, the gate and the kernel reviewed

**Done**
- **B-01 merged.** Solution skeleton, 9 projects, Release clean at 0 warnings. Reviewed twice: the
  first review found a solution-wide red `dotnet test` that no planned gate stage would have caught.
- **B-02 reviewed** → CHANGES_REQUESTED: stage 6 reported PASS having executed zero tests, and
  nothing in the backlog would ever have made it notice.
- **B-03 reviewed** → CHANGES_REQUESTED with a **blocker**: `Money.Allocate` silently lost or invented
  minor units for ordinary ratio weights. 20.6% of 300 000 randomised splits did not sum to their
  total. Reproduced independently by the orchestrator before acting.
- Architect closed four spec corrections and swept §6 for acceptance rows narrower than the ADRs they
  implement. Found five, of which **B-07's would have permitted cross-tenant database adoption**.

**verify.sh** — green on the integration branch throughout.

**Problems** — the `decimal` precision trap was invisible to a clean build, 203 passing tests and
strict analyzers. Only a reviewer reading the implementation against the ADR found it.

**Lessons folded in** — `CLAUDE.md` gained the self-check list; every rejection so far has been one of
three shapes, and they are now stated where developers read them before starting.

---

## Iteration 4 — 2026-09-11 — parallel build, and optimisation at the product owner's request

**Done**
- **B-02 and B-03 merged.** The quality gate is live (stages 0–3, 6, 11, plus a 16-case self-test
  harness); `Aurora.SharedKernel` landed 208 tests and raised the gate's test floor from 0 to 200.
- **B-05 completed and in review** — catalog database, 363 solution tests, 41 integration tests.
- **B-04, B-12, B-02-FU** dispatched in parallel.
- **Optimisation pass**, on the product owner's instruction: review depth and length tiered by risk;
  reviewers now write their own review files instead of returning 5 000 words through the
  orchestrator's context; `docs/DEVELOPER_BRIEF.md` added as a routing document; Standard and Light
  tasks may now stack on a gate-green predecessor branch; parallelism limits measured (4 CPUs is the
  binding constraint, not disk or memory).

**Measured** — B-01 44 min of agent time, B-02 74, B-03 96. Roughly half of the two larger tasks went
on the rejection round, which is what the self-check list and tiering target.

**Problems**
1. **Three concurrent Fable agents exhausted Fable's quota in under a minute**, all dying before doing
   work. Fable's quota is separate from Opus's and tighter. The loop now prefers one or two and falls
   back to Opus on a rate limit rather than idling.
2. **A completion notice badly understated what an agent had done** — B-05's showed a single sentence
   while its branch held 24 commits. Always check the branch before concluding nothing happened.

**Wasted effort** — the three killed Fable spawns. Nothing else; every interrupted task resumed from
committed work.

## Iteration 5 — 2026-09-11

**Reviews returned, all four rejecting.** B-04 REJECT (2 mechanical majors, but the reviewer planted
eleven real violations in production code and watched every rule go red — the rule set is sound).
B-05 security re-review CHANGES_REQUESTED with 2 High: the privilege oracle that replaced
`ALTER DEFAULT PRIVILEGES` is blind to column-level grants and to PG 17's `MAINTAIN`, and the
request-path role owns the tenant routing tables — the reviewer repointed another tenant's database
and cluster host as `aurora_app`, with no DDL and no superuser. B-12 CHANGES_REQUESTED twice: four
majors from the peer review and one High from security, the latter a case-insensitivity bypass of the
`Aurora.*` assembly rule. Three rework agents dispatched; every one returns to a second reviewer.

**Automation built** (`docs/architecture/automation.md`). Three levers against the cost of a review
round trip, which on the tasks measured so far has been roughly half of a task's wall clock:

- Two hooks. A `PreToolUse(Bash)` guard blocks force-push, a push to any branch but the integration or
  a `task/*` branch, deleting an ADR, staging a `.env`, and `dotnet` without `dev-env.sh` sourced in
  the same call. A `PostToolUse(Write|Edit)` guard reports a country compared to a string literal in
  core, an ambient clock read, a `float`/`double` in `src/`, a `TODO` with no backlog id, and a
  credential-shaped literal. `hooks-selftest.sh` asserts every rule from both sides and prints its
  case count: 35 cases, 17 blocking, 18 allowing.
- `scripts/dev-test.sh`: the executed counts and the failures, nothing else. 89% less output than raw
  `dotnet test` on a passing run. Zero executed tests is a failure, not a pass.
- Three skills (`aurora-status`, `dispatch`, `integrate`) and `senior-developer` gaining
  `memory: project`, `maxTurns: 400` and `disallowedTools: Agent`.

**What building it taught.** Writing the hook selftest caught four defects in the hooks, three of them
rules that never fired at all because the path derivation was wrong — the guards would have sat there
looking like enforcement. Then *using* the guard caught two more that the selftest had not: a push
whose output went through a pipe was refused, and a commit message describing a blocked command was
analysed as if it were one. Both are now cases in the selftest. The allow side of a guard is not a
formality; it is half of what the guard is.

**Next:** integrate the three reworks as they return, each through a second reviewer. Then B-06.

### Merged this iteration

Reviews are now posted on each task's pull request. This table is the repository's own record of
them, so a session with no GitHub access can still find the verdict and what it rested on.

| Task | PR | Verdict | Reviewer | What the review rested on |
|---|---|---|---|---|
| B-12 | #4 | APPROVE (second reviewer, Full) | senior-reviewer | Reproduced both carrying claims itself rather than accepting them. Mutated the tax implementation to naive and watched the new boundary property go red; 500 draws, 500 constructible, 188 phantom minor units against naive and 0 against shipped; confirmed rate and target are genuinely arbitrary (500 distinct rates) while sign, midpoint and currency are the fixed lists the remark claims. Broke the generator's construction deliberately to check the property goes red rather than being discarded. Probed twelve alternative assembly-name spellings through the package load context: no third bypass, and no culture-sensitive comparison anywhere. Reverting `src/` to the pre-rework commit turned 16 new tests red, confirming all four unbounded-range rows had been green-as-passing. Gate PASS at 440 executed, self-test 21/21. |
| ARCH-CORRECTIONS | #6 | APPROVE (second reviewer, Full) | senior-reviewer | Reproduced all ten PostgreSQL behaviours the ADR-0028 amendment claims, against 17.11 — the REVOKE form leaves `pg_default_acl` empty and permits a later GRANT; the positive grant constrains a table created afterwards; the row trigger is cloned to a new partition and the truncate trigger is not; event triggers need a superuser this project does not define. Two rounds of new majors, both false claims *introduced by the fixes* — the second in text the first fix commit had just added. Verified the collateral check by hand: all 12 references to ADR-0008's own §6.1/§6.2 byte-identical to base after the renumbering. GitHub refused `APPROVE` with `403: Submitting APPROVE reviews is not permitted for this session type` — a session-policy restriction, not an authorship one, so the verdict is in the review body. |
| B-05 | #3 | APPROVE (third security reviewer, Full) | security-reviewer | Re-ran all nineteen attack shapes from both earlier rounds; every one returns `42501` except `UPDATE … RETURNING database_name`, which crosses no privilege. Re-derived all three counted assertions against its own cluster and found them exact. Verified by execution on PG 17.11 that the per-schema `ALTER DEFAULT PRIVILEGES … REVOKE` form stores nothing — independently re-confirming ADR-0028 §2's second mechanism as a permanent no-op on a third cluster. Four mediums remain, none blocking, now recorded as FOLLOWUP-010…013 rather than routed a fourth time. Gate PASS at 603 unit tests on the merged tree (B-05 alone 371, B-12 alone 440), integration 54/54. |
| B-04 | #8 | APPROVE (third reviewer, Full) | senior-reviewer | Executed eight fault injections rather than re-deriving the author's table: ghost `.csproj` under `src/Fixtures/` (21 of 136 red unbuilt, F1 reporting its `double` when built, against 42/42 green before the fix), `TMPDIR` inside the repo, the cross-assembly base walk, the plain-prefix and exact-list faults, the hosted set dropped, the simple-name catalog exemption, and the pre-rework argument-keyed T2. **Could not break** three things it tried: a hosted service reached through `IPlatformJob : IHostedService`, one implementing `IHostedLifecycleService`, and ADR-0032's sanctioned open-generic factory registration. Accepted the interim tenancy prefix on the explicit condition that it does not survive B-07 (FOLLOWUP-018). Gate PASS at 736 on the merged tree; floor re-rounded 600 → 730. PR #2 was closed in favour of #8: its head was the pre-rework branch, which diverged from the rework lineage when the author rebased, and force-pushing is a hard limit. |

## Iteration 6 — 2026-09-12

**Three merges: B-12, B-05, B-04, plus the ADR corrections.** Every one took two or three reworks and a
reviewer who reproduced rather than accepted. The hardening floor is now five of its tasks complete.

**What the reviews found that reading would not have.** A privilege oracle blind to column-level grants
and to PG 17's `MAINTAIN`. Then `INSERT` reproducing a whole tenant takeover with no `UPDATE` and no
`DELETE`. Then a `SECURITY DEFINER` function bypassing the *replacement* oracle, because a function's
default ACL is `EXECUTE TO PUBLIC` and it did not read `pg_proc`. A production `.csproj` under any
directory named `Fixtures/` vanishing from every architecture rule with the suite green at 42/42. And an
assembly-name check using `Ordinal` where the .NET loader binds case-insensitively.

**Process defects found and mechanised this iteration:**
- Two PRs showed **pre-rework code for hours** because the reworks lived on `-rework2` branches. B-05
  fast-forwarded; B-04 had diverged and could not, so PR #2 was closed and #8 opened from the branch
  holding the work — force-pushing is a hard limit and hand-merging two rebased copies is what already
  put a defect in a third document.
- **Routings were evaporating.** B-05's third security reviewer reported three of its routings as being
  made for the third time. Now `FOLLOWUP-010`…`025`, and transcription is a step in the merge procedure.
  Three of the first rows cited the wrong ADR sections and had to be corrected — a pointer that does not
  resolve is not a transcription.
- **Removing an agent's worktree makes it unresumable.** Cost a resume: a one-test follow-up went to a
  cold agent instead of the one that wrote the mechanism.
- A **session rate limit killed three agents mid-task**; all three resumed from disk with context.

**Two new rules earned, both about mechanisms that cannot fail** — the project's oldest theme, one level
up each time. *A demonstration that cannot fail is not a demonstration*: ADR-0032's fault "proved" a rule
while merely moving the report from one clause to another. *A floor set below the narrowing it detects,
detects nothing*: T15's floor of 3 was satisfied by three unrelated calls.

**Next:** confirm and merge ADR-0032; close ADR-0029's five highs, then dispatch the front of the B-17
chain (B-03.1, B-17.1, B-17.2), which is ready as written.
