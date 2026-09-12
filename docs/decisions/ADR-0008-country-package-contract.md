# ADR-0008 — The Country Package contract

- **Status:** Accepted (2026-09-11) — the *existence* of Country Packages is **locked by the product owner**; the contract is the architect's design
- **Deciders:** product owner (country-agnostic core), architect (contract)
- **Supersedes:** —
- **Superseded by:** **partially superseded by [ADR-0031](ADR-0031-country-package-contract-corrections.md) (2026-09-11)** — §3.1's "only core assembly" sentence, §7 row 10 (a bounded-range validity rule is added), §8.3 as it applies to `IStatutoryReportDefinition.VersionAsOf`, §9.3's description of what a package directory contains, and §10's ownership of `CountryPackageContractTests<TPackage>`. Every other clause of this ADR stands.
- **Amended by:** **[ADR-0033](ADR-0033-the-tenancy-trust-boundary-is-the-process.md) (2026-09-12)** — §9.4's statement that an `AssemblyLoadContext` is not a security boundary is correct and stands; ADR-0033 carries it the rest of the way for tenancy specifically (the threat, the control, the residual and what must demonstrate each), and **narrows where §9.3's trust levels take effect**: a process that can route tenants loads only packages whose signature establishes `FirstParty`. §9.2's "same thread, same transaction" is unchanged and is named there as the cost of ever moving to an out-of-process host. **§4 is amended by [ADR-0035](ADR-0035-installed-packages-defined-and-the-ddl-handle-confirmed.md) §2 (2026-09-12)**, which supplies the definition of the `InstalledPackages` set a `TenantScope` carries — §4 describes what a package contributes to a tenant database and never defined that set.
- **Related:** ADR-0004 (PostgreSQL), ADR-0007 (tenancy), ADR-0018 (retention), ADR-0021 (money and rounding), ADR-0023 (tax registration)
- **Primary input:** `../research/features/localization-packages.md` (ten recommended extension points, and the three failure classes every incumbent exhibits) and `../research/regulation/first-country-package.md` (New Zealand as the first reference package)

> The core must never contain `if (country == "SE")`. This ADR is what makes that statement enforceable rather than aspirational.

---

## 1. Context

`CLAUDE.md` locks a country-agnostic core with jurisdiction behaviour shipped as installable, versioned **Country Packages**. The researcher surveyed Odoo, Dynamics 365 Business Central, NetSuite and SAP Business One and found that all four converged on the same shape — a country-neutral core plus an installable localization unit — and all four leak in the same three places:

1. **Version drift between core and package.** Odoo's *"Error while loading the localization. You should probably update your localization app first"* is a named, recurring support issue: core and localization are versioned semi-independently with no automated compatibility gate.
2. **Ownership ambiguity when a package and a tenant customization touch the same object.** A NetSuite managed-bundle update silently resets locally-modified field properties. A BC per-tenant extension that adds a field to a standard Microsoft table collides with a later core update — *"the field is already defined in the Base Application"* — and the documented fix is to delete the extension's data and schema.
3. **An irreversible initial choice.** SAP Business One's localization is selected when the company database is created and cannot be changed afterwards; the only remedy is a new database and a data migration.

This ADR designs each of those three failure classes out, by construction. It is not an attempt to copy the incumbents' shape; it is an attempt to copy their shape and fix their seams.

---

## 2. Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Core branches per country, gated by configuration** | Simplest thing that could work; no loading, no signing, no contract versioning | Forbidden by `CLAUDE.md`; every new jurisdiction edits and re-tests core; the branch count multiplies with every rule; this is exactly the state NetSuite had to migrate *out of* when it built SuiteTax |
| **B. Data-only packages (no code): rates, templates and mappings as configuration rows** | Safest by far — no assembly loading, no signing, no trust problem, no ALC; upgrades are just data | Breaks on the first NZBN check digit and the first UBL mapping. An e-invoicing mapping expressed declaratively becomes a programming language we design, implement, debug and version — strictly worse than C#. Rejected as a *sole* mechanism, adopted as the *preferred* mechanism wherever data genuinely suffices |
| **C. Plugin assemblies with a manifest, owning their own database schema** *(chosen)* | Data where data suffices (rates, chart templates, retention), code where code is genuinely needed (checksums, format adapters, document mappings); a package owns a schema, so install/uninstall is a schema operation; the manifest carries an enforceable core-contract range; the same mechanism serves a first-party package today and a partner package later | We must build discovery, manifest validation, compatibility enforcement, loading, signing and per-package migrations; an `AssemblyLoadContext` is not a security boundary (§9.4) |
| **D. A service per country, called over the network** | Hard isolation; independent deploy | A network hop inside a posting transaction; N deployments per jurisdiction; it still needs exactly the contract in this ADR, plus latency, retries and partial failure. All of C's design work, none of C's simplicity |

