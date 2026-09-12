# ADR-0013 — Versioned REST API, public API and webhooks

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0005 (Blazor Server), ADR-0009 (auth), ADR-0010 (authorization), ADR-0015 (events)

## Context

`CLAUDE.md` requires versioned APIs, consistent resource naming, RFC 9457 Problem Details for errors, and idempotency keys on commands that create financial documents. Beyond that, a public API and webhooks are what let an ERP live in a customer's ecosystem — and, per ADR-0005's consequences, the REST API is also **the migration path away from Blazor Server** if circuit costs ever force it. It is not an afterthought; it is the second consumer that keeps the application layer honest.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Versioned REST + OpenAPI** *(chosen)* | Universally understood by the integrators who will actually use it (accountants' tools, e-commerce platforms, bespoke scripts); cacheable; trivially testable with `curl`; OpenAPI generation is in-box in .NET 10 (`Microsoft.AspNetCore.OpenApi` 10.0.12, MIT) | Over-fetching; N+1 round trips for graph-shaped reads |
| GraphQL | One round trip for graph reads; client-driven shape | Query cost control against a per-tenant database is a research project; authorization per field is a large surface; far outside the skill set of the typical integrator in this segment |
| gRPC | Efficient, strongly typed | Poor fit for third-party integrators and browser clients; no human-readable debugging |
| No public API in v1 | Less work now | Kills the ADR-0005 escape hatch and makes every integration a bespoke export. Rejected |

**Versioning scheme:** URL path (`/api/v1/...`) over header or query-string versioning. It is visible in logs, in a browser address bar and in a support ticket. `Asp.Versioning.Http` 10.2.3 (**MIT**, verified 2026-09-11).

**Minimal APIs over controllers**, grouped per module, because endpoint definitions then sit next to the module they belong to and the group carries the shared authorization and versioning metadata.

## Decision

1. **`/api/v{major}/...`**, major version only. A breaking change means a new major version; additive changes never do. **Two majors are supported concurrently, and a deprecated major runs for at least 12 months** after its successor ships, announced through a `Sunset` header and the developer portal.
2. **Resource naming:** plural nouns, kebab-case, no verbs — `/api/v1/sales-orders/{id}/lines`. Actions that are not CRUD are sub-resources representing the event: `POST /api/v1/sales-invoices/{id}/postings`, not `/postInvoice`.
3. **Errors are RFC 9457 Problem Details**, always, including validation failures (`errors` extension member keyed by field path). `type` is a stable, documented URI per error class; `traceId` is always present (ADR-0016). A stack trace never crosses the boundary.
4. **Idempotency:** every command that creates or posts a financial document requires an `Idempotency-Key` header. The key, the request fingerprint and the response are stored per tenant for 24 hours; a replay with the same key returns the original response; the same key with a different body is `409`. **A network retry must never create a second invoice** — this is a correctness requirement, not a convenience.
5. **Concurrency:** `ETag` on resource reads, `If-Match` required on updates, `412` on mismatch. Optimistic concurrency is visible at the API, not just in the database.
6. **Pagination is mandatory and cursor-based** (opaque cursor, `limit` with a hard maximum of 200). There is no unpaginated collection endpoint. Ever.
7. **Authentication** via OAuth 2.0 bearer tokens from OpenIddict (ADR-0009); scopes map to permissions (ADR-0010); the tenant comes from the token, cross-checked against the route.
8. **Rate limiting** per (tenant, client) using ASP.NET Core's built-in limiter, with `429` plus `Retry-After`. A partner's runaway script must not become another tenant's outage — though ADR-0007's per-tenant pools already bound the damage.
9. **Webhooks** are the outbound half. Subscriptions are per tenant and per event type, fed from the outbox (ADR-0015), delivered at-least-once with exponential backoff and a dead-letter queue. Payloads are signed with a per-subscription HMAC-SHA-256 over `timestamp + body`; consumers must reject stale timestamps. Every event carries the same `message_id` the outbox used, so consumers can deduplicate. Delivery history is visible to the tenant.
10. **OpenAPI is generated and committed**, and a contract test fails the build when the document changes without a version bump — the same discipline ADR-0008 §3.1 applies to the package contract, for the same reason.
11. **The API is a first-class consumer of the same application services the UI uses.** No endpoint may touch a `DbContext` or a domain aggregate directly; a fitness test enforces it. This is what keeps the ADR-0005 escape hatch real.

## Consequences

- Positive: integrators can build against us on day one; the API doubles as the migration path away from Blazor Server (ADR-0005).
- Positive: idempotency keys and `If-Match` push two classes of financial defect out of existence rather than documenting them.
- Positive: because UI and API share the application layer, authorization and validation cannot diverge between them.
- Negative: a 12-month deprecation window means running two majors for a year, with the test matrix that implies. That is the price of being an integration target rather than a silo.
- Negative: cursor pagination is harder to implement than offset and rules out "jump to page 47". Accepted: offset pagination over a growing ledger produces duplicated and skipped rows, which in an ERP is a correctness bug wearing a UX costume.
- Negative: idempotency storage is per-tenant state with its own cleanup job.

## Revisit when

Integrators consistently need graph-shaped reads (consider a narrow read-only GraphQL facade over the same application services, not a replacement), or a bulk-import use case makes per-request REST semantics impractical (a bulk endpoint with a job handle is the answer, not a new protocol).
