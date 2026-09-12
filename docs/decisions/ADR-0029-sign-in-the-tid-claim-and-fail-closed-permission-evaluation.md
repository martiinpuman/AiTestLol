# ADR-0029 — Sign-in, the `tid` claim and fail-closed permission evaluation

- **Status:** Accepted (2026-09-11), **amended 2026-09-11 and 2026-09-12** after two security reviews (`../reviews/ADR-0029.md`: 2 blockers, 9 high). **Read Amendments 1 and 2 at the end of this document before implementing anything — where they disagree with §1-§9, and where Amendment 2 disagrees with Amendment 1, the later text wins.** Amendment 2 closes three findings that Amendment 1's own fixes introduced.
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

`catalog.identity_user.credential_ref` (ADR-0007 §9.2) is a foreign key to **`catalog.identity_credential`**: password hash, security stamp, lockout counters and end, and the concurrency stamp. Reasons, in order of weight: offboarding and erasure delete credentials as one row while the user row survives as the pseudonymised actor reference every audit event points at (ADR-0018); a membership or routing query never has the hash in its projection; and it keeps §9.2's column list true rather than quietly contradicted. **Amended (A1.3 M-7):** separating the row is not enough — `catalog.identity_credential` is readable only by a new `aurora_identity` login, revoked from `aurora_app`.

**`Microsoft.AspNetCore.Identity.EntityFrameworkCore` is not referenced.** Its purpose is `IdentityDbContext` and `UserStore<…>` over ASP.NET Core Identity's *own* schema, which ours deliberately is not. What we use — `UserManager<T>`, `SignInManager<T>`, `PasswordHasher<T>`, `IUserStore<T>` and its siblings, cookie authentication, `RevalidatingServerAuthenticationStateProvider`, `CircuitHandler` — all ship in the `Microsoft.AspNetCore.App` 10.0.12 shared framework, verified in this environment on 2026-09-11 (`Microsoft.Extensions.Identity.Core.dll`, `Microsoft.Extensions.Identity.Stores.dll`, `Microsoft.AspNetCore.Identity.dll`, `Microsoft.AspNetCore.Authentication.Cookies.dll`, `Microsoft.AspNetCore.Components.Server.dll`). **The bootstrap identity and authorization path adds no third-party dependency.** Password hashing is the framework's; ADR-0009's "no custom password hashing, ever" is unchanged.

### 3. The cookie

One cookie, name `__Host-aurora.auth`. The `__Host-` prefix is not decoration: it obliges `Secure`, `Path=/` and **no `Domain` attribute**, so the browser will not send a cookie minted at `acme.aurora.example` to `globex.aurora.example`. `HttpOnly`, `SameSite=Lax`, sliding expiration 8 h, absolute 12 h.

**That browser control covers host-based tenancy and nothing else.** Path-based tenancy (ADR-0007 §3.2 strategy 2, `/t/{tenantKey}/…`) puts every tenant on one host and one cookie; and because Aurora is one deployment with one data-protection key ring, a cookie minted for tenant A **validates** at tenant B's hostname. It is authentic, unexpired and correctly signed. The only thing standing between it and tenant B's data is the `tid` cross-check. That is why §4 is a tenancy control and not a convenience.

### 4. `tid`: minted in one place, checked in three, never a resolution source

**Minted** only by the sign-in service (`Aurora.Platform.Identity`), only after all of: credentials verified; the tenant of the *sign-in request* resolved by host or path (never from a form field, a query string, a previous cookie or a header); `catalog.user_tenant_membership` for (user, that tenant) is `Active`; `catalog.tenant.state` is `Active`. The claim value is the host-resolved `TenantId`. No other code adds, edits or re-signs it; a **new** `tid` is only ever produced by a full re-authentication of the session, never by amending a principal in place.

**Checked** at three points, because a Blazor Server session outlives every HTTP request that created it. **Amended (A1.2 H-4, H-6, H-9): there are four checkpoints, checkpoint 1 moves into `OnValidatePrincipal`, checkpoint 2 compares `tid`, `sub` and the security stamp, and `OnRefreshingPrincipal` is checkpoint 4.**

| # | Checkpoint | What is compared | Owner |
|---|---|---|---|
| 1 | Every HTTP request carrying an authenticated principal, in the tenant-resolution middleware, **after** host/path resolution and **before** `ITenantScopeFactory.OpenAsync` | `tid` vs the host/path-resolved `TenantId` | B-18.4 |
| 2 | Circuit creation, and every reconnect of a persisted circuit (ADR-0005, .NET 10 persisted circuit state), in a `CircuitHandler` | `tid` on the principal handed to the circuit vs the tenant the creating request resolved; the result is **pinned** to the circuit (ADR-0007 §3.3) | B-18.6 |
| 3 | Every revalidation tick (30 min, ADR-0009 rule 3) | `tid` vs the circuit's pinned tenant, **and** security stamp still current, **and** Tenant Membership still `Active`, **and** `catalog.tenant.state` still `Active` | B-18.6 |

**`tid` is a constraint, never a source.** ADR-0007 §3.2's strategy 3 is not a fallback: if strategies 1 and 2 yield no tenant, the request is refused (`404`, unknown host) — it is never resolved from the claim. An authenticated principal that carries **no** `tid` at all is likewise refused: absence is a mismatch, not a skip.

**On disagreement, in this order:**

1. **No `TenantScope` is opened.** Not for the claim's tenant, not for the resolved tenant. The request touches no tenant database.
2. **Superseded by A1.3 M-1: the status is `404` and is identical to the response for an unknown tenant key**, because `403`-here/`404`-there enumerates the customer list. `403` with RFC 9457 Problem Details, `type` `https://aurora.example/problems/tenant-mismatch`. The body names **neither** tenant and does not say which side was wrong — unlike a permission denial (ADR-0010 rule 10), where the missing permission is not a secret. Whether a tenant exists, and whether your session is valid for it, are both secrets from a non-member.
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

