# Aurora.Architecture.Tests

The solution-wide fitness rules (`docs/architecture/testing-strategy.md` §5, `solution-layout.md` §6
row B-04). No database, no container; the whole suite runs in well under a second.

**The one idea in this project:** a rule that has never been seen to fail is not a rule. Every rule
here ships with a deliberately-violating fixture and a test asserting the rule reports it — and, for
the body-level rules, asserting *where* it reported it, so a rule narrowed back to signatures turns
the fixture tests red instead of passing quietly.

---

## 1. The rules

| Id | Rule | What the mechanism actually inspects | Reads |
|---|---|---|---|
| **F1** | No floating-point type in a signature, local, instruction or called member | Field and property types; method return and parameter types; **method-body locals**; floating-point IL opcodes (`ldc.r8`, `conv.r8`, `conv.r4`, `conv.r.un`, the `r4`/`r8` element and indirect forms); and every member an instruction names whose signature mentions `double`, `float` or `Half` | IL + metadata |
| **S3** | No ambient clock read in domain or application code | Every instruction naming `DateTime.Now/UtcNow/Today` or `DateTimeOffset.Now/UtcNow`, in any method body in the kernel or a `*.Domain` / `*.Application` assembly. A property read is a call to its getter, so it is visible in an expression, a field initialiser, a lambda or a local function alike | IL |
| **T1** | No public or protected constructor on a tenant `DbContext` | The accessibility flag on every `.ctor` of every type whose base chain reaches `Microsoft.EntityFrameworkCore.DbContext`, the catalog context excepted **by full name** (`Aurora.Platform.Tenancy.Catalog.CatalogDbContext`, where B-05 declares it) — any other `CatalogDbContext` is a tenant context | Metadata |
| **T2** | The `AddDbContext` family is called only inside `Aurora.Platform.Tenancy`, and only for the catalog context (ADR-0032 §4.1.1 — the part of that ADR its review confirmed) | Every `call` whose member name starts `AddDbContext` or `AddPooledDbContextFactory` — the name selects the **population**, never an exemption. Reported when the **call site's assembly** is not exactly `Aurora.Platform.Tenancy`, whatever the argument; or, inside it, when there is no generic argument or any generic argument is not exactly `Aurora.Platform.Tenancy.Catalog.CatalogDbContext` — a type parameter (`!!0`) included. A registration through any other API (`AddScoped<SalesDbContext>()`, a `ServiceDescriptor`, a helper's *call site*) is outside this population and belongs to the container-surface rule of ADR-0032 §4.1.1, which is not here yet | IL |
| **T3** | A tenant `DbContext` factory implemented only in `Aurora.Platform.Tenancy` | The implemented-interface list of every production type whose assembly name does not start with `Aurora.Platform.Tenancy`, matched on the simple names `ITenantDbContextFactory\`1` and ADR-0027 §1's DDL-path sibling `ITenantMigrationContextFactory\`1`. The prefix is a known, open gap — see §6 | Metadata |
| **T4** | `IHttpContextAccessor` only in the tenant-resolution middleware | Every type mention in production code — base types, interfaces, fields, properties, parameters, returns, **locals**, and the declaring type, signature and generic arguments of every member an instruction names | IL + metadata |
| **T5** | No static, singleton-registered or hosted type has a `TenantScope`, `TenantDatabaseHandle` or `TenantAccess` field or property | (a) any `static` field or property whose type names one of those three; (b) the generic arguments of every call whose member name contains `Singleton`; (c) **every type that is a hosted service by shape** — its base chain reaches `Microsoft.Extensions.Hosting.BackgroundService`, or its own or an inherited interface list names `Microsoft.Extensions.Hosting.IHostedService` — because a hosted service is a singleton however its registration is spelled, and `AddHostedService` contains no "Singleton" (re-review H-3). Each type in (b) and (c) is checked through its base chain for such a field or property, and the outcome says how many singleton-lifetime types it examined. Not seen: a singleton whose implementation appears only inside a factory lambda | IL + metadata |
| **T6** | `TenantDatabaseHandle` named only by the assemblies on the allow-list (ADR-0027 §1) | Every type mention in every production assembly whose name does not start with an allow-listed prefix — signature **and** body, which is what "in any signature or body" means. Allow-list today: `Aurora.Platform.Tenancy`. The prefix is a known, open gap — see §6 | IL + metadata |
| **L1** | `Aurora.SharedKernel` and every `.Domain` depend on the BCL and the kernel only | Declared `PackageReference` and `FrameworkReference`; the `Direct` entries of the committed `packages.lock.json`; the emitted `AssemblyRef` table; and every type named from a banned namespace (EF, ASP.NET, `Microsoft.Extensions`, Npgsql, `System.Data`, `System.Text.Json`, Newtonsoft, Serilog) | Project files + metadata |
| **L1-L5** | Every project reference is one `solution-layout.md` §2 permits | The `ProjectReference` elements each `.csproj` under `src/` declares, classified by the naming convention of `solution-layout.md` §1. Direct references only — a host reaching Infrastructure *through* `Aurora.Composition` is the design | Project files |
| **M1** | Every cross-module reference is in the `modules.md` §6 matrix | Every declared `ProjectReference` between two different `Aurora.Modules.*` modules, against the matrix held as data in `ModuleMatrix`. A module with no row may reference no other module | Project files |