**Decision: option C, with option B's discipline applied inside it.** The rule that keeps C honest: *a package contributes **data** unless the thing genuinely cannot be expressed as data.* §7 records, per extension point, whether it is data, code, or both.

---

## 3. What a package is: identity, manifest, versioning, discovery

### 3.1 Identity and the two version numbers that matter

A Country Package has a stable id (`aurora.country.nz`), a short **package key** used for its schema (`nz`), and a SemVer **package version** (`1.4.0`).

The core exposes a separate, deliberately slow-moving number: the **core contract version**, the version of the `Aurora.Countries.Contracts` assembly. It is *not* the product version. It changes only when an extension-point contract changes:

| Change | Bump |
|---|---|
| Remove or change the signature of a contract member; change the meaning of an existing member | **MAJOR** |
| Add a new extension point; add an optional member with a default implementation; add an enum member to a registry a package only reads | **MINOR** |
| Documentation, clarification, no public-surface change | **PATCH** |

~~`Aurora.Countries.Contracts` is the **only** core assembly a package may reference. An architecture fitness test asserts that no package assembly references `Aurora.*.Domain`, `Aurora.*.Infrastructure`, `Aurora.Web` or any module's internals.~~ **Superseded by ADR-0031 §2:** a package may reference the contract assembly **and the tier-0 assemblies the contract is expressed in** — `Aurora.SharedKernel` and `Aurora.Documents.Canonical` — and no other `Aurora.*` assembly. The rule runs from package metadata before any package code executes (`PackageAssemblyReferenceRule`), not as a build-time fitness test, so it also covers a package this repository did not build.

The contract assembly's public surface is guarded by an **approved-API snapshot test** (`PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`, via `Microsoft.CodeAnalysis.PublicApiAnalyzers`). Any change to the public surface fails the build until a developer moves the line into the approved file — which is the moment the SemVer bump is decided, consciously, by a human. This is the mechanism that stops core-contract drift from happening silently, which is failure class #1.

### 3.2 The manifest

Each package embeds `package.manifest.json` as an assembly resource:

```json
{
  "id": "aurora.country.nz",
  "key": "nz",
  "displayName": "New Zealand",
  "version": "1.4.0",
  "coreContractRange": "[2.0.0, 3.0.0)",
  "jurisdiction": { "countryCode": "NZ", "defaultCurrency": "NZD", "locales": ["en-NZ", "mi-NZ"] },
  "capabilities": [
    "ChartOfAccountsTemplate", "TaxRuleSet", "StatutoryReport", "EInvoicingProfile",
    "PaymentFileFormat", "StatementImportFormat", "InterchangeFormat",
    "IdentifierValidator", "LocalePack", "RetentionPolicy"
  ],
  "schema": "pkg_nz",
  "dependsOn": [ { "id": "aurora.region.anz", "range": "[1.0.0, 2.0.0)" } ],
  "conflictsWith": [],
  "localeOnly": false,
  "publisher": "Aurora",
  "trust": "FirstParty"
}
```

`coreContractRange` uses **NuGet version-range syntax** (`[2.0.0, 3.0.0)`), parsed by `NuGet.Versioning`. Chosen over inventing a range syntax because every .NET developer already reads it and the parser is a maintained, permissively licensed library.

**(ADR-0031 §1 confirms this section against §9.3: the embedded resource is the only copy; the package directory carries the assembly and `package.sig`, and no sidecar manifest.)** The manifest is read with `System.Reflection.MetadataLoadContext` — **metadata only, no code execution** — so the platform can validate compatibility, dependencies and signature *before* it ever runs a line of package code. A package's `ICountryPackage.Manifest` property must equal the embedded resource; a contract test enforces it, so the manifest cannot lie about the code.

### 3.3 Discovery

Packages live in `./packages/<id>/<version>/` in the deployed image (v1: first-party only, shipped with the release). At startup the platform enumerates the directory, reads each manifest via `MetadataLoadContext`, verifies signatures (§9), and builds an in-memory **package catalogue** of *available* packages. Nothing is loaded into an executable context until a tenant actually has the package installed.

