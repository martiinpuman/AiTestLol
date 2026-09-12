# Competitor Landscape — SMB ERP for Trading/Wholesale & Light Manufacturing

Status: draft v1. Author: researcher. Date: 2026-09-10.

## Conclusion

For our target buyer (10–250 employees, trading/wholesale or light manufacturing, no single home jurisdiction), the effective competitive set is not "one Nordic incumbent" but a **layered market**:

1. **Accounting-first SaaS that outgrows the target segment fast** — Xero, QuickBooks Online, Zoho Books. Cheap, easy, but structurally short of real inventory/manufacturing (no BOM, no multi-warehouse costing, no routing) once a wholesaler or light manufacturer grows past a few people. Companies here typically bolt on a specialist inventory tool (Cin7 Core, Katana, DEAR) rather than switch ERP.
2. **Mid-market cloud ERP built to be grown into** — NetSuite, Dynamics 365 Business Central, Acumatica, Sage Intacct. This is where a trading/light-manufacturing company that has outgrown QuickBooks/Xero actually lands. All four are usable from roughly 10–20 employees but are priced and sold for 20–250+.
3. **On-prem-heritage, partner-delivered mid-market ERP** — SAP Business One. Still the default recommendation from SAP's reseller channel for the same size band, sold almost exclusively through VARs.
4. **Open-core / self-hostable generalist** — Odoo. The only competitor that is simultaneously a plausible reference for cheap SMB deployments *and* for how to structure country-specific modules (see `features/localization-packages.md`).
5. **Vertical specialists for our two target industries** — Katana (light manufacturing MRP), Cin7 Core/DEAR (wholesale/distribution inventory), which repeatedly show up as the tool an SMB adds *around* an accounting core rather than replaces it with.
6. **Strong regional accounting incumbents** exist in every geography (Fortnox/Visma in the Nordics, Sage in the UK, others elsewhere) and dominate the very small end of the market in their home country through accountant-firm integration, not through ERP breadth. They matter as a reminder that "accounting + payroll + bank feed, deeply local" is itself a competitive moat — one our Country Package model must be able to approximate.

**Implication for us:** our real competitive gap to close first is not against SAP/NetSuite feature-for-feature — it's against the "Xero/QuickBooks + bolt-on inventory tool" combination, because that is what our target buyer is actually living with today. A credible order-to-cash + inventory + basic manufacturing core, with country-agnostic accounting underneath, directly displaces that combination.

---

## Method and caveats

Pricing for cloud ERP changes often and is heavily negotiated/discounted in practice; none of it is load-bearing for our architecture, only for positioning. Where a figure comes from an aggregator/analyst site rather than the vendor's own page, it is marked **(secondary)** and should be re-verified before being used in any customer-facing material. All URLs were accessed 2026-09-10.

---

## International SaaS / cloud ERP references

### Odoo
- **Positioning:** Modular, open-core suite (Community = free/open-source, Enterprise = paid) covering CRM, sales, inventory, manufacturing (MRP), accounting, HR, website/e-commerce, and more, sold both self-hosted and as Odoo.sh/Odoo Online SaaS.
- **Target size:** From solo/micro business (a free "One App Free" plan exists) up through a few hundred employees; heavy presence in price-sensitive and emerging markets because Community is free to self-host.
- **Module coverage:** Broadest in the sample — 80+ first-party apps. Manufacturing (MRP) app has BOM, work orders, and routing comparable to a light-manufacturing MRP tool.
- **Pricing/packaging:** Enterprise cloud is per-user/month with two tiers — Standard ≈ $24.90–$38.90/user/mo and Custom (adds Odoo Studio, multi-company, external API) ≈ $37.40–$76.20/user/mo depending on commitment; pricing is "pay for the apps you use" rather than fixed feature bundles. Community edition has no license fee. Typical SMB implementation cost cited at $5,000–$200,000+ depending on scope. **(secondary, aggregator figures)**
  Source: https://www.erpresearch.com/pricing/odoo (accessed 2026-09-10); official pricing page https://www.odoo.com/pricing (not independently re-fetched this pass — verify before quoting externally).

