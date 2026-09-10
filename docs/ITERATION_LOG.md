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
