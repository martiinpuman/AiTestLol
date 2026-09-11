# ADR-0029 — Sign-in, the `tid` claim and fail-closed permission evaluation

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Supersedes:** **in part** ADR-0009 rule 4 — the phrase *"cross-checked against host/path resolution on every request"* is replaced by the three named checkpoints in §4. A Blazor Server circuit issues no HTTP requests while it is alive (ADR-0005 rule 2), so "every request" leaves the longest-lived session in the product unchecked. Every other decision in ADR-0009 stands.
- **Superseded by:** —
- **Related:** ADR-0007 §3.2 / §3.3 / §4.3 / §9.2 / §9.3 (made precise here, not changed), ADR-0009 (identity), ADR-0010 (authorization — mechanics of rules 1, 4, 5 and 7 specified here), ADR-0012 §3 (cache table extended), ADR-0013 rule 3 / rule 7, ADR-0018 §1, ADR-0027 (`TenantAccess`), ADR-0028 §6 (two sides of the tenant boundary), `../product/specs/SPEC-001-tenant-provisioning-and-first-login.md` BR-3/BR-6/AC-5, `../product/specs/SPEC-002-company-create-and-list.md` BR-3/BR-4/AC-1/AC-2

## Context

The project-manager found, and re-checked twice, that nothing in bootstrap rows `B-01` … `B-15` builds sign-in or permission evaluation, while several rows presuppose both: SPEC-001 AC-5 ("*when* U signs in for the first time"), SPEC-001 BR-1 ("an authorized caller"), SPEC-002 BR-3/BR-4 and B-15.2's endpoint authorization. `B-07.3` seeds system roles, the permission catalogue and the administrator Membership row — and **nothing in the plan ever reads them**, nor does any row create the `access` schema they are seeded into.

ADR-0009 and ADR-0010 decided *what* to build. They do not decide four things a developer cannot start without, and each of the four is a place where a wrong guess is a tenancy or authorization defect rather than a style disagreement:

1. **Where credential material lives.** ADR-0007 §9.2 gives `catalog.identity_user` a `credential_ref` column; ADR-0009 rule 1 says "custom stores over `catalog.identity_user`". Those two readings put the password hash in different places.
2. **What "Membership" means.** The glossary, SPEC-001 BR-3 and ADR-0010 rule 4 use one word for two rows in two different databases — `catalog.user_tenant_membership` (*may this person enter this tenant*) and the tenant-side role assignment (*what may they do here*). Provisioning must create both; a developer reading "exactly one Membership" will create one.
3. **Where `tid` is minted and cross-checked, and what happens when it disagrees.** ADR-0009 rule 4 is one sentence. The failure it exists to prevent — a still-valid cookie acting in another tenant's context — is the failure this whole milestone exists to prevent, and it cannot be prevented by a sentence.
4. **What a caller gets when evaluation produces no decision at all.** ADR-0010 rule 10 specifies the *denied* caller. It is silent on the caller whose permission set could not be read, whose command carries no declaration at runtime, or whose company scope resolves to the empty set — and every one of those, done naively, is an allow.

An ADR is therefore needed. It amends; it does not restate ADR-0009 or ADR-0010.

## Options considered

Only the decisions that had a credible alternative are listed. The rest follow from ADR-0009/0010 and are recorded in §2–§8 without a comparison table.