`catalog.installed_package(tenant_id, package_id, version, state, installed_at, installed_by)` records what each tenant has. `state` ∈ `Installing | Active | Deactivated | UpgradePending | Failed`.

---

## 4. How a package contributes data — and why it never injects fields into core tables

> **Amended by [ADR-0035](ADR-0035-installed-packages-defined-and-the-ddl-handle-confirmed.md) §2 (2026-09-12).** ADR-0007 §3.4 cites *this section* for the `InstalledPackages` set a `TenantScope` carries, and this section does not define it — it is about what a package contributes to a tenant database. ADR-0035 §2 is the definition, and §2.3 carries a consequence that belongs to **this** ADR: `Entries`/`TryGet` return a package in any state, so capability and slot resolution reads `Active`/`TryGetActive`. Without that split a `Deactivated` package still fills a §6.1 slot and §5.3's deactivation switches nothing off. §2.2 also records why the set carries **text** rather than `PackageId`/`PackageVersion`: tenancy contracts may not reference the package contract (fitness rule L2), and coupling them would put a §3.1 MAJOR bump on tenancy's critical path.

This section exists because of failure class #2. Business Central's documented upgrade failure — a per-tenant extension adds a field to a standard table, a later core update adds an equivalent field, and the upgrade dies with *"the field is already defined in the Base Application"* — is caused by one specific design choice: letting an extension add columns to a table it does not own. We do not allow it.

### 4.1 The four ownership rules

**R1 — A package owns exactly one schema.** `pkg_<key>` in the tenant database (`pkg_nz`). It may create, alter and drop objects there and nowhere else. Country Packages own `pkg_<id>` schemas, as ADR-0004 rule 1 states.

**R2 — A package may never add, alter or drop a column, index, constraint, trigger or view in a core module schema.** Extension data goes in a **package-owned side table keyed by the core aggregate's id**:

```sql
create table pkg_nz.party_nzbn (
    party_id     uuid primary key,          -- the core Party aggregate's id. No FK. See R3.
    nzbn         char(13) not null,
    verified_at  timestamptz,
    constraint nzbn_is_gln check (nzbn ~ '^[0-9]{13}$')
);
```

Enforcement is threefold:
- **Build time:** a migration safety test generates each package migration's SQL and fails if it names any schema other than the package's own.
- **Install time:** the installer hashes the tenant's core schema DDL (a query over `information_schema.columns`, `.table_constraints` and `pg_indexes` for every non-`pkg_` schema) before and after running package migrations. Any difference aborts the install and rolls back the schema. A package that tries to touch core is caught on the first tenant, not on the hundredth upgrade.
- **Deployment:** package migrations run under a role granted `CREATE` on `pkg_<key>` and `USAGE` + `SELECT` on core schemas only.

**R3 — No foreign key from a package schema into a core schema.** This is deliberate and it costs us referential integrity, so the reasoning matters: an FK from `pkg_nz` into `sales.invoice` makes every future core `Contract` migration (ADR-0007 §7.2) fail on exactly those tenants that have the package installed — the Business Central failure, arriving from the other direction. Core must be able to evolve its own tables without knowing which packages exist. Integrity is maintained instead by:
- the core publishing `PartyArchived` / `SubjectErased` / `DocumentVoided` integration events that packages subscribe to (ADR-0015), and
- a nightly per-tenant **orphan sweep** job that reports (and, for non-financial rows, removes) package rows whose core id no longer exists.

**R4 — A package may only *add* rows to a core table through the declared seeding API, and every such row is tagged with its origin.** Core tables that accept contributed rows (`ledger.account`, `tax.tax_code`, `reporting.report_definition`, `platform.retention_rule`) carry:

```
source            text not null check (source in ('Core','Package','Tenant'))
source_package_id text
source_version    text
tenant_modified_at timestamptz
```

### 4.2 Three-way merge: how we avoid NetSuite's silent reset

NetSuite's managed bundles overwrite tenant-modified properties on every update. Our package upgrade **never silently overwrites a tenant's edit**:

| Row state | On package upgrade |
|---|---|
| `source='Package'`, `tenant_modified_at is null` | The new value is applied. The package owns it. |
| `source='Package'`, `tenant_modified_at is not null` | The new value is **not** applied. A `PackageUpdatePending` item is written with the old value, the tenant's value and the package's new value. A user with the `Package.Manage` permission resolves it explicitly. |
| `source='Tenant'` | Never touched by a package, ever. |
| `source='Core'` | Never touched by a package, ever. |

