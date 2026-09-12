# ADR-0010 — Authorization: roles and permissions per tenant and company

- **Status:** Accepted (2026-09-11) — mechanics of rules 1, 4, 5 and 7 (the evaluator's signature and outcomes, what a `null` company scope resolves to, fail-closed behaviour, where the permission set may and may not be carried) specified by ADR-0029; the decisions here are unchanged
- **Deciders:** architect
- **Supersedes:** —
- **Superseded by:** —
- **Related:** ADR-0007 (tenancy), ADR-0009 (authentication), ADR-0013 (API), ADR-0029 (fail-closed permission evaluation)

## Context

`CLAUDE.md` requires authorization on every endpoint and an authorization model scoped to **tenant and company**. A tenant is one customer; inside it are one or more legal entities (companies). A bookkeeper may post journals in company A and only read company B. A warehouse operator may ship but not price. Authorization must also hold for the public API and for background jobs acting on a user's behalf.

Authentication answers *who* (ADR-0009, catalog). Authorization answers *what may they do here* and therefore lives in the **tenant database**, schema `access`.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| ASP.NET Core role checks (`[Authorize(Roles = "...")]`) | Zero work | Roles are not permissions. Every new capability means editing every role check; tenants cannot define their own roles; no company scoping |
| **Permissions as claims + roles as tenant-defined bundles + a scope on every assignment** *(chosen)* | Fine-grained and stable: code checks a permission, never a role, so adding a role is data; tenants define their own roles; the assignment carries the company scope, which is exactly the requirement | We build the permission catalogue, the evaluation and the caching |
| External policy engine (OPA, Casbin) | Expressive, decoupled policy | A second language and runtime for a model that is plain RBAC plus one scope dimension; novelty an SMB ERP does not need; a network hop or an embedded interpreter on every check |
| Row-level security in PostgreSQL for company scoping | Enforced by the database | Company scope is a business rule that changes per assignment; encoding it in RLS policies means DDL on every permission change. RLS is the right tool for *tenant* isolation, which ADR-0007 already solves differently |

## Decision

**Permission-based authorization, with roles as tenant-owned bundles and a company scope on every assignment.**

1. **Permissions are code-declared constants**, namespaced `module.resource.action` — `sales.invoice.post`, `inventory.adjustment.approve`, `access.role.manage`. The full catalogue is generated from code and seeded per tenant. A fitness test asserts every declared permission is in the catalogue and every catalogue entry is referenced by at least one command.
2. **Code never checks a role.** It checks a permission. Roles are data.
3. **A role is a named set of permissions inside one tenant.** System roles are seeded and not editable; tenants may define their own.
4. **Assignment is `(userId, roleId, companyId?)`.** A null `companyId` means every company in the tenant, including companies created later — which is what an owner or external accountant wants. This single nullable column is the whole "per tenant *and* company" requirement.
5. **Enforcement is at the application service, not the endpoint.** Every command and query type declares `[RequiresPermission("...")]`; a pipeline behaviour evaluates it against the `TenantScope` and the principal before the handler runs. Fitness rule S1 fails the build for an undeclared command. Endpoint attributes are added as defence in depth, but the application layer is the real gate — otherwise the Blazor UI path and the REST path would need two implementations of the same rule, which is how they drift.
6. **Data scoping is a query filter, not a post-filter.** A user with access to a subset of companies gets an EF global query filter on `CompanyId` parameterized from the resolved scope. Filtering after materialisation is both slow and a disclosure risk through counts and paging metadata.
7. **Evaluation is cached** per `(tenant, user)` in HybridCache with a tenant-prefixed key and a short TTL, invalidated on role or assignment change (ADR-0012).
8. **Operator access is not a role.** Aurora staff never hold standing permissions in a tenant. Support access is an explicit, time-boxed, reason-coded grant written to `catalog.operator_audit_event` and surfaced to the tenant. This is the only path by which a non-member reaches tenant data.
9. **Background jobs carry the permission context of whoever caused them**, or run as a named system principal with an explicitly enumerated permission set. A job never runs "as nobody".
10. Denials return `403` with RFC 9457 Problem Details that state the missing permission — a missing permission is not a secret, and hiding it wastes support time.

## Consequences

- Positive: adding a capability is a new permission constant plus a seed row; tenants reorganise their own roles without a release.
- Positive: one enforcement point covers UI, API and jobs, so there is nowhere for the three to diverge.
- Positive: the fitness test makes "authorization on every endpoint" a build failure rather than a review checklist item.
- Negative: permission sprawl is a real risk — hundreds of constants nobody can reason about. Mitigation: permissions are declared next to the command they guard, grouped by module, and the catalogue is rendered in the role editor with descriptions from the declaration.
- Negative: cached permission sets mean a revoked permission can persist for the cache TTL. TTL is 60 seconds and revocation invalidates explicitly; for a long-lived Blazor circuit this combines with ADR-0009's 30-minute revalidation. Both numbers are stated so an auditor can see them.
- Negative: ABAC-style rules ("only documents you created", "only orders under 10 000") are not expressible. When the first one arrives, it becomes a permission plus a handler-level rule, not a policy language — and if three arrive, revisit.

## Revisit when

Attribute-based rules appear in three or more places, or a tenant needs role definitions shared across its companies in a way the `(role, companyId)` shape cannot express, or permission evaluation shows up in a latency profile.