| Question | Option | Pros | Cons |
|---|---|---|---|
| A mismatch between `tid` and the resolved tenant | **Refuse: `403`, before any `TenantScope` is opened** *(chosen)* | ADR-0007 §3.2's own rule ("never resolved by preferring one of them"); the request touches no database, so a mis-routed request cannot read, write or even connect | A multi-tenant user who navigates to a second tenant sees a refusal rather than a switch, until the deferred switch endpoint (§9) exists |
| | Re-mint the cookie for the newly resolved tenant if the user is a member | Seamless for the external accountant | The system would silently re-issue tenant authority from a request an attacker can craft; "silently upgrade a stale session" is the exact shape of the bug. Rejected |
| | Prefer the claim over the host | Never breaks a bookmark | A cookie would become a routing instruction. Rejected outright |
| The `tid` claim's role in resolution | **A constraint, never a source** *(chosen)* | An authenticated request to a host that maps to no tenant is refused, not resolved from the cookie; there is exactly one resolution input per request shape | A request to the apex host cannot "find its way home"; that is a UI concern, solved with a tenant picker that redirects to a tenant host |
| | Strategy 3 as a fallback when 1 and 2 miss | Convenient | Makes the cookie authoritative in precisely the case where nothing corroborates it |
| Where the permission set lives at evaluation time | **Read per evaluation from the tenant database, cached 60 s** *(chosen)* | Revocation takes effect in ≤ 60 s even inside an 8-hour circuit; the cookie carries no authority beyond identity | One cache read per guarded command |
| | Permissions as claims in the auth cookie | No lookup | Revocation would wait for the 30-minute revalidation or the cookie lifetime, and a stolen cookie would carry frozen authority. ADR-0010's option name ("permissions as claims") is about the *evaluation-time principal*, and §6 pins that reading |
| Failed sign-in and routing violations | **Platform side only, `catalog.authentication_event`** *(chosen)* | An unauthenticated or unproven caller can never append a row to a tenant's hash-chained audit log (ADR-0028 §5); works when the tenant is unknown or untrusted | One more catalog table, with its own append-only enforcement |
| | Tenant-side `audit.audit_event` | One audit store | Lets an attacker with any Aurora cookie write attacker-chosen rows into a customer's tamper-evident chain, and is impossible for an unknown email |

## Decision

### 1. Two Memberships, named apart, both created at provisioning

- **Tenant Membership** — `catalog.user_tenant_membership(user_id, tenant_id, state, …)`. Answers *may this person enter this tenant*. Read before a `tid` is minted; therefore in the catalog (ADR-0007 §9.3).
- **Role Assignment** — `access.role_assignment(user_id, role_id, company_id NULL, …)` in the **tenant** database. Answers *what may they do here* (ADR-0010 rule 4).

`company_id` is a `CompanyId` value with **no foreign key** to `organization.company`: modules never share tables (`../architecture/modules.md` §1.1), and rule 4's `null` needs no referential integrity. SPEC-001 BR-3's "exactly one Membership" means **one of each**, and its idempotency (BR-4) is per row.

`../product/glossary.md` uses one term for both; splitting it is a glossary change, which is the project-manager's and the developers' to make (`CLAUDE.md` ownership) — requested in the summary, not made here.

### 2. Credential material is a separate catalog row

`catalog.identity_user.credential_ref` (ADR-0007 §9.2) is a foreign key to **`catalog.identity_credential`**: password hash, security stamp, lockout counters and end, and the concurrency stamp. Reasons, in order of weight: offboarding and erasure delete credentials as one row while the user row survives as the pseudonymised actor reference every audit event points at (ADR-0018); a membership or routing query never has the hash in its projection; and it keeps §9.2's column list true rather than quietly contradicted.

**`Microsoft.AspNetCore.Identity.EntityFrameworkCore` is not referenced.** Its purpose is `IdentityDbContext` and `UserStore<…>` over ASP.NET Core Identity's *own* schema, which ours deliberately is not. What we use — `UserManager<T>`, `SignInManager<T>`, `PasswordHasher<T>`, `IUserStore<T>` and its siblings, cookie authentication, `RevalidatingServerAuthenticationStateProvider`, `CircuitHandler` — all ship in the `Microsoft.AspNetCore.App` 10.0.12 shared framework, verified in this environment on 2026-09-11 (`Microsoft.Extensions.Identity.Core.dll`, `Microsoft.Extensions.Identity.Stores.dll`, `Microsoft.AspNetCore.Identity.dll`, `Microsoft.AspNetCore.Authentication.Cookies.dll`, `Microsoft.AspNetCore.Components.Server.dll`). **The bootstrap identity and authorization path adds no third-party dependency.** Password hashing is the framework's; ADR-0009's "no custom password hashing, ever" is unchanged.

### 3. The cookie

