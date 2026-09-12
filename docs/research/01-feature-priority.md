# Feature Priority — What to Build First

Status: draft v1. Author: researcher. Date: 2026-09-10.

## Conclusion

The starting hypothesis in `researcher.md` — **platform/master data → order-to-cash → accounting core → procure-to-pay → inventory → reporting → manufacturing/projects** — mostly survives scoring, with one material correction:

**Inventory (stock, valuation, multi-warehouse) must move up to sit alongside order-to-cash and the accounting core, not after procure-to-pay.** The landscape pass shows this directly: the products our target buyer actually lives with (Xero, QuickBooks) fail them specifically on inventory, and the products they graduate to (Cin7 Core, Katana) exist *only* to fill that gap — see `docs/research/00-landscape.md`. You cannot do a believable order-to-cash flow for a trading/wholesale business (our #1 target industry) without inventory quantity and valuation underneath it: "ship goods" and "recognize cost of goods sold" are the same event. Sequencing inventory after procure-to-pay would mean building a sales order flow with no real stock to sell against.

Recommended build order: **(1) Platform & master data → (2) Accounting core (ledger, CoA, tax engine skeleton) → (3) Inventory (stock, valuation, warehouses) → (4) Order-to-cash → (5) Procure-to-pay → (6) Statutory reporting / period close → (7) Plan-to-produce (light manufacturing) → (8) Projects/time.**

The accounting core is promoted ahead of inventory/O2C (relative to the original hypothesis) because both O2C and inventory need to post to a ledger and apply tax from day one to be more than a toy — and because the accounting core is also where the Country Package extension points (chart of accounts, tax rules) first get exercised, which CLAUDE.md identifies as an architectural risk to retire early.

---

## Scoring method

Each candidate capability is scored 1 (low) to 5 (high) on:
- **Necessity** — can a typical target customer run their business at all without it?
- **Prevalence** — how many competitors ship it as standard? (from `00-landscape.md`)
- **Dependency** — how many other features need this one to exist first? (higher score = more other things depend on it, i.e. build it early)
- **Differentiation** — would doing it better than incumbents win customers?
- **Effort (inverted)** — low implementation effort scores high.

**Total** is a simple sum (max 25); it is a discussion aid, not a formula to defer to blindly — see the "what would change my mind" section.

| Capability | Necessity | Prevalence | Dependency | Differentiation | Effort (inv.) | Total | Notes |
|---|---|---|---|---|---|---|---|
| Tenant/platform core (auth, tenant resolution, catalog DB) | 5 | 5 | 5 | 1 | 2 | 18 | Not a "feature" a customer sees, but nothing else can be built or tested without it. Effort is genuinely high (CLAUDE.md's own risk #1) but dependency dominates. |
| Master data (party/customer/vendor, item, price list, unit of measure) | 5 | 5 | 5 | 1 | 3 | 19 | Every other module reads these. Value objects (Money, OrganizationNumber) belong here — this is also the first place a Country Package's identifier-validator extension point gets exercised. |
| Accounting core: chart of accounts + posting ledger + immutable journal entries | 5 | 5 | 5 | 2 | 2 | 19 | Financial correctness (CLAUDE.md hard requirement: immutable postings, reversals only) starts here. Every sales/purchase/inventory event ultimately posts here. |
| Tax engine skeleton (rate lookup, effective-dated rates, tax-on-line calculation) | 5 | 5 | 4 | 3 | 2 | 19 | Must exist as a country-agnostic *engine* before the first Country Package can supply data into it — this is extension point #2 from `features/localization-packages.md`, and per NetSuite's SuiteTax history, retrofitting a rules engine under a hard-coded tax model later is expensive (NetSuite itself had to do this migration). |
| Inventory: stock quantity, multi-warehouse, valuation method (FIFO/weighted average) | 5 | 5 | 4 | 3 | 2 | 19 | Necessity is 5 specifically *for our two target industries* (trading/wholesale, light manufacturing) — a generic services SMB could live without it, but ours can't. Xero/QuickBooks' weakness here (landscape doc) is the gap we're best positioned to close. |
| Order-to-cash: quote→order→delivery→invoice→payment | 5 | 5 | 3 | 3 | 2 | 18 | Depends on master data, tax engine, and (for a trading business) inventory. Universal across every competitor studied. |
| Procure-to-pay: PO→receipt→3-way match→vendor invoice→payment | 4 | 5 | 3 | 2 | 3 | 17 | Slightly lower necessity than O2C — a very small trading company can survive on manual purchasing longer than on manual sales — but still table-stakes and every incumbent ships it. |
| Statutory reporting (VAT/GST return, basic financial statements) | 4 | 5 | 2 | 2 | 3 | 16 | High necessity long-term (it's a legal requirement, not optional), but a new tenant can go some weeks without filing before it's existential — hence lower than the transactional modules for *first build order*, not for eventual priority. This is also where the first Country Package's statutory-report extension point gets proven end-to-end. |
| Record-to-report / period close (accruals, FX revaluation, close checklist, reversals) | 4 | 4 | 2 | 3 | 2 | 15 | Every incumbent supports this but with wildly varying quality (this is Sage Intacct's and NetSuite's core differentiation pitch in the mid-market) — real room to differentiate on close automation. |
| Bank payment/statement integration (file formats, reconciliation) | 3 | 4 | 1 | 2 | 2 | 12 | Necessary eventually, low dependency (nothing needs it to exist first), and formats are entirely Country-Package territory — good candidate to defer without blocking anything. |
| E-invoicing (outbound Peppol-style transmission) | 3 | 3 | 1 | 4 | 2 | 13 | Not yet universal among competitors studied (only some editions/regions), but regulatory momentum is strong (NZ/Australia/Singapore/EU all moving toward mandatory e-invoicing in 2025–2027 per `regulation/first-country-package.md`) and doing it well is a genuine differentiator if we're early. High differentiation, but correctly sequenced after the O2C invoice itself exists. |
| Plan-to-produce: BOM, routing, work orders, MRP | 3 | 3 | 2 | 4 | 1 | 13 | Necessity is real but scoped to light-manufacturing customers only (half our stated target, not all of it). Effort is high — this is Katana's and BC Premium's *entire product*. High differentiation potential specifically because Xero/QuickBooks/Zoho have none of this (landscape doc), so shipping even a modest version beats the "no ERP + bolt-on tool" status quo those customers currently accept. |
| Projects/time tracking | 2 | 3 | 1 | 2 | 3 | 11 | Lower necessity for our two named target industries (trading/wholesale, light manufacturing) than for services businesses; not dropped, just correctly last. |
| CRM/lead management | 2 | 4 | 1 | 1 | 3 | 11 | Prevalent as a bundled module (Odoo, Zoho) but low necessity for our specific target — a trading/wholesale SMB already has a customer list from master data; full CRM (pipeline, campaigns) is a "nice to have," not core ERP. |
| Multi-currency (as a cross-cutting capability, not a module) | 4 | 4 | 3 | 2 | 2 | 15 | Called out separately because it's not one module — it touches O2C, P2P, accounting core and reporting simultaneously (see `processes/record-to-report.md` for the FX revaluation mechanics). Necessity is high specifically because "no jurisdiction is privileged" implies cross-border trade is a normal case for us, not an edge case. Recommend building the Money value object multi-currency-aware from day one rather than retrofitting. |

---

## What this means concretely for sequencing

1. **Platform/master data and the accounting core are inseparable from each other in practice** — both scored 19 and both are pure dependencies for everything else. Build them together, not sequentially.
2. **Inventory ties with order-to-cash and the tax engine at 19/18** — the correction to the original hypothesis. Recommend building a minimal inventory slice (quantity on hand, one valuation method, single default warehouse) *before or alongside* the first order-to-cash slice, then widening both together (multi-warehouse, multiple valuation methods) rather than finishing O2C first and bolting inventory on after.
3. **Procure-to-pay and statutory reporting can trail by one iteration** without blocking anything else — nothing downstream needs them to exist first.
4. **Plan-to-produce is correctly last among the "must eventually build" items** — it's real necessity but scoped to only one of our two named industries, and it is the single highest-effort item on the table. Building a thin O2C+inventory+accounting core first, proven against trading/wholesale, gives us a stable base to extend into light manufacturing rather than trying to build both industries' needs simultaneously.
5. **E-invoicing scored higher on differentiation than its total suggests it should be built early** — flagging explicitly: this is a case where the table's simple sum understates urgency. If the architect and PM want one thing pulled forward opportunistically once O2C's Invoice aggregate exists, e-invoicing (as the first exercise of the Country Package e-invoicing-profile extension point) is a strong candidate, timed to land together with the first reference Country Package (see `regulation/first-country-package.md`).

---

## What evidence would change my mind

- **If early design partners are services businesses, not trading/light-manufacturing** — projects/time tracking and CRM would need to jump ahead of inventory and plan-to-produce. Nothing found this pass suggests that, but CLAUDE.md explicitly invites the researcher to challenge the target-industry choice, and this scoring assumes it holds.
- **If the architect determines database-per-tenant provisioning (CLAUDE.md risk #1) is materially harder than expected**, the platform/master-data item's effort score (currently 2, i.e. high effort) should be revised down further, which would argue for spending an entire extra iteration there before touching any business module — worth an explicit go/no-go checkpoint rather than assuming it's solved once B5's walking skeleton exists.
- **If our first two reference customers turn out to need multi-currency from day one (e.g., an importer/exporter)**, the multi-currency row's necessity score should rise from 4 to 5 and it should be treated as a platform-core dependency rather than a cross-cutting nice-to-have layered on later — this would be consistent with "no jurisdiction is privileged" but I have not validated it against an actual prospect yet.
- **If real users tell us bank reconciliation is a daily pain point rather than a periodic one** (UNVERIFIED this pass — no direct user-complaint evidence gathered on bank feeds specifically, only pricing/positioning), its dependency/necessity scores should be revisited; this pass relied on general ERP-process knowledge, not direct competitor-review mining, for that row specifically.
- **A successful, well-scoped light-manufacturing design partner appearing early** would justify pulling plan-to-produce forward, given how clearly Katana's existence (landscape doc) proves unmet demand in exactly our stated target segment.

## Sources
Scoring draws on `docs/research/00-landscape.md` (module coverage per competitor) and `docs/research/features/localization-packages.md` (extension-point dependencies) already sourced above, plus the general order-to-cash/procure-to-pay/record-to-report process sources cited in the `processes/` files. No new sources were fetched specifically for this scoring pass; it is a synthesis document.
