# ADR-0006 — Architectural style: a modular monolith with enforced boundaries

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** `../architecture/modules.md`, ADR-0007, ADR-0015

## Context

Aurora ERP is one product, built by a small team, serving businesses of 10–250 employees. Its modules — sales, inventory, ledger, tax — are tightly coupled by business meaning: shipping goods and recognising cost of goods sold are the same event, and posting an invoice must be transactional with the ledger entry it produces. At the same time the system must be maintainable for a decade and must not be painted into a corner if one part ever needs to scale independently.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **Modular monolith with test-enforced boundaries** *(chosen)* | One deployment, one transaction boundary where correctness needs one; refactoring across a boundary is a compiler-checked rename, not a coordinated release; a small team can hold it in their heads; module boundaries are still real because fitness tests make them real | Boundaries decay without enforcement; one bad module can consume the whole process's resources; every module scales together |
| Microservices from the start | Independent scaling and deployment; hard isolation | A posting spanning sales, inventory, tax and ledger becomes a distributed transaction or a saga with compensations — trading correctness (quality attribute #1) for scale we do not need; N deployments, N databases *per tenant* under ADR-0007, which multiplies an already expensive tenancy model; the boundaries would be guessed before the domain is understood |
| Layered monolith, no module boundaries | Simplest to start | Becomes a ball of mud in an ERP faster than almost any other domain, because every entity plausibly relates to every other. This is the failure mode we are specifically trying to avoid |
| Serverless functions | Elastic, pay-per-use | Blazor Server holds a stateful circuit; the runtime model is incompatible. Cold starts against per-tenant connection pools would be pathological |

## Decision

Build a **modular monolith**: one solution, two host processes from one image (`Aurora.Web`, `Aurora.Worker`), modules as bounded contexts with strict boundaries that are **enforced by automated fitness tests, not by discipline**.

The enforcement, specified in `../architecture/modules.md` and `../architecture/testing-strategy.md` §5:

1. A module owns a database schema; nothing else reads or writes it.
2. A module exposes exactly one contract assembly; other modules may reference only that.
3. Tiers make the graph acyclic: a module may reference only strictly lower tiers; same-tier modules communicate by integration event.
4. Aggregates reference other modules' aggregates by identifier only.
5. Every rule above is a test that fails the build, and each such test ships with a deliberately-violating fixture proving it can fail.

**Designed for extraction, not extracted.** Because a module's only inbound surface is its contract assembly and its events already cross an at-least-once boundary with idempotent consumers, replacing an in-process implementation with a remote client is a change in `Aurora.Composition` and nowhere else. The plausible candidates are Reporting and DocumentExchange. Ledger is explicitly not a candidate: making posting remote would trade correctness for scale, inverting the quality ranking in `../architecture/overview.md` §4.

## Consequences

- Positive: transactional correctness where the domain demands it; one thing to deploy, migrate and observe — which matters enormously given ADR-0007 already gives us thousands of databases to operate; cross-module refactoring stays cheap while the domain is still being learned.
- Positive: the boundary rules are visible in CI, so a boundary violation is a red build rather than a code-review argument.
- Negative: every module shares one process's memory and CPU; a runaway report in Reporting can affect order entry on the same instance. Mitigated by moving long work to `Aurora.Worker` (ADR-0014) and by per-tenant pool caps (ADR-0007 §5.2).
- Negative: modules deploy together, so a change anywhere requires the whole gate to pass. That is a feature for correctness and a cost for throughput; `verify.sh` staying under ten minutes (`../architecture/solution-layout.md` §5.4) is what keeps it tolerable.
- Negative: the fitness-test suite is real code that must be maintained. The alternative — boundaries maintained by memory — does not survive one personnel change.

## Revisit when

A single module's resource profile diverges sharply from the rest (sustained CPU or memory pressure traceable to Reporting or DocumentExchange), or the team grows past the point where one deployment cadence is tolerable (roughly three independent teams). The response is extracting that one module, not adopting microservices wholesale.
