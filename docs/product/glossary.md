# Glossary — Aurora ERP

The ubiquitous language for this product. Every spec, backlog item and conversation uses these terms exactly as defined here. A new business concept gets a new entry in this file before it gets a new field in a form — that is what keeps the code, the specs and the UI saying the same thing. Developers may extend this file (per `CLAUDE.md`); the project-manager keeps it consistent with the specs.

Terms are business definitions: what the word means to the person running the business, not how it is implemented.

---

**Account Role** — A named financial job that an account in the chart of accounts can do (for example "the account that holds tax owed to the tax authority" or "the account that holds the value of goods on the shelf"). The core system always asks for an account by its role, never by its number, so that a New Zealand chart of accounts and a future country's chart of accounts can both answer the same question in their own way. A Country Package is what actually tells the system which real account fills each role.

**Audit Event** — A permanent, tamper-evident record of one thing that happened in the system: who did it, when, from where, to which record, and what changed. Every financial change and every security-relevant action (signing in, changing a role, installing a package) creates one. Audit events are never edited or deleted, even by mistake — a correction is always a new record, never a rewrite of history.

**Backorder** — The part of an ordered quantity that a business has promised a customer but cannot deliver yet because there is not enough stock on hand. A backorder is not a failure state to hide; it is a normal, trackable condition that keeps the sales order line open until the rest of the goods ship.

**Company** — One legal entity that does business: the thing that has its own name, its own tax registrations, its own chart of accounts, and its own set of financial statements. A single customer of Aurora ERP (a Tenant) may contain more than one Company — for example a group with a trading business in one country and a manufacturing business in another.

**Country Package** — An installable, versioned add-on that teaches Aurora ERP how to do business correctly in one country or region: which taxes apply and at what rate, what the standard chart of accounts looks like, how a statutory tax return is laid out, how to send an electronic invoice, how to validate a company registration number, and how long records must be kept. The core product contains none of this knowledge itself; a tenant adds exactly the packages it needs.

**Credit Note** — A document that reduces what a customer owes, issued after an invoice because goods were returned, an order was overcharged, or a delivery fell short. A credit note always points back to the invoice it corrects and can correct just part of one line — it is never the original invoice edited to look different after the fact.

**Delivery** — The record of goods that were actually picked, packed and shipped to a customer, separate from both the sales order that requested them and the invoice that bills for them. A single order is often delivered in several partial shipments, and a single delivery is often billed on its own schedule rather than the moment it ships — Delivery and Invoice are always two different documents for this reason.

**Invoice** — The document that bills a customer (a Sales Invoice) or records what a vendor billed the business (a Vendor Invoice) for goods or services. Once an invoice is posted, its amounts are fixed; any later correction happens through a Credit Note, a Debit Note, or a full reversal — never by editing the posted invoice.

**Item** — A product or service the business buys, sells, or holds as stock: identified by a code, described in words a customer or buyer would recognize, and priced. Some items are physically stocked (tracked as Stock); others (a consulting hour, a delivery fee) are not.

**Journal Entry** — A single, always-balanced record in the general ledger: every entry's debits equal its credits, in every currency it touches. Every financial event in the business — an invoice, a payment, a stock movement, a manual adjustment — eventually becomes one or more journal entries. Once a journal entry is posted it is permanent; the only way to correct it is a Reversal.

**Membership** — The fact that one person belongs to one Tenant, with one or more Roles assigned, optionally limited to specific Companies within that tenant. A person who works across several client businesses (an external accountant) holds several memberships, one per tenant, and never confuses which business they are currently working in.

**Money** — An amount together with the currency it is denominated in. There is no such thing as a bare number representing an amount of money in this product; every amount always carries its currency, and amounts in different currencies are never added together directly.

**Outbox** — The guarantee that when something important happens inside the system (an invoice is posted, a company is created), every other part of the system that needs to know eventually finds out — even if a server crashes at the worst possible moment, and even if delivery is a little delayed. Nothing that matters is ever silently lost, and nothing is ever reported as having happened when it did not actually commit.

**Period Close** — The point at which an accounting period (typically a month) is reviewed, reconciled, and then locked so that no further ordinary transactions can be posted into it. After a period is closed, a mistake found later is corrected in the current open period by a Reversal that refers back to the original — the closed period itself is never reopened and rewritten.

**Permission** — A single, specific thing a user may be allowed to do — for example, create a company, post an invoice, or approve a stock write-off. Permissions are the actual unit of authorization in this product; a Role is simply a named bundle of permissions that a tenant can assign to its people.

**Posting** — The act of permanently recording a Journal Entry in the general ledger. Before posting, a document (an invoice, a stock movement) is a draft that can still be changed freely; once posted, it is part of the business's permanent financial record and can only be corrected by a Reversal.

**Provisioning** — The automated process that turns a new customer's request into a working, fully isolated Aurora ERP system: its own database, its own first Company, its own administrator account, and whichever Country Packages it asked for — usable within about a minute, without any manual setup step by Aurora staff.

**Reversal** — A new Journal Entry (or a full document reversal) that exactly cancels out a previous posting, used whenever a posted financial record turns out to be wrong. The original record is never edited or deleted; the reversal and the original both remain visible, so the full history of what was recorded — and why it changed — is always reconstructable.

**Role** — A named set of Permissions that a tenant can assign to a person, optionally limited to one or more Companies (for example, "Bookkeeper" in Company A only). Some roles come built in (an "Owner" role that can do everything); a tenant may also define its own roles to match how it actually organizes work.

**Rounding Residual** — The small leftover amount (a fraction of a cent) that appears when tax or a total is calculated on several lines and rounded, versus rounding the whole document at once. Rather than silently folding this residual into the last line's amount, the system records it as its own explicit, auditable amount, so every document's totals always add up exactly and visibly.

**Sales Order** — A customer's confirmed commitment to buy specific items in specific quantities, which the business has accepted and will fulfil. A sales order line stays open, independently tracking how much has been ordered, how much has actually been delivered, and how much has actually been invoiced, until all three agree — those three quantities routinely differ during the life of a real order and that is expected, not an error.

**Stock** — The quantity of a physical Item that a business actually holds, at a given location, at a given moment — together with what that quantity is worth in money. Every time stock physically moves (goods come in, goods ship out, a count finds a discrepancy), both the quantity and its financial value change together, and that value change is what flows into the general ledger as the cost of the goods sold.

**Tax Point Date** — The specific date used to decide which tax rule and which tax rate apply to a transaction. Because tax rates change over time, printing an invoice from two years ago must always reproduce the rate that applied on that invoice's own tax point date — never today's rate.

**Tax Registration** — A formal record that a Company is registered with a specific tax authority (for example, for GST or VAT), including the registration's own identifying number and the rules that follow from it. A company may hold more than one tax registration over time or across jurisdictions, even though most companies in Aurora ERP's first release have exactly one.

**Tenant** — One paying customer of Aurora ERP: one subscription, one completely private and isolated system, one set of installed Country Packages. Everything a tenant does — every Company, every record, every user — lives inside that tenant's own system and is never visible to, or reachable from, any other tenant's system, under any circumstance.