One cookie, name `__Host-aurora.auth`. The `__Host-` prefix is not decoration: it obliges `Secure`, `Path=/` and **no `Domain` attribute**, so the browser will not send a cookie minted at `acme.aurora.example` to `globex.aurora.example`. `HttpOnly`, `SameSite=Lax`, sliding expiration 8 h, absolute 12 h.

**That browser control covers host-based tenancy and nothing else.** Path-based tenancy (ADR-0007 §3.2 strategy 2, `/t/{tenantKey}/…`) puts every tenant on one host and one cookie; and because Aurora is one deployment with one data-protection key ring, a cookie minted for tenant A **validates** at tenant B's hostname. It is authentic, unexpired and correctly signed. The only thing standing between it and tenant B's data is the `tid` cross-check. That is why §4 is a tenancy control and not a convenience.

### 4. `tid`: minted in one place, checked in three, never a resolution source

**Minted** only by the sign-in service (`Aurora.Platform.Identity`), only after all of: credentials verified; the tenant of the *sign-in request* resolved by host or path (never from a form field, a query string, a previous cookie or a header); `catalog.user_tenant_membership` for (user, that tenant) is `Active`; `catalog.tenant.state` is `Active`. The claim value is the host-resolved `TenantId`. No other code adds, edits or re-signs it; a **new** `tid` is only ever produced by a full re-authentication of the session, never by amending a principal in place.

**Checked** at three points, because a Blazor Server session outlives every HTTP request that created it:

| # | Checkpoint | What is compared | Owner |
|---|---|---|---|
| 1 | Every HTTP request carrying an authenticated principal, in the tenant-resolution middleware, **after** host/path resolution and **before** `ITenantScopeFactory.OpenAsync` | `tid` vs the host/path-resolved `TenantId` | B-18.4 |
| 2 | Circuit creation, and every reconnect of a persisted circuit (ADR-0005, .NET 10 persisted circuit state), in a `CircuitHandler` | `tid` on the principal handed to the circuit vs the tenant the creating request resolved; the result is **pinned** to the circuit (ADR-0007 §3.3) | B-18.6 |
| 3 | Every revalidation tick (30 min, ADR-0009 rule 3) | `tid` vs the circuit's pinned tenant, **and** security stamp still current, **and** Tenant Membership still `Active`, **and** `catalog.tenant.state` still `Active` | B-18.6 |

**`tid` is a constraint, never a source.** ADR-0007 §3.2's strategy 3 is not a fallback: if strategies 1 and 2 yield no tenant, the request is refused (`404`, unknown host) — it is never resolved from the claim. An authenticated principal that carries **no** `tid` at all is likewise refused: absence is a mismatch, not a skip.

**On disagreement, in this order:**

1. **No `TenantScope` is opened.** Not for the claim's tenant, not for the resolved tenant. The request touches no tenant database.
2. `403` with RFC 9457 Problem Details, `type` `https://aurora.example/problems/tenant-mismatch`. The body names **neither** tenant and does not say which side was wrong — unlike a permission denial (ADR-0010 rule 10), where the missing permission is not a secret. Whether a tenant exists, and whether your session is valid for it, are both secrets from a non-member.
3. The session is **not** renewed on the violating response (no sliding-expiration refresh), and the cookie is **not** deleted — deleting it turns any crafted cross-tenant link into a logout of the victim, and buys nothing against the attacker who already holds the cookie.
4. A `catalog.authentication_event` row (§7) plus a structured log at `Warning` with tenant and correlation ids, and a counter. Nothing is written to either tenant's `audit.audit_event`.
5. **A distinct exception type.** `TenantClaimMismatchException`, **not** ADR-0007 §4.3's `TenantRoutingViolationException`. §4.3's exception marks the tenant `SchemaBlocked`, which is right for a server-side mis-route and catastrophic here: it would let anyone holding a stale cookie take a tenant offline. A fitness-adjacent unit test asserts a claim mismatch leaves `catalog.tenant.state` unchanged.

**How it is tested** (the mechanism, not the status code):

