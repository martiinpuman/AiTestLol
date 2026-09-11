# ADR-0030 — Architecture fitness rules are read from IL metadata and project files; ArchUnitNET is withdrawn

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Supersedes:** the **Architecture tests** row of ADR-0020's options table (the choice of `TngTech.ArchUnitNET`), the ArchUnitNET bullet in its Consequences, and the ArchUnitNET clause of its *Revisit when*. **Every other row and both standing rules of ADR-0020 are unchanged.** Wherever an earlier document names "an ArchUnitNET test" as the enforcement mechanism — ADR-0005 rule 1, `../architecture/overview.md` §4 row 4, `../architecture/testing-strategy.md` §5 — read "an architecture fitness test in `tests/Aurora.Architecture.Tests`".
- **Superseded by:** —
- **Related:** ADR-0020 (test tooling), ADR-0026 (dependency supply-chain gate), `../architecture/testing-strategy.md` §5, `../architecture/solution-layout.md` §6 row B-04, `../architecture/dependencies.md` §3 and §5, `../reviews/B-04.md`, `../reviews/B-03.md` finding m-1

## Context

ADR-0020 chose `TngTech.ArchUnitNET` 0.13.4 for architecture tests on 2026-09-11 and, in the same table cell, wrote that *"source-level rules … are Roslyn analyzers/tests, because reflection cannot see them"*. B-04 then built the first rule set and put the split in a different place: the eleven rules that govern compiled code read **IL metadata** with `System.Reflection.Metadata`, and the rules that govern the project graph parse `.csproj` and `packages.lock.json` as XML/JSON. Nothing in the solution references ArchUnitNET.

Two facts made that the right build and the wrong repository state.

**The rule the bootstrap plan actually asks for is a method-body rule.** `docs/reviews/B-03.md` m-1 recorded a method with `decimal` in and `decimal` out that computed through a `double` local and a `(double)` cast. `testing-strategy.md` §5.4 states F1 as *"no `double` or `float` field, property, parameter or return type"* — a **signature** rule, which that method satisfies — while `solution-layout.md` §6 row B-04 requires the rule to catch the local and the cast, or to be renamed to what it inspects. In IL those are a `[Local] System.Double` entry, a `conv.r8` and an `ldc.r8`. The B-04 reviewer planted exactly that method in `src/Aurora.SharedKernel`, rebuilt, and recorded five violations at four distinct sites (`../reviews/B-04.md` §1, row F1).

**The ADR, the strategy document, the dependency list and `Directory.Packages.props` all still name ArchUnitNET, and nothing consumes it.** The repository is the team's only memory, so the next developer writing a rule reads ADR-0020, adds the package, and the solution carries two mechanisms for one job. That is the defect this ADR closes.

**What this ADR does not claim.** It does not claim ArchUnitNET *could not* express F1. Nobody on this project has tested that, either way, and an ADR that asserts an untested limitation is the same defect class as the documents it is correcting. The claim made here is narrower and is demonstrated: the mechanism that exists in the tree fires on the violations the plan names, needs no dependency, and is the only mechanism with a consumer.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Rules over IL metadata (`System.Reflection.Metadata`) for compiled code, plus direct parsing of `.csproj` / `packages.lock.json` for the project graph** *(chosen)* | In-box: no package, no licence question, no pre-1.0 version under the gate. Sees method bodies, so F1 catches the B-03 shape. Reads the project graph as text, which is where a forbidden `ProjectReference` or an undeclared `PackageReference` actually lives — a compiled-type model never sees an *unused* reference, and B-04 planted an unused `Microsoft.EntityFrameworkCore` reference on a `.Domain` project and watched L1 fire on both the `.csproj` and the lock file | We own ~720 lines of scanner. A rule is written against a lower-level API than a fluent DSL, so each rule costs more to write and must state what it examined |
| B. Keep ArchUnitNET and add a second mechanism for body-level and project-graph rules | Fluent rule model for the type rules | Two mechanisms for one job, with the boundary between them re-litigated at every new rule; a pre-1.0 dependency under the quality gate; and the second mechanism is the one that catches the defect that actually escaped |
| C. Roslyn source analysis for everything | Sees syntax, comments and `.razor` | Every rule pays for a compilation; rules over *built* artefacts (assembly identity, lock files) are not source questions at all; and the population becomes "files we globbed" rather than "assemblies the build produced", which is the guard that caught a stale build in B-04 |

## Decision

