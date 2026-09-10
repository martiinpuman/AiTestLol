# State

**Phase:** bootstrap
**Bootstrap step:** B2 (research + design foundation)
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

## Where we are
Bootstrap step B1 is complete. The repository skeleton, agent definitions, locked parameters and toolchain are in place.
The toolchain is verified working: .NET SDK 10.0.401 at `/usr/share/dotnet`, Docker daemon running, `postgres:17-alpine` pulled.
Nothing has been designed, specified or built yet. `src/`, `tests/` and `scripts/verify.sh` are empty.

## Known risks
1. **Database-per-tenant is operationally heavy.** Migration orchestration across N databases, connection-pool exhaustion, and provisioning latency are all real. The architect must design for these explicitly in the multi-tenancy ADR; they are not to be discovered later by a developer.
2. **The Country Package extension model is unproven.** It is easy to design extension points that turn out to be the wrong shape. The first package exists to test the seams, so build it early rather than late.
3. **Blazor Server holds a live circuit per user.** Data-dense ERP grids over SignalR need virtualization and paging from the first grid component, not as a later optimisation.

## Exact next action
Bootstrap **B2**, two specialists in parallel:
- **researcher** → landscape pass into `docs/research/`: competitor landscape, feature-priority scoring, core business processes, and which jurisdiction has the clearest public rules to serve as the first reference Country Package.
- **ui-designer** → design foundation into `docs/design/`: principles, tokens, components, app shell, and static HTML prototypes.

Then B3 (architect), B4 (project-manager), B5 (walking skeleton).
