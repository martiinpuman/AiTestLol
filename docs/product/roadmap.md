# Roadmap — Aurora ERP

Status: draft v1 · Author: project-manager · Date: 2026-09-11
Inputs: `../research/01-feature-priority.md`, `../architecture/modules.md` §7 (build order), `../architecture/solution-layout.md` §6 (B-01…B-15), `../decisions/ADR-0007`, `../decisions/ADR-0008`, `../HUMAN_INBOX.md` (2026-09-11 block, Q9).

## How to read this

Milestones are ordered by the researcher's feature-priority scoring and the architect's module-dependency tiers, not by convenience. Each milestone states a **business outcome** — what a real user can do afterward that they could not do before — plus the capabilities included, what is deliberately left out, and exit criteria that can be checked mechanically or by a short demo. Milestone numbers are stable identifiers used across `docs/BACKLOG.md` and future specs; do not renumber them if a milestone's scope later grows.

**The one hard gate in this roadmap:** no business module (Milestone 2 onward) starts before Milestone 1's exit criterion — `scripts/verify.sh` green with B-15 complete — is met. The architect was explicit about this in `solution-layout.md` §6, and it is not a suggestion: tenancy and the Country Package seam are rewrites if retrofitted, so they are proven once, completely, before anything is built on top of them.

