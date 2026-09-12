# SPEC-001 — Tenant provisioning and first login

Status: ready · Author: project-manager · Date: 2026-09-11
Milestone: 1 (Walking skeleton) · Backlog: `B-07` (primary), `B-06` (underlying platform mechanism, specified directly in `../../architecture/solution-layout.md` §6)
Research: `../../research/01-feature-priority.md` ("Tenant/platform core" row, scored 18/25 — necessity/dependency dominate a low differentiation/effort score)
Architecture: `../../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §8 (the nine-step saga this spec puts a business face on), `../../decisions/ADR-0009-identity-and-authentication.md`, `../../decisions/ADR-0008-country-package-contract.md` §5.1

## Problem and user

**Who:** A new Aurora ERP customer's designated first administrator — the person who will, moments later, be the one setting up their company's master data. There is a second, implicit actor: **the platform itself**, which must execute provisioning automatically and correctly once a request is authorized, with no manual step by Aurora staff (`CLAUDE.md`: "Tenant lifecycle is first-class").

**Which process step:** This is step zero of every business process in this product. A tenant that is provisioned incorrectly, partially, or insecurely poisons everything built on top of it — there is no sales order, no invoice, no journal entry possible before this works. The researcher's feature-priority scoring (`01-feature-priority.md`) rates this the highest-dependency item on the whole list for exactly this reason, even though it scores low on differentiation: no customer ever says "I love your tenant provisioning," but every customer needs it to have happened correctly before they can do anything else.

**Why it is a spec and not just an engineering task.** `../../architecture/solution-layout.md` §6 already gives B-07's engineering acceptance criteria (idempotent steps, resumability, a reaper, a 60-second target). Those are necessary but not sufficient: they say nothing about *who* is allowed to trigger provisioning, what the resulting first user can actually do, or what must be audited. That is this spec's job.

## Scope and non-goals

**In scope:**
- What triggers provisioning (an authenticated, authorized internal call — see BR-1), what it produces (a usable tenant, its first Company, its first administrator, its installed Country Package(s)), and how the first administrator gets from "the platform finished provisioning" to "logged in and looking at their tenant."
- The tenancy, permission and audit shape of provisioning, so a developer does not have to infer it from the saga's engineering steps.

**Non-goals (deliberately excluded):**
- A public, self-service, marketing-facing sign-up page with plan selection and payment collection. Per the roadmap's Milestone 1 non-goals, provisioning in this milestone is triggered by an authenticated internal call (an operator action, or the automated test/onboarding harness that exercises this exact spec) — not by an anonymous visitor filling in a form. Building that page is a commercial-launch concern with its own billing and compliance questions that have not been researched, and building it now would be exactly the gold-plating `CLAUDE.md` and this role warn against.
- Choosing *which* Country Packages a tenant may request beyond "the packages that exist in the catalogue" — package content itself (New Zealand's actual GST rules) is Milestone 3's concern, not this spec's.
- Per-tenant database login roles (ADR-0007 §3.5 stage 2 — tracked as `FOLLOWUP-001`, scheduled before the first paying customer, not before this milestone).
- Enterprise SSO and per-tenant MFA policy enforcement (ADR-0009's own stated deferrals; tracked as `FOLLOWUP-007`).
- Anything about a *second* company being added to an existing tenant, or a *second* package being installed into a live tenant — both are real, tested capabilities per ADR-0007/ADR-0008, but this spec's job is the first company and the first package(s), created at provisioning time. SPEC-002 covers creating additional Company records once a tenant already exists.

## Domain concepts

See `../glossary.md`. This spec introduces or relies on: **Tenant**, **Company**, **Country Package**, **Provisioning**, **Membership**, **Role**, **Permission**, **Audit Event**, **Outbox**.

## Business rules

**BR-1 — Who may trigger provisioning.** A provisioning request is created only by an authenticated caller holding a platform-level capability to provision tenants (an Aurora operator, or the automated harness used by this milestone's own tests standing in for a future self-service flow). It is never anonymous and never triggered by a request that has not yet resolved a tenant (there is nothing to resolve — this is the one action in the whole system that necessarily happens before a `TenantScope` exists).

**BR-2 — What a provisioning request must specify.** A unique, human-chosen tenant key (used for routing, per ADR-0007 §3.1); a display name; a residency region; the email address of the intended first administrator; and zero or more requested Country Package ids. Requesting zero packages is valid — ADR-0008 states a tenant may enable zero or more packages — but this milestone's own proof tests always request at least one, specifically to exercise the seam end to end.

**BR-3 — What provisioning produces, automatically, with no further manual step:**
- Exactly one new, fully isolated tenant database, migrated to the current schema version.
- Exactly one default Company record inside it, named from the request's display name.
- The tenant's seeded system roles and permission catalogue (ADR-0010).
- Exactly one Membership: the requested first administrator, assigned the built-in administrator role, scoped to every Company in the tenant (a `null` company scope, per ADR-0010 rule 4) — so the first user is never locked out of the very thing they just created.
- Every requested Country Package, installed and in the `Active` state (ADR-0008 §5.1) — or the whole provisioning attempt fails loudly and is retried/reported, never silently skipped.
- A working sign-in credential path for the first administrator (an invitation with a way to set their own password, or an initial credential — exact delivery mechanism is an engineering decision for whoever builds this, not a business rule; the requirement is only that the administrator can reach a real sign-in, never that Aurora staff hand them a shared or guessed password).

**BR-4 — Idempotency and partial failure are the customer's problem to never see.** If provisioning is retried (because a step failed and was resumed, per ADR-0007 §8), the customer never ends up with two databases, two default companies, or two administrator memberships for the same request. This is the saga's job (fully specified in `solution-layout.md` §6, B-07); this spec only asserts that the *outcome* the business user sees is always exactly one of everything, never a duplicate and never a partial result silently presented as complete.

**BR-5 — A tenant that fails to provision is never presented as ready.** If provisioning cannot complete (a requested Country Package does not exist, a database cannot be created, a step fails past its retry budget), the tenant is never made reachable through routing (`catalog.tenant.state` never reaches `Active`) and the request's outcome is visibly `ProvisioningFailed`, not a tenant that quietly works except for the missing piece.

**BR-6 — First login proves the isolation, not just the plumbing.** The first administrator's very first sign-in resolves their tenant unambiguously (ADR-0007 §3.2's resolution order) and their session (the Blazor Server circuit) is pinned to that tenant for its whole life (ADR-0007 §3.3) — a user who is a member of more than one tenant (the accountant case) is not in scope for this milestone's tests, but the mechanism must not preclude it later.

**BR-7 — Every provisioning outcome is audited.** Successful provisioning writes an Audit Event for the tenant's own creation (actor: the requester who triggered it; type `Operator` or `System` per who actually initiated it), visible from inside the newly created tenant. A failed or retried attempt is recorded in the platform-level operator audit trail (`catalog.operator_audit_event`, ADR-0007 §9.2) — not inside a tenant database that may not fully exist yet.

## Acceptance criteria

**AC-1 — Happy path produces a usable tenant.**
*Given* an authorized caller submits a provisioning request naming a tenant key, a display name, a residency region, an administrator email, and one Country Package id that exists in the catalogue,
*when* provisioning completes,
*then* the tenant's state is `Active`, it has exactly one Company, exactly one administrator Membership scoped to every company in the tenant, and the requested Country Package is `Active` for that tenant.

**AC-2 — Unauthorized provisioning is refused.**
*Given* a caller without the platform capability to provision tenants,
*when* they attempt to submit a provisioning request,
*then* the request is refused with a `403` naming the missing permission, and no tenant, database, or catalog row is created.

**AC-3 — Resuming after a mid-saga failure never duplicates anything.**
*Given* a provisioning run that is deliberately killed after an intermediate step has completed (per the saga's own step-by-step design in `solution-layout.md` §6, B-07),
*when* the run is resumed,
*then* it completes with exactly one tenant database, exactly one default Company, and exactly one administrator Membership — never two of any of them.

**AC-4 — An unresolvable package request fails loudly, not silently.**
*Given* a provisioning request naming a Country Package id that does not exist in the platform's package catalogue,
*when* provisioning runs,
*then* the tenant never reaches `Active` state, is never reachable through tenant routing, and the request's outcome is reported as `ProvisioningFailed` with the unresolvable package named.

**AC-5 — The first administrator can log in and see their own tenant, and nothing else.**
*Given* tenant A has been provisioned with administrator user U,
*when* U signs in for the first time,
*then* U's session resolves to tenant A, U can see tenant A's default Company, and U cannot read or reach any record belonging to a second, separately provisioned tenant B — proven by the two-tenant isolation fixture (ADR-0007 §12, `../../architecture/testing-strategy.md` §7).

**AC-6 — Provisioning is audited on both sides of the tenant boundary.**
*Given* a completed provisioning run,
*when* an authorized user inspects tenant A's own audit log,
*then* an Audit Event exists recording the tenant's creation (who requested it, when); *and*, independently, the platform operator audit trail (`catalog.operator_audit_event`) records the same event at the platform level.

**AC-7 — Provisioning meets its time target.**
*Given* a provisioning request for a single Country Package,
*when* it runs without any injected failure,
*then* the tenant reaches `Active` state in under 60 seconds (ADR-0007 §8's stated target), measured in the test suite.

## API and data notes

- Provisioning is exposed as a command the internal/operator caller invokes (not a public, anonymous endpoint) — the exact route and payload shape are an engineering decision, but per ADR-0013 it must still return RFC 9457 Problem Details on every failure path (BR-5, AC-2, AC-4), and per ADR-0010 the command must declare its required permission explicitly so fitness rule S1 covers it.
- This is a lifecycle command, not a financial-document command, so an `Idempotency-Key` header per ADR-0013 §4 is not required by that rule; BR-4/AC-3's idempotency guarantee instead comes from the saga's own step-level idempotency (ADR-0007 §8), which is the correct place for it since retries are driven by the saga's reaper, not by a client resubmitting a request.
- No new catalog or tenant-database tables are introduced by this spec beyond what `solution-layout.md` §6 (B-05 through B-08) already defines; this spec constrains *behaviour* over that existing shape, not the schema itself.

## UX reference

There is no dedicated screen for triggering provisioning in this milestone (per the non-goals above, it is not a public sign-up form). The one UI surface this spec does touch is **first sign-in**: a conventional email/password (plus optional TOTP, per ADR-0009) authentication form. This is covered adequately by the base input and button component states in `docs/design/components.md` §§1–2 with no bespoke interaction pattern of its own, so no dedicated screen spec is required for this milestone. If a self-service sign-up flow is scheduled in a future milestone, it will need its own screen spec from the ui-designer at that time.

## Tenancy, permission and audit requirements

- **Tenancy:** Provisioning is the one operation that runs before any `TenantScope` exists for the tenant being created; every other action this spec describes (seeding, first login) runs with a properly resolved `TenantScope` exactly like every other tenant operation from that point forward. No shortcut or bypass of the standard tenant-resolution path is introduced for "just this once."
- **Permission:** Triggering provisioning requires a platform-level capability (BR-1, AC-2) distinct from any tenant-scoped permission (there is no tenant yet to scope a permission to). The first administrator's own in-tenant permissions come from the seeded administrator role (ADR-0010 rule 3), not from any special-cased "creator" flag.
- **Audit:** Both the tenant-side and platform-side audit requirements in BR-7/AC-6 are mandatory, not optional — this is the only spec in the walking skeleton where an action legitimately needs an audit trail on both sides of the tenant boundary, and it is called out explicitly so it is not missed.

## Follow-ups deliberately left out

- Self-service, anonymous sign-up with plan/billing selection (commercial-launch scope, not architecture-proving scope).
- Per-tenant database login roles at provisioning time (`FOLLOWUP-001`; stage 1 shared-role credentials, per ADR-0007 §3.5, are acceptable for this milestone).
- Adding a second Company to an already-provisioned tenant, and installing a second Country Package into a live tenant — both real, both specified in ADR-0007/ADR-0008, neither exercised by this spec (they are exercised generically by the engineering tests in `solution-layout.md` §6, B-13, but have no product-facing spec yet because there is no UI for either in this milestone).
- Enterprise SSO and per-tenant MFA policy (`FOLLOWUP-007`).
- Data residency choice beyond recording the requested region — actually routing across regional catalogs is described in ADR-0007 §11.3 and is not re-specified here.
