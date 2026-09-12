# SPEC-002 — Company: create and list (walking-skeleton vertical slice)

Status: ready · Author: project-manager · Date: 2026-09-11
Milestone: 1 (Walking skeleton) · Backlog: `B-15.1`, `B-15.2`, `B-15.3`
Depends on: SPEC-001 (a tenant, its first administrator, and an installed Country Package must already exist)
Architecture: `../../architecture/solution-layout.md` §6 (B-15's acceptance criteria — engineering level), `../../architecture/modules.md` §5 tier 2 (Organization), `../../decisions/ADR-0007` §12, `../../decisions/ADR-0010`, `../../decisions/ADR-0015`, `../../decisions/ADR-0018`

## Problem and user

**Who:** A tenant administrator (or any user later granted the relevant permission) — the same person SPEC-001 just gave a working login to. **Which process step:** the very first thing anyone does with a freshly provisioned tenant beyond logging in: confirm the system is really theirs by creating a real record and seeing it come back. Every later milestone (Master Data in full, then every transactional module) is built on the same shape this slice proves — a tenant-scoped aggregate, created through a permission-checked command, persisted, audited, announced to the rest of the system, and listed back through both the API and the UI. Getting this one thin slice completely right, end to end, is the entire point of the walking skeleton (`solution-layout.md` §6: "Do not start business modules before B-15 is green").

**Why Company, and why so minimal.** The architect chose Company as the proof-of-concept entity (`solution-layout.md` §6, B-15: "create a company, read it back"). This spec deliberately keeps Company's shape to the bare minimum needed to prove the slice — a name and nothing else business-specific. The real, full-featured Company record (registered identifiers, base currency, primary jurisdiction, tax registrations, sites, fiscal periods) belongs to Milestone 2 (Master Data), which will extend this same aggregate rather than replace it. Building those attributes now, before Organization is actually being designed as a module, would be exactly the kind of guessing this role exists to avoid.

## Scope and non-goals

**In scope:**
- Creating a Company (name only) through a permission-checked command, reachable from both `/api/v1` and the Blazor Server UI.
- Listing the Companies visible to the current user, tenant-isolated and paginated from day one.
- The audit entry, the outbox event, and a background job that reacts to it — the specific proof points `solution-layout.md` §6 names for B-15.
- Proving all of the above holds independently across two separately provisioned tenants (SPEC-001), including through a background job and an integration event, per the mandatory `TenantIsolationContract` pattern (ADR-0007 §12.2).

**Non-goals:**
- Editing or deactivating a Company, or any Company attribute beyond its name (base currency, registered identifiers, tax registrations, sites, fiscal periods, number series) — all deferred to Milestone 2's full Organization-module spec.
- More than one Company per tenant being a meaningful UI concern (a company switcher, multi-company navigation) — `docs/design/app-shell.md`'s tenant/company switcher is a real, specified component, but wiring it up with real switching behaviour is Milestone 2 scope; this slice only needs the current, single default company to be listed correctly alongside whatever new company the tests create.
- The full data-grid component (`docs/design/components.md` §8) for the Company list. A tenant will have a handful of companies at this milestone; using the 150,000-row grid component here would create a false dependency on Milestone 1.5 and would be gold-plating. This spec uses a plain, non-virtualized list.
- An `Idempotency-Key` requirement on create (see "API and data notes").

## Domain concepts

See `../glossary.md`: **Company**, **Tenant**, **Permission**, **Role**, **Audit Event**, **Outbox**, **Country Package**.

## Business rules

**BR-1 — A Company belongs to exactly one Tenant.** Its identifier is meaningful only inside that tenant's own database; nothing about it is ever visible from, or reachable by, a request, background job, or event handler resolved to a different tenant (ADR-0007 §4, §10, §12).

**BR-2 — Name is the only required attribute at this milestone.** A Company's name must be non-empty, trimmed of leading/trailing whitespace, and no longer than 200 characters. This limit is a sensible, illustrative bound for this milestone, not a researched legal constraint — Milestone 2 may revisit it once real registered-name rules are considered.

**BR-3 — Creating a Company requires a declared permission.** Following the `module.resource.action` convention set by ADR-0010's own examples, this spec requires a permission in the `organization` module governing Company management (the exact constant name, e.g. `organization.company.manage`, is the implementing developer's to finalize and seed into the permission catalogue, per ADR-0010 rule 1). A user without it is refused; the seeded administrator role from SPEC-001 has it by default.

