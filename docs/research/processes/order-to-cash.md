# Order-to-Cash (O2C)

Status: draft v1. Author: researcher. Date: 2026-09-10.

## Conclusion

Order-to-cash is a document chain — **Quote → Sales Order → Delivery/Shipment → Invoice → Payment/Cash Application** — where each document has its own lifecycle state and its own partial-fulfillment problem. The single design decision with the widest blast radius: **a delivery and an invoice are not the same event and must be separate aggregates**, because real trading/wholesale businesses routinely deliver in partial shipments against one order and invoice differently than they ship (consolidated, or per-shipment, or on a billing schedule). Getting this wrong (fusing "shipped" and "invoiced" into one step, as the cheapest accounting-only tools implicitly do) is exactly the gap that pushes O2C-only tools like Xero out of the trading/wholesale segment (see `00-landscape.md`).

The nastiest edge cases below (partial delivery, credit notes, rounding, multi-currency, reversals) are not corner cases for our target industries — they are the normal case for a wholesale business with backorders, damaged-goods returns, and export customers. They should be first-class design inputs, not exception handling bolted on later.

---

## The core flow

```
Quote/Estimate --(accept)--> Sales Order --(confirm, credit check)--> [Reserved/Allocated]
       |                                        |
       |                                        v
       |                              Pick / Pack / Ship  --(partial or full)--> Delivery(ies)
       |                                        |
       |                                        v
       +--------------------------------> Invoice(s) --(post to ledger)--> AR open item
                                                 |
                                                 v
                                    Payment received --(match)--> Cash application --> AR closed/partially closed
```

### 1. Quote / Estimate (optional)
- **Documents:** Quote (not yet binding).
- **States:** Draft → Sent → Accepted / Rejected / Expired.
- **Decision points:** Price and discount approval if outside standard terms.

### 2. Sales Order
- **Documents:** Sales Order, referencing one or more quote lines or created directly.
- **States:** Draft → Confirmed → (Credit hold) → Released for fulfillment → Partially fulfilled → Fulfilled → Closed / Cancelled.
- **Roles:** Sales rep creates; credit control approves or holds based on customer credit limit and payment history — **credit management is a core O2C step, not an afterthought**: "continuous credit monitoring is not optional; it is a core requirement of sound order-to-cash management."
  Source: https://www.netsuite.com/portal/resource/articles/accounting/order-to-cash-otc-o2c.shtml — accessed 2026-09-10.
- **Decision point:** Credit hold — does the order proceed, wait for payment/guarantee, or get partially released?

### 3. Fulfillment (pick/pack/ship) → Delivery
- **Documents:** Pick list, packing slip, Delivery/Shipment document (this is the point of inventory decrement and, in perpetual-inventory accounting, COGS recognition).
- **States (per delivery, and per order as an aggregate of its deliveries):** Not started → Partially delivered → Fully delivered.
- **Nasty edge case — partial delivery:** A sales order line for 100 units may ship as 60 now, 40 later (backorder), or split across two warehouses. The order must track **ordered vs. delivered vs. invoiced quantity per line**, independently, because each can legitimately diverge. "Fulfilment must be executed accurately because any discrepancy between what was ordered and what was delivered creates a legitimate reason for the buyer to withhold or delay payment."
  Source: https://conexiom.com/blog/what-is-the-order-to-cash-process-and-what-are-its-8-stages — accessed 2026-09-10 (secondary, general O2C explainer, consistent with domain-standard practice).