### NetSuite (Oracle)
- **Positioning:** Cloud ERP/financials suite, OneWorld multi-subsidiary/multi-currency edition is its distinguishing strength; sold with a fixed-scope "SuiteSuccess" implementation methodology.
- **Target size:** Aggregator sources place NetSuite's sweet spot above ~$25M revenue and describe it as dominant in the $50M–$500M band; a "small" ~10-user deployment is still reported at $12,000–$60,000/year. This is heavier than our 10–250 employee target's low end can typically justify. **(secondary)**
  Source: https://www.erpresearch.com/pricing/oracle-netsuite (accessed 2026-09-10); https://www.houseblend.io/articles/netsuite-company-size-mid-market-revenue (accessed 2026-09-10).
- **Module coverage:** Financials, CRM, inventory/order management, WMS, manufacturing, procurement — full mid-market ERP breadth, but manufacturing/light-industry depth is thinner than Dynamics 365 BC Premium or SAP B1's manufacturing add-ons.
- **Pricing/packaging:** Base platform $999–$5,000/month + $129–$199/full user/month, before modules and implementation; SuiteSuccess fixed-scope starters from ~$25,000. **(secondary)**

### Microsoft Dynamics 365 Business Central (BC)
- **Positioning:** Microsoft's SMB/lower-mid-market ERP, deep Microsoft 365/Power Platform integration, extension-based customization model (see localization doc).
- **Target size:** Explicitly the SMB tier of the Dynamics family (NAV's cloud successor); comfortably fits 10–250 employees.
- **Module coverage:** Essentials = finance, sales, purchasing, inventory, warehousing, project management, basic supply chain. Premium adds **manufacturing and service management** — directly relevant to our light-manufacturing target segment.
  Source: Microsoft's own list pricing summarized by https://cargas.com/software/microsoft/dynamics-365-business-central/pricing/ (accessed 2026-09-10).
- **Pricing/packaging:** Per-user/month, list pricing effective Nov 2025: Essentials $80/user/mo, Premium $110/user/mo, Team Member (light/read+basic-entry access) $8/user/mo, Device license $45/device/mo, billed annually. Implementation typically $40,000–$100,000+. **(secondary for implementation cost; list prices reported consistently across sources)**

### SAP Business One (B1)
- **Positioning:** SAP's dedicated SMB ERP (distinct product line from S/4HANA), sold **exclusively through an indirect VAR/partner channel** — SAP does not sell it direct.
- **Target size:** SAP's own stated target is revenue below ~€50M and 10–250 employees — almost an exact match to our stated target band. 83,000+ customers in 170+ countries.
  Source: https://carlsquare.com/insights/sap-market-update-business-one/ (accessed 2026-09-10).
- **Module coverage:** Financials, sales, purchasing, inventory, production (BOM/MRP-lite), CRM, service, plus a large ecosystem of country **localization add-ons** (see localization doc) and industry-specific partner add-ons.
- **Pricing/packaging:** List price ≈ $154/user/month (cloud subscription) or ~$3,850 one-time perpetual per Professional user before partner discount; street pricing $95–$250/user/month depending on region and partner. Year-one total cost commonly cited at £16,000–£80,000+ (partner services are a large share of TCO because implementation is 100% partner-delivered). **(secondary)**

### Xero
- **Positioning:** Accounting-first SaaS, extremely strong in bookkeeping/accountant workflows and bank feeds; explicitly **not** an ERP for inventory-heavy or manufacturing businesses.
- **Target size:** Micro to small business; unlimited users on every plan is a deliberate go-to-market choice (price is per-company, not per-seat).
- **Module coverage:** No manufacturing capability at all. Inventory is basic item tracking; "Xero Inventory Plus" (an add-on) brings FIFO costing and basic reorder points but still has no BOM/assemblies, no serial/lot tracking, and only "informational" replenishment. Multi-location support is limited.
  Source: https://www.inflowinventory.com/blog/xero-inventory-management/ (accessed 2026-09-10) — vendor blog of a competing inventory tool, so treat the specific claims as directionally correct but potentially self-serving; corroborated independently by general Xero-limitation coverage in the same search set.
- **Pricing/packaging:** US list (effective March 2026): Early $25/mo, Growing $55/mo, Established $90/mo, unlimited users on all tiers. **(secondary)**
- **Practical implication:** Xero customers doing real wholesale/light manufacturing pair it with a third-party inventory/MRP tool ($200–$1,000+/month) — this is exactly the "seam" our unified core should remove for this segment.

### Zoho (Books / Inventory / One)
- **Positioning:** Low-cost, broad app suite (40+ apps under "Zoho One") anchored by Zoho Books (accounting) and Zoho Inventory; strongest in APAC/India and price-sensitive segments; not a manufacturing ERP.
- **Target size:** Freelancer through SMB; Zoho explicitly lists retail, wholesale distribution, e-commerce and consumer goods as Zoho Inventory's target verticals.
  Source: https://www.zoho.com/us/inventory/pricing/ (accessed 2026-09-10).
- **Module coverage:** Books (accounting/AR/AP), Inventory (multi-warehouse stock, sales/purchase order flow, basic composite items — not full BOM/routing manufacturing), integrates with the wider Zoho One suite (CRM, HR, etc.) rather than being a single monolith.
- **Pricing/packaging:** Zoho Inventory Standard from $39/organization/month (up to 500 orders/month) — priced per transaction volume tier, not per user, which is unusual and notable as a packaging pattern.

---

## Vertical / niche specialists relevant to our target industries

### Katana Cloud Inventory (MRP)
- **Positioning:** Purpose-built visual MRP for small manufacturers; explicitly light-manufacturing, not a full ERP (usually paired with Xero/QuickBooks for accounting).
- **Target size:** Small manufacturing teams, ~5–50 users cited as the sweet spot for its Pro plan.
- **Pricing:** Starts at $299/month; add-ons (traceability, extra manufacturing/warehouse modules) push real cost to $747–$1,095/month. Free tier exists for very small (3 locations/30 SKUs).
  Source: https://www.brahmin-solutions.com/blog/katana-pricing (accessed 2026-09-10). **(secondary)**

### Cin7 Core (formerly DEAR Systems)
- **Positioning:** Inventory/order management for small-to-mid wholesale distributors, light manufacturers and multichannel sellers; originated in New Zealand, acquired/rebranded by Cin7 in 2021.
- **Module coverage:** Purchasing, sales order management, multi-warehouse inventory, basic manufacturing (BOM, work orders), barcode/WMS features for pick-pack-count. Unlimited locations on every plan.
- **Pricing:** Standard $349/mo (5 users, 6K orders), Pro $599/mo (10 users, 24K orders + MRP), Advanced $999/mo (15 users, 120K orders + WMS).
  Source: https://www.stackscored.com/pricing/inventory-warehouse-management/cin7/ (accessed 2026-09-10). **(secondary)**
- **Relevance:** This is the closest single-vendor proxy for "what a trading/wholesale + light-manufacturing SMB actually needs" at the transactional layer — a strong feature-scope reference even though it is not itself a full ERP (no general ledger depth, no statutory reporting).

### Acumatica
- **Positioning:** Cloud ERP distinguished by **no per-user pricing** — licensed by application module + transaction-volume tier, unlimited named users included. Strong in construction, manufacturing, distribution, retail.
- **Target size:** Small-to-mid market; explicitly marketed at companies with many occasional/light users (warehouse staff, field crews) where per-seat pricing would be punitive.
- **Pricing:** From $6,396/year (Essentials, ≤10 users, 1,000 transactions/month) up to $25,000–$75,000/year for 50–200-user mid-market deployments.
  Source: https://octurasolutions.com/resources/acumatica-pricing-2026-how-consumption-licensing-really-works (accessed 2026-09-10). **(secondary)**
- **Relevance:** Its consumption-based packaging is worth studying independent of its ERP feature set — "unlimited users, pay for modules + volume" may suit our database-per-tenant, no-per-seat-friction goals better than the pure per-user model most competitors use. Flag for the PM/pricing decision, not just research trivia.

### QuickBooks Online (Advanced)
- **Positioning:** The most common first accounting system for a US micro/small business; "Advanced" tier adds some batch and reporting features but is not built for inventory-heavy operations.
- **Module coverage gaps relevant to us:** No serial/lot tracking, no barcode support natively, no multi-warehouse control, no BOM/assemblies, no real-time inventory automation (reorder points, auto-PO). Designed for ≤25 users.
  Source: https://acctivate.com/quickbooks-online-inventory-limitations/ and https://www.fishbowlinventory.com/blog/quickbooks-inventory-management (both accessed 2026-09-10, both vendor blogs of competing inventory add-ons — treat as directionally reliable, commercially motivated).
- **Pricing:** Advanced ≈ $340/month (secondary, single data point).

---

## Regional accounting incumbents (illustrative, not exhaustive — no jurisdiction is privileged for us)

These matter as a **pattern**, not as a market to chase: in most countries, the very-small end of the market (sub-10 employee, bookkeeper-run) is dominated by a local accounting-first product deeply wired into that country's accountant workflows, bank feeds, and statutory filings — not by any of the international players above.

- **Fortnox / Visma (Nordics):** Fortnox alone is reported to serve 500,000+ companies in Sweden and is built around shared real-time access between an SME and its external accounting firm — a workflow pattern (accountant co-access to the live ledger) worth noting for our product even though Sweden itself is not privileged. Visma eEkonomi is the comparable Visma product line for the same segment.
  Source: https://www.chift.eu/blog/the-accounting-software-landscape-in-sweden-a-2026-guide-for-saas-vendors and https://en.noresca.se/when-fortnox-and-visma-are-not-enough-why-growth-companies-choose-netsuite-for-international-expansion/ (both accessed 2026-09-10). Note also (same source) that Fortnox/Visma customers who outgrow them for **international expansion** commonly move to NetSuite — evidence that "accounting-first, single-country" and "ERP, multi-country" are genuinely different product categories, reinforcing why a country-agnostic core with installable packages is a defensible position rather than a purely academic one.
- **Sage** occupies an equivalent role in the UK/parts of Europe with Sage 50/200/Intacct spanning micro to mid-market. UNVERIFIED at this pass: current Sage 200 pricing (search did not surface current figures; Sage Intacct figures were found: $400–800/user/month reported, $25,000–75,000/year typical — but Intacct targets mid-market professional services/nonprofits more than trading/wholesale, so treat as context only).

---

## Sources (landscape)
1. https://www.erpresearch.com/pricing/odoo — accessed 2026-09-10
2. https://www.erpresearch.com/pricing/oracle-netsuite — accessed 2026-09-10
3. https://www.houseblend.io/articles/netsuite-company-size-mid-market-revenue — accessed 2026-09-10
4. https://cargas.com/software/microsoft/dynamics-365-business-central/pricing/ — accessed 2026-09-10
5. https://carlsquare.com/insights/sap-market-update-business-one/ — accessed 2026-09-10
6. https://www.zoho.com/us/inventory/pricing/ — accessed 2026-09-10
7. https://www.inflowinventory.com/blog/xero-inventory-management/ — accessed 2026-09-10
8. https://www.brahmin-solutions.com/blog/katana-pricing — accessed 2026-09-10
9. https://www.stackscored.com/pricing/inventory-warehouse-management/cin7/ — accessed 2026-09-10
10. https://octurasolutions.com/resources/acumatica-pricing-2026-how-consumption-licensing-really-works — accessed 2026-09-10
11. https://acctivate.com/quickbooks-online-inventory-limitations/ — accessed 2026-09-10
12. https://www.fishbowlinventory.com/blog/quickbooks-inventory-management — accessed 2026-09-10
13. https://www.chift.eu/blog/the-accounting-software-landscape-in-sweden-a-2026-guide-for-saas-vendors — accessed 2026-09-10
14. https://en.noresca.se/when-fortnox-and-visma-are-not-enough-why-growth-companies-choose-netsuite-for-international-expansion/ — accessed 2026-09-10
15. https://www.erpresearch.com/pricing/sage-intacct — accessed 2026-09-10

## What would change this analysis
- Direct vendor pricing pages (not aggregators) for NetSuite, SAP B1 and Acumatica — none publish public list prices, so all figures above are quote-derived aggregator estimates and should be re-verified before any external claim.
- Real win/loss data once we have actual prospects, rather than published positioning.
- UNVERIFIED: exact current app/module count for Odoo Enterprise; current Sage 200 pricing; whether Acumatica's "no per-user" claim still holds for every edition (some add-on modules may be seat-priced).
