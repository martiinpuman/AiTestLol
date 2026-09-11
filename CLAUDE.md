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

## Self-check before you submit (developers)

Every task reviewed so far was rejected on the first pass, and all three rejections were the same
three shapes. Check your own work against them before handing it over — a reviewer finding these is a
wasted round trip.

1. **Does any comment, name or doc claim behaviour that nothing actually produces?**
   B-01 shipped a `.csproj` comment asserting `dotnet test` never targeted a project. It did, and the
   run aborted. The claim and the mechanism that produces it must sit together, and if you cannot
   point at the mechanism, delete the claim.
2. **Can each check you added report that it measured nothing?**
   B-02's unit-test stage printed `PASS` while executing zero tests. A stage, assertion or gate that
   reports success without a count cannot distinguish "all good" from "nothing ran". Attach the
   number — tests executed, packages checked, rules asserted — and make it visible.
3. **Can the test you wrote actually fail?**
   B-03's property test for "the parts always add up" drew weights from ten hand-picked literals, so
   the arithmetic was always exact and the law could never break. Arbitrary in name, enumerated in
   fact. For any property test, state in the test itself which input dimensions are genuinely
   arbitrary and which are a fixed list — then go and break the code to watch the test fail.

The general rule behind all three: **a mechanism that cannot fail is not a check**, and the more
universal its name sounds, the more it will be trusted. Demonstrate the failure, do not assert it.

Two more, learned the same way:
- **`decimal` is exact until it is not, and it does not tell you.** Multiplication and division round
  silently past 28–29 significant digits. Any money algorithm that multiplies an amount by a
  caller-supplied factor and then relies on the result being a whole number of minor units must work
  in integers (`System.Numerics.BigInteger` is BCL and free at tier 0) and verify its own
  postcondition before returning.
- **Inject a fault from inside the system under test, not from outside on a timer.** A `sleep` racing
  a build is flaky on a different machine; a hook that fires at a chosen point is deterministic.

## Review tiers

Peer review is required by the Definition of Done, but its depth is proportionate to risk.

| Tier | What it covers | Process |
|---|---|---|
| **Full** | Money, the ledger, tax, tenancy and isolation, authentication and authorization, migrations, anything a Country Package can influence | Implement → review → rework → **re-review by a second reviewer** → merge. No shortcuts. |
| **Standard** | Everything else that ships behaviour: modules, screens, APIs, background jobs | Implement → review → rework → **the orchestrator verifies the fixes** and merges, re-reviewing only if the fixes were substantial or the reviewer asked for a second look |
| **Light** | Scaffolding, build configuration, tooling, docs-only changes | Implement → review → orchestrator verifies and merges |

The tier is named in the task brief. When in doubt, go up a tier — and a reviewer may escalate a task
it was given at a lower tier by saying so in the verdict.

**How deep a review goes is also set by the tier.** Reviews are the single largest cost in this
project's cycle time — on the two largest tasks so far, roughly half the total agent time went on the
rejection round — so depth must be spent where it changes outcomes.

| Tier | What the reviewer does |
|---|---|
| **Full** | Everything. Read the implementation against the ADR line by line, verify claims by running them, prove the tests bite by mutation, and attack the two or three properties the design actually rests on. This is what found the allocation blocker. |
| **Standard** | Run the gate. Verify the **top three risks** you identify in the change, by executing them rather than reasoning about them. Read the rest for correctness without exhaustive proof. Say explicitly which three you chose and why. |
| **Light** | Run the gate. Confirm the acceptance criteria are met. Check the change does not weaken an existing guarantee. Stop there. |

At Standard and Light, a reviewer who finds something that smells like a Full-tier risk should
escalate rather than quietly doing a Full review — say so in the verdict and let the orchestrator
decide. Depth that nobody asked for is depth nobody budgeted for.

### Reviewers write their own review file

A review is saved at `docs/reviews/<TASK-ID>.md` (or `<TASK-ID>-rereview.md`). **The reviewer writes
that file itself**, with a heredoc, and returns to the orchestrator only:

- the verdict,
- a one-line summary of each blocker and major,
- the gate result,
- anything the orchestrator must route to another role.

Do not return the full review text. It was being written once by the reviewer, read once by the
orchestrator, and written again to the file — three times the tokens for one document, and the
orchestrator's copy was the one that could drift from the file.

Reviewers still change nothing else. Writing under `docs/reviews/` is the single exception to the
read-only rule, and it exists so a review cannot be lost or garbled in transit. A reviewer that edits
anything under `src/`, `tests/`, `scripts/`, `docs/architecture/` or `docs/decisions/` has broken the
rule that a review may never quietly change the code it approves.

### Length is part of the job

A finding is worth what it changes, not what it weighs.

| Tier | Target |
|---|---|
| **Full** | As long as the findings require. Evidence for a blocker is never cut. |
| **Standard** | ~800 words. Verdict, the top three risks and how you executed them, findings. |
| **Light** | ~300 words. Verdict, gate result, findings. |

Two habits regardless of tier: state a finding once, in the place it belongs, and put the evidence
that proves it next to it. A "Patterns noted" section earns its place only when the pattern is new —
if it restates one already in this file's self-check list, cite it in a clause instead.

## Starting a dependent task before its predecessor merges

A dependent task used to wait for its predecessor to be reviewed **and** merged, which put a whole
review's latency on the critical path. It may now start earlier, under conditions:

**Allowed** when the dependent task is **Standard** or **Light** tier, and the predecessor's branch is
**gate-green** — `./scripts/verify.sh` passes on it — even though its review is still open. Branch from
the predecessor's `task/<ID>`, not from the integration branch, and say in the first commit message
which branch you are stacked on.

**Never** when either task is **Full** tier. Tenancy, money, the ledger, tax, auth and migrations wait
for a merged predecessor. The whole point of a Full review is that the design may still change, and
rebuilding on a design that moved is more expensive than the wait.

**Whoever starts early owns the rebase.** When the predecessor merges, rebase onto the integration
branch and re-run the gate before handing over. If the predecessor's review forces a change that
invalidates your work, that is the cost of starting early — say so plainly rather than patching around
it.

The orchestrator decides and names this in the brief. Absent an explicit instruction, wait for the
merge.

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
