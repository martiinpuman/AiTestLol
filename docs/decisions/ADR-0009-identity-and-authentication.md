# ADR-0009 — Identity and authentication

- **Status:** Accepted (2026-09-11) — rule 4 superseded **in part** by ADR-0029; the mechanics of rules 1-3 (credential storage, cookie shape, what revalidation checks) are specified by ADR-0029 and the decisions here are unchanged. Rule 3's 30-minute circuit revalidation stands; ADR-0029 Amendment 1 adds a 60-second cookie-path security-stamp validation beside it and states exactly what each bounds
- **Deciders:** architect
- **Supersedes:** —
- **Superseded by:** **in part** by ADR-0029 — rule 4's *"cross-checked ... on every request"*, which leaves a live Blazor Server circuit unchecked, is replaced by the three named checkpoints in ADR-0029 §4. Every other decision in this ADR stands.
- **Related:** ADR-0005 (Blazor Server), ADR-0007 §3, §9.3, ADR-0010 (authorization), ADR-0013 (public API), ADR-0029 (sign-in, `tid`, permission evaluation)

## Context

Authentication must resolve a person **before** a tenant is known — you cannot look someone up in a tenant database you have not chosen yet. Hence platform identity lives in the catalog (ADR-0007 §9.3) and holds authentication only; authorization is per tenant. An external accountant is one identity across several tenants and is a first-class case, not an edge case.

Required: interactive sign-in for the Blazor Server circuit, MFA, machine-to-machine access for the public API (ADR-0013), webhook delivery signing, and a credible path to enterprise SSO without redesign. OWASP ASVS L2 is the reference.

## Options considered

| Option | Licence / cost (verified 2026-09-11) | Pros | Cons |
|---|---|---|---|
| **ASP.NET Core Identity + cookie auth + OpenIddict 7.7.0 for OAuth2/OIDC** *(chosen)* | MIT (Identity, in-box) + **Apache-2.0** (OpenIddict) | No licence cost, no external runtime; Identity gives password hashing, lockout, TOTP MFA and token providers out of the box; OpenIddict is a certified, actively maintained OAuth 2.0/OIDC server library that runs *inside* our host, so the public API and future SPA/mobile clients are served by the same process and the same user store; standards-based, so federating an external IdP later is configuration | We own the identity code, including the parts that are easy to get subtly wrong; OpenIddict has a real learning curve |
| Duende IdentityServer | **Commercial**: tiers reported at USD 5,750 / 12,500 / 24,900 per year (2026) | The most complete .NET OIDC product; excellent docs | Violates `CLAUDE.md`'s permissive-licence requirement, and costs money — a hard limit for this team. Rejected on licensing, not on quality |
| Keycloak | Apache-2.0 | Mature, feature-rich, free, battle-tested SSO and federation | A separate JVM service to deploy, upgrade and secure, breaking the "one image, two entrypoints" model; the user store would live outside the catalog, duplicating the membership model that ADR-0007 §9.3 already needs |
| Managed IdP (Entra External ID, Auth0, Cognito) | Per-MAU pricing | Least code; someone else is on call | Forbidden by the hard limits (no service sign-ups, no cloud resources, no spending); also per-user pricing on a per-seat ERP is a poor cost shape |

## Decision

1. **ASP.NET Core Identity** with custom stores over `catalog.identity_user`. Email is globally unique on the platform; there is no per-tenant password realm. Password hashing, lockout, TOTP MFA and recovery codes use the framework's own implementations — deliberately boring.
2. **Cookie authentication** for the Blazor Server circuit. Sign-in happens over a real HTTP request; the circuit then carries the principal.
3. **`RevalidatingServerAuthenticationStateProvider` with a 30-minute interval.** Without it, a circuit that lives for eight hours keeps a principal that was revoked seven hours ago. This is the single most-missed Blazor Server security detail and it is mandatory here.
4. **Tenant selection happens after authentication.** The `tid` claim is minted per tenant session and is cross-checked against host/path resolution on every request (ADR-0007 §3.2); disagreement is a `403`, audited.
5. **OpenIddict** issues tokens for the public API: authorization code + PKCE for user-delegated access, client credentials for machine-to-machine. **An "API key" is an OAuth client, not a bespoke secret** — one credential model, one revocation path, one audit trail. Refresh tokens rotate; reuse of a rotated token revokes the family.
6. **Enterprise SSO is deferred, not designed out.** Federation is `AddOpenIdConnect` plus a per-tenant `catalog` row mapping an email domain to an external issuer. No schema change is required to enable it later.
7. Webhook payloads are signed with a per-subscription HMAC-SHA-256 secret and a timestamp, with replay rejection (ADR-0013).
8. Secrets — signing keys, client secrets — come from the host's secret store. Signing certificates rotate with an overlap window so tokens issued under the previous key still validate.

## Consequences

- Positive: no licence cost, no third-party dependency on a critical path, one process, and a standards-based surface that federation, mobile clients and partner integrations can all use unchanged.
- Positive: because identity is ours and lives in the catalog, an external accountant with memberships in twelve tenants is a normal query, not an integration problem.
- Negative: we own security-critical code. Mitigation: use framework primitives rather than hand-rolled crypto; ASVS L2 checklist as a review gate on every auth change; no custom password hashing, ever.
- Negative: OpenIddict configuration is intricate and mistakes are silent. Mitigation: integration tests for each grant type, including negative tests (expired token, wrong audience, replayed refresh token, token issued for tenant A presented to tenant B).
- Negative: MFA is opt-in per user at launch. Enforcing it per tenant policy is a backlog item, not a v1 feature — recorded so it is not mistaken for done.

## Revisit when

A customer requires SAML (OpenIddict is OIDC/OAuth only — SAML would mean a gateway or Keycloak), or the number of tenants demanding per-tenant federated SSO makes the mapping table's operational cost visible, or OpenIddict's licence or maintenance status changes.
