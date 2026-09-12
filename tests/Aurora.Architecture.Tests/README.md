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
| **T3** | A tenant `DbContext` factory implemented only in `Aurora.Platform.Tenancy` | The implemented-interface list of every production type whose assembly is neither `Aurora.Platform.Tenancy` nor a dotted segment below it (`Aurora.Platform.Tenancy.Contracts` is inside; `Aurora.Platform.TenancyBypass` is not), matched on the simple names `ITenantDbContextFactory\`1` and ADR-0027 §1's DDL-path sibling `ITenantMigrationContextFactory\`1`. The segment-bounded prefix is an interim form — see §6 | Metadata |
| **T4** | `IHttpContextAccessor` only in the tenant-resolution middleware | Every type mention in production code — base types, interfaces, fields, properties, parameters, returns, **locals**, and the declaring type, signature and generic arguments of every member an instruction names | IL + metadata |
| **T5** | No static, singleton-registered or hosted type has a `TenantScope`, `TenantDatabaseHandle` or `TenantAccess` field or property | (a) any `static` field or property whose type names one of those three; (b) the generic arguments of every call whose member name contains `Singleton`; (c) **every type that is a hosted service by shape** — its base chain reaches `Microsoft.Extensions.Hosting.BackgroundService`, or its own or an inherited interface list names `Microsoft.Extensions.Hosting.IHostedService` — because a hosted service is a singleton however its registration is spelled, and `AddHostedService` contains no "Singleton" (re-review H-3). Each type in (b) and (c) is checked through its base chain for such a field or property, and the outcome says how many singleton-lifetime types it examined. Not seen: a singleton whose implementation appears only inside a factory lambda | IL + metadata |
| **T6** | `TenantDatabaseHandle` named only by the assemblies on the allow-list (ADR-0027 §1) | Every type mention in every production assembly that is neither an allow-listed assembly nor a dotted segment below one — signature **and** body, which is what "in any signature or body" means. Allow-list today: `Aurora.Platform.Tenancy`, so `Aurora.Platform.Tenancy.Contracts` (which declares the handle) is inside and `Aurora.Platform.TenancyBypass` is not. The segment-bounded prefix is an interim form — see §6 | IL + metadata |
| **L1** | `Aurora.SharedKernel` and every `.Domain` depend on the BCL and the kernel only | Declared `PackageReference` and `FrameworkReference`; the `Direct` entries of the committed `packages.lock.json`; the emitted `AssemblyRef` table; and every type named from a banned namespace (EF, ASP.NET, `Microsoft.Extensions`, Npgsql, `System.Data`, `System.Text.Json`, Newtonsoft, Serilog) | Project files + metadata |
| **L1-L5** | Every project reference is one `solution-layout.md` §2 permits | The `ProjectReference` elements each `.csproj` under `src/` declares, classified by the naming convention of `solution-layout.md` §1. Direct references only — a host reaching Infrastructure *through* `Aurora.Composition` is the design | Project files |
| **M1** | Every cross-module reference is in the `modules.md` §6 matrix | Every declared `ProjectReference` between two different `Aurora.Modules.*` modules, against the matrix held as data in `ModuleMatrix`. A module with no row may reference no other module | Project files |
| **MIG1** | Every migration declares `Expand`, `Contract` or `DataOnly` with a reason; only a `Contract` names the `Expand` it contracts, and that Expand exists (ADR-0007 §7.2 rule 1) | The `[MigrationSafety]` attribute, read back by reflection as the real type, on every non-abstract production type whose base chain reaches `Microsoft.EntityFrameworkCore.Migrations.Migration`; the `Contracts` link resolved against the population by migration id; duplicate ids | Reflection over loaded migration assemblies |
| **MIG2** | Destructive SQL only in a `Contract`; a `DataOnly` migration is plain data statements; a created trigger is enabled `ALWAYS`; nothing the scanner cannot read (ADR-0007 §7.2 rule 2 as amended by ADR-0037 §2–§4) | The SQL **Npgsql's own generator emits from each migration's `UpOperations`** against its target model - `DropColumn(…)` included, which has no string to scan - every command, every statement, every dollar-quoted body, and every string literal standing where PostgreSQL reads a literal as code (`DO '…'`, `CREATE FUNCTION … AS '…'`, adjacent constants joined as PostgreSQL joins them), through a tokenizer that follows the PostgreSQL lexer. See §7 | Generated SQL |

