# Project: Multi-Tenant SaaS ERP (working name: **Aurora ERP**)

This is an autonomous team project. The repository is the team's only memory: nobody remembers earlier sessions, so everything that matters must be written down in the files below.

## Product parameters (the human edits these)

### Locked by the human — do not change without asking in docs/HUMAN_INBOX.md
These were decided by the product owner on 2026-09-10. They are **not** open for an agent to revisit.

| Parameter | Decision |
|---|---|
| Backend | **C# on .NET 10 (LTS)** |
| Persistence | **Entity Framework Core** |
| Database | **PostgreSQL** |
| Frontend | **Blazor Server (`InteractiveServer` render mode)** |
| Tenant isolation | **Database per tenant**, plus one shared *catalog* database for tenant registry, subscriptions and installed country packages |
| Localization scope | **Country-agnostic core.** All country-specific law, tax, accounting and format logic ships as **Country Packages** that a tenant installs into their own system. |

### Open parameters (the architect decides, recorded as ADRs)
- Product: multi-tenant SaaS ERP for small and medium-sized businesses (roughly 10 to 250 employees).
- Primary market: no single market is privileged. The core must run a business anywhere; anything a specific jurisdiction demands belongs in a Country Package.
- First target industries: trading/wholesale and light manufacturing. The researcher may challenge this with evidence.
- UI languages: English (en) is the base locale. Every user-facing string is localized from day one so a Country Package can ship additional locales without touching core code.
- Hosting assumption: containers, cloud-agnostic. The team never creates real cloud resources.

### What "Country Package" means
A Country Package is an installable, versioned unit of jurisdiction-specific behaviour. A tenant enables zero or more of them. A package may contribute:
- a chart of accounts template,
- tax/VAT rules, rates and rate-validity periods,
- statutory report definitions and filing formats,
- e-invoicing profiles and transport rules,
- bank payment and statement file formats,
- accounting-data interchange formats (import/export),
- number/date/currency formatting and additional locales,
- identifier validation rules (company registration numbers, tax IDs, bank account formats),
- retention and archiving rules.

The core defines the extension points as contracts. The core must never contain an `if (country == "SE")`. The first reference package to build is **whichever one the researcher shows has the clearest, best-documented public rules** — the point of the first package is to prove the extension points are real, not to serve a particular market.

## Repository map
| Path | Purpose | Owner |
|---|---|---|
| docs/STATE.md | Current state and the exact next action | orchestrator |
| docs/BACKLOG.md | All tasks and their status | project-manager (orchestrator updates status) |
| docs/ITERATION_LOG.md | Append-only log, one entry per iteration | orchestrator |
| docs/HUMAN_INBOX.md | Questions for the human, and the human's answers. Human answers override earlier decisions. | everyone may add questions |
| docs/research/ | Market, process and regulation research | researcher |
| docs/product/ | Roadmap, specs (SPEC-###), glossary | project-manager |
| docs/architecture/ | Overview, module map, health checks, dependencies.md | architect |
| docs/decisions/ | ADR-####-title.md. Never deleted, only superseded. | architect |
| docs/design/ | Design system, screen specs, prototypes/ | ui-designer |
| docs/reviews/ | TASK-###.md review records | orchestrator saves senior-reviewer output |
| src/, tests/, scripts/ | Code | senior developers only |
| scripts/verify.sh | The single quality gate: build, all tests, architecture tests, lint, format check | senior developers |

## Ownership
Only senior developers change src/, tests/ and scripts/. Other roles write only in their own docs folder. If you need a change outside your area, ask for it in your summary to the orchestrator. Developers may also update docs/product/glossary.md and module README files.

## Engineering standards
- Clean Architecture: dependencies point inward. The domain layer has no references to frameworks, databases or UI. Enforced by automated architecture tests, not goodwill.
- DDD: one bounded context per module. Aggregates protect their invariants and reference other aggregates by ID. Use value objects for concepts like Money, Quantity, OrganizationNumber and VatRate. Use the ubiquitous language in docs/product/glossary.md and add terms when you introduce them. Modules talk through public contracts or events, never through each other's tables.
- TDD: failing test first, then the code, then refactor. Every acceptance criterion maps to at least one test. Domain logic gets fast unit tests; each module gets integration tests against a real PostgreSQL database (Testcontainers).
- SOLID and small units. Clear names over comments. No dead code, no commented-out code, no TODO without a backlog ID.
- **Multi-tenancy (database per tenant):** the tenant is resolved once per request and determines which database the request talks to. A developer must not be able to obtain a `DbContext` without a resolved tenant — that is a compile-time or container-level guarantee, not a convention. The catalog database is the only shared store, and it holds no tenant business data. Every module has automated tests proving a request in tenant A's context cannot read or write tenant B's database, including through background jobs and integration events.
- **Tenant lifecycle** is first-class: provisioning creates and migrates a database; migrations must roll out safely across every tenant database and be resumable; offboarding must be able to export and then destroy exactly one tenant's data.
- Security: authorization on every endpoint, validation at the boundary, parameterized queries, no secrets in the repo. OWASP ASVS level 2 is the reference.
- Financial correctness: money is a decimal amount plus a currency, never floating point. Posted ledger entries are immutable; corrections are reversals. Every financial change is audit-logged with who, when, what and tenant.
- APIs: versioned, consistent resource naming, RFC 9457 Problem Details for errors, idempotency keys on commands that create financial documents.
- Database migrations must be safe for existing tenants (expand/contract, no destructive change in one step).
- UI: follow docs/design/. WCAG 2.2 AA. No hard-coded user-facing strings; everything is localized. Dates, numbers and currency are formatted per locale.
- Observability: structured logs with tenant and correlation IDs. No personal data in logs.

## Git
- Conventional commits, e.g. `feat(sales): ...`, `fix(ledger): ...`, `docs(adr): ...`.
- One branch per task: `task/<TASK-ID>`. The integration branch must always pass scripts/verify.sh.
- **This project develops on the branch `claude/multi-tenant-saas-erp-pv2nap`.** Wherever these documents say "main", it means that branch. Never push to any other branch.
- Never force-push, never rewrite history, never commit secrets or .env files.

## Definition of Done (per task)
1. All acceptance criteria in the spec are met and covered by tests.
2. scripts/verify.sh passes on the task branch rebased on the integration branch.
3. Tenant isolation and authorization tests exist for any new data access or endpoint.
4. Approved by a senior-reviewer who is not the author (recorded in docs/reviews/TASK-###.md).
5. Docs affected by the change are updated.

## Local toolchain
The .NET SDK lives at `/usr/share/dotnet`. Any script or agent that runs `dotnet` must first source `scripts/dev-env.sh`, which puts it on PATH and starts the Docker daemon if it is not already running. Integration tests use Testcontainers against `postgres:17-alpine`.

## Hard limits for every agent
Never: deploy anything, create cloud resources, spend money, sign up for services, use real customer or personal data, commit secrets, force-push, or delete docs/decisions/. New third-party dependencies need a permissive license (MIT, Apache-2.0, BSD) and an entry in docs/architecture/dependencies.md; watch for libraries that have moved to commercial licensing.