Milestone 1.5 (grid foundation) is a **technical milestone**, not a business outcome, inserted between the walking skeleton and Master Data for a specific reason explained there. It does not renumber the business milestones that follow, and in particular it is **not** the "M2" that `../architecture/testing-strategy.md` §12 and `solution-layout.md` §5.2 stage 10 mean when they say the 80% domain-coverage floor turns on "from milestone M2" — that M2 is Master Data (this roadmap's Milestone 2), the first milestone with real business domain code to hold a coverage floor against.

---

## Milestone 1 — Walking skeleton

**Goal.** A new customer can be provisioned a fully isolated, working Aurora ERP system in under a minute; their first user can log in, create one real business record (a Company) with one permission check enforced, and see it listed back — with every step proven to be correctly scoped to that customer's tenant alone, permanently audited, and reliably announced to the rest of the system. This is not a customer-visible feature; it is proof that the two decisions everything else depends on — database-per-tenant isolation (ADR-0007) and the Country Package seam (ADR-0008) — actually work, before a single line of real business logic is written on top of them.

**Included capabilities** (exactly the architect's B-01…B-15, `../architecture/solution-layout.md` §6):
- Solution skeleton, build-wide settings, and the `scripts/verify.sh` quality gate (all 11 stages).
- `Aurora.SharedKernel` (Money, Quantity, Percentage, DateRange, strongly-typed ids, `Result`).
- The architecture fitness-test suite (layer, module-boundary, tenancy, financial-correctness, country-agnosticism, security and migration-safety rules).
- The catalog database, the tenancy platform (`TenantScope`, connection resolution, the tenant-scoped `DbContext` factory), tenant provisioning as a resumable saga, and the cross-tenant migration runner.
- `Aurora.TestKit`'s two-tenant fixture and the mandatory `TenantIsolationContract` pattern.
- `Aurora.Countries.Contracts` and `Aurora.Countries.Hosting` (manifest loading, signature verification, the extension-point interfaces) and a working package installer, proven by installing a package into a live tenant.
- `scripts/new-module.sh`, so every module built from Milestone 2 onward starts from a proven template.
- The walking-skeleton vertical slice itself: create a Company and list it back, through both `/api/v1` and Blazor Server, in two separately provisioned tenants, with one permission check, one audit entry, one outbox event, one background job, and one installed Country Package all covered by tests.

**Non-goals** (deliberately excluded, not forgotten):
- Any real business functionality beyond the one proof-of-concept entity (Company). No Site, FiscalYear, NumberSeries, TaxRegistration, or any other Organization-module concept yet — those belong to Milestone 2.
- A public, self-service sign-up screen with plan selection and billing. Provisioning is triggered by an authenticated internal call (an operator action or an automated test/onboarding harness); a marketing-facing sign-up flow is a commercial-launch concern, not an architecture-proving one, and is out of scope until it is actually needed.
- Any UI beyond the smallest possible form and list for Company — in particular, **not** the full data-grid component (saved views, column pinning, filter chips). A tenant's company list is a handful of rows; using the heavyweight grid here would be gold-plating and would create a false dependency on Milestone 1.5.
- Enterprise SSO, per-tenant MFA policy, per-tenant database login roles (ADR-0007 §3.5 stage 2), an operator console, and compiled EF models — all real, all deliberately deferred (tracked as follow-up backlog items, see "Operational commitments" below).
- More than one Country Package. The walking skeleton proves the installer works generically; the first *reference* package (New Zealand) is built in Milestone 3, where there is finally a tax engine and a chart of accounts for it to fill.

**Exit criteria** (all must be true; this is the go/no-go checkpoint the researcher asked for):
1. `scripts/verify.sh` passes on the integration branch with every stage enabled (Docker running), including the architecture fitness suite and the tenant-isolation contract tests.
2. Two independently provisioned tenants exist in the test suite; a Company created in tenant A is provably unreadable from tenant B — through a direct query, a background job, and an integration event — and a deliberately mis-routed connection is provably rejected.
3. Creating a Company is blocked for a user without the relevant permission (a `403` with the missing permission named) and succeeds for a user who has it, with an audit entry recorded in the same transaction.
4. A `CompanyCreated` integration event is durably recorded via the transactional outbox in the same transaction as the Company row, and is observed being handled by at least one background-job consumer.
5. Each of the two test tenants has a Country Package installed via the real installer (not a stub), and the tenant's installed-package state is queryable.
6. A user can perform the whole create-and-list flow through the Blazor Server UI, not only through the API.

---

## Milestone 1.5 — UI data-grid foundation (technical gate, not a business outcome)

**Why this exists here.** `docs/design/components.md` §8 specifies one grid used everywhere in the product, expected to hold up at 150,000 rows and 40 columns over a Blazor Server SignalR circuit. No permissively licensed Blazor grid proves that out of the box, and the architect flagged two consequences in `../HUMAN_INBOX.md` (2026-09-11, Q9): first, that a **time-boxed spike must prove the approach before ADR-0024 is treated as settled**, with Radzen as the pre-cleared fallback if it fails; second, that **grid chrome — saved views, column settings, pinning, filter chips, the bulk-action bar — is the single largest piece of UI work in the project** and must be its own scheduled slice, never something quietly absorbed into the first screen that happens to need a list. Both belong early: every list screen from Milestone 2 onward (customers, items, journal lines, stock ledgers) is built once against whatever this milestone proves, not reinvented per screen.

**Included capabilities:**
- The performance spike: 150,000 synthetic rows × 40 columns, `QuickGrid` + `Virtualize`, measured p95 round-trip latency over a real SignalR circuit against the budget in `docs/architecture/scalability.md`.
- A recorded verdict: either ADR-0024 (QuickGrid) is confirmed, or it is superseded by an amendment adopting the Radzen fallback — either way, a decision made with evidence, not carried as an assumption.
- A reusable grid-chrome component (whatever the winning engine): server-side paging/sorting/filtering, row virtualization, column show/hide/reorder/resize/pin, saved views (own and shared), filter chips, the bulk-action bar with per-item progress and partial-failure reporting, per `docs/design/components.md` §8 — built against fixtures and test data, not yet wired to any real business module's data.

**Non-goals:**
- Any module-specific columns, filters, or saved-view content. This milestone builds the chrome; each business screen that consumes it (starting in Milestone 2) supplies its own columns and data source.
- Horizontal virtualization beyond what the spike needs to prove the 40-column case; further optimization is deferred until a real screen needs it.

**Exit criteria:**
1. The spike's measured p95 latency at 150,000 rows × 40 columns is recorded against the budget, and the accept/reject decision is written as an ADR-0024 confirmation or amendment.
2. The grid-chrome component exists, is covered by bUnit component tests (including keyboard navigation and the empty/loading/error states from `components.md` §8), and can page a 100,000-row fixture while issuing at most one page's worth of data per request (per `testing-strategy.md` §9).
3. At least one Milestone 2 screen (see below) is built consuming this component, proving it is genuinely reusable and not theoretical.

---

## Milestone 2 — Master data

**Goal.** A back-office user can set up and maintain the records every other part of the business depends on — their company's own details, the customers and vendors they trade with, and the items and services they buy and sell — accurately, without creating duplicate records, and with the system honestly telling them when it cannot yet validate something (for example, a tax identifier with no Country Package installed to check it).

**Included capabilities** (`../architecture/modules.md` §5, tier 2 — Organization, Parties, Products):
- Organization: full `Company` record (registered identifiers, base currency, primary jurisdiction, one or more Tax Registrations), `Site`/warehouse location master, `FiscalYear` and `AccountingPeriod`, `NumberSeries`.
- Parties: `Party` with customer and/or vendor roles (a party is frequently both), addresses, contacts, payment terms, credit limit; identifier value objects (`OrganizationNumber`, `TaxIdentifier`) delegate validation to whichever Country Package is installed, and honestly report "unverified" where none is.
- Products: `Item` (stocked/service/non-stocked), `UnitOfMeasure` and conversions, item categories, a basic `PriceList`.
- List and detail screens for all three, built on Milestone 1.5's grid chrome.

**Non-goals:**
- Any sales, purchasing, inventory quantity, or ledger posting behaviour — those are later milestones. Master data here is set up, not transacted against.
- Full discount-rule sophistication on price lists (a single price per item per currency is enough to prove the shape).
- Multiple Country Packages' identifier rules simultaneously (New Zealand's NZBN validator arrives in Milestone 3; until then, identifier fields correctly show "unverified," which is itself the required, honest behaviour per ADR-0008 extension point 7 — not a bug to fix early).

**Exit criteria:**
1. A user can create, edit, list, and search Company, Party, and Item records through both the UI and `/api/v1`, each tenant-isolated, permission-gated, and audited, with the list screens built on Milestone 1.5's grid chrome.
2. The 80% domain-coverage floor (`testing-strategy.md` §12) is enabled in `verify.sh` stage 10 and passing.
3. A party's tax identifier field correctly and visibly shows "unverified" in the absence of an installed Country Package, and correctly validates once one is installed (proven with a minimal test package, ahead of Milestone 3's real one).