- A cookie minted in tenant A's host and **replayed verbatim** against tenant B's host, in a two-host test harness that **shares one data-protection key ring** — because separate key rings would make the test pass for the wrong reason (the cookie would simply fail to decrypt) and the production system has one ring. Assert `403` **and a counted zero**: an instrumented `ITenantConnectionResolver`/scope factory recording that tenant B's database was resolved or connected **0** times.
- The same replay against `/t/{b}/…` on a **single** host — the case `__Host-` does not cover, and the one that proves the check rather than the browser.
- An authenticated request to a host that maps to no tenant: `404`, and the claim is not consulted.
- A principal with no `tid`: refused.
- A mismatch leaves `catalog.tenant.state` unchanged (no `SchemaBlocked`).
- Revocation mid-circuit: revoke the Tenant Membership by calling the real revocation path while the circuit is live, advance `FakeTimeProvider` past the revalidation interval, assert the circuit's principal is invalidated. The fault is injected **inside** the system under test, never by a `sleep` racing the revalidator.

### 5. Permission evaluation fails closed, and "no decision" is louder than "denied"

`IPermissionEvaluator` in `Aurora.Platform.Access.Contracts` (**not** `IAuthorizationService` — `../architecture/modules.md` §4's working name collides with `Microsoft.AspNetCore.Authorization.IAuthorizationService`, and a collision on the one type every guarded command touches is a trap; modules.md is corrected accordingly):

```
ValueTask<PermissionDecision> EvaluateAsync(ClaimsPrincipal principal, TenantScope scope,
                                            Permission permission, CompanyId? target, CancellationToken ct);
```

`PermissionDecision` has exactly **two** constructible outcomes, `Granted(CompanyScope)` and `Denied(Reason)`. There is no `Unknown`, no `Indeterminate` and no nullable return: a value that can be neither is a value someone will treat as a pass.

| Situation | Outcome | Surface |
|---|---|---|
| No authenticated principal | Not a denial — **authentication missing** | `401` + challenge (API), redirect to sign-in (UI) |
| Principal, tenant resolved, permission held for the target | `Granted` | handler runs |
| Permission not held; or held only for other companies | `Denied` | `403`, Problem Details naming the missing permission (ADR-0010 rule 10) |
| Permission constant absent from the tenant's seeded catalogue | `Denied` + log at `Error` + counter | `403`. Code and seed data disagree; a typo must not take a screen down, and must not pass |
| Permission in the catalogue held by **no** role — including the built-in administrator role | `Denied`, for everyone | `403` |
| Company scope resolves to the **empty set** | `Denied` | `403`. An empty filter list is never "no filter" |
| Request type reaches the pipeline with **no** `[RequiresPermission]` declaration | **No decision** — throws | `500`, Problem Details naming no permission, log at `Error`, counter `authz_no_decision_total`. A bug, not a user error |
| No `TenantScope` on the ambient path | **No decision** — throws `NoTenantResolvedException` | `500`, same treatment |
| Permission set unreadable (cache miss **and** tenant database or catalog unavailable) | **No decision** | `503` + `Retry-After`. Never served from an expired cache entry, never "last known good" |

**A caller with no decision at all is refused, and the refusal is deliberately a different, louder outcome than a denial.** A spike of `403`s is a misconfigured role; a spike of `500`/`503` from the authorization pipeline is an outage or a defect, and the two must never be reported as the same event.

**Rule 4's `null` company scope.** A Role Assignment with `company_id IS NULL` grants the permission for **every company in that tenant, including companies created later** — and for **no company in any other tenant**, which follows from the row living in the tenant database and is restated because it is the assumption an extracted service would break first. Evaluation:

- target company given → `Granted` iff an assignment exists with that permission and (`company_id = target` **or** `company_id IS NULL`);
- no target (a tenant-wide query such as SPEC-002's list) → `Granted(CompanyScope.AllCompaniesInTenant)` if any `null`-scoped assignment carries the permission, otherwise `Granted(CompanyScope.Of(ids))` over the distinct ids that do;
- `CompanyScope.Of` **throws on an empty collection** — the empty set cannot be constructed, so it cannot silently become `WHERE 1=1`.

`CompanyScope` is the parameter of ADR-0010 rule 6's EF global query filter: `AllCompaniesInTenant` emits no `CompanyId` predicate (tenant isolation is the database, ADR-0007), and any explicit set emits `CompanyId = ANY(@p)`. Filtering after materialisation stays forbidden.

**The built-in administrator is data, not a branch.** There is no `if (isAdministrator)` anywhere in the evaluator — ADR-0010 rule 2 ("code never checks a role") is only true if the evaluator itself obeys it. The administrator role holds an explicit, seeded permission set; the per-tenant seeder is idempotent and re-runs on every `access`-schema migration, granting **newly declared** permissions to the built-in administrator role and to no other role. That is how SPEC-002's `organization.company.manage` reaches an administrator provisioned before the Organization module existed, without a wildcard. The test that proves no hidden superuser branch survives: declare a test-only permission, seed it into the catalogue, grant it to nobody, assert the administrator is **denied**.

### 6. Permissions are evaluation-time, never carried in a cookie or a token

The permission set is read from the tenant database per evaluation, cached 60 s per `(tenant, user)` under ADR-0012's tenant-prefixed key and invalidated by the role-admin commands. It is **never** serialised into the authentication cookie, into circuit state, or into an OpenIddict access token. Consequence, and the reason for the rule: a revoked permission stops working within 60 s **even inside a live 8-hour circuit**, because the circuit's principal never held it.

For the public API (ADR-0013 rule 7, not bootstrap scope), a token's scopes **narrow** what the client may do; the user's permission set is still read from the tenant database, and the effective set is the intersection. A token never widens authority.

The claim set minted at sign-in is small and closed: subject, email, security stamp, `tid`, `tkey`, `amr`. It is pinned by an **approved-claims snapshot test** (§8, rule S6) so that adding a claim is a reviewed edit to a committed file rather than a line in a factory nobody reads.

### 7. Where authentication and authorization events are audited — and therefore what depends on B-16.2

Both sides of the tenant boundary, on the rule of ADR-0028 §6, with one addition of its own: **an unauthenticated or unproven caller must never be able to append to a tenant's hash-chained audit log.**

| Event | Store | Why |
|---|---|---|
| Sign-in **succeeded** (tenant resolved, `tid` minted) | tenant `audit.audit_event` | Trusted tenant, known actor; "who entered my system and when" is a question the customer asks |
| Sign-out | tenant `audit.audit_event` | Same |
| Sign-in **failed** — unknown email, wrong password, lockout, no Tenant Membership, tenant not `Active` | `catalog.authentication_event` | The tenant may be unknown or the caller unproven; and writing attacker-supplied identifiers into a customer's tamper-evident chain is the injection this rule exists to stop |
| Invitation issued / redeemed / redemption refused | `catalog.authentication_event` | Pre-tenant-session by construction |
| `tid` mismatch / no-tenant-for-host refusal | `catalog.authentication_event` | The claim is not trusted, so no tenant is trusted |
| **Permission denial** raised by the enforcement pipeline | tenant `audit.audit_event` | Authenticated, tenant resolved and trusted. A bare `IPermissionEvaluator` probe used to hide a UI button is **not** audited — only a refusal at the enforcement point |

`catalog.authentication_event(id, occurred_at, event_type, outcome, attempted_user_id NULL, attempted_email_hash bytea, tenant_id NULL, host, source, correlation_id, detail jsonb)`. `attempted_email_hash` is SHA-256 of the normalized email: enough to correlate an attack across attempts, and it does not widen ADR-0007 §9.3's single recorded personal-data exception to people who are not users. Append-only by the three mechanisms of ADR-0028 §2, probed as `aurora_app` **and** as the owner role — the same per-table retrofit B-16.1 applies to `catalog.operator_audit_event`, because the `catalog` schema also holds mutable tables and cannot take a schema-wide policy.

**A sign-in that cannot be recorded does not happen:** if the `catalog.authentication_event` write fails, the sign-in fails. An authentication log that is allowed to drop rows under load is not an authentication log.

**Dependency consequence, stated because it is the question that prompted this ADR:** only the two rows that write tenant-side events depend on **B-16.2** (`IAuditWriter` and the hash chain) — sign-in (B-18.5) and the enforcement pipeline (B-17.3). Everything else in the identity and access set writes platform-side only and is free of the B-16 chain, which is what lets most of it run in parallel with it.

### 8. Two new fitness rules, and one existing rule that must count

Added to `../architecture/testing-strategy.md` §5.6, each with a deliberately-violating fixture proving it fails:

- **S6 — approved claims.** The set of claim types minted at sign-in equals a committed approved-claims file; no claim type in it is a permission or a role. The test reports the number of claim types asserted.
- **S7 — `tid` is minted in one place.** The claim-type constant `AuroraClaimTypes.TenantId` is referenced only by the sign-in service, the tenant-resolution cross-check and their test assemblies — a named allow-list, in the shape of ADR-0027 §1's `TenantDatabaseHandle` allow-list.
- **S1, extended.** ADR-0010 rule 5's existing rule must report **how many** application-service request types it asserted and fail below a floor, and it gains a runtime counterpart: the pipeline behaviour refuses to invoke a handler whose request type carries no declaration (§5). A reflection rule that finds no types passes silently; a build gate and a runtime gate fail in different ways, which is the point of having both.

### 9. What this ADR deliberately does not decide

Named so nobody mistakes silence for a decision, and so nobody designs them inside a bootstrap row: the **tenant-switch endpoint** for a multi-tenant user (the rule is fixed — a new `tid` only ever comes from a full re-authentication — the endpoint is not designed, and SPEC-001 BR-6 puts the accountant case out of this milestone); **TOTP enrolment and MFA policy** (ADR-0009 already defers it; bootstrap ships password, lockout and security stamp, and `catalog.identity_user.mfa_state` stays `None`); **per-IP or per-host sign-in rate limiting** (bootstrap relies on ASP.NET Core Identity's per-account lockout); **OpenIddict and the public-API token path**; **the role and permission administration UI**; **operator support access** (ADR-0010 rule 8); and **the default-Company seeding question** — SPEC-001 BR-3 has provisioning create a Company while `B-15.1` owns the `Company` aggregate, which is the same ownership shape as the gap this ADR closes and is raised to the orchestrator rather than answered here.

## Consequences

- Positive: the bootstrap sequence becomes buildable. SPEC-001 AC-5, SPEC-002 AC-1/AC-2 and B-15.2's endpoint authorization stop being criteria with no mechanism underneath them, and B-07.3's seed data acquires a reader.
- Positive: the `tid` control is testable as a control. The replayed-cookie test with a shared key ring reproduces the real threat rather than a weakened version of it, and asserts a **counted zero** connections to the other tenant rather than only a status code.
- Positive: zero new third-party dependencies, verified against the installed shared framework.
- Positive: separating "denied" from "no decision" gives operations two different alarms for two different causes, and makes every unreachable-store path an explicit refusal.
- Negative: nine bootstrap rows where the plan had none, and two of them (`B-17.1`, `B-18.3`) land **before** `B-07.3`, which slips. That is the cost of the gap having been real; discovering it during `B-15.2` would have cost a rework of the whole spine.
- Negative: a per-evaluation permission read is a cache lookup on every guarded command. ADR-0012's 60 s TTL bounds the database cost; the alternative bounds revocation at 8 hours, which is not a trade an ERP may make.
- Negative: the multi-tenant user's experience is a `403` until the switch endpoint exists. Deliberate: refusing is safe and correcting it later is additive.
- Negative: `catalog.authentication_event` is a second append-only table in the catalog with its own per-table enforcement, because the `catalog` schema cannot take ADR-0028 §2's schema-wide policy.

## Revisit when

The tenant-switch endpoint is scheduled (it will re-open §4's disagreement handling, and must re-open it as a new ADR, not as a branch in the middleware); or the public API's token path is built (§6's intersection rule meets OpenIddict for real); or enterprise SSO arrives (ADR-0009 rule 6 — federation changes where the principal comes from but must not change where `tid` is minted); or measured `403`s from §4 are dominated by legitimate navigation rather than by tests, which would mean the resolution model, not the check, is wrong.
