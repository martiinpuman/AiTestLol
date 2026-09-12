# Inventory & Warehouse — Brief Sketch

Status: draft v1 (brief per brief's prioritization, though flagged in `01-feature-priority.md` as needing to be built early). Author: researcher. Date: 2026-09-10.

## Conclusion
Inventory is not one process but a **ledger parallel to the accounting ledger**: every stock movement (receipt, shipment, transfer, adjustment, count) both changes an on-hand quantity *and* has a cost effect that must post to the accounting core. The valuation method chosen (FIFO, weighted/moving average, or standard cost) determines how that cost effect is computed and must be a per-item (or per-item-category) setting, not a single tenant-wide constant, because trading businesses often need FIFO for resale goods and standard cost for manufactured sub-assemblies simultaneously.

## Core mechanics
- **Stock movement types:** Receipt (from PO or production), Shipment (to sales order), Transfer (warehouse-to-warehouse), Adjustment (write-off, found stock), Count (cycle or full physical).
- **Valuation methods:**
  - **FIFO** — oldest cost layer consumed first; gives the most balance-sheet-accurate valuation as prices change.
  - **Weighted/moving average** — simpler, blends cost across all units of an item; common where units are indistinguishable.
  - **Standard cost** — a predetermined fixed cost per item regardless of actual purchase price, with variance posted separately when actual cost differs.
  Source: https://www.finaleinventory.com/accounting-and-inventory-software/inventory-costing-methods — accessed 2026-09-10.
- **Cycle counting:** rather than one disruptive annual count, an ABC-classified rolling count schedule (count high-value/high-movement items more often) is the standard practice for keeping perpetual inventory accurate without halting operations.
  Source: https://www.finaleinventory.com/guides/inventory-valuation-methods/ — accessed 2026-09-10.

## Nasty edge cases (brief)
- **Negative stock / oversell:** what happens when a sales order tries to ship more than is on hand — block, allow with a warning, or allow and let a subsequent receipt "catch up" the valuation retroactively (this last option is the hardest to get right with FIFO).
- **Multi-warehouse allocation:** the same SKU can exist in several warehouses with different quantities; an order fulfillable only by combining partial stock from two warehouses is a normal case for a wholesale business, not an edge case.
- **Landed cost timing** (see `procure-to-pay.md`): freight/duty arriving after goods are already received and partially sold requires a correcting valuation entry.
- **Serial/lot tracking** for traceability (recalls, expiry) — QuickBooks/Xero's absence of this (per `00-landscape.md`) is a named competitive gap for our target industries.

## Roles
| Role | Responsibility |
|---|---|
| Warehouse operator | Executes receipts, shipments, transfers, counts |
| Inventory/planning | Sets reorder points, resolves negative-stock and allocation conflicts |
| Accounting | Reviews valuation variances, approves write-offs/adjustments |

## Sources
1. https://www.finaleinventory.com/accounting-and-inventory-software/inventory-costing-methods — accessed 2026-09-10
2. https://www.finaleinventory.com/guides/inventory-valuation-methods/ — accessed 2026-09-10
3. https://www.stackscored.com/pricing/inventory-warehouse-management/cin7/ — accessed 2026-09-10 (feature-scope reference, see `00-landscape.md`)

## What would change this
This sketch is deliberately brief; a full treatment should be commissioned alongside the accounting-core posting design, since inventory's cost-of-goods-sold posting is where inventory and record-to-report meet (see `record-to-report.md`).
