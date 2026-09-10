# Procure-to-Pay (P2P) — Brief Sketch

Status: draft v1 (brief, per brief's prioritization — deeper treatment deferred until after O2C/inventory/accounting core). Author: researcher. Date: 2026-09-10.

## Conclusion
P2P mirrors O2C's document-chain shape (Requisition → Purchase Order → Goods Receipt → Vendor Invoice → Payment) with one structurally important difference: the **three-way match** between PO, goods receipt and vendor invoice is the process's central control, not an optional step — it exists specifically to catch the case where any two of "what we ordered," "what we received," and "what we're being billed for" disagree.

## Flow
1. **Requisition/need identification** → **Purchase Order** issued to a vendor (states: Draft → Approved → Sent → Partially received → Fully received → Closed).
2. **Goods receipt** recorded against the PO (may be partial — mirrors O2C's partial-delivery problem in reverse: ordered vs. received vs. invoiced quantity per PO line, tracked independently).
3. **Vendor invoice** arrives; **three-way match** compares PO, goods receipt and invoice on quantity, unit price, total and line description; matches within tolerance are cleared for payment, mismatches are flagged as exceptions for review.
   Source: https://sourceday.com/blog/three-way-match/ and https://ramp.com/blog/accounts-payable/3-way-match — both accessed 2026-09-10.
4. **Payment** scheduled and executed per the vendor's terms; posts to AP and, via a bank payment file (Country Package territory), to the bank.

## Nasty edge cases (brief)
- **Partial receipt against a PO**, exactly as O2C's partial delivery, but from the buy side.
- **Price or quantity variance within tolerance** should auto-clear; outside tolerance must route to an exception queue rather than blocking or silently accepting.
- **Vendor credit notes** for damaged/returned goods, mirroring O2C's credit note handling.
- **Landed cost / additional charges** (freight, duty) arriving on a separate vendor invoice after the goods receipt has already been valued — requires the inventory valuation to be adjustable after the fact without violating posted-ledger immutability (i.e., via a correcting entry, not an edit).

## Roles
| Role | Responsibility |
|---|---|
| Requester / buyer | Raises requisition/PO |
| Approver | Authorizes PO within spending policy |
| Warehouse/receiving | Records goods receipt, checks against PO |
| AP clerk | Performs three-way match, resolves exceptions, schedules payment |

## Sources
1. https://kissflow.com/procurement/procure-to-pay-process-guide/ — accessed 2026-09-10
2. https://sourceday.com/blog/three-way-match/ — accessed 2026-09-10
3. https://ramp.com/blog/accounts-payable/3-way-match — accessed 2026-09-10

## What would change this
This sketch is deliberately brief per the brief's prioritization; a full treatment (comparable to `order-to-cash.md`) should be commissioned once P2P is scheduled for build (see `01-feature-priority.md` sequencing).