---

## Milestone 3 — Accounting core, and the first Country Package (New Zealand)

**Goal.** A company's financial transactions can be posted to a general ledger that is always balanced and never silently wrong, and GST is calculated correctly for a New Zealand-registered business — proving, with a real jurisdiction rather than a synthetic test package, that the Country Package contract genuinely keeps jurisdiction-specific rules out of the core.

**Included capabilities** (`modules.md` tier 3 — Ledger, Tax; `../research/regulation/first-country-package.md`):
- Ledger: `Account` and chart of accounts (seeded by the installed Country Package, never hard-coded), `AccountRoleMapping`, `JournalEntry` with immutable lines, the posting service that rejects an unbalanced entry, an entry into a closed period, a currency-inconsistent entry, or an entry with no audit actor.
- Tax: `TaxCode`, `TaxCategory`, effective-dated determination rules, `ITaxEngine.Determine`/`.Calculate`, always evaluated as-of an explicit business date.
- The New Zealand Country Package: chart-of-accounts template, flat 15% GST rule set (effective-dated), the NZBN check-digit identifier validator against published test vectors, and a retention-policy declaration. The package's e-invoicing profile is declared but not yet exercised — that waits for Milestone 8, where a Sales Invoice actually exists to send.
- Manual, directly-entered journal postings (there is no Sales or Purchasing module yet to generate them automatically) and basic period open/close.

**Non-goals:**
- Sales, purchasing, or inventory-driven postings — nothing produces a journal entry automatically yet. This milestone proves the posting and tax-determination *services*, callable and correct, ahead of anything calling them from a document flow.
- Statutory return filing or any financial statement production (Milestone 7).
- FX revaluation at period end (Milestone 7, where the full record-to-report close mechanics land) — Money is multi-currency-aware from day one regardless (see "Cross-cutting" below), but revaluation is a close-time behaviour.
- A second Country Package (Australia is next, but only once a real second module exists to prove the contract composes across two jurisdictions with genuinely different tax administrations — not before).