Uninstall-purge removes exactly the rows with `source='Package'` and this package's id that are unreferenced — nothing else. Because origin is recorded in the row, "what did this package add?" is a query, not an archaeology exercise.

### 4.3 Effective-dated data is append-only once used

A package upgrade **never edits a rule row whose validity period has started and which a posting references**. A rate change is a new row plus a close-out of the previous row's `valid_to` (§6). The seeding API exposes `AddVersion` and `CloseOut`; it has no `Update`. That is not a convention — the API literally lacks the method.

---

## 5. Install, upgrade, uninstall — and the compatibility gate

### 5.1 Install

Install is an **idempotent saga**, reusing ADR-0007 §8's machinery. It runs during provisioning (ADR-0007 §8, step 7) and, equally, against a live tenant at any later time — which is the fix for failure class #3.

| # | Step | Notes |
|---|---|---|
| 1 | Resolve version from the package catalogue | Highest version satisfying the request and all constraints |
| 2 | **Verify the signature** (§9) | Refuse unsigned or untrusted outside Development |
| 3 | **Check `coreContractRange` against the running core contract version** | Refuse with both versions named and the required package version stated |
| 4 | Check `dependsOn` and `conflictsWith` against the tenant's installed set | Refuse, naming the conflicting package |
| 5 | Check **slot conflicts**: does an already-active package fill the same slot for the same company? | Refuse; two packages may not both define, e.g., the statutory VAT return for one company |
| 6 | `CREATE SCHEMA pkg_<key>`; grant | Idempotent |
| 7 | Snapshot core DDL hash; run package migrations; re-snapshot and compare (R2) | Abort on difference |
| 8 | Run contribution seeding | Upsert by natural key, `source='Package'`, respecting §4.2 |
| 9 | Register capabilities; write `catalog.installed_package`; publish `CountryPackageInstalled` | Transactional in the catalog |

### 5.2 The gate that Odoo does not have: core upgrade

This is the mechanism that answers failure class #1, and it operates in two places.

**At release build time — the fleet compatibility report.** Before a release enters the migration waves of ADR-0007 §7.4, the build produces, for every `(package_id, version)` present anywhere in the fleet (a single catalog query), a verdict against the *target* core contract version. A release with unresolved incompatibilities **cannot enter the `general` wave**. The incompatibility is discovered on our build server, not on a customer's tenant.

**At tenant migration time — resolve packages first, and hold back rather than half-upgrade.** For each tenant, the runner:

1. Computes a **package upgrade plan**: for each installed package, the lowest version compatible with *both* the current and the target core contract version. (A package release is required to overlap both, which makes rolling upgrades possible.)
2. If a plan exists → upgrade packages, then migrate core, then run the packages' post-core migrations.
3. If no plan exists for some package → the tenant is `Skipped` with reason `PackageIncompatible`. **It stays on the old core version and keeps working.** It is reported, not broken.

Odoo's failure mode is that core and localization upgrade independently and meet in an inconsistent state at runtime. Ours cannot: a tenant is either fully on version N with compatible packages, or fully on N−1. There is no third state.

**Deprecation policy, so packages have time.** A MAJOR core-contract bump must be preceded by at least one MINOR release in which the outgoing member is `[Obsolete]` and both paths work, and by a **deprecation window of two releases or six months, whichever is longer**. Removing a contract member without that window is a release-blocking review failure.

### 5.3 Uninstall

Two operations, because they are not the same thing and conflating them destroys accounting records:

- **Deactivate** — always available. The package stops filling its slots; its data remains; historic documents still resolve their tax rates and account mappings. This is what a tenant that stops trading in a country actually does.
- **Purge** — drops `pkg_<key>` and removes this package's unreferenced `source='Package'` rows. **Blocked** if any posted document references package-contributed data, which in practice means it is available only to a tenant that installed a package and never traded under it. Attempting it returns a Problem Details response listing the referencing document types.

The honest statement: once a tenant has posted under a jurisdiction, that jurisdiction's data is part of their statutory record and is never deleted. Deactivate is the real operation; purge exists for the "installed it by mistake last Tuesday" case.

---

## 6. Roles, slots and effective-dated data

### 6.1 The core defines the slot; the package fills it

This is the single strongest idea in the incumbent survey, present in both of the disciplined examples (Odoo's chart-of-accounts + fiscal-position split, NetSuite's Tax Reporting Framework + SuiteTax). Concretely:

