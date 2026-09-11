# ADR-0023 — Tax registration and the tax engine

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0008 §6, §7 point 2, §8 (multi-country tenants), ADR-0021 (rounding)

## Context

Tax is where a country-agnostic core is most likely to fail. NetSuite's own history is the cautionary evidence: its original model had tax schedules effectively hard-coded, and it had to migrate to **SuiteTax**, a rules-based engine, at considerable cost (`../research/features/localization-packages.md`). Retrofitting a rules engine under a hard-coded tax model is one of the most expensive migrations a business system can undergo, and it is entirely avoidable by building the engine first and letting rules arrive as data.

The structural question this ADR must settle now, because the schema cannot be changed cheaply later: **how many tax registrations can a company have?**

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Hard-coded rates and rules per country in core | Fastest to a first working invoice | Forbidden by `CLAUDE.md`; every rate change is a release; NetSuite's documented migration is the evidence of where it ends |
| **Country-agnostic determination engine in core + effective-dated rules as Country Package data** *(chosen)* | Rate changes are package data releases; a new jurisdiction adds no core code; the engine is testable independently of any jurisdiction | We build a determination engine before we have a second jurisdiction to validate it against |
| An external tax service (Avalara, Vertex, etc.) | Someone else maintains the rules | Costs money and requires a sign-up — hard limits; puts a network call inside the posting path; and it still needs exactly this contract to call it. The seam is kept: `ITaxEngine` could be backed by an external provider per tenant later |
| **One tax registration per company** | Simplest model | Breaks the moment a company registers for indirect tax in a second jurisdiction — a normal event for an exporter, and a schema change to a table by then holding years of postings |
| **`Company` has 1..N `TaxRegistration` from day one** *(chosen)* | The schema and every contract signature admit the multi-jurisdiction case with no migration; v1 implements only the primary path | A nullable-but-unused dimension in v1 that developers must carry and not misuse |

## Decision

### 1. `ITaxRegistration` is 1..N on `Company` from the first migration

```
org.tax_registration(id, company_id, jurisdiction, scheme, number, is_primary, valid_from, valid_to)
```

- Exactly one registration is `is_primary` at any date (an exclusion constraint over `(company_id, validity)` where `is_primary`).
- **Every tax determination and every statutory report slot is keyed by `(company, taxRegistration, asOfDate)` in its contract signature**, even though v1 only ever passes the primary registration.
- This realises assumption A2 in `../architecture/overview.md` §6 and the deferred case in ADR-0008 §8.3: the multi-jurisdiction behaviour is out of scope for v1, the **schema and the contracts are not**. Turning it on later is behaviour and UI, never a data migration.

### 2. Core owns the determination pipeline; packages supply the rules

Determination inputs, all explicit, none implicit:

| Input | Source |
|---|---|
| Supplier company and its applicable tax registration | Organization |
| Counterparty, its country and its tax status/registration | Parties |
| Item tax category (standard, reduced, zero, exempt, out of scope) | Products |
| Place of supply, and whether goods or services | Document |
| **Tax point date** | Document — see §3 |
| Document type (invoice, credit note, prepayment) | Sales / Purchasing |

Output per line: one or more tax codes, each with rate, basis, reporting category, and — where applicable — a reverse-charge marker and an exemption reason code.

`ITaxEngine.DetermineAsync(context, asOf, ct)` and `.CalculateAsync(lines, asOf, ct)` live in core. The Country Package supplies `ITaxRuleProvider` data only: effective-dated rates, categories, rounding basis, reverse-charge and exemption codes (ADR-0008 §7 point 2). **Core contains no jurisdiction branch**, and fitness rule C1 fails the build if one appears.

### 3. The tax point date is explicit and may differ from the document date

The date that determines which rate applies is the **tax point** (also called the time of supply), which in different jurisdictions is the invoice date, the delivery date, or the payment date. The Country Package declares which. There is no overload of any tax API that omits an explicit date (fitness rule C5), because a convenience overload reading the clock is how a reprint of a 2025 invoice silently acquires the 2027 rate.

### 4. Tax on a posted document is frozen

A posted document's tax amounts never recalculate. A rate correction is a credit note plus a new invoice, consistent with `CLAUDE.md`'s immutability rule. Recalculating history would change a filed return.

### 5. Categories that e-invoicing needs

Reverse charge, zero rating and exemptions are modelled as **tax categories with a category code and an exemption reason code**, not as a zero rate with a note. EN 16931 (and therefore every Peppol profile, including PINT A-NZ) requires a VAT category code and, for exempt or reverse-charged lines, a reason. Modelling them as structured data now is what lets the e-invoicing extension point map them mechanically later (ADR-0008 §7 point 4).

### 6. Rounding

Tax rounding follows ADR-0021: per-line or per-document basis as declared by the package, `AwayFromZero` by default, residual to `AccountRole.RoundingResidual` and never absorbed into a line.

### 7. Tax reporting is a slot, not a query

Statutory return boxes map to **account roles and tax categories** (ADR-0008 §7 point 3), never to raw SQL over core tables, so a core schema change cannot break a jurisdiction's return.

## Consequences

- Positive: a rate change — including a mid-year one — is a Country Package data release with no core change and no redeploy.
- Positive: the 1..N registration decision costs one table and a nullable dimension now, and saves a migration over a table holding years of immutable postings later. This is the cheapest insurance in the whole design.
- Positive: structured categories and exemption reasons make the e-invoicing mapping mechanical rather than heuristic.
- Negative: v1 carries a dimension it does not exercise. Developers may be tempted to pass `null` or to add a convenience overload without it. Both are blocked: the parameter is non-nullable and fitness rule C5 forbids the overload.
- Negative: a determination engine built before a second jurisdiction exists risks being shaped by New Zealand's simplicity. Mitigation: the engine is validated against **two** jurisdictions before it is considered stable — New Zealand first, Australia second, exactly as the researcher sequenced it, and Ireland or the UK third as the first real multi-rate VAT test.
- Negative: tax point date as a first-class concept is one more thing every document must carry and every developer must understand. It is also the difference between a correct and an incorrect return.

## Revisit when

A single legal entity needs simultaneous obligations in several jurisdictions (the deferred case — the schema already allows it; see `../HUMAN_INBOX.md` Q6), a jurisdiction requires tax-on-tax or cascading taxes the current rule shape cannot express, or a tenant asks for an external tax service, in which case `ITaxEngine` gains a provider-backed implementation and nothing else changes.