**L1's scope is `Aurora.SharedKernel` plus every `*.Domain`, and not the other two tier-0
assemblies.** That is exactly the scope `testing-strategy.md` §5.1 names. `Aurora.Documents.Canonical`
and `Aurora.Countries.Contracts` are tier 0 but are not named there, and ADR-0008 §3.1 requires the
latter to carry an approved-API snapshot test — which needs an analyzer package reference. Sweeping
them into L1 would ban a dependency the architecture asks for. Their constraint is the one
`modules.md` §3 states — tier 0 references tier 0 and nothing else — and the L1-L5 rule enforces it.

`testing-strategy.md` §5 lists more rules than these. The rest — L6, M2-M5, F2-F4, C1-C5, S1/S2/S4/S5,
Q1/Q2, MIG1-MIG5, A1/A2 — need a `DbContext` model, an endpoint, a migration or a Country Package to
inspect, none of which exist yet. B-09 owns the migration rules; the others land with the code they
govern. **They are not silently absent: this table is the list of what exists, and §6 below is the
list of what does not.**

---

## 2. Which rules bite today, and which are asleep

An inert rule and a vacuous rule look identical from the outside: both green, both reporting nothing,
both trusted. The difference is written down in `RuleInventoryTests` and **checked**, not asserted.

| Rule | State today | Why |
|---|---|---|
| F1, S3, L1, L1-L5 | **Live** | They examine the kernel, the hosts and the project graph, all of which exist |
| T4 | **Live** | `IHttpContextAccessor` exists in the framework; any production code could name it today |
| T3, T5, T6 | **Live, nothing to find** | They scan every production type on every run and would report the first violation immediately. The *types* they govern (`ITenantDbContextFactory<>`, `TenantScope`, `TenantDatabaseHandle`) arrive with B-06 |
| T1 | **Inert** | No tenant `DbContext` exists in production yet. B-05 brings the catalog context (exempt as the exact pair); B-06 brings the first tenant context |
| T2 | **Inert** | `Aurora.Platform.Tenancy` does not exist yet. B-05 creates it, with the one permitted `AddDbContext` call site: the catalog registration |
| M1 | **Inert** | Fewer than two business modules exist, so there are no cross-module edges |

`RuleInventoryTests` asserts the absence of each awaited type and of the awaited assembly **by
name**. The day B-05 or B-06 adds one, those tests fail with an instruction saying what to change.
Inertness expires loudly.

Each inert row also carries a deliberately-violating fixture. `RuleInventoryTests` runs the rule over
it and requires a violation back, so a rule recorded as asleep is shown to be asleep rather than
broken.

It also reflects over every rule class and fails if one has no inventory row, so a rule cannot be
added without someone stating what it measures.

---

## 3. How a rule is proven to fail

Rules are functions from a population to a `RuleOutcome`. The production test runs one over the
solution; the fixture test runs the **same function** over a deliberately-violating population.

