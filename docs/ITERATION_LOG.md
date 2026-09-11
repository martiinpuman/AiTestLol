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