1. **Rules whose subject is compiled code read IL metadata** with `System.Reflection.Metadata`, over the assemblies the build produced, in `tests/Aurora.Architecture.Tests`.
2. **Rules whose subject is not compiled** read the artefact that holds it: `.csproj` and `packages.lock.json` for reference rules today; a rule over `.razor` markup or over generated migration SQL will read those files. Choosing Roslyn for a future source rule is a decision for the ADR that introduces that rule, not a standing commitment here.
3. **`TngTech.ArchUnitNET` and `TngTech.ArchUnitNET.xUnit` are not approved dependencies.** They are removed from `dependencies.md` §3, recorded in §5.1 as withdrawn, added to the machine-readable rejection block, and their `PackageVersion` pins are removed from `Directory.Packages.props` by the task named in `solution-layout.md` §6.4.
4. **Reintroducing a type-model rule library needs a new ADR**, and that ADR must name the rule it exists for. "It would be nicer to write" is not a reason to carry a dependency under the quality gate.

### What demonstrates each part of this, and where the demonstration lives

| Claim | What would show it false | Where the demonstration lives |
|---|---|---|
| Every live rule can fail | Plant the violation the rule names in real `src/` code, rebuild, watch the rule go red, revert | `../reviews/B-04.md` §1 — eleven planted violations across F1, S3, T1–T6, L1, L1-L5 and M1, each reverted and the suite re-run green. Standing form: the deliberately-violating fixtures in `tests/Aurora.Architecture.Tests/Fixtures/` exercised by `TenancyRuleTests` and `LayeringRuleTests` |
| No rule reports success having examined nothing | A rule whose subject population is empty passes silently | `RuleOutcome.SubjectsExamined` + `SubjectKind` on every rule, a mandatory floor in `RuleAssert.Holds`, and the per-rule floors in `RuleInventoryTests` |
| A rule that is inert today is inert on purpose, and wakes up | The awaited type appears and the rule stays asleep | The `Inert` table in `RuleInventoryTests`, each row naming the task that brings its subject, plus an inertness guard per rule that fails when the subject appears. B-04's reviewer planted each awaited type and recorded all three guards expiring |
| The population cannot silently shrink | Delete one built assembly, or edit a source file without rebuilding, and see the rules run over less than the solution | `ProductionPopulationTests` and `SolutionLayout`'s staleness refusal: removing `Aurora.Documents.Canonical.dll` turned 19 of 109 tests red; a source file newer than its assembly refuses to run the rules at all |
| `System.Reflection.Metadata` needs no package reference on .NET 10 | Add the reference and find it is required | `tests/Aurora.Architecture.Tests/Aurora.Architecture.Tests.csproj` declares no such `PackageReference` and the project builds in Release in the B-04 gate run |

### Limitations, written down rather than papered over

- **IL scanning cannot see what is not in IL.** Boxed floating-point values, arithmetic performed in an assembly outside the scanned population, and anything a rule's population excludes. The project README §1 records this per rule; F1's "What it cannot see" paragraph is the model for how a rule states its blind spot.
- **The fitness tests do not yet have their own gate stage.** `verify.sh` stage 7 is `PENDING (owned by B-11)`; until it lands the rules run inside stage 6 and are selected only by `[Trait("Category", "Architecture")]`, a trait that nothing yet requires a test class to carry (B-04 finding n-5).
- **Two of B-04's own names claim more than their mechanism produces** (its review's M-1 and M-2). They belong to that task's rework. This ADR does not assume they are closed, and nothing here depends on them being closed.
- **Nothing mechanically prevents ArchUnitNET from coming back until `verify.sh` stage 4 exists.** A `PackageVersion` without a `PackageReference` restores nothing, so today's cost is documentation drift rather than exposure; exposure starts the moment someone adds the reference — which is exactly what a stale ADR row invites. The rejection-list entry becomes enforcing when B-11 implements stage 4 and `scripts/check-dependencies.sh`, which does not exist today.

## Consequences

- Positive: one mechanism, with a consumer, for every rule that exists; no pre-1.0 package under the quality gate; no licence to re-verify at each health check for this part of the stack.
- Positive: the module dependency matrix is data in the test (`ModuleMatrix`, a transcription of `modules.md` §6, checked row by row in the B-04 review) rather than a fluent chain — which was ADR-0020's stated reason for choosing ArchUnitNET, and is satisfied without it.
- Negative: ~720 lines of scanner are ours to maintain, and a rule costs more to write than a fluent one-liner. The containment is the rule inventory: a rule cannot be added without an inventory row stating what it measures and a floor stating how much it measured.
- Negative: two documents that a reader may reach first — ADR-0005 rule 1 and `overview.md` §4 — were written before this decision. They are corrected by the redirection in this ADR's *Supersedes* line and by the edit to `overview.md`; ADR-0005's text is left as written, because an accepted ADR's text is history.

## Revisit when

A rule arrives whose subject is neither compiled code nor a project file — `.razor` markup, generated migration SQL, or a source-level rule such as `testing-strategy.md` §5.2 M4's scan of SQL string literals — and the file-reading approach is genuinely worse than a compiler API. That is a new ADR naming that rule. Reintroducing ArchUnitNET specifically also needs a new ADR, and it must first demonstrate the rule it would replace failing under it.