- **Type-level fixtures** live in `Fixtures/Violations/`, inside this assembly, compiled by the real
  compiler with the same settings as production code. When a rule fires on one, it fires on compiler
  output rather than on a test author's idea of it. They are types, not tests: nothing executes them,
  and they are invisible to every production rule because the production population is built from
  projects under `src/` (`ProductionPopulationTests` asserts that separation).
- **Project-level fixtures** are real `.csproj` and `packages.lock.json` files written to a temporary
  directory by `FixtureProjectTree` and read by the production `ProjectFile.Read`. Checked in, they
  would be found by every tool that globs for them — an IDE, `dotnet sln add`, B-11's dependency gate
  — and a fixture that breaks somebody else's build is worse than the problem it demonstrates. No
  compiler is involved in parsing a `.csproj`, so nothing is lost.
- **Compliant fixtures** sit beside the violating ones: an internal constructor two inheritance steps
  from `DbContext`, the conventional `CatalogDbContext` registration, a *scoped* type holding a
  `TenantScope`. A rule that fired on those would be banning the design ADR-0007 prescribes, and
  would be switched off within a week. Silence on them is only meaningful because the subject counts
  assert the rule actually examined them.

Four fixtures are **stand-ins**: `TenantScope`, `TenantDatabaseHandle`, `ITenantDbContextFactory<>`
and `ITenantMigrationContextFactory<>` do not exist until B-06. The rules match them by *simple name*, which ADR-0007 fixes, rather than by full name, whose
namespace B-06 has not chosen — a rule that guessed the namespace and guessed wrong would match
nothing and report no violations, in green, forever. **When B-06 lands the real types, delete the
stand-ins and point the fixtures at them**; `RuleInventoryTests` is what will remind you.

The one *exemption* runs the other way, because a too-wide exemption fails silently where a too-wide
subject match fails loudly (ADR-0032 §2, §4.2). Matched by simple name, any `CatalogDbContext`
anywhere escaped T1, T2 and the inertness guard (re-review m-1), so the catalog context is exempt only
as the exact **(full name, assembly) pair** — `Aurora.Platform.Tenancy.Catalog.CatalogDbContext` in
`Aurora.Platform.Tenancy`, where B-05 declares it. A look-alike anywhere else, and a renamed or moved
catalog context, are tenant contexts to every rule and to the inertness guard. A fifth stand-in serves
this: `Fixtures/StandIns/CatalogDbContext.cs` is compiled at B-05's exact full name, since the
full-name half of the pair *can* be compiled. The assembly half cannot — everything here compiles into
`Aurora.Architecture.Tests` — so `CatalogFixture` and `CrossAssemblyFixture` relabel the scanner's
records into the assembly a test needs (`with { AssemblyName = … }`) and change nothing else about
them. That is how one compiled fixture assembly exercises T2's call-site key and the exemption path
of T3.

---

## 4. The population, and why it cannot quietly shrink

`SolutionLayout` builds the population from **every `.csproj` under `src/`, with no filter** — not a
hand-kept list, so a module added tomorrow is covered without anyone remembering, and not a glob over
build output, because an assembly nobody built would then simply vanish from the population and every
rule would report "no violations" over the smaller set. `ProductionPopulationTests` asserts the
"every" against an independent, unfiltered enumeration of `src/`, so a filter cannot come back
quietly: the re-review of this task (H-1) showed that one excluding any directory named `Fixtures/`
made a production project placed there invisible to every rule, with nothing reporting it. The
project-file fixtures are written to a temporary directory outside the repository, and that too is
asserted rather than assumed.

It **throws**, rather than returning a partial population, when:

- a project under `src/` produced no assembly (build the solution; if the project is genuinely not
  part of it, add it to `Aurora.sln` so the gate covers it); or
- a project's sources are **newer than its assembly**.

The second was found while proving F1 fails. A `double` planted in `Aurora.SharedKernel` left F1
green, because `dotnet test` on this project alone rebuilds only this project and the rules happily
scanned the kernel's previous build. `verify.sh` builds in stage 3 before any test stage, so a full
gate run is never affected — but a developer running this project alone would have been.