**L1's scope is `Aurora.SharedKernel` plus every `*.Domain`, and not the other two tier-0
assemblies.** That is exactly the scope `testing-strategy.md` §5.1 names. `Aurora.Documents.Canonical`
and `Aurora.Countries.Contracts` are tier 0 but are not named there, and ADR-0008 §3.1 requires the
latter to carry an approved-API snapshot test — which needs an analyzer package reference. Sweeping
them into L1 would ban a dependency the architecture asks for. Their constraint is the one
`modules.md` §3 states — tier 0 references tier 0 and nothing else — and the L1-L5 rule enforces it.

`testing-strategy.md` §5 lists more rules than these. The rest — L6, M2-M5, F2-F4, C1-C5, S1/S2/S4/S5,
Q1/Q2, MIG3-MIG5, A1/A2 — need a `DbContext` model, an endpoint, a release manifest or a Country
Package to inspect, none of which exist yet. B-09 owns MIG3 and MIG4 (§6); the others land with the
code they govern. **They are not silently absent: this table is the list of what exists, and §6 below
is the list of what does not.**

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
| T2 | **Live** | `Aurora.Platform.Tenancy` exists since B-05, with the one permitted `AddDbContext` call site: the catalog registration. T2 examines it on every run, floor 1 |
| M1 | **Inert** | Fewer than two business modules exist, so there are no cross-module edges |
| MIG1, MIG2 | **Live** | Two production migrations exist (B-05's `InitialCatalog`, 31 statements; B-19's `AppendOnlyTrails`, 18); both rules examine both on every run, floor 1 migration in the inventory, and `MigrationRuleTests` holds MIG2 to a floor **per migration** - each measured count less a tenth, 28 and 17 - so that one migration generating nothing cannot hide behind the other's count, and pins the bodies it read from each (2 and 1) |

`RuleInventoryTests` asserts the absence of each awaited type **by name**. The day B-06 adds one,
those tests fail with an instruction saying what to change. Inertness expires loudly - T2's did,
the day B-05 landed the tenancy assembly.

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

**This is a deviation from ADR-0020 and is recorded as one. The scanner also reports how many bodies it read, and MIG2 carries that count beside the
statement count, so a run that read fewer bodies than the SQL contains is visible rather than
silent.** It is narrow: the rules here are a thin
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
  T6's allow-lists are a segment-bounded prefix** (`TenancyNames.IsWithin`: the assembly itself or
  a dotted segment below it). That refuses the executed m-2 bypass, `Aurora.Platform.TenancyBypass`,
  and admits `Aurora.Platform.Tenancy.Contracts`, the case the exact list got wrong — so it is
  strictly better than the plain prefix under either outcome and binds nothing. **It is an interim
  floor, not the exact-name decision:** any `Aurora.Platform.Tenancy.Anything` still authorises
  itself one segment down, and only the exact set the ADR will settle closes that. Whoever
  implements the decision replaces `IsWithin` and the tests that pin it.
- **Roslyn source rules** (M4, S4, Q1, A1 in `testing-strategy.md` §5): interpolated SQL, unbounded
  `ToListAsync`, literal strings in `.razor` parameters. They need the source, not the assembly, and
  a Roslyn workspace is a different dependency and a different runtime budget.
- **EF model rules** (F2, M3, M5, Q2): they need a built `DbContext` model, which means constructing
  one, which means a tenant scope. They belong with the first module.
- **MIG3, the release gate, and MIG4, index concurrency.** B-09's first slice ships MIG1 and MIG2;
  MIG3 needs a release manifest that does not exist yet, and MIG4 sits on the same scanner and
  needs to know which tables a migration created. Both are the next slices of B-09. Until MIG4
  lands, a `CREATE INDEX` without `CONCURRENTLY` on a live table is refused by nothing here. MIG5
  (a package migration names only its own schema) waits for the first package migration.