### 4. Invoicing
- **Documents:** Invoice (may consolidate multiple deliveries, or split one delivery across invoices, or be milestone/schedule-based independent of delivery for services).
- **States:** Draft → Posted (immutable per CLAUDE.md's financial-correctness rule) → Paid / Partially paid / Overdue → (Disputed) → Written off.
- **Decision point:** Invoice timing policy — on order, on delivery, on a billing schedule, or on milestone — this must be configurable per customer/contract, not hard-coded to one model.

### 5. Payment and cash application
- **Documents:** Incoming payment (bank transfer, card, cheque), remittance advice.
- **States:** Unapplied → Partially applied → Fully applied (closes one or more invoices).
- **Decision point:** Which open invoice(s) does an incoming payment apply to when the amount doesn't exactly match one invoice, or the remittance advice is missing/wrong? This is a real operational bottleneck ("cash application") in every O2C description found this pass, not a trivial matching step.
  Source: https://upflow.io/blog/ar-collections/order-to-cash-process and https://www.emagia.com/blog/what-is-the-order-to-cash-process/ — both accessed 2026-09-10.

---

## Nasty edge cases (first-class design inputs)

### Partial deliveries and partial invoicing
As above: ordered, delivered, and invoiced quantities per line must be tracked independently and reconciled, with the order line remaining "open" until fully delivered and fully invoiced (which can happen in either order, or interleaved).

### Credit notes
- Triggered by returns, pricing disputes, delivery shortfalls, or post-invoice corrections. "Fulfilment errors trigger disputes, generate credit notes, and push the entire cash collection process back by weeks or months" — i.e., credit notes are not a rare exception path, they are a normal consequence of the fulfillment process and must be fast to issue and reconcile.
  Source: https://www.emagia.com/resources/glossary/order-to-cash-process-flow/ — accessed 2026-09-10.
- **Design implication:** a credit note is its own document type referencing the original invoice (never a negative invoice edited in place, consistent with CLAUDE.md's "posted ledger entries are immutable; corrections are reversals"), and it must be able to partially credit a single invoice line (e.g., 3 of 10 units returned) as well as fully void an invoice.
- Peppol/EN 16931's own model (see `regulation/first-country-package.md`) treats a credit note as a first-class document type sharing the invoice's semantic model — corroborating that this is the standard pattern, not something specific to our design.

### Rounding
- Line-level net amount = quantity × unit price (± line charges/allowances), then tax is computed and rounded per line or per document depending on jurisdiction rule, then a document-level total is checked for consistency. Peppol BIS Billing 3.0's own business rules exist specifically to bound rounding differences: "the invoice total VAT amount is the sum of all VAT category VAT amounts," with an explicit `PayableRoundingAmount` element to absorb the residual cent(s) of rounding at the document level.
  Source: https://validatefin.com/en/blog/peppol-bis-billing-rules and https://docs.peppol.eu/poacc/billing/3.0/bis/ — accessed 2026-09-10.
- **Design implication:** our Invoice aggregate needs an explicit, auditable rounding-residual field/line rather than silently absorbing sub-unit differences into the last line — this also directly informs the e-invoicing extension point in `features/localization-packages.md`, since a jurisdiction's e-invoicing profile may mandate a specific rounding convention.

### Multi-currency
- A sales order, delivery and invoice may all be denominated in a currency other than the tenant's functional/reporting currency. The FX rate used at order time, delivery time and invoice time can legitimately differ; the invoice's posted ledger amount is fixed at the rate on the invoice date and does not move afterward (see `record-to-report.md` for the revaluation of the resulting open AR balance at period end).
- **Design implication:** Money must be (amount, currency) from day one (already a CLAUDE.md hard requirement), and the O2C document chain must record which FX rate (and its source/date) applied to each document, not just the tenant's "current" rate.

### Reversals
- Consistent with CLAUDE.md: a posted invoice is never edited; a mistake is corrected by a reversing entry (a credit note, or a full-invoice reversal + reissue), preserving the audit trail. This applies equally to a misapplied payment (cash application reversal) as to an invoice.

---

## Roles summary
| Role | Responsibility |
|---|---|
| Sales rep / order desk | Creates quotes and orders |
| Credit control | Approves/holds orders against credit limit; manages collections |
| Warehouse / fulfillment | Picks, packs, ships; records actual delivered quantity |
| Billing / AR | Issues invoices and credit notes per the tenant's billing policy |
| Cash application / AR | Matches incoming payments to open invoices; manages disputes |

## Sources
1. https://www.netsuite.com/portal/resource/articles/accounting/order-to-cash-otc-o2c.shtml — accessed 2026-09-10
2. https://conexiom.com/blog/what-is-the-order-to-cash-process-and-what-are-its-8-stages — accessed 2026-09-10
3. https://upflow.io/blog/ar-collections/order-to-cash-process — accessed 2026-09-10
4. https://www.emagia.com/blog/what-is-the-order-to-cash-process/ — accessed 2026-09-10
5. https://www.emagia.com/resources/glossary/order-to-cash-process-flow/ — accessed 2026-09-10
6. https://docs.peppol.eu/poacc/billing/3.0/bis/ — accessed 2026-09-10
7. https://validatefin.com/en/blog/peppol-bis-billing-rules — accessed 2026-09-10

## What would change this
UNVERIFIED this pass: exact tolerance conventions competitors use for "close enough" cash-application auto-matching (e.g. write off differences under $X automatically) — worth a targeted follow-up once we interview or observe a real AR clerk's workflow, since published sources describe the problem but not the numeric tolerances practitioners actually use.
