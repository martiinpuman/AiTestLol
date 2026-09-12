# How Incumbents Handle Localization — and What Our Country Package Contract Should Expose

Status: draft v1. Author: researcher. Date: 2026-09-10.

## Conclusion — recommended extension points for our Country Package contract

Every incumbent studied has independently converged on the same shape: **a small, fixed core with a "country" selector chosen once, plus an installable unit that only ever adds data and behaviour, never patches core code.** Based on that convergence, our Country Package contract should expose at least these extension points. Each is justified below by which incumbent(s) demonstrate it and where it breaks down for them — our contract should specifically fix the failure modes, not just copy the shape.

| # | Extension point | Contributes | Evidenced by |
|---|---|---|---|
| 1 | **Chart-of-accounts template** (seed data, not schema) | A starting account tree + default account roles (e.g. "AR control account", "input VAT account") that core financial logic references by *role*, never by hard-coded account number | Odoo `l10n_*` (installs a CoA + fiscal positions on top of a country-agnostic `account.account` model) |
| 2 | **Tax rule set**: rates, rate-validity periods, tax-on-tax, rounding basis, reverse-charge/exemption codes | Versioned data (not code) that the core tax engine evaluates; must support **effective-dated** rates so a rate change doesn't require a redeploy | NetSuite SuiteTax (rules-based tax engine that "OneWorld can localize by country" instead of hard-coded percentages) |
| 3 | **Statutory report definitions** (declarative report templates against core ledger data) | Report layout + field mapping, e.g. a VAT return; must be data/template driven so a new report doesn't need a core code change | NetSuite Tax Reporting Framework + country-specific "localization SuiteApps" installed on top of it |
| 4 | **E-invoicing profile**: outbound document mapping to a transport format (e.g. Peppol BIS/UBL) + validation rules + identifier scheme | Mapping from our canonical Invoice/CreditNote aggregate to the country's required XML/JSON shape, plus business-rule validation before transmission | Peppol BIS Billing 3.0 itself is the generalizable pattern: one canonical semantic model (EN 16931), country/region **CIUS** (Core Invoice Usage Specifications) narrow it per jurisdiction (e.g. PINT A-NZ) without changing the base schema — this is precedent for "one canonical Invoice, many country profiles" |
| 5 | **Bank file format** (payment initiation export + bank statement import parsers) | Format adapters only — must not require the payments domain model itself to change | Implicit in every incumbent's "localization adds file formats" pattern (SAP B1, NetSuite) though none of our sources document this as cleanly as tax; treat as UNVERIFIED-by-incumbent-docs but architecturally consistent |
| 6 | **Accounting-interchange (import/export) adapter** | A serializer/deserializer between our canonical ledger model and a country or standard-specific exchange file (SAF-T-like, or a national format) | No single incumbent studied documents this as a discrete, versioned unit as cleanly as tax — this is a **gap in the incumbents**, which is itself useful evidence: it's a real seam worth us modeling explicitly rather than assuming "everyone already solved this" |
| 7 | **Identifier validator** (company registration numbers, tax IDs, bank account formats) as a pure function/value-object rule, pluggable per country | A `Validate(string) -> bool` + checksum algorithm the core's generic "OrganizationNumber"/"VatId" value objects delegate to | GS1/NZBN check-digit algorithm is public and delegated exactly this way in NZ; EU VIES is the equivalent cross-country validation service pattern |
| 8 | **Locale formatting pack** (number/date/currency display, additional UI language) | Pure presentation data — must never touch business logic | Odoo and BC both ship translation separately from fiscal logic (see BC's XLIFF-based translation pipeline below), evidence that these two concerns *should* be separably installable even though they're often bundled |
| 9 | **Retention/archiving policy declaration** | Declarative: minimum retention period per document type, immutability requirements | Not directly evidenced in the incumbent docs surveyed this pass — carried over from CLAUDE.md's own Country Package definition; flagged as a point to verify against at least one real bookkeeping-law source before the architect finalizes the contract shape (see `regulation/first-country-package.md` for New Zealand's specifics as a first data point) |
| 10 | **A version/compatibility declaration the package makes about the core** (which core contract version it targets) | Package manifest metadata, checked at install and at every core upgrade | NetSuite's "managed bundle" model (auto-updates, but can silently reset locally-changed properties) and BC's per-tenant-extension merge conflicts on upgrade are both **cautionary evidence** that without an explicit, enforced compatibility contract, either (a) packages silently overwrite tenant customizations, or (b) core upgrades silently break packages. Our contract must make both directions of this an explicit, testable check — not a runtime surprise. |

