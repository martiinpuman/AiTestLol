# Glossary — Aurora ERP

The ubiquitous language for this product. Every spec, backlog item and conversation uses these terms exactly as defined here. A new business concept gets a new entry in this file before it gets a new field in a form — that is what keeps the code, the specs and the UI saying the same thing. Developers may extend this file (per `CLAUDE.md`); the project-manager keeps it consistent with the specs.

Terms are business definitions: what the word means to the person running the business, not how it is implemented.

---

**Account Role** — A named financial job that an account in the chart of accounts can do (for example "the account that holds tax owed to the tax authority" or "the account that holds the value of goods on the shelf"). The core system always asks for an account by its role, never by its number, so that a New Zealand chart of accounts and a future country's chart of accounts can both answer the same question in their own way. A Country Package is what actually tells the system which real account fills each role.

**Allocation** — The splitting of one amount across several lines: a whole-order discount spread over the items it applies to, a shipment's freight cost spread over the goods in it, one payment spread over the invoices it settles. The parts of an allocation always add back up to the original amount exactly — the system never loses a cent or invents one — and the leftover cents that a division cannot split evenly go to the lines with the largest fraction left over, earliest line first, so that splitting the same amount twice gives the same answer twice.

**Audit Event** — A permanent, tamper-evident record of one thing that happened in the system: who did it, when, from where, to which record, and what changed. Every financial change and every security-relevant action (signing in, changing a role, installing a package) creates one. Audit events are never edited or deleted, even by mistake — a correction is always a new record, never a rewrite of history.

**Backorder** — The part of an ordered quantity that a business has promised a customer but cannot deliver yet because there is not enough stock on hand. A backorder is not a failure state to hide; it is a normal, trackable condition that keeps the sales order line open until the rest of the goods ship.

**Company** — One legal entity that does business: the thing that has its own name, its own tax registrations, its own chart of accounts, and its own set of financial statements. A single customer of Aurora ERP (a Tenant) may contain more than one Company — for example a group with a trading business in one country and a manufacturing business in another.

**Company Scope** — The set of a Tenant's Companies that a person, or a piece of work done on their behalf, is allowed to see and act on. It takes one of exactly two forms: every Company in the tenant, including ones created later (what an owner or an external accountant has), or a named list of specific Companies (what a bookkeeper assigned to one subsidiary has). There is deliberately no such thing as a scope over no companies: a request that could see nothing is refused outright rather than quietly answered as if it could see everything.

**Core Contract Version** — The version number of the agreement between the core product and Country Packages: what a package is allowed to ask of the system, and what the system promises to keep working. Every package states the range of core contract versions it was built against, and the system refuses to install or upgrade a package outside that range rather than discovering the mismatch later as a wrong number on a tax return. It moves slowly and deliberately, and is not the product's own version number.

**Country Package** — An installable, versioned add-on that teaches Aurora ERP how to do business correctly in one country or region: which taxes apply and at what rate, what the standard chart of accounts looks like, how a statutory tax return is laid out, how to send an electronic invoice, how to validate a company registration number, and how long records must be kept. The core product contains none of this knowledge itself; a tenant adds exactly the packages it needs.

**Credit Note** — A document that reduces what a customer owes, issued after an invoice because goods were returned, an order was overcharged, or a delivery fell short. A credit note always points back to the invoice it corrects and can correct just part of one line — it is never the original invoice edited to look different after the fact.

**Delivery** — The record of goods that were actually picked, packed and shipped to a customer, separate from both the sales order that requested them and the invoice that bills for them. A single order is often delivered in several partial shipments, and a single delivery is often billed on its own schedule rather than the moment it ships — Delivery and Invoice are always two different documents for this reason.

**Extension Point** — One named job that the core product knows it cannot do by itself and hands to a Country Package: supplying a chart of accounts, answering what tax applies on a date, laying out a statutory return, mapping an electronic invoice, reading a bank statement, validating a registration number, and so on. There are ten. The core defines what each one is asked and what a usable answer looks like; the package decides the answer for its own country.