**Core owns an `AccountRole` registry** — `AccountsReceivableControl`, `AccountsPayableControl`, `OutputTaxPayable`, `InputTaxRecoverable`, `InventoryAsset`, `CostOfGoodsSold`, `FxGainLoss`, `RetainedEarnings`, `RoundingResidual` (ADR-0021), `Suspense`. Core posting logic asks:

```csharp
AccountId account = await accountResolver.ResolveAsync(AccountRole.OutputTaxPayable, companyId, ct);
```

Core never names an account number. A package's chart-of-accounts template supplies both the accounts and the role → account mapping. A fitness test bans account-number-shaped literals and any `CountryCode`/`ISO 3166` comparison in domain and application assemblies — that test is the mechanical form of "no `if (country == "SE")"`.

The same pattern applies to document numbering rules, statutory report slots, identifier kinds, payment file formats and retention rules: core declares the slot and its contract; the package fills it; core resolves by `(slot, company, date)`.

### 6.2 Effective dating, so a rate change is data and never a redeploy

Every package-contributed rule row is effective-dated with a half-open `daterange`, and overlaps are made **impossible by the database**, not by application code:

```sql
create extension if not exists btree_gist;

create table pkg_nz.tax_rate (
    id        uuid primary key,
    tax_code  text        not null,
    rate      numeric(9,6) not null,
    basis     text        not null,     -- e.g. 'LineNet', 'DocumentNet'
    validity  daterange   not null,
    source_version text   not null,
    exclude using gist (tax_code with =, validity with &&)
);
```

`btree_gist` is enabled per tenant database by the provisioner (ADR-0007 §8, step 3), because the `=` operator on `text` inside a GiST exclusion constraint requires it. This is the concrete realisation of the capability ADR-0004 cites.

Two rules follow and are tested:

- **Lookup is always as-of a business date, never `now()`.** `ITaxRuleProvider.ResolveAsync(taxCode, asOf: document.DocumentDate, ...)`. A fitness test bans `DateTime.Now`, `DateTime.UtcNow` and `DateTimeOffset.UtcNow` in domain and application assemblies (time comes from `TimeProvider`), and the tax contract has no overload without an explicit date. Re-printing a two-year-old invoice must reproduce the rate that applied then — this is the bug that effective dating exists to prevent, and it is very easy to reintroduce with a convenience overload.
- **A rate change ships as data.** A new `tax_rate` row plus a close-out of the predecessor. New rate, no redeploy, no core change, no package code change — only a package data version. Publishing a 2027 GST change is a package patch release.

Rounding basis, tax-on-tax composition, reverse-charge and exemption codes are effective-dated on the same pattern.

---

## 7. The ten extension points

Derived directly from `../research/features/localization-packages.md`. "Kind" records whether the extension point is satisfied by data, by code, or by both — this is the discipline that keeps option C from degenerating into "everything is code".