- **Two rules ADR-0027 asks for that are not here.** §1's "every tenant `DbContext` constructor takes
  exactly one `TenantAccess`-derived parameter" would have to guess the shape of a base class that
  does not exist, and a rule built on a guess is the vacuous kind. §2's "restrict
  `AllowUnstampedDuringProvisioning` call sites to the provisioning saga's step types by name" needs
  those step types, which B-07.1 creates. Both belong with the code they govern; ADR-0027 landed
  after this task's acceptance row was written, and what T3, T5 and T6 already cover of it is noted
  in the table above.
- **`Down()` of a migration.** MIG2 reads `Up()` only. `Down()` is never run in production
  (ADR-0007 §7.4: rollback is a per-tenant restore, not a database operation) and is where an
  Expand's own drops legitimately live.
- **`verify.sh` stage 7**: B-11 owns it. Until then these tests run inside stage 6 — every class
  carries `[Trait("Category", "Architecture")]`, which stage 6's `Category!=Integration&Category!=Ui`
  filter includes and stage 7 can select on.

---

## 7. The migration rules: what the scanner reads, and what it cannot see

MIG2 is a parser, and a parser that reads a subset of its input and reports as though it read all
of it is the defect this project has met most often. So the chain from the assertion to the
behaviour is written down here, link by link, with the link it stops at.

**Link 1 - the SQL is generated, not read from source.** `MigrationSafety/MigrationPopulation.cs`
finds every non-abstract `Migration` subclass in the unfiltered production population, loads that
assembly by path, instantiates the migration, runs `Up()` under the Npgsql provider name and hands
its `UpOperations` and its runtime-initialized target model to Npgsql's own
`IMigrationsSqlGenerator` - what `Migrator.GenerateUpSql` does. `DropColumn(…)` contains no string
saying `DROP COLUMN`; the generated SQL does. `MigrationRuleTests` asserts the generated text of
`InitialCatalog` contains `CREATE TABLE catalog.tenant (`, which no source string contains.

**Link 2 - the text is tokenized the way PostgreSQL's lexer tokenizes it.** `SqlTokenizer` follows
the lexical rules for the things that can hide a keyword: `--` and nested `/* */` comments are
dropped; `'…'`, `E'…'`, `U&'…'`, `B'…'` and `X'…'` literals are kept and read only where PostgreSQL
reads a literal as code; `"…"` is a name; `$$…$$` and `$tag$…$tag$` bodies are **code, and are
tokenized again** - a `DROP TABLE` inside a `DO` or function body is real DDL, and the fixture
`ExpandHidingADropInADollarQuotedBody` proves it is found, two tags deep. **A quoted body is the
same body.** `DO $$…$$` and `DO '…'` are one statement, and `CREATE FUNCTION … AS '…'` is the
original spelling; the first review of this branch showed the scanner reading only the dollar-quoted
form and reporting `DO 'BEGIN DROP TABLE …; END'` as *clean* (B-1). A literal that is the body of a
`DO` (its `LANGUAGE` name excepted) or follows `AS` in a `CREATE [OR REPLACE] FUNCTION|PROCEDURE` is
now decoded - doubled quotes collapsed, `E'…'` backslash escapes resolved - and read as a
dollar-quoted body is, **at every nesting level**: the body position is decided from the tokens
around the literal (`DO [LANGUAGE name]` before it; `AS` before it in a statement that defines a
`FUNCTION` or `PROCEDURE`) and never from how the statement begins, because inside a procedural
block the statement that creates a routine begins with `IF` or `BEGIN`. The third review (N-1)
found the previous form deciding from the first token, so a quoted routine body nested in a `DO`
block was reported clean while its `$fn$` twin was read; `ExpandHidingADropInANestedQuotedBody`
and the scanner pair beside it prove both spellings now read alike, and the scanner reports how
many bodies it read so a run that read fewer than expected is visible. `ExpandHidingADropInAQuotedBody`
proves the top-level case, beside `ExpandWithAProceduralLookAlikeInData`, the same text as data,
which stays clean. **Adjacent
constants are one constant.** PostgreSQL's lexer joins string constants separated by a newline
before the grammar sees them, so `DO 'BEGIN DR'⏎'OP TABLE …; END'` is one body; the second review
(N-2) showed the scanner reading the first literal only and reporting nothing - the seventh
failure form on the path the B-1 fix had added. A literal body now takes every adjacent literal into
its run, and the run is decoded and joined before it is read; `ExpandHidingADropInAConcatenatedBody`
proves it, and the same two literals as `INSERT` data stay clean. A body in a literal the tokenizer
does not decode (`U&'…'`, a bit string, an `E'…'` with a numeric escape) - anywhere in the run - is
reported as **unscannable**. Case, whitespace and newlines between keywords change nothing, because matching
is on tokens. Anything the tokenizer cannot finish - an unterminated literal, identifier, comment
or dollar quote, a character with no rule - throws, and the migration is reported as
**unscannable**, never as clean.