**Invoice** — The document that bills a customer (a Sales Invoice) or records what a vendor billed the business (a Vendor Invoice) for goods or services. Once an invoice is posted, its amounts are fixed; any later correction happens through a Credit Note, a Debit Note, or a full reversal — never by editing the posted invoice.

**Item** — A product or service the business buys, sells, or holds as stock: identified by a code, described in words a customer or buyer would recognize, and priced. Some items are physically stocked (tracked as Stock); others (a consulting hour, a delivery fee) are not.

**Journal Entry** — A single, always-balanced record in the general ledger: every entry's debits equal its credits, in every currency it touches. Every financial event in the business — an invoice, a payment, a stock movement, a manual adjustment — eventually becomes one or more journal entries. Once a journal entry is posted it is permanent; the only way to correct it is a Reversal.

**Membership** — The fact that one person belongs to one Tenant, with one or more Roles assigned, optionally limited to specific Companies within that tenant. A person who works across several client businesses (an external accountant) holds several memberships, one per tenant, and never confuses which business they are currently working in.

**Money** — An amount together with the currency it is denominated in. There is no such thing as a bare number representing an amount of money in this product; every amount always carries its currency, and amounts in different currencies are never added together directly.

**Outbox** — The guarantee that when something important happens inside the system (an invoice is posted, a company is created), every other part of the system that needs to know eventually finds out — even if a server crashes at the worst possible moment, and even if delivery is a little delayed. Nothing that matters is ever silently lost, and nothing is ever reported as having happened when it did not actually commit.

**Package Manifest** — What a Country Package says about itself before any of it runs: which country it speaks for, which version it is, which core contract versions it works with, what it contributes, and which other packages it needs or cannot live alongside. The system reads the manifest, checks it, and verifies the package's signature before running a single line of the package's own code.

**Period Close** — The point at which an accounting period (typically a month) is reviewed, reconciled, and then locked so that no further ordinary transactions can be posted into it. After a period is closed, a mistake found later is corrected in the current open period by a Reversal that refers back to the original — the closed period itself is never reopened and rewritten.

**Permission** — A single, specific thing a user may be allowed to do — for example, create a company, post an invoice, or approve a stock write-off. Permissions are the actual unit of authorization in this product; a Role is simply a named bundle of permissions that a tenant can assign to its people.

**Posting** — The act of permanently recording a Journal Entry in the general ledger. Before posting, a document (an invoice, a stock movement) is a draft that can still be changed freely; once posted, it is part of the business's permanent financial record and can only be corrected by a Reversal.

**Provisioning** — The automated process that turns a new customer's request into a working, fully isolated Aurora ERP system: its own database, its own first Company, its own administrator account, and whichever Country Packages it asked for — usable within about a minute, without any manual setup step by Aurora staff.

**Quantity** — A number of something together with the Unit of Measure it is counted in. Exactly like Money, a quantity in this product is never a bare number: 10 is not a quantity, 10 pieces is. Quantities counted in different units are never added together or compared, because ten pieces plus two kilograms is not twelve of anything; converting between units always needs the item's own conversion factor. A quantity may be negative — a return, a stock issue and a write-off are all normal business.

**Residency Region** — The part of the world a Tenant's data is allowed to live in, chosen when the tenant is created because some customers are required by law or by their own policy to keep their records in a particular country. The region a tenant declares and the region its database actually sits in are checked against each other rather than trusted: a tenant cannot be moved onto a machine outside its region, even by mistake.

**Reversal** — A new Journal Entry (or a full document reversal) that exactly cancels out a previous posting, used whenever a posted financial record turns out to be wrong. The original record is never edited or deleted; the reversal and the original both remain visible, so the full history of what was recorded — and why it changed — is always reconstructable.

**Role** — A named set of Permissions that a tenant can assign to a person, optionally limited to one or more Companies (for example, "Bookkeeper" in Company A only). Some roles come built in (an "Owner" role that can do everything); a tenant may also define its own roles to match how it actually organizes work.