| # | Extension point | Contract (in `Aurora.Countries.Contracts`) | Kind | Owned data | Notes |
|---|---|---|---|---|---|
| 1 | Chart-of-accounts template | `IChartOfAccountsTemplate`, `IAccountRoleMapping` | Data | Seeds `ledger.account` with `source='Package'`, plus role mappings | Core references accounts by **role** only (§6.1) |
| 2 | Tax rule set | `ITaxRuleProvider`, `ITaxCategoryMapping` | Data | `pkg_<key>.tax_rate`, `tax_category`, exemption/reverse-charge codes | Effective-dated (§6.2). Core `ITaxEngine` evaluates; the package never computes tax itself. ADR-0023 |
| 3 | Statutory report definition | `IStatutoryReportDefinition`, `IFilingFormat` | Data + code | `pkg_<key>.report_definition`, box mappings | Boxes map to **account roles and tax categories**, never raw SQL — so a core schema change does not break a package's report. The filing transport is code |
| 4 | E-invoicing profile | `IEInvoicingProfile` (`Map`, `Validate`, `IdentifierScheme`, `Transport`) | Code | Validation rule sets | One canonical `Invoice`/`CreditNote` aggregate, many country profiles — the EN 16931 + CIUS pattern (PINT A-NZ is NZ/AU's). Core never learns UBL |
| 5 | Bank file formats | `IPaymentFileFormat`, `IStatementImportFormat` | Code | Format metadata | Adapters only. The payments domain model does not change per country |
| 6 | Accounting interchange | `IInterchangeFormat` (export + import) | Code | Schema resources | Used by tenant offboarding export (ADR-0007 §11.4). NZ has no mandatory national format, so the reference package implements the openly published OECD SAF-T schema — proving the seam without inventing a format |
| 7 | Identifier validator | `IIdentifierValidator` for `CompanyRegistrationNumber`, `TaxId`, `BankAccount` | Code | Test vectors | Core value objects `OrganizationNumber`, `TaxIdentifier`, `BankAccountNumber` delegate to the registered validator for the relevant jurisdiction and are **unvalidatable without one** — a party in a jurisdiction with no package gets a documented "unverified" state, never a silent pass |
| 8 | Locale pack | `ILocalePack` (resources, number/date/currency formats) | Data | `.resx`/`.po` resources, format overrides | **Shippable independently of fiscal logic** (`localeOnly: true` in the manifest) — the Business Central XLIFF lesson: translation and fiscal localization are separate concerns that are usually, wrongly, fused |
| 9 | Retention and archiving policy | `IRetentionPolicy` | Data | `platform.retention_rule` rows per document type | Minimum retention, immutability, and whether GDPR erasure is **deferred** until retention lapses (ADR-0007 §11.5, ADR-0018). Flagged UNVERIFIED against a primary legal source by the researcher; the contract shape is safe regardless because it is pure declaration |
| 10 | Core-contract compatibility declaration | `coreContractRange` in the manifest | Data | — | Enforced at install *and* at core upgrade (§5.2). This is the extension point that makes the other nine survivable over a decade. **ADR-0031 §3 adds a validity rule: the range must be bounded at both ends, and an unbounded range makes the manifest invalid** |

---

## 8. Multi-country tenants — the question research could not answer

### 8.1 The open question

The researcher found **no incumbent that cleanly supports adding a second country's compliance profile to an already-live tenant**: Odoo requires a separate *company*; Business Central's country is fixed per *environment*; SAP Business One's is fixed at database creation. There is no precedent to copy. This ADR decides it anyway, because leaving it undecided means the first module to touch tax picks an answer by accident.

### 8.2 Decision

**Packages are installed per tenant, activated per company.**

- A **tenant** is one customer with one database. Country Packages are installed into the *tenant* — their schema and data live in the tenant database.
- A **company** (legal entity) inside that tenant has exactly one **primary jurisdiction**, which determines its chart of accounts, its statutory reports and its filing obligations. That is where a package is *activated*.
- A tenant may therefore hold **companies in different countries**, each with its own activated package. This is in scope from day one, and it is the supported answer to "we started selling in Australia".
- **Installing a second Country Package into a live tenant is a supported, tested scenario** — not a migration, not a new database, no downtime. There is an explicit acceptance test for it (§10). This is the deliberate fix for the SAP B1 failure class.

### 8.3 What is out of scope for v1, and why the seam stays open

**One legal entity with statutory obligations in more than one jurisdiction simultaneously** — the EU OSS / multi-VAT-registration case — is **out of scope for v1 behaviour**, and **in scope structurally from day one**:

- `Company` has `ITaxRegistration` as a **1..N** relationship from the first migration (ADR-0023, and assumption A2 in `../architecture/overview.md` §6). The schema admits it.
- Every tax resolution and every statutory report slot is keyed by `(company, taxRegistration, asOfDate)` in the contract signature, even though v1 only ever passes the single primary registration. **ADR-0031 §4 settles what this binds: `IStatutoryReportDefinition.VersionAsOf` takes `(CompanyId, TaxRegistrationId, DateOnly)`. `ITaxCategoryMapping.DefaultCodeFor` is explicitly not decided there.**
- v1 refuses, at activation, to fill the same slot twice for one company (§5.1, step 5), with an error that names the limitation rather than producing a silently wrong return.

This is the **most reversible** option available: no schema migration is needed to enable it, no contract signature changes, and the work when it arrives is behaviour plus UI — not a data model rewrite. Building it now would mean designing a multi-jurisdiction tax determination engine against zero validated customer demand, which is how an SMB ERP acquires an enterprise-sized tax module nobody asked for.

The question and this recommendation are recorded in `../HUMAN_INBOX.md` (2026-09-11, Q6). **We do not wait for the answer** — the chosen option is the one that costs least to change if the human disagrees.

---

## 9. Loading and trust

### 9.1 Why an `AssemblyLoadContext` at all

Referencing packages at compile time would make "installable package" a lie: every package would be in every deployment, install would be a feature flag, and we would never discover whether the seam is real until a partner asked for one. Three concrete needs justify the machinery:

1. Two packages may need different versions of the same private dependency (a schematron runner, a fixed-format parser).
2. Deactivation should be able to **unload** a package — collectible `AssemblyLoadContext` gives us that.
3. A partner-authored package must one day be loadable without rebuilding the core (assumption A5).

This is the novelty in this ADR, and that is its written justification. Everything else here is a manifest, a schema and some `daterange` columns.

### 9.2 How loading works

- One **collectible `AssemblyLoadContext` per (package, version)**, with an `AssemblyDependencyResolver` over the package's `.deps.json`.
- `Aurora.Countries.Contracts` is resolved from the **default** context — never loaded into the package's context. Both sides must see the same `Type` identity or every cast fails at runtime with a baffling message. This is the classic plugin bug; the resolver explicitly returns `null` for the contracts assembly so the default context wins.
- Metadata inspection (manifest, signature, referenced assemblies) uses `MetadataLoadContext` and never executes package code.
- Package code runs on the same thread and the same transaction as the caller. It is **not** a separate process.

### 9.3 Signature verification

- ~~Each package directory contains the assembly, `package.manifest.json` and a **detached signature**~~ **Superseded by ADR-0031 §1:** each package directory contains the assembly and `package.sig`, a **detached signature** over the SHA-256 of the assembly file followed by the manifest bytes exactly as embedded in that assembly. There is no sidecar manifest; the embedded resource is the only copy.
- **ECDSA P-256 with SHA-256**, verified against a public key in the platform trust store, pinned by thumbprint in configuration. Chosen because it is entirely in the .NET base class library (`System.Security.Cryptography.ECDsa`) — **no third-party dependency, no licence question**. Authenticode is Windows-specific and we deploy Linux containers; strong naming is not a security feature and is explicitly not used for this.
- Trust levels: `FirstParty` (Aurora's key), `Partner` (a key added by an operator, recorded in `catalog.operator_audit_event`), `Unsigned` (refused unless `Packages:AllowUnsigned=true`, which the host refuses to honour outside the Development environment — asserted by a configuration test, because a flag that only a comment prevents from reaching production will reach production).
  > **Two amendments (2026-09-12).** [ADR-0039](ADR-0039-the-admission-floor-holds-in-every-environment.md) §2: this environment rule is the *weaker* of two. A host that **routes tenants** refuses `AllowUnsigned` in **every** environment including Development (ADR-0033 §5.2), and a trusted key marked `DevelopmentOnly` is refused outside Development in the same shape. ADR-0039 §4: the detached signature covers the manifest-bearing assembly **and, through the manifest's new `files` member, a SHA-256 for every file the package ships**; the load context resolves only from that list and a directory holding an unlisted file is refused. Without it, admission verified one file and executed a directory.
- Private keys are never in the repository. Signing happens in the release pipeline.

### 9.4 An `AssemblyLoadContext` is not a security boundary — stated plainly

> **Amended by [ADR-0033](ADR-0033-the-tenancy-trust-boundary-is-the-process.md) (2026-09-12).** This section is right and nothing in it is withdrawn. What it does not say is what follows for **tenancy**: a loaded package can reflect on `TenantScope`'s `internal` constructor and mint a valid scope for any tenant id, and ADR-0007 §4.3's stamp *agrees* with it, because the connection really does reach that tenant. ADR-0033 §4 records the checked position — .NET offers **no** in-process privilege boundary against a loaded assembly, and there is no sandbox to opt into — §5 names admission as the control and writes down the residual, and §5.6 says what must demonstrate both. Two precisions on this section's own wording: the collectible context gives version isolation **and** an *attempted* unload, which package code can prevent completing; and "first-party only in v1" becomes an enforceable admission floor rather than a policy sentence (ADR-0033 §5.2), which is a gate that **does not exist yet**.

Loaded package code runs with full trust in-process: it can read any file the process can read, open any socket, and reflect over anything. `AssemblyLoadContext` provides **version isolation and unloadability, not sandboxing**. Therefore:

- **v1 accepts first-party packages only.** They ship in the image and are reviewed like core code.
- A partner package would additionally require our code review and our signature.
- If we ever accept genuinely untrusted third-party code, the isolation must be a **process or container boundary** with a network contract — that is a new ADR, and it is a much larger piece of work than it looks. Nobody should read §9.1 and conclude we have a plugin sandbox.

---

## 10. Testing a package

Every package inherits `CountryPackageContractTests<TPackage>` — the same "shared base class, enforced by a fitness test" approach as the tenant isolation contract (ADR-0007 §12). **The base class does not exist yet: ADR-0031 §5 gives it an owner (a new bootstrap row after B-13.2, in `tests/Aurora.Countries.TestKit`). Until that row lands, this table is a specification and nothing enforces it.**

| Test | Asserts |
|---|---|
| `Manifest_matches_assembly` | Embedded manifest equals `ICountryPackage.Manifest`; schema name equals `pkg_<key>` |
| `Migrations_touch_only_own_schema` | Generated SQL names no other schema (§4.1 R2) |
| `Install_does_not_alter_core_ddl` | Core DDL hash unchanged across a real install (§5.1 step 7) |
| `All_declared_capabilities_resolve` | Every capability in the manifest returns a working implementation |
| `Effective_dated_rows_have_no_gaps_or_overlaps` | Over the package's declared coverage period; overlap is already impossible in the database, gaps are not |
| `Identifier_validator_matches_published_vectors` | Known-good and known-bad values from the public specification (for NZ: the NZBN/GS1 GLN check digit) |
| `EInvoice_output_validates` | Against the published XSD offline, plus golden-file tests for the CIUS business rules |
| `Install_uninstall_round_trip` | Against a real PostgreSQL database (Testcontainers) |
| `Install_into_a_live_tenant_with_existing_data` | The §8.2 scenario: provision with package A, post documents, then install package B — no data migration, no downtime, package A's historic resolutions unchanged |
| `Core_contract_range_is_enforced` | Install is refused against an out-of-range core contract version, with both versions in the message |

The first reference package is **New Zealand**, on the researcher's recommendation: flat 15% GST, a publicly documented NZBN check digit with a free unauthenticated lookup API, the openly published PINT A-NZ Peppol profile, and IRD Gateway Services with self-service sandbox registration and no accreditation gate. Its purpose is to prove these ten extension points are real. Australia is the natural second package and the real test of whether the contract *composes*.

---

## 11. Consequences

**Positive**

- The three failure classes the researcher found in every incumbent are designed out: version drift by the enforced `coreContractRange` plus the fleet compatibility report (§5.2); ownership ambiguity by package-owned schemas, side tables and three-way merge (§4); irreversible initial choice by install-into-a-live-tenant as a first-class, tested operation (§5.1, §8.2).
- A tax rate change is a package data release. No core change, no code review of core, no fleet redeploy.
- Because core resolves accounts by role and taxes by contract, a fitness test can mechanically assert the absence of jurisdiction branches in core. "No `if (country == ...)`" becomes a failing build rather than a code-review opinion.
- Uninstall, export and audit of "what did this package contribute?" are all queries, because origin is recorded per row.

**Negative, and owned**

- **This is real platform machinery**: manifest, catalogue, signature verification, ALC loading, per-package migrations, the compatibility gate, the merge engine. It must exist before the first business module can post tax correctly, which makes it a bootstrap-milestone cost, not a later one.
- **No foreign keys from package schemas into core** (§4.1 R3) trades referential integrity for core evolvability. The orphan sweep is a compensating control, not an equivalent one. This is a genuine and deliberate loss.
- **The contract surface is now a versioned public API.** Every change costs a deliberate SemVer decision and possibly a six-month deprecation window. That is the point, and it is also friction on every future change to an extension point. Keep the contract small.
- **An ALC is not a sandbox** (§9.4). We carry the trust decision in process, mitigated only by "first-party only" in v1.
- **Slot conflicts are refused, not merged.** A tenant that genuinely needs two packages contributing to one slot for one company will hit a wall. That is §8.3's out-of-scope boundary, made visible rather than silently mis-computed.
- Declarative statutory-report box mappings (§7, point 3) are a small DSL. It is deliberately restricted to account roles and tax categories to stop it growing into a query language; if a report cannot be expressed that way, the answer is a new core reporting primitive, not a more powerful DSL.

---

## 12. Revisit when

- A partner asks to ship a package we do not build (activates §9's trust levels and forces the sandboxing question in §9.4).
- A single legal entity needs statutory obligations in more than one jurisdiction — §8.3's deferred case; the human may already have answered `HUMAN_INBOX` Q6 by then.
- The core contract requires its first MAJOR bump — that release is the real test of §5.2 and should be run as a rehearsal against the fleet compatibility report before it ships.
- More than roughly ten packages exist, at which point the package catalogue probably needs a real feed with version resolution rather than a directory scan.
- A jurisdiction requires behaviour that none of the ten extension points admits. Add an eleventh extension point with a MINOR bump; **do not** add a branch to core.
