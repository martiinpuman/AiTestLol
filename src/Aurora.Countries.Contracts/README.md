# Aurora.Countries.Contracts

Tier 0 (`docs/architecture/modules.md` §3). The ten extension points a Country Package implements,
and the values those extension points are expressed in. **A package may reference this assembly and
the two tier-0 assemblies it is expressed in — `Aurora.SharedKernel` and
`Aurora.Documents.Canonical` — and no other `Aurora.*` assembly.** The mechanism is
`PackageAssemblyReferenceRule` in `Aurora.Countries.Hosting`: an allowlist of those three names,
checked against the package's reference table from metadata before any of its code runs. ADR-0008
§3.1 names only this assembly; `Money`, `DateRange` and the canonical documents the extension points
are stated in live in the other two, so the rule as enforced admits those as well.

Its public surface is a versioned contract. `PublicAPI.Shipped.txt` is the approved snapshot;
`Microsoft.CodeAnalysis.PublicApiAnalyzers` fails the build (RS0016) on any public member missing
from it, and `scripts/approve-contract-api.sh` is the deliberate step that puts one there — which is
the moment a human decides the SemVer bump. `ContractPublicApiTests` compares the file with the
assembly **at type level** in both directions, so a new public type or a line naming a type that no
longer exists fails whether or not the analyzer is still in the build. Member-level drift is the
analyzer's job and only the analyzer's: remove the package and a changed signature stops being
caught, which is why the test also asserts the file still holds the shape of a real snapshot.

## The core contract version

`<Version>` in the `.csproj` **is** the core contract version of ADR-0008 §3.1, read back at runtime
by `CoreContract.Version`. One number, one place. It is not the product version, and it moves only
when an extension point changes:

| Change | Bump |
|---|---|
| Remove or change a contract member; change what one means | MAJOR |
| Add an extension point, an optional member, or an enum member a package only reads | MINOR |
| Documentation | PATCH |

A MAJOR bump reaches every package in the fleet and costs a deprecation window (ADR-0008 §5.2). Keep
the contract small.

## The ten extension points

Each is marked `[ExtensionPoint(capability)]`, and `CountryPackageCapabilities` builds its map by
reading those attributes — so the map cannot drift from the interfaces, and a capability with no
interface or two fails at startup rather than at an install.

| # | Capability | Interface | Kind |
|---|---|---|---|
| 1 | `ChartOfAccountsTemplate` | `IChartOfAccountsTemplate` (+ `IAccountRoleMapping`) | Data |
| 2 | `TaxRuleSet` | `ITaxRuleProvider` (+ `ITaxCategoryMapping`) | Data |
| 3 | `StatutoryReport` | `IStatutoryReportDefinition` (+ `IFilingFormat`) | Data + code |
| 4 | `EInvoicingProfile` | `IEInvoicingProfile<TDocument>` | Code |
| 5 | `PaymentFileFormat` | `IPaymentFileFormat` | Code |
| 6 | `StatementImportFormat` | `IStatementImportFormat` | Code |
| 7 | `InterchangeFormat` | `IInterchangeFormat` | Code |
| 8 | `IdentifierValidator` | `IIdentifierValidator` | Code |
| 9 | `LocalePack` | `ILocalePack` | Data |
| 10 | `RetentionPolicy` | `IRetentionPolicy` | Data |

The eleventh extension point of ADR-0008 §7 is `coreContractRange` in the manifest. It is data, not
an interface, and it is the one that makes the other ten survivable over a decade.

## Four shapes worth knowing before changing anything here

**Core defines the slot; the package fills it.** `AccountRole`, `TaxCategory`, `StatutoryReportSlot`
and `RecordCategory` are core registries. Core asks for `AccountRole.OutputTaxPayable`; it never
names 2200. This is what makes "the core must never contain `if (country == "SE")`" mechanical
rather than aspirational, and `ExtensionPointRegistryTests` asserts that no extension point even
takes a country code as an argument.

**Effective dating is checked, not remembered.** A type marked `[EffectiveDated]` was true over a
stated period and may not be true now. `EffectiveDatingTests` reflects over this assembly and fails
on any interface method that resolves a single marked value without taking a `DateOnly`. The thing
it is guarding against is a convenience overload added in good faith three modules away, which
reprices a two-year-old invoice at today's rate the first time somebody reprints it.

**A package-supplied rate is a caller-supplied factor.** `TaxRate` refuses anything that does not fit
`numeric(9,6)`, and `TaxRate.ApplyTo` never multiplies two decimals: it takes both operands apart
into whole numbers, works in `BigInteger`, rounds once at the currency's minor unit under the
package's own `MidpointRounding` rule, and checks the result is payable before returning it. The
*scale* is not the package's to choose — tax that is not a whole number of minor units cannot be
paid, and a rule declared at two decimal places would be wrong for a company trading in yen.
Two tests separate it from the obvious implementation: the pinned example
`TaxRateTests.A_product_too_precise_for_decimal_does_not_round_a_cent_into_existence` — one cent, on
one line, from an amount nobody would call unusual — and the constructed-boundary property in
`TaxRatePropertyTests`, which derives amounts one ulp short of a half minor unit and fails the
obvious implementation on its first case. The random properties there cannot reach that region, and
the file says so.

**The report box mapping is a closed hierarchy.** `ReportBoxSource` has a `private protected`
constructor and three nested sealed cases, so a package cannot add a fourth. ADR-0008 §11 wants the
box DSL restricted to account roles and tax categories to stop it growing into a query language; the
type system is what restricts it.

## Testing a package against this contract

`tests/unit/Aurora.Countries.Contracts.UnitTests` covers the contract itself. A package's own tests
belong with the package (ADR-0008 §10), and `tests/fixtures/Aurora.Countries.TestPackage` is a
worked example of what a package looks like: a manifest embedded as an assembly resource, one public
`ICountryPackage`, and extension points that can actually refuse something.
