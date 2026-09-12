# Plan-to-Produce (Light Manufacturing) — Brief Sketch

Status: draft v1 (brief per brief's prioritization). Author: researcher. Date: 2026-09-10.

## Conclusion
For our "light manufacturing" target (assembly/kitting-level, not heavy discrete or process manufacturing), the minimum viable chain is **Bill of Materials (BOM) → Work Order → Material consumption → Finished-goods receipt**, with **routing** (the sequence of operations) as a secondary layer that can be added once the basic BOM-explosion/work-order flow works. This matches how Katana (our closest vertical-specialist reference, see `00-landscape.md`) scopes its own product, and keeps the first build effort proportionate to "light" manufacturing rather than full MRP/APS sophistication.

## Core mechanics
- **Bill of Materials (BOM):** defines the component/sub-assembly/raw-material structure of a manufactured item — the base building block of any production module.
  Source: https://www.managementstudyguide.com/production-module-bom-and-routing.htm — accessed 2026-09-10.
- **Routing:** defines the sequence of operations (and often work centers/labor) used to convert components into the finished item; a prerequisite for scheduling but not for a minimal BOM-only work order.
- **Work Order:** the controlled production record — product, quantity, materials, routing operations, schedule, labor and cost — that connects planning to the shop floor and, on completion, drives finished-goods receipt into inventory and consumption of the component quantities out of inventory.
  Source: https://www.simplemanufacturing.com/manufacturing-work-orders-erp/ — accessed 2026-09-10.
- **Material Requirements Planning (MRP):** the planning layer that nets demand (sales orders, forecasts) against on-hand and on-order inventory and proposes purchase/production orders to cover the shortfall — this is a later-stage capability layered on top of BOM+work orders, not a prerequisite for the first slice.
  Source: https://www.netsuite.com/portal/resource/articles/inventory-management/material-requirements-planning-mrp.shtml — accessed 2026-09-10.

## Nasty edge cases (brief)
- **Partial work order completion:** a work order for 100 units may yield 95 good units and 5 scrapped — component consumption and finished-goods receipt must both handle a quantity that doesn't match the planned quantity.
- **BOM versioning:** a product's BOM changes over time (engineering change); in-flight work orders must keep using the BOM version they started with, while new work orders pick up the latest.
- **Co-products/by-products** (less common in "light" manufacturing but present in some trading-adjacent light assembly) — one work order producing more than one output item.

## Roles
| Role | Responsibility |
|---|---|
| Production planner | Creates work orders from demand, sequences them |
| Shop floor operator | Executes operations, records actual consumption/output |
| Inventory | Receives finished goods, releases consumed components |

## Sources
1. https://www.managementstudyguide.com/production-module-bom-and-routing.htm — accessed 2026-09-10
2. https://www.simplemanufacturing.com/manufacturing-work-orders-erp/ — accessed 2026-09-10
3. https://www.netsuite.com/portal/resource/articles/inventory-management/material-requirements-planning-mrp.shtml — accessed 2026-09-10
4. https://www.brahmin-solutions.com/blog/katana-pricing — accessed 2026-09-10 (scope reference, see `00-landscape.md`)

## What would change this
This is the briefest sketch of the five process docs, consistent with `01-feature-priority.md`'s finding that plan-to-produce is correctly sequenced last. A full treatment should be commissioned when this capability is actually scheduled for build.