**Link 3 - what counts as destructive is decided by effect (ADR-0037 §2.1), matched on the token
stream anywhere in a statement.** A statement is destructive when, from some prior state the
migration does not control, it narrows what the schema offers (limb A, removal - visible, with a
widening exception) or reduces the set of executions in which a declared invariant is enforced
(limb B, suppression - the object stays named and defined, and there is no widening exception).
Spelling is evidence of effect and never a substitute for it. **Limb A:** `DROP` of anything but a
default, `NOT NULL`, identity or expression; `DROP CONSTRAINT`, bare or with `CASCADE`; `TRUNCATE`
of a table; `DELETE FROM` and `MERGE … THEN DELETE`; any `RENAME`; `ALTER [COLUMN] … TYPE`;
`ALTER [COLUMN] … SET NOT NULL`; `SET SCHEMA`; `ADD [COLUMN] … NOT NULL` with neither `DEFAULT` nor
`GENERATED`; `DETACH PARTITION`. **Limb B, enumerated by the catalog column that records "in
force"** (`docs/architecture/postgres-invariant-suppression.md`; the rows the scanner covers are
declared in `SqlStatementScanner.CoveredSuppressionRows` and asserted equal to what it implements
by a probe per row): `pg_trigger.tgenabled` and `pg_rewrite.ev_enabled` through `DISABLE` (`D`),
`ENABLE REPLICA` (`R`, fires only under `session_replication_role = 'replica'` and never for an
ordinary write - the four `ENABLE ALWAYS` statements B-19 spent, reversed) and **plain `ENABLE`
(`O`, a reduction from `A`, the one firing mode that cannot reduce - the call the second review
accepted as a look-alike and ADR-0037 §2.4 overturned)**, for triggers, rules and event triggers;
the `session_replication_role` GUC in every spelling (`SET`, `SET LOCAL`, `ALTER ROLE … SET`,
`ALTER DATABASE … SET`, `[pg_catalog.]set_config(…)`); `DISABLE ROW LEVEL SECURITY` and
`NO FORCE ROW LEVEL SECURITY`; `DROP TRIGGER` and `CREATE OR REPLACE TRIGGER`, which re-create a
guard with a narrower scope under an unchanged name; and `CREATE OR REPLACE` of anything - not
because the scanner cannot compare bodies, but because a migration runs once per database under an
advisory lock, so `OR REPLACE` buys no idempotence it needs; what it buys is a meaning that depends
on state the migration does not control, where `CREATE` meeting an unexpected object fails loudly,
which is information (ADR-0037 §2.5). A finding is a violation unless the migration is a
`Contract` - or unless MIG2 attributes it to an EF operation that proves it a widening, which only
two findings admit: a bare `DROP CONSTRAINT` whose command a `DropCheckConstraintOperation`
produced, naming the same schema, table and constraint, where the previous migration's model
declares that constraint as a `CHECK` (ADR-0037 §3.2 - the constraint's *name* is never evidence;
`DropCheckConstraint("ck_x")` is cleared, `DropPrimaryKey`, `DropUniqueConstraint` and
`Sql("… drop constraint ck_x")` are not, and the rule reports how many drops it examined and how
many it attributed); and an `ALTER COLUMN … TYPE` whose command an `AlterColumnOperation` produced,
from `varchar(n)` to a wider `varchar(m)` or from `varchar` to `text`, with no `USING`, no
`COLLATE` and no other action in the statement (ADR-0037 §4 - `numeric` is excluded for want of
verified evidence, and raw SQL has no old type to compare). `SqlScannerTests` pins each shape from
both sides: the destructive spelling is named, and the look-alike beside it - a trigger on
`DELETE OR TRUNCATE`, a `GRANT … DELETE`, an `ON DELETE RESTRICT`, a `DROP NOT NULL`, an
`ENABLE ALWAYS TRIGGER`, an `ENABLE ROW LEVEL SECURITY`, a `SET search_path`, a plain
`CREATE FUNCTION`, an `ATTACH PARTITION`, `DROP TABLE` in a literal or a comment - is let through
*and counted*.

