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
