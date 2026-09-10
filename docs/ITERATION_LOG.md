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
