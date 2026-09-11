# ADR-0030 — Country Package contract: manifest location, the reference allowlist, bounded core-contract ranges, registration-keyed report resolution, and who owns the package contract tests

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Supersedes:** five clauses of ADR-0008 — §3.1's "only core assembly" sentence, §9.3's first bullet where it disagrees with §3.2 about where the manifest lives, §7 row 10 (which now carries a validity rule), §8.3's second bullet as it applies to `IStatutoryReportDefinition`, and §10's silence about who builds `CountryPackageContractTests<TPackage>`. **Everything else in ADR-0008 stands**, including its option C, the four ownership rules of §4, the three-way merge, §5's install/upgrade/uninstall design and §9.4's statement that an `AssemblyLoadContext` is not a security boundary.
- **Superseded by:** —
- **Related:** ADR-0008, ADR-0023 §1, ADR-0021 §3, `../architecture/modules.md` §3, `../architecture/solution-layout.md` §6.2, `../reviews/B-12.md` ("To route — to the architect", items 1–5)

## Context

B-12 built `Aurora.Countries.Contracts` and `Aurora.Countries.Hosting`. Its review routed five questions here, and they share one shape: ADR-0008 was written before any of it existed, and in each of the five places the **implementation is the more careful document**. Three of the five are ADR sentences that no mechanism produces — the defect class `CLAUDE.md`'s self-check #1 names — and two are open questions where a developer is currently blocked on an architect's answer.

Each answer below names the mechanism that would show it working and where that demonstration lives. Where there is no such mechanism today, that is written down as an open limitation instead of as a claim.

## Decision

### 1. The manifest is embedded. The package directory carries the assembly and a detached signature, and no second copy of the manifest

ADR-0008 **§3.2 wins**: `package.manifest.json` is an embedded assembly resource. §9.3's *"Each package directory contains the assembly, `package.manifest.json` and a detached signature"* is corrected to: **the assembly and `package.sig`**.

Two copies of one document inside one package is a disagreement waiting to happen, and a sidecar can be edited without touching the assembly it describes — which is the swap the compatibility gate exists to refuse. Reading the embedded resource through `MetadataLoadContext` means the bytes the host validates are the bytes that ship inside the code.

**The signed content** is SHA-256 of the assembly file, followed by the manifest bytes exactly as embedded (`PackageSignature.ContentToSign`). §9.3's description of "which bytes are signed" is thereby pinned.

**A precision that keeps the sentence honest:** with the manifest embedded, the manifest bytes are already inside the hashed assembly, so it is the **assembly hash** that binds manifest to code — not the appended copy. The append is kept because it makes "what was signed" well defined without reference to where the manifest came from, and it costs nothing. Nobody should read §9.3 and conclude the append is what stops a manifest swap.

*Demonstrations that exist:* `PackageSignatureVerifierTests.A_package_changed_after_it_was_signed_is_refused` and `.The_signed_content_covers_both_the_assembly_and_the_manifest`.

*Demonstration that does not exist yet, recorded as open:* §3.2 says `ICountryPackage.Manifest` must equal the embedded resource and that *"a contract test enforces it"*. The only fixture today parses its own embedded manifest, so code and resource cannot disagree and the check cannot fail (`../reviews/B-12.md` m2). Until a second fixture whose `Manifest` property is hand-built and wrong in one field exists, **that sentence of §3.2 is a specification, not an enforcement**.

### 2. A package may reference the contract assembly and the tier-0 assemblies the contract is expressed in — three, not one

ADR-0008 §3.1's *"`Aurora.Countries.Contracts` is the **only** core assembly a package may reference"* could never have been true: the extension points hand packages `Money`, `DateRange`, `Result`, `CompanyId` and `TaxRegistrationId`, so a package cannot implement `ITaxRuleProvider` without naming `Aurora.SharedKernel`. The implementation's allowlist — `Aurora.Countries.Contracts`, `Aurora.SharedKernel`, `Aurora.Documents.Canonical` — is correct, and the sentence is replaced by it.

**The allowlist is derived, not chosen.** It is exactly *{the contract assembly} ∪ {the `Aurora.*` projects `Aurora.Countries.Contracts` declares a `ProjectReference` to}*. Stating it that way makes it checkable: a test parses that `.csproj` and asserts `PackageAssemblyReferenceRule.AllowedAuroraAssemblies` equals those references plus the contract itself. It fails when core takes a new dependency into the contract — the moment somebody must decide whether the package trust boundary really should widen — and it fails when the allowlist grows past what the contract needs. Today that `.csproj` references exactly `Aurora.SharedKernel` and `Aurora.Documents.Canonical`, so the rule holds as written.