**Superseded by A1.2 H-1:** the first parameter is an `AccessSubject`, not a `ClaimsPrincipal`, because a method given both halves of the tenancy proof must not be free to ignore them.

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

`CompanyScope` is the parameter of ADR-0010 rule 6's EF global query filter — **and A1.2 H-2 makes that transport structural: it lives in `Aurora.SharedKernel` and is a required constructor parameter of any context mapping an `ICompanyScoped` entity**: `AllCompaniesInTenant` emits no `CompanyId` predicate (tenant isolation is the database, ADR-0007), and any explicit set emits `CompanyId = ANY(@p)`. Filtering after materialisation stays forbidden.

**The built-in administrator is data, not a branch.** There is no `if (isAdministrator)` anywhere in the evaluator — ADR-0010 rule 2 ("code never checks a role") is only true if the evaluator itself obeys it. The administrator role holds an explicit, seeded permission set; the per-tenant seeder is idempotent and re-runs on every `access`-schema migration, granting **newly declared** permissions to the built-in administrator role and to no other role. **Superseded by A1.2 H-3: the seeder grants a committed approved list, not "everything in the catalogue" — as written this was a wildcard, and it made the no-superuser test below unpassable.** That is how SPEC-002's `organization.company.manage` reaches an administrator provisioned before the Organization module existed, without a wildcard. The test that proves no hidden superuser branch survives: declare a test-only permission, seed it into the catalogue, grant it to nobody, assert the administrator is **denied**.

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

`catalog.authentication_event(id, occurred_at, event_type, outcome, attempted_user_id NULL, attempted_email_hash bytea, tenant_id NULL, host, source, correlation_id, detail jsonb)`. `attempted_email_hash` is SHA-256 of the normalized email: enough to correlate an attack across attempts, and it does not widen ADR-0007 §9.3's single recorded personal-data exception to people who are not users. Append-only — **by ADR-0028 Amendment 1's mechanisms, not the §2 ones this sentence originally cited; see A2.7**.

**A sign-in that cannot be recorded does not happen:** if the `catalog.authentication_event` write fails, the sign-in fails. An authentication log that is allowed to drop rows under load is not an authentication log.

**Dependency consequence, stated because it is the question that prompted this ADR:** only the two rows that write tenant-side events depend on **the row that ships `IAuditWriter` and the hash chain — `B-16.2` in `docs/BACKLOG.md`** (corrected twice: A1.4 fixed the substance, A2.6 fixed the number) — sign-in (B-18.5) and the enforcement pipeline (B-17.3). Everything else in the identity and access set writes platform-side only and is free of the B-16 chain, which is what lets most of it run in parallel with it.

### 8. Two new fitness rules, and one existing rule that must count

Added to `../architecture/testing-strategy.md` §5.6, each with a deliberately-violating fixture proving it fails:

- **S6 — approved claims.** The set of claim types minted at sign-in equals a committed approved-claims file; no claim type in it is a permission or a role. The test reports the number of claim types asserted.
- **S7 — the tenant claims are written in exactly two places** (**renamed and widened by A1.2 H-6**: as originally written it inspected references to one constant, so `new Claim("tid", …)` and any `ClaimsIdentity` construction slipped past it, and `tkey` was unguarded). The claim-type constant `AuroraClaimTypes.TenantId` is referenced only by the sign-in service, the tenant-resolution cross-check and their test assemblies — a named allow-list, in the shape of ADR-0027 §1's `TenantDatabaseHandle` allow-list.
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
- Negative: `catalog.authentication_event` is a third append-only table in the catalog with its own per-table enforcement, because the `catalog` schema holds mutable tables too and cannot take a schema-wide default-privileges policy (A2.7).

## Revisit when