**Exit criteria:**
1. A balanced, manually-entered journal entry posts successfully; an unbalanced one, one targeting a closed period, and one with no audit actor are each rejected with a clear reason.
2. The New Zealand package is installed on a test tenant; its chart of accounts and GST rate are queryable through the core's account-role and tax-engine contracts, never through a jurisdiction check in core code (enforced by fitness rule C1).
3. `ITaxEngine.Calculate` returns 15% GST for a New Zealand-registered company as of a given tax point date, and reproduces the same rate when asked about a historical date even after a (test) rate change.
4. The NZBN identifier validator passes the published known-good and known-bad test vectors.
5. A period can be opened and closed; a posting attempt into a closed period is rejected and the rejection is audited.

---

## Milestone 4 — Inventory (minimal slice)

**Goal.** A trading company can see how much of each item it actually has on hand and what that stock is worth, with real cost effects flowing into the ledger built in Milestone 3 — laying real stock underneath the order-to-cash flow that arrives next, instead of building a sales flow with nothing behind it.

**Included capabilities** (`modules.md` tier 4; `../research/processes/inventory.md`):
- `StockItem` on-hand quantity per item, in a single default warehouse.
- One valuation method (weighted average — the simplest correct method; FIFO and standard cost are a later widening pass, per the research's explicit sequencing).
- `StockMovement`: Receipt and Adjustment (Shipment and Transfer are driven by Sales/Purchasing, which do not exist yet — their movement types are modelled now but not yet exercised end-to-end).
- The cost effect of every movement posts to the ledger through `IPostingService`, using `AccountRole.InventoryAsset` and `AccountRole.CostOfGoodsSold` — Inventory never names an account.

**Non-goals:**
- Multi-warehouse allocation, FIFO/standard cost, lot/serial tracking, cycle counting, and a configurable negative-stock policy — all deferred to the widening pass alongside Milestone 5, per the research's recommendation to widen inventory and sales together rather than finishing one first.
- Landed cost allocation (Milestone 6, procure-to-pay).

**Exit criteria:**
1. A stock receipt increases on-hand quantity and posts a balanced journal entry debiting `AccountRole.InventoryAsset`.
2. An adjustment (write-off) decreases quantity and posts the corresponding balanced correction.
3. On-hand quantity and current valuation are queryable per item.
4. An attempt to ship (once Sales exists, tested with a stub) more than is on hand is blocked by default, with the override path explicitly deferred and documented as such — never a silent negative balance.

---

## Milestone 5 — Order-to-cash

**Goal.** A trading company can quote, confirm, deliver and invoice a customer order — tracking ordered, delivered and invoiced quantities independently per line, exactly as real partial shipments and split billing require — and always know which customer invoices are still open.

**Included capabilities** (`modules.md` tier 5; `../research/processes/order-to-cash.md`):
- Sales: `Quote`, `SalesOrder`, `Delivery`, `SalesInvoice`, `CreditNote` as separate aggregates (non-negotiable per `modules.md` §5).
- Payments (AR side): `OpenItem`, incoming `Payment`, `CashApplication`, reacting to `SalesInvoicePosted` — Payments does not reference Sales directly.
- The inventory widening pass alongside this milestone: multi-warehouse allocation and a second valuation method (FIFO), per the research's explicit "widen together" recommendation.
- Credit management: a credit-limit check and credit hold on order confirmation.

**Non-goals:**
- The vendor/purchasing side (Milestone 6).
- Statutory VAT/GST return filing (Milestone 7) and e-invoicing transmission (Milestone 8) — though the `SalesInvoice` aggregate this milestone produces is exactly what both of those later milestones need to exist first.
- Automatic cash-application tolerance rules for near-but-not-exact payment amounts — this was flagged UNVERIFIED by the researcher (no confirmed practitioner tolerance numbers); a manual-match path is sufficient for v1, and automatic tolerance matching is a follow-up once real data exists to set a sensible default.

**Exit criteria:**
1. The full order → partial delivery → invoice (consolidated and per-shipment) → payment happy path is covered by tests, including a backorder that clears on a later delivery.
2. A credit note against part of a single invoice line, and a full-invoice reversal, are both covered by tests and never edit a posted invoice in place.
3. Rounding is handled with an explicit, auditable rounding-residual amount on every invoice, never silently absorbed into a line.
4. A multi-currency order and invoice each record the FX rate, its source, and its date independently.
5. An aged AR balance report is correct against a seeded set of open and partially-applied invoices.

---

## Milestone 6 — Procure-to-pay

**Goal.** A trading company can raise a purchase order, record what was actually received, three-way-match it against the vendor's invoice, and pay the vendor — catching a quantity or price mismatch before it is paid, not after.

**Included capabilities** (`modules.md` tier 5; `../research/processes/procure-to-pay.md`):
- Purchasing: `PurchaseOrder`, `GoodsReceipt`, `VendorInvoice`, the three-way match (quantity, unit price, total, description within a configurable tolerance), `DebitNote`, a simplified single-basis landed-cost allocation.
- Payments (AP side), symmetric with Milestone 5's AR side.

**Non-goals:**
- Approval-workflow sophistication beyond a single approver step — a full delegation-of-authority engine is not core to proving procure-to-pay.
- Multiple landed-cost allocation bases (weight, value, volume) — one default basis is enough for v1; more is a widening pass if a design partner needs it.

**Exit criteria:**
1. A purchase order, partial goods receipt, and vendor invoice within tolerance auto-clear for payment; one outside tolerance is routed to an exception queue, never silently accepted or silently blocked with no reason shown.
2. A vendor credit/debit note is its own document, mirroring Milestone 5's credit note pattern.
3. An aged AP balance report is correct.
4. A landed cost arriving after goods are already received and partially consumed correctly adjusts valuation through a correcting entry, never an edit to a posted movement.

---

## Milestone 7 — Statutory reporting and period close

**Goal.** A controller can close a fiscal period with real confidence — reconciled, reviewed, and locked against ordinary further posting — and produce New Zealand's GST return directly from the ledger, proving the statutory-report extension point end to end against a real filing obligation.

**Included capabilities** (`modules.md` tier 6 — Reporting; `../research/processes/record-to-report.md`):
- Reporting module: read models built from integration events, financial statement definitions (a basic balance sheet and profit-and-loss), statutory report runs.
- Record-to-report close mechanics: accrual entries with an optional auto-reversal on the next period's first day, foreign-currency revaluation of open balances at period-end spot rate (posting an unrealized gain/loss to its own account, distinct from realized gain/loss), trial balance review, and period lock.
- The New Zealand GST return as the first `IStatutoryReportDefinition` + `IFilingFormat` pair, exported in the format IRD expects for manual filing.

**Non-goals:**
- Automated electronic submission to IRD's Gateway Services — a reviewable export a controller files themselves is sufficient for v1; direct e-filing is a differentiator to schedule once the export path is proven and, per `../HUMAN_INBOX.md` Q7, once IRD sandbox registration has actually been attempted.
- Multi-entity consolidation and intercompany elimination — flagged by the researcher as a genuine open question the architect has not solved, and explicitly not attempted here.
- Any jurisdiction's statutory report beyond New Zealand's GST return.

**Exit criteria:**
1. A period can be locked; an ordinary posting attempt into a locked period is rejected, while a reversal referencing an original entry in the now-locked period posts correctly into the current open period.
2. FX revaluation at period end posts an unrealized gain/loss to its own account for a seeded open foreign-currency invoice, without altering the invoice's original posted amount.
3. An accrual entered with auto-reversal correctly reverses on the first day of the next period without manual follow-through.
4. The GST return reproduces the correct figures from a seeded set of posted documents and exports in IRD's expected format.

---

## Milestone 8 — E-invoicing

**Goal.** A New Zealand business can send a Peppol-conformant electronic invoice for a posted Sales Invoice, and receive one from a supplier — the strongest possible proof that the Country Package e-invoicing extension point is real, because it is the one extension point every incumbent surveyed handles as bespoke, bolted-on code.

**Included capabilities** (`modules.md` tier 6 — DocumentExchange; ADR-0008 §7 point 4):
- DocumentExchange: outbound and inbound e-document transmission, transport receipts, an exact archive of every transmitted payload.
- The New Zealand package's `IEInvoicingProfile`, mapping `Documents.Canonical` to PINT A-NZ, validated offline against the published schema and business rules.

**Non-goals:**
- Becoming a certified Peppol Access Point ourselves — we integrate with one; operating network infrastructure is out of scope and, per `overview.md` §5.3's hard limits, we do not sign up for or pay for external services during this build, so a sandbox/mock access point stands in until a real integration is commercially warranted.
- Automatic matching of an inbound e-invoice to an existing Purchase Order — manual review of an inbound e-invoice is acceptable for v1.
- Any e-invoicing profile beyond PINT A-NZ.

**Exit criteria:**
1. A posted `SalesInvoice` maps to a PINT A-NZ-conformant document and validates against the published schema and CIUS business rules in a golden-file test.
2. Every outbound transmission and its exact payload is archived and queryable, whether or not the transmission ultimately succeeded.
3. A test inbound e-invoice is received, archived, and displayed as an incoming vendor document.

---

## Milestone 9 — Manufacturing and Projects (named, not scheduled)

Per `modules.md` §5, tier 7: **Manufacturing** (BOM, routing, work order, MRP) and **Projects** (project, task, time entry, WIP) are named here only so nobody invents a different name for them later. Both depend on Master Data and Inventory when they eventually start, which is why they cannot come earlier than this. Per the researcher's own "what would change my mind" note, this milestone is scheduled only once a validated light-manufacturing (or services) design partner exists — building it against zero validated demand is exactly the mistake the research flagged. No dates, capabilities, or exit criteria are committed here.

---

## Cross-cutting: multi-currency is a property, not a milestone

Per `modules.md` §7: `Money` is `(decimal amount, Currency)` from the very first commit (Milestone 1's `Aurora.SharedKernel`), and every document that touches money records which FX rate applied, its source, and its date — never just "the tenant's current rate." Retrofitting multi-currency into a ledger after the fact is a rewrite, so no milestone above treats it as a feature to add; each milestone that introduces money-bearing documents is expected to already handle more than one currency correctly, and Milestone 7 is where the FX *revaluation* mechanic (a period-close behaviour, not a document behaviour) is proven.

---

## Operational commitments that are not milestones

These are real, scheduled work the architect flagged as follow-ups (`solution-layout.md`, `../decisions/ADR-0007`, `../decisions/ADR-0009`, `../architecture/testing-strategy.md`, `../architecture/dependencies.md`). They do not gate any milestone above, but they are tracked in `docs/BACKLOG.md` (status `draft`) so they are scheduled work, not a wish list:

| Commitment | Must land by | Backlog ID |
|---|---|---|
| Per-tenant database login roles (ADR-0007 §3.5 stage 2) | Before the first paying customer | `FOLLOWUP-001` |
| Compiled EF models | Before first release | `FOLLOWUP-002` |
| Operator console over the job queue | Before the fleet grows past what an engineer can debug by hand | `FOLLOWUP-003` |
| 80% domain-coverage floor enforced in `verify.sh` | Milestone 2 exit (see Milestone 2 above) | `FOLLOWUP-004` |
| xUnit v3 evaluation | Opportunistic, before it becomes a forced migration | `FOLLOWUP-005` |
| Confirm `Bogus`'s exact licence text | Before `Bogus` is first used (expected around Milestone 4/5 performance tests) | `FOLLOWUP-006` |
| Per-tenant MFA policy (today: opt-in per user only) | Before any tenant with a compliance requirement for mandatory MFA | `FOLLOWUP-007` |
| Quarterly single-tenant restore drill | Ongoing, starting once the first tenant holds real (test) data | `FOLLOWUP-008` |