**Read the `.csproj`, not the compiled assembly**, and this is not a style preference. `Aurora.Documents.Canonical` is an **empty project** today, so the contract uses none of its types, so the C# compiler does not emit an assembly reference for it: the built `Aurora.Countries.Contracts.dll` names `Aurora.SharedKernel` and does **not** name `Aurora.Documents.Canonical` (verified on the B-12 branch's own build output, 2026-09-11). A derived rule reading the compiled reference table would compute a two-element set and fail against a correct three-element allowlist. The forward-looking entry is exactly the case that breaks the tempting version of the rule.

**Where the rule runs.** From package metadata, before any package code executes (`PackageAssemblyReferenceRule` via `CountryPackageLoader`). That is strictly stronger than the build-time fitness test §3.1 asked for, because it also covers a package this repository did not build. The fitness test is therefore **not** additionally required; if someone writes one anyway, it must not become the only enforcement, because a build-time test cannot see a package that arrives as a file.

*Demonstration that exists:* `PackageMetadataReaderTests.The_assemblies_a_package_references_are_read_and_checked` — for a **compliant** package.

*Limitation of the mechanism, stated rather than discovered later:* `PackageMetadataReader` reads the references the compiler **emitted**, which are the ones the package actually uses. A package that declares a forbidden `Aurora.*` reference and never uses a type from it is invisible to the rule — and harmless for the same reason, since nothing links against it. A package reaching into core by reflecting over assembly names at runtime is outside what any metadata rule can see; ADR-0008 §9.4 already says why an `AssemblyLoadContext` is not the boundary that would stop it.

*Demonstration that does not exist yet:* nothing exercises the refusal path. A rule that has never been watched refusing is not a rule. Required: a second fixture package that **uses a type from** an `Aurora.*` assembly not on the allowlist — a throwaway fixture assembly with one type is enough, and it must be used, or the compiler emits no reference and the fixture proves nothing — and a test asserting the loader refuses it **before** loading, naming the assembly.

### 3. `coreContractRange` must be bounded at both ends, and an unbounded range is an invalid manifest

The reviewer's assumption in `../reviews/B-12.md` M2 is **correct**; the rework should proceed on it.

`VersionRange.TryParse` accepts `"1.0.0"`, `"[1.0.0, )"` and `"(, )"` as ranges with no upper bound, and they satisfy every future version. Extension point 10 (§7 row 10) exists to make version drift *refusable*; a range that admits every future MAJOR refuses nothing. A MAJOR core-contract bump is defined in §3.1 as removing a member or changing its signature — a package cannot have been verified against a contract version that did not exist when it was built, so an open range is not a claim anybody could have checked. §5.2's fleet compatibility report asks, per `(package_id, version)`, for a verdict against a **target** contract version; with open ranges every verdict is "compatible" and the report becomes a green-light generator.

**The rule:** a `coreContractRange` lacking either bound makes the manifest **invalid** — not merely incompatible. Validity is a property of the manifest and does not depend on which core happens to be running, so it is enforced **where the manifest is constructed**, and an unbounded range never reaches the package catalogue. `CoreContractGate` keeps its own check with the same message, because the gate also runs over catalogue manifests during the §5.2 fleet report and over a *target* version the process is not running. The lower bound is required for the same reason as the upper one: `"(, 3.0.0)"` claims compatibility with contract versions that predate the extension point. An exact pin (`[1.4.0]`) is bounded and legal. The failure names the package, the declared range and a bounded form.

*Demonstration:* `"1.0.0"`, `"[1.0.0, )"` and `"(, )"` are each refused, naming the field — and `CoreContractGateTests`' currently-**passing** `("1.0.0", "1.4.0")` row becomes a refusal case. That row is the evidence the open range is approved behaviour today, so changing it is the check that the rule went in.

**The consequence we accept:** every MAJOR core-contract bump forces a new release of every package, even one that used nothing that changed. That is the price of the gate Odoo does not have, and §5.2's deprecation window — one MINOR release with the outgoing member `[Obsolete]`, and two releases or six months, whichever is longer — is what makes it payable.

### 4. §8.3 binds `IStatutoryReportDefinition.VersionAsOf`

Yes. The signature becomes:

```csharp
Result<StatutoryReportVersion> VersionAsOf(CompanyId company, TaxRegistrationId registration, DateOnly asOf);
```