`ProductionPopulationTests` additionally asserts the anchor assemblies are present, that no test
assembly is in the population, that the fixtures are invisible to it, and that the IL walker actually
walked hundreds of method bodies rather than reading metadata and no IL.

---

## 5. Why `System.Reflection.Metadata` and not ArchUnitNET

ADR-0020 chose `TngTech.ArchUnitNET` for architecture tests, and noted that "source-level rules are
Roslyn analyzers/tests, because reflection cannot see them". `docs/reviews/B-03.md` m-1 is the proof
of the second half: a rule reflecting over fields, properties, parameters and return types did not
see a `double` local or a `(double)` cast, which is precisely the violation F1 exists to stop.
`solution-layout.md` §6 row B-04 then requires this rule set to catch exactly that.

IL is the level at which it is visible, and `System.Reflection.Metadata` reads IL and metadata
straight out of the PE file: in the BCL, no dependency, nothing loaded or executed, and it works on
any `.dll` on disk whether or not its dependencies are present. Reading the built artifact rather
than the source also means a violation introduced by a source generator is caught, which a source
scan would miss.

**This is a deviation from ADR-0020 and is recorded as one.** It is narrow: the rules here are a thin
layer over one scanner (`Metadata/`, ~720 lines including its doc comments), which is the same mitigation
ADR-0020 states for ArchUnitNET's pre-1.0 version number. ArchUnitNET remains an approved dependency
and nothing here prevents adopting it for rules where its fluent model reads better. The architect
should decide whether to amend ADR-0020 or to keep both mechanisms.

---

## 6. What is deliberately not here

- **The container-surface and door-closing rules of ADR-0032 §4.1.1 and §4.2**: the rule that
  catches `AddScoped<SalesDbContext>()`, a `ServiceDescriptor` built from a `Type`, and a
  registration helper's *call site*; and the rules that close the door of a member returning,
  naming or constructing a tenant `DbContext` outside its owning assemblies. They need a
  two-assembly fixture population and are a separate task (their ids are being reallocated: the
  ADR's first draft collided with another branch). Until they land, those shapes are covered by
  nothing here — T2's and T1's "what it cannot see" say so in the code.
- **Exact-name allow-lists for T3 and T6, and the rules guarding them** (ADR-0032 §4.3 T13, §4.4
  T14). Both were built on this branch and taken out again when the ADR went back for revision:
  the exact set it named omitted `Aurora.Platform.Tenancy.Contracts`, which declares the very types
  T6 confines, and T14's only entry named an assembly that does not exist yet. The code is in this
  branch's history (commit `50b428b`) for whoever picks them up. **Until the ADR settles, T3's and
  T6's allow-lists are prefixes and re-review m-2 is open:** an assembly named
  `Aurora.Platform.TenancyBypass` authorises itself.
- **Roslyn source rules** (M4, S4, Q1, A1 in `testing-strategy.md` §5): interpolated SQL, unbounded
  `ToListAsync`, literal strings in `.razor` parameters. They need the source, not the assembly, and
  a Roslyn workspace is a different dependency and a different runtime budget.
- **EF model rules** (F2, M3, M5, Q2): they need a built `DbContext` model, which means constructing
  one, which means a tenant scope. They belong with the first module.
- **Migration rules** (MIG1-MIG5): B-09 owns them.
- **Two rules ADR-0027 asks for that are not here.** §1's "every tenant `DbContext` constructor takes
  exactly one `TenantAccess`-derived parameter" would have to guess the shape of a base class that
  does not exist, and a rule built on a guess is the vacuous kind. §2's "restrict
  `AllowUnstampedDuringProvisioning` call sites to the provisioning saga's step types by name" needs
  those step types, which B-07.1 creates. Both belong with the code they govern; ADR-0027 landed
  after this task's acceptance row was written, and what T3, T5 and T6 already cover of it is noted
  in the table above.
- **`verify.sh` stage 7**: B-11 owns it. Until then these tests run inside stage 6 — every class
  carries `[Trait("Category", "Architecture")]`, which stage 6's `Category!=Integration&Category!=Ui`
  filter includes and stage 7 can select on.