**BR-4 — Listing Companies is scoped to what the caller's role assignments allow.** Per ADR-0010 rule 4, a `null` company scope on a role assignment means "every company in the tenant, including ones created later." At this milestone, the seeded administrator (SPEC-001) has exactly that scope, so the list simply shows every Company in the tenant; the query must still be written as a properly scoped query (not "list everything, there's only one role today") so it is correct without modification once Milestone 2 introduces company-scoped assignments.

**BR-5 — Every creation is audited.** Creating a Company writes an `[Auditable]`-style Audit Event in the same transaction as the insert, with `before = null`, `after` = the created record's fields, the acting user, and a correlation id linking it to the originating API/UI request (ADR-0018 §1, §2).

**BR-6 — Every creation announces itself.** A `CompanyCreated` integration event is written to the transactional outbox in the same transaction as the Company row (ADR-0015) — never published only after the fact, and never published if the transaction rolls back.

**BR-7 — Something real reacts to the event.** At least one background job consumes `CompanyCreated` end-to-end (per a real, if minimal, purpose — for example, initializing a per-company settings row) so that this slice proves the full job-dispatch path (ADR-0007 §10.1), not just that an event row exists.

**BR-8 — The list is always a bounded, paginated query.** Even though a tenant will hold very few companies at this milestone, the query is written with an explicit page size and no unbounded materialization, establishing the pattern the fitness rule Q1 (`testing-strategy.md` §5.6) will hold every later module to.

**BR-9 — Tenant isolation holds through every path, not just the obvious one.** A Company created in tenant A must be unreadable from tenant B through a direct list query, through the background job from BR-7 running for tenant B, and through tenant B never receiving tenant A's `CompanyCreated` event — the three cases the mandatory `TenantIsolationContract` (ADR-0007 §12.2) exists specifically to catch.

## Acceptance criteria

**AC-1 — An authorized user creates a Company.**
*Given* a signed-in user holding the Company-management permission in tenant A,
*when* they submit a create request with the name "Nordwind Trading AB",
*then* a new Company record exists in tenant A with that name, and the response identifies the created record.

**AC-2 — An unauthorized user is refused.**
*Given* a signed-in user in tenant A without the Company-management permission,
*when* they attempt to create a Company,
*then* the request is refused with a `403` Problem Details response naming the missing permission, and no Company row, audit entry, or outbox event is created.

**AC-3 — Validation rejects an empty or over-long name.**
*Given* an authorized user,
*when* they submit a create request with an empty name, a whitespace-only name, or a name longer than 200 characters,
*then* the request is refused with a validation error identifying the name field, and no record is created.

**AC-4 — Creation is audited in the same transaction.**
*Given* a successful Company creation,
*when* the transaction commits,
*then* exactly one Audit Event exists recording the creation, the acting user, and the correlation id of the originating request — and if the creation were to fail after the database write but before commit (simulated in a test), no Audit Event exists either.

**AC-5 — Creation is announced through the outbox in the same transaction.**
*Given* a successful Company creation,
*when* the transaction commits,
*then* a `CompanyCreated` outbox row exists in the same transaction, and it is observed being delivered to and handled by the background job from BR-7.

**AC-6 — A created Company is listed back.**
*Given* one or more Companies exist in tenant A,
*when* an authorized user requests the Company list (via `/api/v1` and, separately, via the Blazor Server UI),
*then* every Company in tenant A is returned, and no Company from any other tenant appears.

**AC-7 — The list is empty-state honest.**
*Given* a freshly provisioned tenant before this slice's own tests have created any additional Company,
*when* an authorized user views the Company list,
*then* the seeded default Company (SPEC-001) is shown, and if it were somehow absent the UI would show a specific, actionable empty state (per `docs/design/components.md` §17) rather than a bare blank area.

**AC-8 — Tenant isolation holds through the direct query, the background job, and the event.**
*Given* two separately provisioned tenants, A and B (per the `TwoTenantDatabaseFixture`, ADR-0007 §12.1),
*when* a Company is created in tenant A,
*then* (a) tenant B's Company list never includes it, (b) the BR-7 background job triggered for tenant A never runs against tenant B's database, and (c) a handler resolved to tenant B's scope never receives tenant A's `CompanyCreated` event.

**AC-9 — A deliberately mis-routed connection is caught.**
*Given* tenant A's `TenantScope` is (in a test) handed a connection string pointing at tenant B's database,
*when* any operation in this slice runs,
*then* it throws `TenantRoutingViolationException` rather than silently reading or writing tenant B's data (ADR-0007 §4.3, §12.2's mandatory sixth test).