The tenant-switch endpoint is scheduled (it will re-open §4's disagreement handling, and must re-open it as a new ADR, not as a branch in the middleware); or the public API's token path is built (§6's intersection rule meets OpenIddict for real); or enterprise SSO arrives (ADR-0009 rule 6 — federation changes where the principal comes from but must not change where `tid` is minted); or measured `403`s from §4 are dominated by legitimate navigation rather than by tests, which would mean the resolution model, not the check, is wrong.

---

# Amendment 1 — 2026-09-11, after security review

**Applies to:** everything above. Where this amendment and §1–§9 disagree, **this amendment wins**; the affected sentences are marked in place.

**Source:** `../reviews/ADR-0029.md` — CHANGES_REQUESTED, 2 blockers, 9 high, 7 medium, 4 low. The reviewer attacked and could not break the replay test, refuse-don't-re-mint, `tid` as constraint and never source, absence-is-mismatch, the two-constructible-outcomes rule, keeping permissions out of the cookie, drawing the audit boundary at *unproven* rather than *unauthenticated*, and the argument for not reusing `TenantRoutingViolationException`. **None of those is reopened here.** Every change below closes a place where the guarantee was carried by a convention — middleware ordering, an unnamed cache key, "the caller will pass the right scope", "the framework will leave the principal alone" — rather than by a mechanism that cannot be expressed wrongly.

## A1.1 — Blockers

### B-1: the membership cache read is keyed by user alone (cross-tenant `tid` mint)

The key `c:member:{userId}` was invented in a task row and back-cited to §4, which never defined it. It lets a member of any tenant obtain a minted `tid` for a tenant they do not belong to, within the TTL, from the one legitimate mint site — forging the very membership proof §7's audit boundary rests on.

**Decision — there is no per-tenant membership key, because the tenant is not part of the key at all.** The cached value is an **`IdentitySnapshot`**:

```
IdentitySnapshot(UserId, SecurityStamp, UserStatus, IReadOnlyDictionary<TenantId, MembershipState> Memberships)
```

cached 60 s under `CatalogCacheKey.IdentitySnapshot(userId)` and invalidated on membership change, security-stamp rotation and user-status change. **The tenant is a parameter of the lookup (`snapshot.Memberships.TryGetValue(tenantId)`), never of the key**, so the wrong-tenant key cannot be constructed. One entry per user serves membership, stamp and status for every path — which is also what closes H-5 and H-7 at 60 s instead of 30 minutes.

`CatalogCacheKey.For(kind, …)` is defined as the catalog-side sibling of `TenantCacheKey.For` (ADR-0012 rules 1–2, which had a `c:` prefix and no helper). Every `HybridCache` key in the solution comes from one of the two helpers, and each `CatalogCacheKey` kind is a member of a committed enum so a new cross-tenant cache entry is a reviewed edit. ADR-0012's status line, which repeated the bad key, is corrected.

Falsifiable criteria are added to B-18.2 (ask for `(U, A)` then `(U, B)` with **no time advanced**; the second answer is `false`) and B-18.7 (sign in at A, then attempt sign-in at B with correct credentials and no membership in B inside the TTL: refused, and **zero** rows in B's `audit.audit_event`).

### B-2: an invitation is a platform-wide password-set capability handed to the caller

Credentials are global, so setting a password through an invitation sets it for **every tenant that person belongs to**; the token went to the *caller*; and nobody owned find-or-create on `catalog.identity_user`, so "adopt the existing user" was the unowned default that completed the attack.

**Decision, four parts.**

1. **An invitation may only be issued against an unestablished identity.** `IPlatformUserProvisioning.ResolveForTenantAdministratorAsync(email, tenantId)` is the single owner of find-or-create and returns one of two outcomes: **`Invite`** — the identity has **no credential and no Tenant Membership in any other tenant** — or **`Join`** — an established identity, for which provisioning creates the Tenant Membership only. **A `Join` never produces a password-set token.** **Amended by A2.3: that Membership is created `Invited`, never `Active` — as written the join gated something that had already happened.** Redeeming a join requires authenticating as that user with their existing credential. An identity may therefore be given access to a new tenant, but never a new password, by someone who merely knows its email address.
2. **The token is never returned to the caller.** `IssueAsync` returns an opaque `InvitationHandle` (id, expiry, outcome); the secret leaves the process only through `IInvitationDelivery`, addressed to the invited address. Bootstrap has no mail transport, so `Aurora.Composition` registers a delivery implementation that **throws** when no transport is configured — provisioning fails loudly rather than quietly handing the secret back. The test harness registers a capturing sink. Returning the token in an API response is the same disclosure as logging it, with a nicer wrapper.
3. **Redemption resolves its tenant from the host or path, never from the request body**, and refuses unless `invitation.tenant_id` equals it — the same rule §4 applies to the mint. The redeem endpoint lives on the tenant's own host.
4. ADR-0010 rule 8 is preserved rather than contradicted: no operator ever holds a capability over an established tenant user. **Who may call provisioning at all** is the operator capability model, which rule 8 excludes from the tenant permission model and which no document owns — named in A1.4 as a follow-up, not decided here.

## A1.2 — High findings

### H-1: the evaluator held both halves of the tenancy proof and compared neither

**Decision — remove the ability to present two halves that disagree.** `EvaluateAsync` no longer takes a `ClaimsPrincipal`. It takes an **`AccessSubject`**, a sealed type constructible only inside `Aurora.Platform.Access` by one of two factories:

- `AccessSubject.FromPrincipal(ClaimsPrincipal, TenantScope)` — **performs the comparison as the price of construction**: `tid` present, parsable and equal to `scope.TenantId`, and the principal's security stamp, user status and Tenant Membership for that tenant still valid against the 60 s `IdentitySnapshot` (A1.1). There is no path that reads the principal without this.
- `AccessSubject.ForSystemJob(TenantScope, SystemPrincipalId, IReadOnlySet<Permission>)` — ADR-0010 rule 9's named system principal. **Superseded by A2.2: the permission-set parameter is removed — as written, the factory that closed H-1 needed no proof at all and took its permissions from the caller.** It carries **no `tid` claim at all**, so jobs never become a second site that writes tenant claims (which would have collided with rule S7).

The §5 outcome table gains three rows, and they are deliberately three different outcomes:

| Situation | Outcome |
|---|---|
| `tid` absent, unparsable, or `!= scope.TenantId` | **No decision** — throws, `500`, counter `authz_tenant_mismatch_total`. A defect, never a user error |
| Security stamp stale, user disabled, or Tenant Membership no longer `Active` | **Session no longer valid** — `401` + challenge. Not `403` (it is not an authorization refusal) and not `500` (it is not a defect) |
| Tenant's permission catalogue **empty**, or its `access` schema behind the skew gate | **No decision** — `503` (M-3: a broken tenant must not look like a misconfigured role) |

### H-2: `CompanyScope` was constructed safely and then transported by convention

`CompanyScope.Of` could not be empty, but nothing said how the scope reached the query filter — leaving an ambient accessor (the exact trap ADR-0007 §3.3 bans for tenants) or a hand-written `Where` as the two things a developer would reach for. The company boundary is the one horizontal-escalation boundary inside a tenant; it gets a mechanism, not a convention.

**Decision.** `CompanyScope` and the `ICompanyScoped` marker live in **`Aurora.SharedKernel`** (tier 0, where `CompanyId` already is). A module `DbContext` that maps any `ICompanyScoped` entity has one `internal` constructor taking `(DbContextOptions, TenantAccess, CompanyScope)` — extending ADR-0027 §1's constructor rule by one parameter — and its global query filter is parameterized from that field. `ITenantDbContextFactory<TContext>` gains `CreateAsync(TenantScope, CompanyScope, ct)`, and the single-argument overload **throws** when the context's EF model contains an `ICompanyScoped` entity type: model metadata is the mechanism, so a filtered context cannot be obtained unfiltered. `CompanyScope.AllCompaniesInTenant` emits no predicate; any explicit set emits `CompanyId = ANY(@p)`; the empty set remains unconstructible.

### H-3: "grant the administrator every permission in the catalogue" is a wildcard

It is a wildcard described in the paragraph that boasts of having none, it auto-grants every future module's permissions fleet-wide at every migration, and it makes §5's own no-superuser test unpassable — the two criteria could not both hold against one implementation.

**Decision.** The built-in administrator role is granted an **explicit committed list**, `administrator-permissions.approved.txt`, in the shape of S6's approved-claims file; the seeder grants that list and nothing else. A new permission joins it by a reviewed edit — which is what still delivers §5's stated benefit (SPEC-002's `organization.company.manage` reaching an administrator provisioned before the Organization module existed). A fitness rule asserts every declared `Permission` constant appears in **exactly one** of two committed lists — the administrator list, or a `not-administrator.approved.txt` with a reason per entry — and reports both counts, so a permission can be neither silently granted nor silently unreachable. A second rule forbids declaring a `Permission` constant for a platform or operator capability (reserved `operator.` and `platform.` prefixes): rule 8's capabilities do not live in the tenant catalogue.

### H-4: checkpoint 1's pipeline position and exempt paths were unspecified

Registered before `UseAuthentication`, the control is a silent no-op; and under path-based tenancy every root-mapped path (`/_blazor`, `/health`, static assets, `/sign-in`) resolves to no tenant, so an exemption list would have been invented unreviewed by whoever implemented it.

**Decision — the check runs where the cookie is read, not where someone remembered to register it.** Checkpoint 1 moves into `CookieAuthenticationOptions.Events.OnValidatePrincipal`, which has the whole `HttpContext` (host and path are available) and runs on every request that presents the cookie, so it can be neither mis-ordered nor path-exempted by accident. On failure it calls `RejectPrincipal()`, sets `ShouldRenew = false` (M-2) and records the reason in `HttpContext.Items`; it does **not** call `SignOutAsync` — §4.3's no-cookie-deletion decision stands. A small middleware turns the recorded reason into the Problem Details response. **If that middleware is missing or mis-registered the request is simply anonymous** — a harsher outcome, never a permissive one. That asymmetry is the point, and B-18.6 gets a deliberately-violating fixture that removes the middleware and asserts the replay still refuses.

**Tenant-neutral endpoints** are named in a committed allow-list on the same footing as S2's `[AllowAnonymous]` file, with a justification per entry: `/sign-in`, `/sign-out`, `/_blazor`, `/health*`, static assets, the deferred tenant picker. On such an endpoint no `TenantScope` can be opened, so an unchecked principal there can do nothing tenantful; a fitness rule fails the build for any endpoint that is neither tenant-resolved nor on the list, and reports the count of each. `/sign-out` being on it also removes the wedge the reviewer found — a user holding a cookie for a suspended or deleted tenant can always sign out in-product, so there is never pressure to exempt something that matters. Under host-based tenancy — the primary model — these paths sit on a tenant host and resolve normally; the list matters for the apex host and for path-based deployments.

### H-5: revocation, suspension and `SchemaBlocked` were unenforced for 30 minutes, or forever

The 60-second argument covered permissions only. On the plain HTTP path nothing re-checked membership, tenant state or the security stamp at all, so a revoked member or a suspended tenant kept working for the 12-hour cookie lifetime — and a `SchemaBlocked` tenant kept **writing to a half-migrated database**.

**Decision — one choke point, not five checks.** **Amended by A2.4 (the reason is bound by the factory, not passed — otherwise the allow-list is advisory) and A2.5 (the circuit opens a scope per unit of work, so "every path" becomes true rather than assumed).** `ITenantScopeFactory.OpenAsync` refuses to open a scope for a tenant whose `catalog.tenant.state` is not in the allow-list for the requested `TenantAccessReason`. It already re-reads the routing row under a 60 s cache that is invalidated on state change (ADR-0007 §3.5, ADR-0012 rule 6), and it already runs the §7.5 skew gate, so this costs nothing and bounds tenant-state staleness to 60 s for **every** path, including jobs and the outbox.

| Reason | May open a tenant in state |
|---|---|
| `Request`, `Job`, `Outbox` | `Active` |
| `Provisioning` | `Provisioning`, `Active` |
| `OperatorSupport` | `Active`, `Suspended`, `SchemaBlocked` |
| `Migration` | none — the DDL path uses `TenantDatabaseHandle` (ADR-0027), and a scope opened with this reason is refused |

Refusal is `TenantNotAvailableException`: `503` + `Retry-After` for `Provisioning`/`SchemaBlocked` (transient), `403` for `Suspended`/`PendingDeletion`/`Deleted` (not transient). Neither names another tenant. `Exporting` and `PendingDeletion` have no reason that may open them until offboarding is built — named in A1.4.

Membership, security stamp and user status are bounded at 60 s on every path by `AccessSubject.FromPrincipal` (H-1) reading the `IdentitySnapshot` (B-1). What remains at 30 minutes is narrow and now stated: a live circuit whose user was disabled can keep rendering **already-loaded** state, because it can neither open a scope nor execute a guarded command.

### H-6: `SecurityStampValidator` rebuilds the principal, so "one mint site" did not survive the framework

It replaces the principal with one built from the store; `tid` is not a store claim, so the framework either drops it fleet-wide (turning §4's absence-is-mismatch into a synchronised `403` storm indistinguishable from an attack) or it is copied in `OnRefreshingPrincipal` — a site that writes `tid` with **none** of the mint preconditions, added to rule S7's allow-list under deadline.

**Decision.** We do use `SecurityStampValidator` — H-5 and H-7 need it — with a 60-second validation interval, and **`OnRefreshingPrincipal` is checkpoint 4**, in §4's table, not an allow-list exception. It re-runs the mint preconditions against the current request: Tenant Membership `Active` for the existing `tid`, tenant state `Active`, and the host-resolved tenant equal to the existing `tid`. All pass → `tid` and `tkey` are carried over unchanged. Any fail → the principal is **rejected**. `tid` is therefore never *produced* outside a re-authentication and is *carried over* by exactly one site with stated preconditions. `OnValidatePrincipal` runs stamp validation first and the tenant cross-check second; one event, two ordered steps.

**Rule S7 is rewritten to what it actually inspects**, per `CLAUDE.md` self-check 1: it covers references to the constants `AuroraClaimTypes.TenantId` **and `TenantKey`**, the **string literals** `"tid"` and `"tkey"` anywhere in the solution, and every `ClaimsIdentity`/`ClaimsPrincipal` construction site — allow-listed to the mint, the carry-over and the cross-check plus their test assemblies. Its name becomes *"the tenant claims are written in exactly two places"*, which is what the mechanism checks.

### H-7: sign-out did not end the session it signed out of

**Decision.** Sign-out deletes the cookie, calls `UserManager.UpdateSecurityStampAsync` and invalidates the user's `IdentitySnapshot` entry; a password change does the same. The 60-second stamp validation then ends every other HTTP session, and `AccessSubject.FromPrincipal` refuses every guarded command from a live circuit within the same 60 s. The revalidation failure path on a circuit **forces a navigation to the sign-in page** rather than `ForceSignOut`'s stock anonymous-but-connected render, so already-rendered tenant data leaves the screen and circuit memory instead of sitting on a shared warehouse terminal.

### H-8: unauthenticated catalog writes, plus fail-closed on that write, is a fleet-wide sign-in outage

Three individually correct decisions composed badly: every failed sign-in writes to the shared catalog; a failed write fails the sign-in; all rate limiting was deferred.

**Decision — rate limiting is not deferred past sign-in.** `Microsoft.AspNetCore.RateLimiting` (in-box, no new dependency) applies fixed-window limits per source IP and per email hash to `/sign-in` and to invitation redeem, **rejecting before the credential check and before any catalog write**. A rejected request writes at most **one** row per `(source, email-hash, window, event type)`, deduplicated by a unique key with `ON CONFLICT DO NOTHING` — an insert that does nothing, which is compatible with append-only, unlike the counter column the obvious design would have used. **Superseded by A2.1: that key cannot be created on a partitioned table, and its obvious repair deduplicates nothing. The key is `(occurred_at, dedupe_key)` and `occurred_at` *is* the window start for a bounded row.** `catalog.authentication_event` is **monthly RANGE-partitioned** on `occurred_at` exactly like `audit.audit_event` (ADR-0028 §3), with a 180-day retention window enforced by detaching and dropping whole partitions — because an append-only trigger blocks `DELETE` for the owner too, so retention on this table is a partition operation or it is nothing. *"A sign-in that cannot be recorded does not happen"* stands unchanged: the fix belongs to the rate limiting, not to that rule.

### H-9: circuit reconnect proved the tenant and not the person

**Decision.** Checkpoint 2 compares **`tid`, `sub` and the security stamp** against the tuple pinned at circuit creation; a mismatch on any of the three aborts the circuit. The pinned tuple lives **server-side only, keyed by circuit id, and is never serialised to the client** — otherwise the cross-check would compare a cookie claim against a value the attacker also controls, which is no control at all. Persisted circuit state is keyed to the authenticated subject. A reconnect presenting a valid cookie for a *different user of the same tenant* is refused; that is horizontal escalation inside a tenant, and it passed the design as written.

## A1.3 — Medium and low findings

| # | Decision |
|---|---|
| **M-1** | The tenant-existence oracle is closed: an unknown tenant key and a known-but-not-mine tenant key return **the same status (`404`) and the same Problem Details `type`**, byte-identical apart from the correlation id. This **supersedes §4.2's `403`** and, with it, ADR-0007 §3.2's `403` for a routing violation. The distinguishing detail goes to `catalog.authentication_event` and the log, never to the caller |
| **M-2** | The mechanism for "the session is not renewed" is `ShouldRenew = false` inside `OnValidatePrincipal` (H-4), and the criterion is that the refusal response carries **no `Set-Cookie` header**, asserted by parsing the response |
| **M-3** | An **empty** catalogue, or an `access` schema behind the skew gate, is **no decision → `503`**; a single absent constant in a populated catalogue stays `Denied` + `Error`. A broken tenant must not be indistinguishable from a misconfigured role |
| **M-4** | `attempted_email_hash` becomes **HMAC-SHA-256 with a key from the secret store** (ADR-0011). Unsalted SHA-256 over an enumerable address space is reversible, so §7's claim that it does not widen ADR-0007 §9.3's personal-data exception was false. §7 is corrected: it is a **correlation key, not an anonymisation**, and it inherits H-8's retention window and partition-drop path |
| **M-5** | Pinned, with a test asserting the configured values: `PasswordOptions` ≥ 12 characters and **no composition rules** (the framework default of 6 plus character classes fails ASVS L2); `LockoutOptions` 10 attempts, 15-minute lockout, enabled for new users; invitation TTL 72 hours. A breached-password check is a named follow-up, not an omission |
| **M-6** | A denial writes **at most one** `audit.audit_event` per `(actor, permission, request type)` per 5-minute window, deduplicated in the tenant cache; the **metric** carries the exact count. Otherwise the least privileged member of a tenant can grow an append-only partitioned table without bound and contend the per-tenant audit lock that every business write needs |
| **M-7** | Password hashes get a privilege boundary, not just a separate row: `catalog.identity_credential` is readable only by a new **`aurora_identity`** login used by the identity store's own data source; `aurora_app` holds nothing on it, probed by the ACL comparison of A2.7 (**not** `has_table_privilege`, which this row originally specified), because B-05 finding M-1 already showed this project shipping an over-broad catalog grant once |
| **L-1** | `TenantClaimMismatchException` carries tenant ids in **structured properties only**, never in `Message`, so §4.2's "names neither tenant" does not depend on a Problem Details handler never echoing `ex.Message` |
| **L-2** | The counter and log distinguish "no `tid`" from "`tid` mismatch" (never the response body), so H-6's fleet-wide claim-drop and a genuine replay attack are not the same line on a dashboard |
| **L-3** | The invitation token is never placed in a URL: redemption POSTs the token into the redeem form |
| **L-4** | The antiforgery token is regenerated at sign-in alongside the auth cookie |

## A1.4 — Corrections to §7's dependency claim, and follow-ups this amendment does not answer

**§7's closing paragraph is wrong and is corrected here:** the two rows that write tenant-side events depend on **the row that ships `IAuditWriter` and the hash chain — `B-16.2` in `docs/BACKLOG.md` (renumbered by A2.6; this paragraph originally said B-16.1, which is right against `solution-layout.md` §6.1's older two-row split and points at an empty table against the backlog)** — **not** the `[Auditable]` interceptor row, `B-16.3`. Both write an explicit event; the interceptor is irrelevant to them, and naming it both delayed the rows and invited someone to route a permission denial through an entity-change interceptor.

Also corrected: a permission denial has **no caller transaction**, because the pipeline refuses before the handler runs and no module `DbContext` exists. ADR-0028 §4 requires one. The enforcement pipeline therefore opens its **own** short transaction on `AccessDbContext` (via `ITenantDbContextFactory` with the current scope) and writes the denial event inside it. If that write fails, the denial still stands — the caller is never granted because auditing failed — and the failure is logged at `Error` and counted.

**Named, not answered** (each needs its own ADR or an owner, and none may be decided inside a bootstrap row): the **operator capability model** — who may provision, who may read an invitation handle, what an operator may do to a tenant user — which ADR-0010 rule 8 excludes from the tenant permission model and SPEC-001 BR-1 assumes exists; **who may write `catalog.tenant_host` and what verifies a custom domain**, a tenant-influenced row in the shared database that steers routing; **what the health endpoints expose**, given ADR-0007 §9.4 has them enumerate this instance's cached tenants; **the offboarding `TenantAccessReason`** that may open `Exporting`/`PendingDeletion` scopes; **a scheduler for the retention and partition-management jobs**, since `Aurora.Platform.Jobs` is in no bootstrap row and ADR-0028 §3's partition pre-creation job has the same latent gap; **breached-password checking**; and the **tenant-switch endpoint**, already deferred in §9 and unchanged.

---

# Amendment 2 — 2026-09-12, second security review (PR #5)

**Applies to Amendment 1 and to §1–§9.** Where this amendment disagrees with either, **this amendment wins**; the overridden sentences are marked in place.

Both blockers are closed and five of the nine highs with them. **Three of the five that remain were introduced by Amendment 1's own fixes** — H-1 locked one door and opened another, H-5's choke point turned out to have a one-word bypass and to be unreachable on the path it was raised for, and H-8's bounded write could not be created at all. That is the honest shape of this round: a fix that adds a mechanism adds a surface, and the surface needs the same reading the original did.

## A2.1 — H-A: H-8's bounded write cannot be created, and its obvious repair silently stops deduplicating

Executed against `postgres:17-alpine` (2026-09-11, this environment), confirming both halves of the reviewer's finding:

```
CREATE UNIQUE INDEX ux ON authentication_event (source_hash, email_hash, window_start, event_type);
ERROR:  unique constraint on partitioned table must include all partitioning columns
DETAIL:  UNIQUE constraint ... lacks column "occurred_at" which is part of the partition key.
```

Adding `occurred_at` to that key compiles and runs and **deduplicates nothing**, because every attempt carries a different instant — so H-8's unbounded catalog write is restored by its own fix, silently, with a green test suite.

**Decision — `occurred_at` *is* the window start for a bounded row, and the discriminator is a column, not a predicate.**

`catalog.authentication_event` gains `dedupe_key text NOT NULL`, `granularity ('Instant' | 'Window')` and `window_seconds`. The index is `UNIQUE (occurred_at, dedupe_key)` — it contains the partition key, so PostgreSQL accepts it — and every write is `ON CONFLICT DO NOTHING`.

| Granularity | `occurred_at` | `dedupe_key` | Used for |
|---|---|---|---|
| `Window` | the **window start**, truncated | `{event_type}\|{source_hash}\|{email_hash}` | Every event class whose volume an unauthenticated or unproven caller controls: failed sign-in, rate-limit rejection, redemption refusal, `tid`/routing refusal |
| `Instant` | the instant | a fresh UUID, so it can never collide | Events a proven actor caused: invitation issued, invitation redeemed |

Verified end to end on the same container: three attempts in one window produce **one** row; the next window produces a second; **two `Instant` rows written in the same instant produce two rows**. That third assertion is the one that matters — it is what fails if someone "repairs" the design by adding `occurred_at` to the key and calls it deduplication.

Two consequences, stated so nobody has to infer them. For a `Window` row `occurred_at` is a **bucket, not an instant**; `granularity` exists precisely so a reader cannot mistake one for the other, and per-attempt timing lives in structured logs (which carry no personal data, ADR-0016). And because `ON CONFLICT DO NOTHING` keeps the **first** row, attacker-chosen `detail` from later attempts in a window is discarded rather than merged.

**This supersedes A1.2 H-8's unique key** `(source_hash, email_hash, window_start, event_type)`, which cannot exist.

## A2.2 — H-B: `ForSystemJob` takes a caller-chosen permission set for a caller-chosen tenant

A1.2 H-1 removed the evaluator's ability to be handed two disagreeing halves of the tenancy proof, and in the same paragraph introduced a factory that needs no proof at all: `ForSystemJob(TenantScope, SystemPrincipalId, IReadOnlySet<Permission>)` — no claim, no membership, no cross-check, and the permission set supplied by the caller. Anything that can reach it grants itself anything in any tenant it can open. B-17.3 had no criterion for it.

**Decision — the caller chooses neither the permissions nor the context.**

1. **Signature loses the set:** `AccessSubject.ForSystemJob(TenantScope scope, SystemPrincipalId id)`. A `SystemPrincipal` is **code-declared** exactly as a `Permission` is, with its permission set fixed at compile time, and the evaluator looks the set up from that registry. There is no overload that accepts a permission collection — a test asserts the type exposes none, because an absent overload is the only version of this rule that cannot be worked around.
2. **Every declared system principal is listed in `system-principals.approved.txt`** with its permission set and a reason per entry, asserted by fitness rule S9 (extended), which reports the count. A new system principal is a reviewed edit, like a new claim type and a new administrator permission.
3. **`ForSystemJob` refuses unless `scope.Reason` is `Job` or `Outbox`.** A request-path scope cannot be laundered into a system subject, which is the move a careless handler would otherwise make to get past a denial.
4. **Its call sites are allow-listed** to `Aurora.Platform.Jobs`, the outbox dispatcher and their test assemblies (S9), in the shape of ADR-0027 §1's `TenantDatabaseHandle` rule.

ADR-0010 rule 9's other half — a job carrying "the permission context of whoever caused it" — goes through `FromPrincipal`, which already performs the full comparison (A1.2 H-1). `ForSystemJob` is only the *named system principal* case, and it is now as narrow as that name claims.

## A2.3 — H-C: a `Join` creates an `Active` Membership at issue

A1.1 B-2 said "redeeming a join requires authenticating as that user" while the Membership was already `Active` at issue, so the gate guarded something that had already happened: anyone who may provision could make an arbitrary existing person an active administrator of their tenant, and that person's first knowledge of it would be a notification.

**Decision — a Membership created by provisioning is `Invited`, never `Active`, on both paths.** `MembershipState` is enumerated `Invited | Active | Suspended | Revoked`, and **redemption is the only transition to `Active`**. The mint precondition (§4) and `AccessSubject.FromPrincipal` (A1.2 H-1) already require `Active`, so an un-redeemed `Join` grants exactly nothing: no sign-in, no scope, no evaluation. Making it uniform across `Invite` and `Join` removes the asymmetry that produced the hole rather than patching one branch of it.

SPEC-001 AC-1 still holds literally — exactly one administrator Membership row exists after provisioning — but it is `Invited` until redeemed. If the project-manager intends AC-1 to mean *usable*, that is a spec question and is named in A2.6, not answered here.

## A2.4 — H-D: `TenantAccessReason` is caller-supplied, so H-5's gate is a one-word bypass

A1.2 H-5's state allow-list is keyed on the reason, and `OperatorSupport` is the only value that opens `Suspended` and `SchemaBlocked`. If the reason is an argument, the gate is advisory. ADR-0027's own options table rejects exactly this shape — *"the guarantee acquires an exception whose name is exactly what a careless developer would choose"* — about the `Migration` reason. The same sentence applies to this one and was not applied.

**Decision — the reason is bound by the factory, never passed to it.**

`ITenantScopeFactory.OpenAsync(TenantId, ct)` takes **no reason parameter**. `TenantAccessReason` stays a property of `TenantScope` for the gate and for logging, but it is determined by *which factory binding was resolved*, and each composition root registers exactly one:

| Composition root | Binding | May open |
|---|---|---|
| `Aurora.Web` | `Request` | `Active` |
| `Aurora.Worker` | `Job`, `Outbox` | `Active` |
| Provisioning saga host | `Provisioning` | `Provisioning`, `Active` |
| — | `Migration` | **no binding** — DDL uses `TenantDatabaseHandle` (ADR-0027) |
| — | `OperatorSupport` | **no binding in bootstrap** |

`OperatorSupport` has no binding because the operator console and ADR-0010 rule 8's time-boxed, reason-coded support grant do not exist yet; a test asserts an operator-support scope cannot be obtained from **any** bootstrap composition root. When it is built it arrives as a **separate factory type** taking a support-grant id that it validates against `catalog.operator_audit_event`, resolvable only in the operator composition root — not as an enum value a developer may type. Recording that shape now is the point: the follow-up cannot arrive as a parameter without contradicting this ADR.

Fitness rule T9 is extended: `ITenantScopeFactory` exposes no reason parameter, and no call site outside `Aurora.Platform.Tenancy` names a `TenantAccessReason` value — with a deliberately-violating fixture.

## A2.5 — H-E: "60 s on every path" is false for the path H-5 was raised for

A1.2 H-5 claimed the gate at `OpenAsync` bounds tenant-state staleness to 60 s "for every path". On the circuit path it does not: the reviewer's reading is that the scope is pinned at circuit creation, so the gate runs once and a `SchemaBlocked` tenant keeps being written for thirty minutes — which is the case H-5 exists for. My phrase *"a circuit does not open a scope, it reads the ambient one"* is the error.

**Decision — the circuit pins the `TenantId`; a `TenantScope` is opened per unit of work.** This is what ADR-0007 already says when read together: §3.2 resolves the tenant *"once per unit of work"*, §3.3 pins **the tenant** — not a scope — to the circuit, and §10.4's stale-scope trap plus `IsActive` being false after disposal only make sense if a scope's lifetime is an operation. So one `TenantScope` per component event handler or application-service invocation, opened at its start and disposed at its end; `ITenantScopeAccessor.Current` returns the current unit of work's scope and throws off it. The gate then runs on every unit of work and the 60 s bound is real on the circuit path.

The criterion that makes this falsifiable is a **count**: over a circuit's life the number of scope opens must be greater than one. A circuit-lifetime scope makes that count exactly 1, which is the implementation this amendment exists to prevent. Paired with it: suspend the tenant mid-circuit and assert the next component action fails within 60 s. The fitness rule that forbids a `TenantScope` field on a `CircuitHandler` or a component is **T11** (`testing-strategy.md` §5.3): `task/ARCH-TENANT-DOORS` has merged and reserved `T9`-`T14` for this branch, so the id is allocated rather than deferred.

## A2.6 — The `B-16` numbering contradiction, and how dependencies are written from now on

`solution-layout.md` §6.1 wrote two audit rows — B-16.1 (store **and** `IAuditWriter`) and B-16.2 (the `[Auditable]` interceptor). The project-manager then split the first on size, so `docs/BACKLOG.md` — **the file the orchestrator dispatches from** — has B-16.1 = schema only, B-16.2 = `IAuditWriter` and the hash chain, B-16.3 = interceptor. A1.4's "depend on B-16.1, not B-16.2" is right against §6.1 and **backwards against the backlog**, where it points two rows at an empty table.

**Decision.** The backlog's numbering is authoritative — the split is the project-manager's and it is a good one — and `solution-layout.md` §6.1 is reconciled to it. The two rows that write tenant-side audit events depend on **the row that ships `IAuditWriter` and the hash chain, which is `B-16.2` in `docs/BACKLOG.md`**. A1.4's correction stands in substance (neither row uses the `[Auditable]` interceptor, `B-16.3`) and is wrong in its number.

**And the rule that stops this recurring:** a dependency in an architecture document names **what the row ships**, with the number in parentheses. A number alone is a reference that a legitimate size split silently inverts, which is exactly what happened here.

## A2.7 — Every append-only claim in this ADR cited a mechanism that has since been withdrawn

Discovered on merging the integration branch, not by review: **ADR-0028 Amendment 1 replaced ADR-0028 §2**, because `ALTER DEFAULT PRIVILEGES … REVOKE UPDATE, DELETE` is a **no-op on PostgreSQL 17.11** and the criterion built on it could not fail — and `has_table_privilege`, which §2 and this ADR both named as the probe, is blind to a column-level grant and short by `MAINTAIN` on PostgreSQL 17. Every append-only sentence in §7, A1.3 M-7 and A2.1 inherited both faults.

**Decision — `catalog.authentication_event` and `catalog.identity_credential` follow ADR-0028 Amendment 1, and `catalog` takes no default-privileges policy at all:**

1. Privilege is asserted by comparing `aclexplode(pg_class.relacl)` **plus** `pg_attribute.attacl`, for `aurora_app` **and** `PUBLIC`, against the recorded decision in `CatalogSchemaAllowlist.AppRolePrivileges` — then executed as `aurora_app`: `INSERT` succeeds on the trail, `UPDATE`/`DELETE` return `42501`, and `SELECT` on `identity_credential` returns `42501`.
2. Each entry **names its writer**, per `solution-layout.md` §6.4 item 5 criterion 3: a privilege decision that cannot name who needs it has not been made.
3. The `BEFORE UPDATE OR DELETE` row trigger sits on the **partitioned parent** and is probed as `aurora_migrator` through the parent, against a partition, and against a partition created *after* the trigger — and a `BEFORE TRUNCATE` statement trigger must be **attached to every partition the partition job creates**, because truncate triggers are not cloned.
4. **`ALTER DEFAULT PRIVILEGES` is not used in schema `catalog`** in either direction: the `GRANT` direction is B-05 finding M-1's blanket-grant shape, and the `REVOKE` direction writes no `pg_default_acl` row at all, which is why it was a no-op. §6.4 item 5 criterion 5 asserts zero rows for the schema and this ADR's tables must keep that true.

The general lesson, and it is the same one as A2.6: **this ADR cited a mechanism by section number and inherited its later withdrawal silently.** Cite the mechanism *and* what it must demonstrate — here, "an `INSERT` that succeeds and an `UPDATE` that returns `42501`, as `aurora_app`" — so that a citation going stale shows up as a test that stops making sense rather than as a probe that quietly passes against a no-op.

## A2.8 — Named, not answered

Added to §9 and A1.4's list: the **operator-support scope factory and its support-grant validation** (A2.4 — the shape is recorded, the flow is not designed); whether **SPEC-001 AC-1** means an administrator Membership that *exists* or one that is *usable*, now that A2.3 makes it `Invited` until redeemed — a project-manager question; and the ownership question raised by A2.7: whether `catalog.authentication_event` should be created by `solution-layout.md` §6.4 item 5's row (the catalog's other append-only tables) rather than by `B-18.9` — B-18.9 now depends on it for `CatalogSchemaAllowlist`, and merging them is the project-manager's call, not this ADR's. (The fitness-rule id for "no `TenantScope` field on a `CircuitHandler` or component" is no longer open: `task/ARCH-TENANT-DOORS` has merged, the §5.3 range is settled, and the rule is **T11**.)
