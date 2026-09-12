# ADR-0017 — Validation

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0005 (Blazor forms), ADR-0013 (Problem Details), ADR-0022 (localization)

## Context

`CLAUDE.md` requires validation at the boundary and aggregates that protect their own invariants. Those are two different jobs and using one mechanism for both produces either an anemic domain model (all rules in validators) or an unusable API (all rules as exceptions from deep inside an aggregate).

## Options considered

| Option | Licence (verified 2026-09-11) | Pros | Cons |
|---|---|---|---|
| `System.ComponentModel.DataAnnotations` only | MIT, in-box | Zero dependency; works with Blazor's `EditForm` out of the box | Attributes cannot express cross-field or asynchronous rules cleanly; conditional rules become unreadable; encourages putting rules on DTOs and nowhere else |
| **FluentValidation 12.1.1 at the boundary + invariants inside aggregates + database constraints** *(chosen)* | **Apache-2.0** | Rules are testable objects; cross-field, conditional and async rules are natural; one validator per command, run identically for the UI and the API; the domain stays the owner of business invariants | A dependency; FluentValidation 12 removed the `FluentValidation.AspNetCore` auto-validation pipeline, so wiring is explicit — which suits a pipeline behaviour we want anyway |
| Hand-rolled guard clauses everywhere | — | No dependency | Every developer invents a different error shape; localization and Problem Details mapping get re-implemented per endpoint |

## Decision

**Three layers, with a clear rule for what belongs where.**

### Layer 1 — boundary validation (FluentValidation)

- One `AbstractValidator<TCommand>` per command, executed by a pipeline behaviour **before** the handler, for UI and API alike. One implementation, two consumers — which is the only way they stay consistent.
- Scope: shape, required-ness, length, range, format, referenced-entity existence, and cross-field consistency (`ValidFrom < ValidTo`).
- Failures become RFC 9457 Problem Details with an `errors` member keyed by the field path (ADR-0013 §3), and `400`.
- **Messages are localized resource keys, never English prose in code** (fitness rule A1). A validator emits a key plus placeholder values; rendering happens at the boundary in the user's culture.
- Blazor forms use the same validators through a `FluentValidationValidator` component so a field turns red for the same reason the API returns `400`.

### Layer 2 — domain invariants (inside the aggregate)

- Enforced by construction: value objects validate in their constructors (`Money` cannot exist without a currency; `Quantity` cannot be negative for a stocked item), and aggregate methods refuse illegal transitions.
- Violations **throw**, they do not return validation results. Reaching an aggregate with data that breaks an invariant is a programming error or a missing layer-1 rule, not user input.
- **This layer is never optional and never bypassed.** A rule such as "a journal entry must balance" or "a posted invoice cannot be edited" lives here, not in a validator, because a validator can be forgotten at a new call site and a constructor cannot.
- No FluentValidation reference in any `.Domain` assembly (fitness rule L1 covers it).

### Layer 3 — database constraints

Every invariant expressible as a constraint is **also** a constraint: `NOT NULL`, `CHECK`, unique indexes, foreign keys within a module, and `EXCLUDE USING gist` for effective-dated ranges (ADR-0008 §6.2). Application code is not the only thing that writes to the database — migrations, maintenance scripts and bulk fixes do too — and the constraint is what makes a corrupt row impossible rather than unlikely.

### The rule for deciding

*Would a well-behaved client already know this is wrong?* → layer 1. *Is this a truth about the business that must hold however the data arrived?* → layers 2 and 3.

## Consequences

- Positive: one validator per command serves UI and API, so the two cannot drift.
- Positive: the domain keeps its invariants, so the model stays rich and the rules stay testable with fast unit tests.
- Positive: database constraints catch the paths code does not cover — the ones that surface as data-integrity incidents years later.
- Negative: a rule can be expressed in more than one layer and a developer may duplicate it. Duplication between layers 1 and 3 is acceptable and often desirable; duplication between 1 and 2 is a smell and is called out in review.
- Negative: FluentValidation 12's removal of auto-validation means explicit wiring. Small one-time cost, and the pipeline behaviour gives us a single place to log, localize and shape errors.
- Negative: localized validation messages are more work than inline strings. Non-negotiable: `CLAUDE.md` forbids hard-coded user-facing strings and a Country Package must be able to ship a locale without touching core (ADR-0022).

## Revisit when

FluentValidation's licence or maintenance status changes (it is Apache-2.0 and actively maintained as of 2026-09-11, with the author asking commercial users to sponsor rather than to pay), or async validators start causing N+1 queries at the boundary — in which case existence checks move into the handler where they can be batched.