The single strongest, most transferable idea across all four incumbents: **the core defines identity-agnostic "roles" or "slots" (a role like "sales VAT account", a schema like EN 16931's semantic model, a framework like Tax Reporting Framework) and a country package fills the slot with country-specific values/mappings — it never injects country-specific branches into core logic.** This is precisely CLAUDE.md's "the core must never contain `if (country == "SE")`" requirement, and it is empirically how the two most disciplined examples (Odoo's CoA/fiscal-position model and NetSuite's Tax Reporting Framework + SuiteTax) are built.

---

## Evidence, per incumbent

### 1. Odoo — Fiscal Localization Packages (`l10n_*`)

**What a package contributes.** A `l10n_<country>` module installs a chart of accounts, "fiscal positions" (rules that swap taxes/accounts based on counterpart location — e.g. domestic vs. intra-EU vs. export), the country's tax rates, and country-specific statements/certifications. Quoting the pattern rather than the exact prose: the module "requires fine-tuning the chart of accounts, activating the taxes to be used, configuring country-specific statements and certifications, and sometimes more."
Source: https://www.odoo.com/documentation/19.0/applications/finance/fiscal_localizations.html — accessed 2026-09-10.

**How it's chosen/installed.** The core installs the matching `l10n_*` module automatically based on the **company's country field** at the point the Accounting app is installed — i.e., country selection is a data value on the tenant/company record, and the package is resolved from it, not hard-coded per deployment. In a multi-company Odoo instance, **branches inherit the parent company's localization; separate legal entities in different countries must be modeled as separate companies**, each with their own package. Coverage is broad: Odoo documents fiscal localizations for 60+ countries and says new ones are added on an ongoing basis.
Source: same as above.

**What stays in core.** The generic ledger models (`account.account`, `account.move`, `account.tax`) are country-agnostic; a localization module only ever *populates* them with country-specific seed data and *adds* narrowly-scoped logic (e.g. a specific statutory-report wizard), not new core tables.

**Where the seams leak.** Community and forum evidence shows a recurring, specific failure mode: **"Error while loading the localization. You should probably update your localization app first,"** thrown when the generic accounting/invoicing app is upgraded ahead of the country module, or vice versa — i.e., the two are versioned somewhat independently and can drift out of sync across an upgrade. Users are advised to test upgrades on a staging copy first, precisely because there is no automated compatibility gate between core and package versions.
Source: https://www.odoo.com/forum/help-1/error-while-loading-the-localization-you-should-probably-update-your-localization-app-first-269733 and related threads in the same forum search — accessed 2026-09-10.

**Lesson for us:** package/core version drift is a *named, recurring* support issue for the most mature open l10n ecosystem in the market. Our package manifest must declare a core-contract version range, and the platform must refuse to enable a package against an incompatible core version rather than fail at runtime with a generic error.

---

### 2. Microsoft Dynamics 365 Business Central — country/regional availability and the extension model

**What "local functionality" contributes, and who ships it.** BC has a genuinely two-tier model:
- **First-party localizations**, built and maintained by Microsoft, currently covering a specific, named list of countries/regions (as of this pass: Austria, Belgium, Czechia, Denmark, Germany, Finland, France, Iceland, Italy, Netherlands, Norway, Spain, Sweden, Switzerland, UK in Europe; Canada, Mexico, US in North America; Australia, India, New Zealand in Asia-Pacific).
- **Partner-built localization apps**, published on Microsoft AppSource/"Marketplace," for every other country — explicitly built **on top of the international ("W1") base version** of BC, i.e., the same extension mechanism a third party uses for a vertical add-on is what a partner uses to localize an entire country.
Source: https://learn.microsoft.com/en-us/dynamics365/business-central/about-localization — accessed 2026-09-10 (Microsoft Learn, last updated 2026-01-19 per page metadata).

**How it's versioned/installed.** BC's extension model (AL language, compiled into `.app` packages) is the same mechanism for a localization, a vertical add-on, and a customer-specific customization ("per-tenant extension", PTE). Country/region is chosen when an environment is created and determines both the localization applied and the Azure region used. Language/UI translation is a separate, XLIFF-file-based pipeline layered on top of — not fused with — the fiscal localization, evidence that the two really can be decoupled (extension point #8 above).

**What stays in core.** The W1 base app is deliberately country-neutral at the code level; every jurisdiction's statutory behaviour (VAT, chart of accounts, standard reports) is added by an extension, whether Microsoft- or partner-authored — architecturally the cleanest precedent we found for "core has zero jurisdiction branches, everything is an installed extension," at real production scale.

**Where the seams leak.** Extensions are not isolated from each other: a per-tenant extension that fills a functionality gap by adding a **field to a standard Microsoft table** can later collide with a Microsoft or Marketplace update that adds an equivalent field to the same table — the documented failure is an upgrade that fails outright with "the field is already defined in the Base Application," with the only clean fix being to delete the PTE's data and schema before the Marketplace/core update can install. More generally, **non-compatible partner apps can block a tenant's upgrade to the next major BC version** if they fail to recompile against it.
Sources: https://demiliani.com/2021/03/01/dynamics-365-business-central-per-tenant-extensions-and-conflicts-with-standard-microsofts-fields/ and https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/upgrade/upgrade-pte-merge-conflict — accessed 2026-09-10.

**Lesson for us:** letting any package (country or otherwise) extend a *shared* core table by adding fields directly is exactly the mechanism that produces this failure class. Our contract should give packages their own extension tables/side-schemas keyed by the core aggregate's ID, not field-level injection into core tables — this is a stronger constraint than BC's, chosen specifically because BC's own docs show us the failure mode to avoid.

---

### 3. NetSuite — SuiteTax and the Tax Reporting Framework / International Tax Reports

**What a package contributes.** NetSuite's tax engine, **SuiteTax**, is itself a rules-based abstraction over "which tax applies" that replaced the older hard-coded tax-schedule model — i.e., NetSuite's own core already went through the "if country == X" → "pluggable rule engine" migration we are trying to avoid ever needing to do. On top of SuiteTax sits the **Tax Reporting Framework**, a foundational SuiteApp providing the permissions, setup, nexus configuration and e-filing submission plumbing common to *all* countries. Country-specific behaviour — localized VAT/GST returns, EU cross-border reports (Intrastat, EC Sales List), tax audit files — is added by installing a further **country or regional localization SuiteApp** (e.g., "EMEA Localization SuiteApp" must be installed before an individual EU country's SuiteApp).
Source: https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/chapter_1528262949.html — accessed 2026-09-10.

**How it's versioned/installed.** Country/regional tax SuiteApps are distributed as **"managed bundles"** — Oracle pushes updates (bug fixes, new features) automatically; the customer does not control the update cadence. The **International Tax Reports** SuiteApp specifically is gated to certain NetSuite editions only (OneWorld, International, UK, Australia, Japan) — evidence that even NetSuite's own localization catalog is not uniformly available across every SKU/edition.
Source: same NetSuite docs set, section on Tax Reporting Framework Localization Requirements — accessed 2026-09-10.

**What stays in core.** The Tax Reporting Framework and SuiteTax's rule engine are core-owned; a localization SuiteApp only supplies country-specific report templates, rate/nexus data and filing formats on top of that framework — the same "core defines the slot, package fills it" pattern as Odoo's CoA/fiscal-position split.

**Where the seams leak.** Because localization content ships as a managed bundle, an update can **silently reset locally-modified properties** the bundle owns — e.g. a custom field's validation/default properties revert to the bundle's shipped definition on every update, forcing the customer to re-apply their own changes after every bundle upgrade. This is a direct, documented instance of "installed package" and "tenant customization" fighting over ownership of the same object.
Source: https://docs.extendtech.net/general/understanding-why-fields-and-preferences-may-reset-during-netsuite-bundle-and-suiteapp-updates and NetSuite's own "Resolving Conflicting Objects" documentation — accessed 2026-09-10.

**Lesson for us:** a package must own a namespace/set of objects that a tenant customization can *extend* but never *mutate in place* — otherwise every package update becomes a silent regression. This directly informs extension point #10 (explicit ownership + compatibility contract, checked and enforced, not assumed).

---

### 4. SAP Business One — localization packages / country versions

**What a package contributes.** SAP calls this a **"local version"** — "a set of functionalities... designed for a specific country or region that adapts a global SAP system to meet local legal, regulatory, tax and business requirements." SAP Business One 10.0's Web Client alone lists 36 supported localizations spanning Europe, the Gulf, the Americas, and Oceania (e.g. Australia, Austria, Belgium, Canada, Czech Republic, Denmark, Egypt, Finland, France, Germany, Israel, Netherlands, New Zealand, Saudi Arabia, South Africa, Sweden, UK, US, and more).
Source: aggregated from SAP Help Portal search results — accessed 2026-09-10 (list reconstructed from search snippets, not a single fetched primary page this pass; treat the exact list as **UNVERIFIED** in detail, though the existence and approximate scale of ~35+ named country localizations is corroborated across multiple SAP Community/Help results).

**How it's chosen/installed — and the critical constraint.** SAP Business One's localization is **selected once, at company/database creation, and cannot be changed afterward.** Multiple independent SAP Community threads confirm this is a hard limitation: "Localization is set upon creation of Database/Company, and you cannot change this setting once the database has been created" — the only remedy for a wrong choice is to create a new company database with the correct localization and migrate data into it.
Source: https://community.sap.com/t5/enterprise-resource-planning-q-a/how-do-i-change-a-database-localization-in-sbo-8-81/qaq-p/7994485 and the associated community thread on "local settings during creation of a new company" — accessed 2026-09-10.

**What stays in core vs. what the package owns.** Not fully documented in the sources reached this pass (SAP's own localization guide returned HTTP 403 to automated fetch — **UNVERIFIED**, flagged for a follow-up pass with a different access method). Directionally, SAP B1's local versions are known in the partner ecosystem to cover chart of accounts templates, tax procedures, legal/statutory reports, and — in some countries (e.g. Israel) — mandatory fiscal add-ons enforced by local law.

**Where the seams leak.** The single clearest, best-evidenced seam in SAP B1 is architectural rather than a bug: **localization is a create-time, immutable decision.** For a multi-tenant SaaS ERP where a tenant might plausibly want to add a second country of operation later, or where we discover mid-implementation that a different package combination is needed, hard-coding this choice at creation time is the *opposite* of what we want. This is the strongest single negative lesson from the whole localization survey.

**Lesson for us:** Country Packages must be **installable and (where safe) removable after tenant creation**, not fixed at tenant provisioning. A tenant should be able to add a second Country Package (e.g., they start selling into a new country) without a database migration or re-creation. This should be an explicit, tested scenario, not an assumption.

---

## Cross-cutting lesson: what "core vs. package" boundary problems all four share

Despite very different architectures (Odoo's Python module system, BC's AL extensions, NetSuite's SuiteBundle/SuiteApp model, SAP B1's create-time local version), the same three failure classes recur:

1. **Version-drift between core and package** (Odoo's "update your localization app first" error; BC's PTE-vs-core-update field collisions).
2. **Ownership ambiguity when both a package and a tenant customization touch the same object** (NetSuite's bundle-update-resets-my-customization problem; BC's PTE field collision is the same problem from the other direction).
3. **An irreversible or hard-to-reverse initial choice** (SAP B1's create-time-only localization).

Our Country Package contract (see table above) is designed specifically to make each of these three failure classes structurally impossible rather than merely rare: explicit versioned compatibility contracts (fixes #1), packages extend via their own namespaced tables/side-schemas rather than mutating core or tenant-owned objects in place (fixes #2), and packages are installable/removable at any point in a tenant's life, not just at provisioning (fixes #3). This should be handed directly to the architect as input to the Country Package ADR.

---

## Sources (localization)
1. https://www.odoo.com/documentation/19.0/applications/finance/fiscal_localizations.html — accessed 2026-09-10
2. https://www.odoo.com/forum/help-1/error-while-loading-the-localization-you-should-probably-update-your-localization-app-first-269733 — accessed 2026-09-10
3. https://learn.microsoft.com/en-us/dynamics365/business-central/about-localization — accessed 2026-09-10
4. https://demiliani.com/2021/03/01/dynamics-365-business-central-per-tenant-extensions-and-conflicts-with-standard-microsofts-fields/ — accessed 2026-09-10
5. https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/upgrade/upgrade-pte-merge-conflict — accessed 2026-09-10
6. https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/chapter_1528262949.html — accessed 2026-09-10
7. https://docs.extendtech.net/general/understanding-why-fields-and-preferences-may-reset-during-netsuite-bundle-and-suiteapp-updates — accessed 2026-09-10
8. https://community.sap.com/t5/enterprise-resource-planning-q-a/how-do-i-change-a-database-localization-in-sbo-8-81/qaq-p/7994485 — accessed 2026-09-10
9. https://docs.peppol.eu/poacc/billing/3.0/bis/ — accessed 2026-09-10 (used for extension point #4's CIUS pattern, detailed further in `regulation/first-country-package.md`)

## What would change this
- A successful direct fetch of SAP's own localization architecture guide (blocked by 403 this pass) would let us confirm exactly what a B1 "local version" contributes vs. core, rather than relying on community-thread reconstruction.
- Direct evidence (not found this pass) on whether any incumbent supports **adding a second country's compliance profile to one already-live tenant** without a new database — this is the single biggest open question our own design needs to answer, since no incumbent surveyed clearly does it well (Odoo requires a separate *company*, not a second package on one company; BC's country is chosen per *environment*; SAP B1 is create-time-only for the whole database).
