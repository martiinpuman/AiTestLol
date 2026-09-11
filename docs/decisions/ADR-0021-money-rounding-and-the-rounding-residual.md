# ADR-0021 — Money, rounding and the rounding-residual account

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0002 (`decimal`), ADR-0004 rule 3, ADR-0008 §6 (effective-dated rounding basis), ADR-0023 (tax)

## Context

Financial correctness is quality attribute #1. Money is the most-touched type in the system and the one whose semantics can never be renegotiated once a ledger contains a million rows. `CLAUDE.md` fixes the essentials — money is a decimal amount plus a currency, never floating point — and leaves the hard part: **where rounding happens, which way it goes, and where the residual lands.**

The concrete problem this ADR exists to solve: the sum of rounded line amounts is not always the rounded sum. Peppol BIS Billing 3.0 acknowledges this directly with an explicit `PayableRoundingAmount` element, and its business rules exist to bound the difference (`../research/processes/order-to-cash.md`). A system that silently absorbs the difference into the last line produces invoices that do not reconcile and ledger entries whose origin nobody can explain three years later.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **`Money` value object: `decimal` amount + ISO 4217 currency, ours** *(chosen)* | `decimal` is 128-bit base-10 and exact for the magnitudes an SMB ERP sees; the currency travels with the amount so a currency-less amount cannot exist; we control rounding semantics completely, which is the whole point | We own and must test it |
| Integer minor units (`long` cents) | Exact by construction; no rounding inside arithmetic | Minor-unit scale varies by currency (JPY 0, most 2, some 3); unit prices need sub-minor precision (`numeric(19,6)`), so a second representation appears anyway; every read and write converts; EF mapping and SQL reporting become unpleasant for accountants |
| A third-party money library (e.g. NodaMoney 2.8.0, Apache-2.0) | Free, permissive, well-tested | `Money` is the single most-touched type in this system and its rounding policy must be *ours* and jurisdiction-overridable (ADR-0008 §6.2). Adapting someone else's semantics is more work and more risk than owning ~250 lines |
| `double`/`float` | Fast | Actively hostile to a general ledger. Banned by fitness rule F1 |

## Decision

### 1. The type

```csharp
public readonly record struct Money(decimal Amount, Currency Currency);
```

- Arithmetic between different currencies **throws**; there is no implicit conversion and no "default currency".
- No implicit conversion to `decimal`. Extracting `.Amount` is explicit and visible in review.
- `Currency` is an ISO 4217 code plus its minor-unit count, from a core table seeded by core (currencies are not jurisdiction-specific; their *usage* is).
- `Percentage` and `Quantity` are separate value objects. A quantity times a unit price is a `Money`; a `Money` times a `Money` is a compile error.

### 2. Storage precision (ADR-0004 rule 3)

Amounts `numeric(19,4)`, unit prices `numeric(19,6)`, exchange rates `numeric(19,10)`. Storage carries **more** precision than presentation so that intermediate values are not rounded prematurely; rounding happens at the defined points below and nowhere else.

### 3. Rounding mode

**The core default is `MidpointRounding.AwayFromZero` ("round half up")**, because that is what most commercial invoicing and tax rules specify. It is set explicitly at every rounding call — never left to a framework default, which differs between `Math.Round` (to-even) and `decimal.Round` overloads and is exactly the kind of implicit behaviour that produces a one-cent discrepancy nobody can locate.

**A Country Package may override the rounding mode and the rounding basis**, effective-dated, as package data (ADR-0008 §6.2). Core never branches on country; it asks for the applicable rounding policy as of the document date.

### 4. Where rounding happens — and only there

1. **Line net** = `quantity × unitPrice`, then allowances and charges, rounded to the currency's minor units.
2. **Line tax** = line net × rate, rounded per the package's basis (per line or per document — both exist in the wild and the package declares which, ADR-0023).
3. **Document totals** = sums of already-rounded components. Nothing is rounded twice.
4. **Functional-currency conversion** = transaction amount × rate, rounded to the functional currency's minor units, once, at the document date.

Every other calculation carries full `decimal` precision.

### 5. The rounding residual — the promise this ADR is named for

When the sum of rounded components differs from the rounded sum, the difference is **explicit and visible**:

- It is recorded on the document as `RoundingAmount` (mapping directly to Peppol's `PayableRoundingAmount`).
- It posts to `AccountRole.RoundingResidual` — a real account, resolved by role, supplied by the Country Package's chart-of-accounts template (ADR-0008 §6.1).
- **It is never silently absorbed into the last line.** Absorbing it changes a line's unit economics and breaks reconciliation against a customer's own records.
- A residual larger than a configured tolerance (default: two minor units per document) **fails the posting** rather than posting quietly. A large residual means a calculation bug, not a rounding artefact, and it must surface immediately.

### 6. Allocation

Distributing an amount across lines — discounts, landed cost, payment application, tax apportionment — uses the **largest-remainder method**, so allocated parts sum **exactly** to the total with no leftover cent. A property-based test asserts `sum(parts) == total` for arbitrary inputs and arbitrary part counts, including negative totals and zero-weight parts.

### 7. Multi-currency

- Every document records: transaction currency and amount, the FX **rate**, its **source**, its **date**, and the company functional-currency amount.
- **The posted functional amount is fixed at the document date and never moves.** Re-translating history is how ledgers stop tying out.
- Open foreign-currency balances are revalued at period end as a separate, reversible posting to `AccountRole.FxGainLoss` (`../research/processes/record-to-report.md`).
- A company has one functional currency; group reporting currency translation is a reporting concern, not a posting concern.

### 8. Enforcement

Fitness rules F1–F4 (`testing-strategy.md` §5.4): no `double`/`float` in domain, application or contracts; `Money` maps to `numeric(19,4)` + `char(3)`; ledger lines are append-only; monetary arithmetic goes through `Money` operators rather than raw `decimal` extracted from one.

## Consequences

- Positive: a currency-less amount cannot exist in code or in the database.
- Positive: rounding is explicit, jurisdiction-overridable without a redeploy, and its residual is an auditable posting rather than a mystery cent.
- Positive: the tolerance check converts a class of silent calculation bugs into loud failures at the moment they occur.
- Negative: `Money` arithmetic is more verbose than `decimal`. That verbosity is the safety.
- Negative: owning the type means owning its tests. Mitigated by property-based tests, which cover far more of the input space than examples would.
- Negative: `numeric(19,4)` caps a single amount near 10^15 and gives four decimals. Sufficient for SMB ERP in every currency we expect, including high-inflation ones; a hyperinflationary currency requiring more digits would be a schema change and is recorded here as a known limit.
- Negative: the 2-minor-unit residual tolerance is a guess. It is configuration, it is logged when approached, and it will be tuned with evidence from the first real tenants.

## Revisit when

A Country Package needs a rounding rule the basis/mode model cannot express (e.g. rounding the invoice total to the nearest five cents, as some cash-rounding regimes require — likely an additive package-declared "cash rounding" step rather than a change here), or a currency needs more than four decimal places of settlement precision.
