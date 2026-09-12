# ADR-0005 — Frontend: Blazor Server (`InteractiveServer`)

- **Status:** Accepted (2026-09-10) — **locked by the product owner**
- **Deciders:** product owner (choice), architect (consequences)

## Context

The UI is a data-dense back-office application: order entry grids, stock lookups, ledger enquiry, period close. Users range from a controller on a laptop to a warehouse operator on a handheld scanner. Every user-facing string is localized from day one; WCAG 2.2 AA is required.

Blazor Server holds one **SignalR circuit per browser tab**, with component state on the server, and ships UI diffs over a WebSocket.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Blazor Server, `InteractiveServer`** *(chosen)* | One language and one type system end to end; no separate API needed for the UI, so the first release is materially faster; secrets and business rules never leave the server; tiny initial download; a data grid can page against the database without an intermediate DTO layer | One live circuit per user with server-held state (~250 KB baseline, more with grid state); requires sticky routing in a web farm; a network blip suspends the UI; latency is felt on every interaction; server memory becomes a function of concurrent users |
| Blazor WebAssembly | No server-held UI state; offline-capable; server scales as a pure API | Multi-megabyte initial download on a warehouse tablet; every screen needs an API endpoint, roughly doubling the surface for the first release; secrets and rules must be assumed public |
| Blazor Web App with `InteractiveAuto` | Server-render first, then transparently move to WASM | Needs both a server and a client implementation of every data access path from day one; the locked decision is `InteractiveServer` |
| React/Angular SPA + REST API | Largest hiring pool for front-end specialists; mature component ecosystems | Two languages, two build chains, two validation implementations; the API surface must exist before any screen does |

## Decision

Use **Blazor Server with the `InteractiveServer` render mode**, as locked.

Design rules that follow, and that are binding:

1. **A component must never touch a `DbContext`, a repository or a domain aggregate directly.** Components call an application service through its module `.Application` interface and receive DTOs from a `.Contracts` assembly. Enforced by an ArchUnitNET test (`Aurora.Web` may not reference any module's `.Domain` or `.Infrastructure` assembly).
2. **The tenant is resolved once, at circuit creation, and pinned to the circuit** — there is no HTTP request during an interactive render, so anything that reads the tenant from `IHttpContextAccessor` is a bug waiting for production. See ADR-0007 §3.
3. **Every grid virtualizes and pages server-side** from the first grid we write (`Virtualize` with an `ItemsProviderDelegate`, page size ≤ 100). Retrofitting this later is the known failure mode for Blazor Server ERPs.
4. **Circuit state is small.** Components hold identifiers and the current page of data, never whole result sets, never a loaded aggregate graph.
5. Long-running work (reports, imports, period close) is dispatched to the worker as a job; the circuit subscribes to progress. A circuit never blocks on work longer than a second.
6. `DetailedErrors` stays off in production (it defaults to off) — circuit exceptions otherwise leak internals to the browser.

## Consequences

- **The architect disagrees, mildly and on the record.** For an ERP with many light, intermittent users — warehouse scanners, shop-floor terminals — server-held circuits convert a cheap user into a persistent memory and CPU cost, and make every interaction latency-sensitive over networks we do not control. `InteractiveAuto` would have been the better default, with `InteractiveServer` for the heaviest data screens.
  **Mitigation, and it is a real one:** rule 1 above (components depend only on application services and DTOs) means the *entire* migration cost to `InteractiveAuto` or WebAssembly later is exposing the existing application-service methods over the already-planned versioned REST API (ADR-0013) and swapping the injected client. No component rewrite. We keep that door open deliberately, and the architecture fitness test is what holds it open. This does not need a superseding ADR to exercise: adding a render mode is additive.
- Server memory is a function of concurrent users. `scalability.md` §3 does the arithmetic and states the ceiling per instance.
- A web farm needs **sticky routing**; an active circuit cannot move between instances. .NET 10's persisted circuit state (`CircuitOptions.HybridPersistenceCache`, `PersistedCircuitInMemoryMaxRetained = 1000`, in-memory retention 2 h, distributed retention 8 h) lets a *reconnect* land on a different instance, which is a genuine resilience improvement over .NET 8 — but it does not remove the need for sticky routing while connected.
- Defaults we will tune and must therefore know (verified from `CircuitOptions` in the ASP.NET Core 10 source, 2026-09-10): `DisconnectedCircuitMaxRetained = 100`, `DisconnectedCircuitRetentionPeriod = 3 minutes`, `JSInteropDefaultCallTimeout = 1 minute`, `MaxBufferedUnacknowledgedRenderBatches = 10`, `MaximumReceiveMessageSize = 32 KB`.
- Because the UI is server-side, we get authorization enforcement for free at the service layer — but only if rule 1 holds. If a component ever queries directly, both the authorization check and the tenant guarantee move into the component, where nobody tests them.

## Revisit when

Concurrent circuits per instance exceed the ceiling in `scalability.md` §3 and horizontal scaling stops being the cheapest answer, or measured interaction latency for remote users exceeds 300 ms p95. The response is a new ADR adding `InteractiveAuto` for named screens, not replacing this one.