**AC-10 — The same flow works end-to-end through the Blazor Server UI.**
*Given* a signed-in, authorized user in the Blazor Server app,
*when* they use the create form to add a Company and then view the list,
*then* the new Company appears in the list without a page reload, and the create action shows the pending/saved states specified in `docs/design/components.md` §§1 and 9 (button loading state, inline validation on blur and on submit).

## API and data notes

- Resource shape follows ADR-0013: `POST /api/v1/companies` (plural noun, no verb) and `GET /api/v1/companies` (cursor-paginated per ADR-0013 rule 6, no unpaginated collection endpoint even here). Errors are RFC 9457 Problem Details throughout, including AC-2's authorization failure and AC-3's validation failure.
- **No `Idempotency-Key` is required on create.** ADR-0013 rule 4 scopes that requirement to commands that create or post a *financial* document; a Company is master data, not a financial document. Double-submission from a slow double-click is handled at the UI layer by the button's own loading/disabled state (`components.md` §1), which is sufficient at this milestone. Revisit if real usage shows otherwise.
- The application-service command declares its required permission per ADR-0010 rule 5 (`[RequiresPermission("...")]` or equivalent), so fitness rule S1 covers it automatically; the same command backs both the API endpoint and the Blazor Server UI action (ADR-0010 rule 5's stated reason: one enforcement point, so UI and API cannot drift apart).
- No schema beyond what `solution-layout.md` §6 (B-15) already implies is introduced here: a minimal `org.company` table (or equivalent, in whichever schema the Organization module ends up owning per `modules.md` §1) with `id`, `tenant`-implicit scoping (the tenant is the database, not a column), `name`, `created_at`, `created_by`.

## UX reference

No bespoke screen mockup exists for this screen yet. Given how minimal this slice is (a single required text field and a short list), this spec supplies the field- and column-level content directly, citing the relevant generic components so a developer is not guessing:
- **Create form:** one field, "Company name" (required, max 200 characters), using the Text input component (`components.md` §2) and the Forms-with-inline-validation pattern (`components.md` §9) for validation timing and error presentation. A single primary "Create company" button (`components.md` §1) restates the action per the design principles (no bare "Save" for an action this foundational).
- **List:** a plain, non-virtualized list or simple table (not the full data-grid component — see non-goals) showing Name and Created date, with the empty state from `components.md` §17 (true-empty variant) for the case in AC-7.
- A `draft` backlog item (`DESIGN-001`) exists for the ui-designer to produce a short, formal screen spec covering exactly this layout before `B-15.3` (the Blazor UI task) is picked up — per this role's own rule that a UI task needs a screen spec even when, as here, the screen is simple enough that this section could plausibly substitute for one. `B-15.3` stays in `draft` status until that screen spec exists.

## Tenancy, permission and audit requirements

- **Tenancy:** every operation in this spec — create, list, the background job, the event handler — receives its `TenantScope` explicitly and never reads it from ambient state (ADR-0007 §4.5); this slice's own tests are the first real exercise of the `TenantIsolationContract` base class (`../../architecture/testing-strategy.md` §7) for a genuine business module, and the Organization module's `TenantIsolationContract` subclass created here is what fitness rule T7 will look for from this point on.
- **Permission:** BR-3/AC-1/AC-2. This is the walking skeleton's "one role check" — the first real permission declared and enforced end to end (API, application service, and UI) in the product.
- **Audit:** BR-5/AC-4 — this is the first real, non-lifecycle Audit Event the product writes (SPEC-001's tenant-creation event is platform-level; this one is the first ordinary business-record audit event, and it sets the pattern every later module follows).

## Follow-ups deliberately left out

- Editing, archiving, or deactivating a Company (Milestone 2).
- Full Company attributes: base currency, registered identifiers, tax registrations, sites, fiscal years/periods, number series (Milestone 2 — Organization module in full).
- Company-scoped (non-`null`) role assignments and the tenant/company switcher's real switching behaviour (Milestone 2, once more than one meaningfully different Company exists in test data).
- The full data-grid component for this list (deliberately deferred; see non-goals — Milestone 1.5 builds the grid chrome, and Milestone 2's Party/Item lists are its first real consumers, not this one).
- An `Idempotency-Key` on Company creation, if double-submission is ever observed to be a real problem in practice.