§8.3's promise is that the multi-registration case is **behaviour and UI later, never a contract change**. `ITaxRuleProvider.Resolve` already takes `(TaxCode?, CompanyId, TaxRegistrationId, DateOnly)` and `StatutoryReportResult` already carries `Company` and `Registration`; the definition is the one place in the statutory-report slot that does not, which makes §8.3 false for exactly one member. Adding the pair now costs nothing — no package exists in any fleet — and adding it later costs a MAJOR bump with a deprecation window, on the day a registration scheme changes which version of a return is filed.

**v1 implementations are expected to ignore both new parameters.** That is the point: the parameters are the seam, not a feature.

**This is a pre-release change to an unshipped contract.** `Aurora.Countries.Contracts` is at `1.0.0`, nothing has installed it, and no `[Obsolete]` window is owed. `PublicAPI.Shipped.txt` moves with it. From the first shipped package onward, the same edit would be a MAJOR bump.

*Demonstration:* an explicit **registry** in the contract test project naming which extension-point members are registration-keyed slots. Every member in the registry must take `(CompanyId, TaxRegistrationId, DateOnly)`, and every `[ExtensionPoint]` member taking a `DateOnly asOf` must appear either in the registry or in a written exception list carrying a reason and an owner — so a new effective-dated member cannot be added without someone deciding which it is. The rule needs a fixture interface written to break it, for the reason `../reviews/B-12.md` m7 gives about the country-code rule: a reflection rule with no violating fixture is a rule nobody has seen fail.

**Explicitly not decided here:** `ITaxCategoryMapping.DefaultCodeFor(TaxCategory, DateOnly)` is the other effective-dated member without the pair. Whether §8.3 binds it is a separate question; until it is answered it goes in the exception list with the follow-up id, which is what stops the registry from quietly blessing it.

### 5. `CountryPackageContractTests<TPackage>` is owned by a new bootstrap row, in its own test-support assembly

**Where it lives:** `tests/Aurora.Countries.TestKit`, a new assembly. It mirrors what `Aurora.TestKit` does for `TenantIsolationContract<T>` (ADR-0007 §12, built by B-10) but stays separate from it, because `Aurora.TestKit` is referenced by every module's integration-test project and must not drag the package host and the contract assembly into all of them.

**Who builds it:** a new bootstrap row, specified in `../architecture/solution-layout.md` §6.2 for the project-manager to transcribe, depending on **B-13.2** (install) and B-12. It is not part of B-12: three of §10's ten cases — `Install_does_not_alter_core_ddl`, `Install_uninstall_round_trip`, `Install_into_a_live_tenant_with_existing_data` — need a real installer and a real database.

**Built whole, not in halves.** The metadata-only cases could be written today, and that is the trap: the first package would subclass a base that proves the easy half, and nobody would come back for the three cases that carry the risk.

**What keeps it from being optional:** a fitness rule asserting every package assembly under `src/packages/` has exactly one subclass of `CountryPackageContractTests<>` — the same shape as rule T7 for `TenantIsolationContract<>`. It is inert until the first package project exists, so it is registered in B-04's `Inert` table with this row's id as the task that brings its subject (ADR-0029's inventory convention), with an inertness guard that fails when a package assembly appears without a subclass.

**Until that row lands, ADR-0008 §10's table is a specification and nothing enforces it.** Written down here so that nobody reads §10 as a description of tests that exist.

## Consequences

- Positive: four documents that disagreed with the code now agree with it, and the two open questions blocking the B-12 rework are answered before it merges rather than after.
- Positive: the reference allowlist stops being a hand-maintained constant and becomes a derived set with a test that fails in both directions.
- Positive: the bounded-range rule closes the front door the gate was built to guard, at the cost of one refusal case in a test that currently approves the open range.
- Negative: one contract signature changes (`VersionAsOf`), which means `PublicAPI.Shipped.txt` churn and a rework edit in flight. Free today, expensive from the first shipped package — which is the whole argument for doing it now.
- Negative: three demonstrations named here do not exist yet (the manifest-disagreement fixture, the reference-rule refusal fixture, the registration-keyed registry and its violating fixture). They are named as required work rather than described as done; a reader who takes this ADR as a statement of current coverage would be wrong, and this sentence is here so they do not.
- Negative: `CountryPackageContractTests<TPackage>` arrives later than the first package author would like, because it queues behind B-13.2.

## Revisit when

A package that is not first-party is admitted (§9.4's trust boundary changes, and the reference allowlist becomes a security control rather than a compatibility one); or the first MAJOR core-contract bump is proposed, at which point the bounded-range rule's cost lands in full and §5.2's deprecation window is exercised for real for the first time.