**Rounding Residual** — The small leftover amount (a fraction of a cent) that appears when tax or a total is calculated on several lines and rounded, versus rounding the whole document at once. Rather than silently folding this residual into the last line's amount, the system records it as its own explicit, auditable amount, so every document's totals always add up exactly and visibly.

**Sales Order** — A customer's confirmed commitment to buy specific items in specific quantities, which the business has accepted and will fulfil. A sales order line stays open, independently tracking how much has been ordered, how much has actually been delivered, and how much has actually been invoiced, until all three agree — those three quantities routinely differ during the life of a real order and that is expected, not an error.

**Stock** — The quantity of a physical Item that a business actually holds, at a given location, at a given moment — together with what that quantity is worth in money. Every time stock physically moves (goods come in, goods ship out, a count finds a discrepancy), both the quantity and its financial value change together, and that value change is what flows into the general ledger as the cost of the goods sold.

**Subscription** — What a Tenant is entitled to for a stretch of time: which plan they are on and how many people may use the system. A tenant has a history of subscriptions rather than one line that gets edited, so a question about what the customer was paying for last March can still be answered; an upgrade ends the current subscription and starts the next one the following day, and a tenant never holds two subscriptions covering the same day.
**Tax Code** — A jurisdiction's own name for one tax treatment — the everyday standard-rate code, a zero-rated export, an exemption. The core system never interprets a tax code: it carries the code on a document line and asks the installed Country Package what that code meant on the document's Tax Point Date. Two countries may use the same letters for different things and nothing breaks, because the answer always comes from the package.

**Tax Point Date** — The specific date used to decide which tax rule and which tax rate apply to a transaction. Because tax rates change over time, printing an invoice from two years ago must always reproduce the rate that applied on that invoice's own tax point date — never today's rate.

**Tax Registration** — A formal record that a Company is registered with a specific tax authority (for example, for GST or VAT), including the registration's own identifying number and the rules that follow from it. A company may hold more than one tax registration over time or across jurisdictions, even though most companies in Aurora ERP's first release have exactly one.

**Statutory Report** — A return or filing that a jurisdiction requires a Company to submit — a periodic GST or VAT return, an annual accounts filing, a cross-border sales listing. A Country Package supplies the layout: which boxes the return has and what each box adds up, always stated in terms the core understands (Account Roles and tax categories) rather than in terms of the database. The core fills the boxes from the ledger; the package renders the file the authority accepts. Two installed packages may not both supply the same return for the same Company — that is refused when the second is activated, rather than producing a figure that is wrong in both countries.

**Tenant** — One paying customer of Aurora ERP: one subscription, one completely private and isolated system, one set of installed Country Packages. Everything a tenant does — every Company, every record, every user — lives inside that tenant's own system and is never visible to, or reachable from, any other tenant's system, under any circumstance.

**Tenant Key** — The short, readable name a Tenant is known by — `acme-trading` — used in the web address they sign in at and in every operational conversation about them. It is not the tenant's identity: a customer may be renamed, and everything the system stores about them keeps pointing at the same tenant regardless. A key is never handed to a second customer, even years after the first one leaves.

**Tenant Lifecycle** — The stages a Tenant passes through, from the moment someone signs up to the moment their data is destroyed: being set up, failed to set up, trading normally, suspended (readable but read-only, after non-payment or at the customer's request), blocked (served a maintenance page because the system cannot currently trust that tenant's data is where it should be), having their data exported on the way out, awaiting deletion at an agreed date, and finally deleted — leaving only a record that the customer existed and when. Suspension and pending deletion are reversible; deletion is not.

**Unit of Measure** — What a Quantity is counted in: pieces, kilograms, metres, litres, hours. Every item says which unit it is bought, held and sold in, and every quantity in the system carries its unit with it. Units are recorded using the international standard codes that electronic invoices require, so that a line sent to a customer's system means there what it meant here.
