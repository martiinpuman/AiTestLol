# State

**Phase:** bootstrap
**Bootstrap step:** B2 (design, in flight) + B3 (architecture, in flight)
**Integration branch:** `claude/multi-tenant-saas-erp-pv2nap`
**Last iteration:** 1 (2026-09-10)

## Product in one line
A multi-tenant SaaS ERP for SMBs with a country-agnostic core, where every jurisdiction-specific rule ships as an installable **Country Package**.

## Locked technical parameters
Decided by the human; agents may not revisit these (see `docs/HUMAN_INBOX.md`).
- .NET 10 (LTS), C#, Entity Framework Core, PostgreSQL
- Blazor Server (`InteractiveServer` render mode)
- **Database per tenant** + one shared catalog database
- Country-agnostic core; jurisdiction logic lives in Country Packages

## Verified environment (do not re-verify)
.NET SDK 10.0.401 at `/usr/share/dotnet` · ASP.NET Core 10.0.12 · Npgsql EF Core 10.0.3 ·
Testcontainers.PostgreSql 4.15.0 · postgres:17-alpine · xunit 2.9.3.
Run `source scripts/dev-env.sh` before any `dotnet` command.
A spike proved database-per-tenant works here: two provisioned databases, cross-tenant reads
correctly return nothing, 9 s. Details in `docs/ITERATION_LOG.md`.

## Done
- **B1 scaffold** — agent definitions, docs skeleton, locked parameters, toolchain, `bootstrap-env.sh`
  + SessionStart hook so a fresh container rebuilds itself.
- **B2 research** — 9 files in `docs/research/`. Two findings that change the plan:
  1. **New Zealand** is the recommended first Country Package: flat 15% GST, self-service IRD
     Gateway API with no accreditation fee, open identifier checksum, published PINT A-NZ profile.
     Chosen for documentation quality over market size.
  2. **Inventory belongs alongside order-to-cash and the accounting core**, not after
     procure-to-pay — trading/wholesale has no believable O2C flow without real stock.

## In flight
- **ui-designer** (B2) → `docs/design/`: principles, tokens, components, app shell, HTML prototypes.
- **architect** (B3) → `docs/architecture/` + `docs/decisions/`: overview, tech-stack ADRs, module map,
  the multi-tenancy ADR, the Country Package ADR, scalability, testing strategy, solution layout.

If a session died mid-flight, check whether those folders have content. If they are empty or clearly
partial, re-brief the specialist rather than trying to finish its work yourself.

## Known risks
1. **The GitHub remote rejects every write.** `git push` returns 403 and the API returns
   "Resource not accessible by integration"; reads work. The Claude GitHub App is installed but
   lacks write permission. **All work exists only in this container.** Retry the push every
   iteration; the human has been asked to grant Contents + Pull requests write access.
2. **Database-per-tenant is operationally heavy.** Migration orchestration across N databases,
   connection-pool exhaustion, provisioning latency. The architect must design for these explicitly.
3. **The Country Package extension model is unproven.** Research found no incumbent cleanly supports
   adding a *second* country to an already-live tenant. The architect decides and records an ADR.
4. **Blazor Server holds a live circuit per user.** Grids need virtualization and paging from the
   first component, not as a later optimisation.

## Exact next action
1. When the ui-designer returns, commit `docs/design/` as `docs(design): ...`.
2. When the architect returns, commit `docs/architecture/` + `docs/decisions/` as `docs(adr): ...`,
   and fold any new HUMAN_INBOX questions into the record.
3. Then **B4**: brief the project-manager for the roadmap, glossary, Milestone 1 specs and a backlog
   with at least 8 `ready` tasks, starting with the walking skeleton.
4. Then **B5**: senior developers build the walking skeleton, solution skeleton and `verify.sh` first.