**Link 3a - a guard a migration creates must be enabled `ALWAYS` in the same migration (ADR-0037
§2.6).** `CREATE TRIGGER` produces `tgenabled = 'O'`, which `session_replication_role = 'replica'`
suppresses; a guard left there is weaker than the one B-19 paid for, and nothing in the destructive
set catches it, because nothing was suppressed - the guard was born weak. MIG2 collects every
`CREATE [CONSTRAINT] TRIGGER … ON table` and every `ALTER TABLE table ENABLE ALWAYS TRIGGER
name|ALL` a migration runs and reports each created trigger that has no matching `ENABLE ALWAYS`,
in every category: `ExpandCreatingAGuardBornWeak` and `ContractRepointingGuardWithoutEnableAlways`
fire, `CompliantExpand` and `ContractRepointingGuardToV2` do not. The legitimate way to change a
guard function is §2.6's path, and it is pinned as a pair: the Expand creates `…_v2` (nothing
points at it); the Contract, one release later, drops the trigger, re-creates it on `_v2`,
re-applies `ENABLE ALWAYS`, and drops `_v1` - visible to anything comparing names, and the two
bodies coexist for one release.

**Link 4 - what it refuses to read is reported, not skipped.** Dynamic SQL (`EXECUTE` of anything
but a trigger's `FUNCTION`/`PROCEDURE` binding or the `EXECUTE` privilege of a `GRANT`/`REVOKE`); a
call to a schema-qualified function or procedure outside `pg_catalog`, whose body was written
somewhere else - after `ON` too, unless the nearest statement verb before it is `CREATE`, `ALTER` or
`DROP`, because after a `SELECT`, `JOIN` or a DML verb `ON` introduces a join condition and a call
in it runs (the first review's n-1); a migration whose `Up()` throws or whose operations the
generator refuses. Each has a fixture and a test asserting the violation, and each is a violation
**for every category**: `ContractThatRunsDynamicSql` and `DataOnlyThatCallsAUserFunction` are the
witnesses, and the Contract's `EXECUTE` is asserted not to be counted as a permitted destructive
statement - the first review's M-2 showed that exempting `Contract` from the unscannable branch left
every test green, and an unreadable Contract is exactly the case that must not pass as a permitted
one.

**Where the chain stops - stated, with the assumption it rests on.**

- A call to an *unqualified* function is read as a built-in. That holds only while every function a
  migration creates is schema-qualified, which every migration on this project is (a schema owns its
  objects); a migration that creates an unqualified function is the case that would break it, and
  nothing here refuses one yet.
- A function *referenced* rather than called - defined, dropped, bound to a trigger, named as a
  column default - is not followed into. The function runs at insert time, not migration time.
- **Whether a `CREATE OR REPLACE` displaces anything is not known, and does not need to be.**
  Every `OR REPLACE` needs a `Contract` on ADR-0037 §2.5's effect argument, and a new object is
  created without it; the replacement body is still read, so a drop inside it is a second finding.
  The legitimate change to a guard function is §2.6's `_v2` path (Link 3a).
- **A `DROP CONSTRAINT` is cleared only through EF's snapshot, never through the database or a
  name.** The chain is `DROP CONSTRAINT` in generated SQL → the operation that generated it, from
  EF's own per-operation generation → its CLR type is `DropCheckConstraintOperation` → the previous
  migration's target model declares that constraint as a `CHECK`. It stops at EF's model snapshot: a
  constraint created by raw SQL is never in the model, never attributed, and always destructive -
  the loud direction, deliberately (ADR-0037 §3.2). Whether a cleared `CHECK` replacement actually
  widens is a human's call at Full tier (§3.3); the mechanism proves only the kind. The `ck_`/`pk_`/
  `uq_`/`ex_` naming convention is refused as a link (§3.1).
- **`CREATE RULE … DO INSTEAD NOTHING` on an existing table is not judged.** It is `DISABLE RULE`
  from the other side - every `INSERT` into the table silently becomes a no-op, the shape B-19's own
  review met - and creating an object is otherwise the additive direction. The architect's ruling on
  it is pending (third review, n-2); until it arrives the statement scans clean and this line is
  what says so.
- **Suppression rows the scanner does not cover:** S5 (search-path shadowing of an unqualified name
  in a body), S7 (`ADD CONSTRAINT … NOT VALID`, deliberate limb B for one release, with nothing
  scheduling its `VALIDATE`), S8 (deferrable constraint triggers), S9 (an invalid index after a
  failed concurrent build - MIG4's, B-09.2), S11 (ACLs and ownership, `OWNER TO` and `BYPASSRLS`
  included - the catalog privilege oracle's domain at runtime). Each has a probe in
  `SqlScannerTests` asserting it scans clean today, so coverage cannot grow or shrink undeclared.
- `Down()` is not read (§6).
- Whether an `ALTER COLUMN … TYPE` widens or narrows is not judged: ADR-0007 §7.2 lists every type
  change, so every one needs a `Contract`. A `varchar(10)` to `varchar(20)` widening is metadata-only
  in PostgreSQL and could be admitted later; that is a refinement for the architect to decide, not a
  gap the rule hides.
- Whether a `SET NOT NULL` column is populated is not judged; every one needs a `Contract`.

**The population cannot quietly shrink.** MIG1 and MIG2 examine the migrations `SolutionLayout`
finds - the same throw-on-missing, throw-on-stale population as every other rule - and
`RuleInventoryTests` holds both to a floor of 1 migration. `MigrationRuleTests` holds MIG2 to a
statement floor **per production migration**, each a count measured on the branch that set it
less a tenth: `InitialCatalog` 31 (floor 28), `AppendOnlyTrails` 18 (floor 17). Per migration and
not in aggregate, because with two migrations one that generated nothing would hide behind the
other's count; proportional and not verify.sh's nearest-ten, because on a count of 18 the
nearest-ten floor left both grants and all three indexes outside it (the second review's n-1); a
migration with no count fails the test, which prints the live count to write. The first review's
M-1 found the original single floor (20) derived from a count (27) measured against an earlier
scanner inside the same commit, so that the catalog could lose its whole privilege block and stay
green; the numbers here are re-measured whenever the scanner changes. The fixture population is
held to its exact count (31, of which one deliberate pair shares an id for MIG1's duplicate-id
branch), so a fixture that fails to load fails a test instead of vanishing from the ones that
assert on it.

**Categories, as the rules hold them.** `Expand`: no destructive finding. `Contract`: destructive
findings permitted (and counted as permitted, so silence on a compliant Contract is shown to mean
"seen and allowed"); must name the Expand it contracts, which must exist and be an Expand. `DataOnly`:
no destructive finding, and every top-level statement begins with `INSERT`, `UPDATE`, `MERGE`,
`WITH`, `SELECT`, `SET` or `RESET` and carries no dollar-quoted body - data-only means reviewable at
a glance, and a procedural backfill declares `Expand` and is scanned like one. An unannotated
migration is MIG1's violation and is judged by MIG2 as the strictest category, so the absence of an
annotation exempts nothing.

