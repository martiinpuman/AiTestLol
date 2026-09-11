# ADR-0032 — What the tenancy fitness rules bind to: registration, hand-out, the catalog context and allow-lists

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Supersedes:** — (nothing is reversed here; ADR-0007 §4.1 and §4.2 are **amended**, see below)
- **Amends:** ADR-0007 §4.1 and §4.2 — those sections state the guarantee in prose ("no tenant `DbContext` is ever registered", "the only public way to obtain one"). This ADR states the boundary a rule reading IL can bind to, and both sections carry a dated pointer here.
- **Superseded by:** —
- **Related:** ADR-0007 (multi-tenancy), ADR-0027 (the DDL path), ADR-0030 (fitness rules read IL metadata), ADR-0003 rule 3 (the catalog context is the one conventionally registered context), `../reviews/B-04-rereview.md` (findings H-2, H-4, m-1, m-2), `../architecture/testing-strategy.md` §5.3, `../architecture/solution-layout.md` §2.1

> Four decisions, no more. Each names the mechanism that enforces it, the fixture that proves the mechanism fires, and the fault that must turn that fixture red. Where a decision cannot be mechanised, §7 says so in those words and calls it a convention.

---

## 1. Context

B-04's second reviewer executed four bypasses of the tenancy fitness rules (`../reviews/B-04-rereview.md`). Two are defects in rule code and belong to that task. **Two are not:** the rules are as wide as ADR-0007 made them, and ADR-0007 did not say where the boundary is.

| Attack, executed | What walked past | Why it is this ADR's problem |
|---|---|---|
| `services.AddScoped<SalesDbContext>()` | T2, which keys on the member name `AddDbContext*` | ADR-0007 §4.2 says "no tenant `DbContext` is ever registered"; it lists three EF Core APIs. A rule cannot enforce a sentence that names three APIs against a container that has thirty |
| `Register<TContext>(s) => s.AddDbContext<TContext>()`, called with the concrete type elsewhere | T2, which counted the helper's call as an *examined subject* and found its generic argument to be `!!0` | The population floor looked healthy while the violation was unreachable. **B-06 will naturally want `AddTenantDbContext<TContext>()`**, which defeats a name-keyed rule by construction |
| `public static SalesDbContext Open(...)` on a type whose context constructor is `internal` | T1 (whose doc says this is "T3's job") and T3 (which reads interface lists only) | ADR-0007 §4.1 closes the constructor door and says the factory is "the only public way". Nothing said whether a *member returning* a context is a second door |
| An assembly named `Aurora.Platform.TenancyBypass`; a type named `CatalogDbContext` in any namespace | T3 and T6 (prefix allow-lists) and T1/T2 (simple-name exemption) | The authorisation and the exemption are both things the violating code chooses for itself |

A developer is reworking B-04 now with a placeholder predicate waiting on the first two answers, and B-06 writes the registration code next. Until B-06 lands, these rules are the *only* thing that will catch a violation of `CLAUDE.md`'s guarantee — "a developer must not be able to obtain a `DbContext` without a resolved tenant".

### 1.1 What the scanner can and cannot see today

Every mechanism below is checked against the model B-04 already built (`tests/Aurora.Architecture.Tests/Metadata/ScannedModel.cs`), because a decision that needs a capability the scanner does not have is a decision that will be quietly downgraded during implementation.

**Visible today:** assembly name and full type name per type; `TypeAttributes` (so type visibility, including nested); method accessibility (`MethodAttributes`, and a property's accessors are methods, so their accessibility is visible); field accessibility (`FieldAttributes`); return types, parameter types and locals as `TypeUse`, whose `Names` set is *flattened whole names* (so `ValueTask<SalesDbContext>` names the context and `SalesDbContextModelSnapshot` does not); and per instruction, the opcode, the declaring type, the member name, the signature and the generic arguments.

**Not visible today:** custom attributes and generic-parameter constraints. `ScannedType` carries no attribute list. **This is why no decision below uses an attribute as its boundary** — an attribute-keyed rule would need a scanner extension first, and would then key on a mark the violating code can apply to itself anyway.

---

## 2. The one principle behind all four decisions

> **A rule may only key on an identity that the violating code cannot mint for itself.**

The same defect appears three times in the re-review, wearing three costumes:

- a **method-name** key (`AddDbContext*`) lets the author of a violation choose a different method name, or wrap the call in one;
- an **assembly-name prefix** (`Aurora.Platform.Tenancy`) lets a new project authorise itself by being christened `Aurora.Platform.TenancyBypass`;
- a **simple type name** (`CatalogDbContext`) lets any type opt out of T1, T2 and the inertness guard by choosing that name.

An assembly's *exact* name and a type's *full* name are also chosen by a developer — but changing them is a diff in a `.csproj` or a namespace declaration that a reviewer sees, and there is exactly one assembly per name in the solution. That is the difference between a boundary and a convention: **a boundary is an identity whose change is a reviewable event; a convention is a name a rule hopes nobody will take.**

Ranked by that principle, the candidate boundaries a rule reading IL could use are:

| Boundary | Can the violating code mint it? | Visible to today's scanner? | Verdict |
|---|---|---|---|
| Exact assembly name | Only by a `.csproj` change a reviewer sees | Yes | **Use it** |
| Full type name | Only by a namespace change a reviewer sees | Yes | **Use it** |
| Member accessibility + declaring-type visibility | No — it is a property of the declaration, not a name | Yes | **Use it** |
| Opcode (`newobj`) | No | Yes | **Use it** |
| Method name / name prefix | Yes, freely | Yes | Reject as a boundary |
| Assembly-name prefix | Yes, freely | Yes | Reject as a boundary |
| Simple type name | Yes, freely | Yes | Reject as a boundary |
| Custom attribute | Yes, freely | **No** | Reject twice over |
| Marker interface | Yes, freely; and it marks types, not call sites | Yes | Reject |

---

## 3. Options considered

### 3.1 Decision 1 — which DI registrations are banned

| Option | Pros | Cons |
|---|---|---|
| **A. Ban every registration naming a tenant context, with no sanctioned helper; key the rules on the call site's assembly and on container-facing code rather than on the API name** *(chosen)* | Nothing to hide behind: a helper's own body sits in the wrong assembly, and its call site still names the concrete context. No exemption to attack. The alternative registration mechanism already exists and costs nothing (§4.1.3) | `AddTenantDbContext<T>()` — the obvious ergonomic helper — is forbidden, and B-06 must be told why before it writes one |
| B. A sanctioned helper marked with an attribute (`[TenantContextRegistration]`) | Reads as intent at the call site | Today's scanner cannot see attributes at all; and an attribute is a mark the violating code applies to itself. Two failures, one of them fatal |
| C. A sanctioned helper distinguished by its name (`AddTenant*`) | Cheapest to implement | This is finding H-2 rewritten as a design: the rule would again key on a name the violator chooses. Rejected on §2 |
| D. A sanctioned helper confined to `Aurora.Platform.Tenancy` by exact assembly name | Keys on a real boundary | The helper must be *called* from the module's composition code with a concrete context as its type argument, so the concrete name still appears outside tenancy — the exemption buys nothing that option A does not already permit, and it leaves a permitted shape for a reviewer to mistake |

### 3.2 Decision 2 — a public member returning a tenant context

| Option | Pros | Cons |
|---|---|---|
| **A. Three keys together: the type (whole-name match), the assembly (only the owning module may name it), and, inside the owning module, effective accessibility plus a construction clause** *(chosen)* | Covers the door from outside (nobody else may name the type) and from inside (nobody may construct one). The sanctioned factory needs **no exemption**: it returns `TContext`, a type parameter, which is not a tenant context's full name — so the rule cannot match it | Three rules rather than one, each with its own fixture and floor |
| B. Accessibility only — "no public member returns a tenant context" | One rule, one key | Misses the executed attack exactly: `internal static class PublicFactoryDoor { public static SalesDbContext Open(...) }` is effectively internal, so an accessibility rule is silent on the fixture the finding was written against |
| C. Assembly only — "nobody outside `Aurora.Platform.Tenancy` may name a tenant context" | Strongest possible statement | Unimplementable: the module that declares the context necessarily names it, and its `.Infrastructure` names it in the schema migrator. A rule that forbids what the design requires is a rule that gets widened in review |
| D. Return type only, allow-listing `ITenantDbContextFactory<>` by name | Small | Allow-listing by interface name reintroduces a name the violator can take, and it is unnecessary — see option A's second sentence |

### 3.3 Decision 3 — identifying the catalog context · 3.4 Decision 4 — allow-list matching

| Option | Pros | Cons |
|---|---|---|
| **Catalog A. Exempt the exact pair (full name, assembly name), and report a same-simple-name collision as a violation** *(chosen)* | The exemption cannot be taken by accident or on purpose; a rename fails loudly and is a one-line reviewable diff | Couples the rule to B-05's namespace. Intended: that is the reviewable event |
| Catalog B. Simple name (today) | Nothing to update when the type moves | Executed bypass m-1: any type named `CatalogDbContext` escapes T1, T2 and the inertness guard. Becomes high the day B-05 merges |
| Catalog C. An `[NotATenantContext]` attribute | Reads as intent | Invisible to the scanner; self-applied. Rejected on §2 |
| **Allow-list A. Exact assembly names, and every entry must exist in the population** *(chosen)* | `Aurora.Platform.TenancyBypass` authorises nothing; a renamed or deleted allow-listed assembly fails loudly instead of silently widening or narrowing the rule | A task that introduces a new allow-listed assembly must edit the list in its own diff. Intended |
| Allow-list B. Segment-bounded prefix (`name == p \|\| name.StartsWith(p + ".")`) | Stops `TenancyBypass` | Still lets `Aurora.Platform.Tenancy.Anything` authorise itself, which is the same defect one segment further down |
| Allow-list C. Prefix (today) | Nothing to update when a project lands | Executed bypass m-2: extension is a naming choice rather than a diff |

---

## 4. Decision

### 4.1 Decision 1 — **every DI registration that names a tenant `DbContext` is banned, whatever the method. There is no sanctioned registration helper.**

`AddTenantDbContext<TContext>()` must not be written. Nor `AddScoped<SalesDbContext>()`, nor a `ServiceDescriptor` built from `typeof(SalesDbContext)`, nor a generic wrapper that passes a tenant context to any of them.

The exemptions are two, both identified by exact names, not by shape:

1. **`Aurora.Platform.Tenancy.Catalog.CatalogDbContext` in assembly `Aurora.Platform.Tenancy`** is not a tenant context and is registered conventionally (ADR-0003 rule 3, ADR-0007 §4.2, §4.3 of this ADR).
2. **The open-generic factory registration** in `Aurora.Platform.Tenancy`: `services.AddScoped(typeof(ITenantDbContextFactory<>), typeof(TenantDbContextFactory<>))`, and the same shape for ADR-0027's `ITenantMigrationContextFactory<>`. This is not an exemption in the rule at all — **it names no context type**, so no rule below can match it. That is the point: the sanctioned path is invisible to these rules by construction rather than by allow-list.

#### 4.1.1 The mechanism

**T2 (amended) — the `AddDbContext` family is called from exactly one place.** Key: **the call site's assembly and the argument's full name, never the member name.**

- Population (counted): every instruction whose member name begins `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` or `AddPooledDbContextFactory`.
- Violation: **any such call in a production assembly other than `Aurora.Platform.Tenancy`** — whatever its generic argument, *including a generic argument that is a type parameter* (`!!0`, `!0`). An argument the rule cannot resolve is a violation, not a silence.
- Violation: such a call inside `Aurora.Platform.Tenancy` whose generic argument is anything other than `Aurora.Platform.Tenancy.Catalog.CatalogDbContext`.
- Consequence for the helper attack: the helper's own body (`s.AddDbContext<TContext>()`) is reported wherever it lives, because it is the site and not the argument that is judged.

**T9 (new) — no container-facing code names a tenant `DbContext`.** Key: **the container surface, which is a set of exact framework type names, not a set of method names.**

- A call site is container-facing if the called member's declaring type sits under `Microsoft.Extensions.DependencyInjection` or `Microsoft.Extensions.Hosting`. A *method* is container-facing if its own signature names one of: `Microsoft.Extensions.DependencyInjection.IServiceCollection`, `Microsoft.Extensions.DependencyInjection.ServiceDescriptor`, `System.IServiceProvider`, `Microsoft.Extensions.Hosting.IHostApplicationBuilder`, `Microsoft.AspNetCore.Builder.WebApplicationBuilder`. That list is exact names, lives in one place, and extending it is a reviewable diff.
- Population (counted): container-facing call sites plus container-facing methods.
- Violation: a container-facing call whose generic arguments or signature name a tenant context; or any mention of a tenant context anywhere inside a container-facing method — its locals, its `ldtoken` operands (`typeof(SalesDbContext)`), its generic arguments.
- Consequence for the executed attacks: `AddScoped<SalesDbContext>()` is reported (declaring type is in the DI namespace); `RegisterContext<SalesDbContext>(services)` is reported **at the call site** (the called member's signature names `IServiceCollection`), which is where the concrete type finally becomes visible.

#### 4.1.2 What a reviewer does to prove the rules catch the helper form

Not "read the rule and agree". Four steps, each producing output:

1. Add to the violations fixture assembly, compiled by the real compiler: (a) `s.AddScoped<FixtureTenantDbContext>()`; (b) `public static IServiceCollection AddTenantDbContext<TContext>(this IServiceCollection s) where TContext : DbContext => s.AddDbContext<TContext>();` **and** a separate call site `services.AddTenantDbContext<FixtureTenantDbContext>()`; (c) `services.Add(ServiceDescriptor.Describe(typeof(FixtureTenantDbContext), typeof(FixtureTenantDbContext), ServiceLifetime.Scoped))`.
2. Run the rules and read the counts: T2 must report (b)'s *helper body*; T9 must report (a), (b)'s *call site* and (c). The report must state how many call sites were examined, not only how many violations were found.
3. Break each rule and watch the fixture go red: restore T2's member-name key and drop the site key — (b)'s body stops being reported; narrow T9's container surface to `AddDbContext` alone — (a) and (b)'s call site stop being reported.
4. Restore, re-run, confirm the suite is green with the same counts as step 2.

Step 3 is the acceptance criterion. A rule whose fixture nobody has watched fail is not evidence.

#### 4.1.3 What a module writes instead — the ban must not be a dead end

A module needs no registration that names its context:

- **Handlers** take `ITenantDbContextFactory<SalesDbContext>` as a constructor parameter (ADR-0007 §4.5). Nothing registers it: the open generic in `Aurora.Platform.Tenancy` serves every closed instantiation.
- **Per-context configuration** (compiled model, interceptors, `HasDefaultSchema`) belongs to a per-module type that names its own context *in its own constructor or body* — inside the module's own assemblies, where §4.2 permits it — and is registered by its **own** name: `services.AddSingleton<IModuleContextConfiguration, SalesContextConfiguration>()`. No tenant context appears in a container-facing member.
- **Schema migration** (ADR-0027 §3): `SalesSchemaMigrator : IModuleSchemaMigrator` takes `ITenantMigrationContextFactory<SalesDbContext>` in its constructor and is registered by its own name. ADR-0027 §3 already forbids the runner from naming module contexts; this is the module-side half of the same statement.

If B-06 finds a case none of these cover, the answer is a new ADR naming that case — not a helper.

---

### 4.2 Decision 2 — **yes: a member that hands out a tenant `DbContext` is a violation.** The rule is the combination of *type*, *assembly* and *effective accessibility*, plus a construction clause — not any one of them alone.

A tenant `DbContext` is **owned**. Its owning assemblies are derived, not listed: the assembly that declares it, plus — when that assembly's name ends in `.Application` — the sibling assembly with `.Application` replaced by `.Infrastructure` (`solution-layout.md` §2.1 puts the context in `.Application` and its migrations in `.Infrastructure`). The derivation uses exact names. A context declared elsewhere (a platform context such as a future audit context) simply has a one-assembly owning set.

**T10 (new) — a tenant `DbContext` type is named only inside its owning assemblies.** Key: **assembly, exact.**

- Population (counted): (tenant context, production assembly) pairs outside the owning set.
- Violation: any mention of the context's full name — base type, interface, field, property, parameter, return, local, or any instruction operand, declaring type, signature or generic argument — in an assembly outside its owning set.
- This closes the executed factory-door attack whenever the door is outside the module, and every registration from `Aurora.Composition`, `Aurora.Web` or `Aurora.Worker`, and `typeof(SalesDbContext)` anywhere else, with one key.
- It matches whole names, so EF Core's generated `SalesDbContextModelSnapshot` and `SalesDbContextModel` are different names and are not false positives; `[DbContext(typeof(SalesDbContext))]` is an attribute and is invisible to the scanner either way.

**T11 (new) — no externally reachable member yields a tenant `DbContext`.** Key: **effective accessibility, which no name can forge.**

- Effective accessibility = the member's own accessibility **and** the visibility of every enclosing type. `public` on an `internal` type is not externally reachable.
- Population (counted): externally reachable members in production code (solution-wide; a large, never-zero number).
- Violation: an externally reachable method (including a property accessor, which is a method in metadata) whose **return type** names a tenant context, or an externally reachable **field or property** whose type names one — unless the same type use also names `ITenantDbContextFactory` or `ITenantMigrationContextFactory`, which is how a handler's `ITenantDbContextFactory<SalesDbContext>` member is distinguished from a member that hands out the context itself.
- A *parameter* typed as a tenant context is **not** a violation: the caller already holds one.
- **The exclusion term is the one place a name is load-bearing, and it is bounded.** Excluding a member because its type "names `ITenantDbContextFactory`" is an exclusion keyed on a simple name, and §2 says a violator can mint one. The distinction that makes this acceptable *temporarily*: a too-wide **subject** match produces false positives, which fail loudly and are fixed; a too-wide **exclusion** produces false negatives, which are silent. So: simple-name matching is acceptable for the subject sets B-04 already matches that way (`TenantScope`, `TenantDatabaseHandle`, the factory interfaces) while B-06 has not fixed their namespaces, but **T11's exclusion must be the two exact full names, and the change that declares `ITenantDbContextFactory<>` must supply them** — with the T14 existence check applied to the excluded names, so an exclusion that resolves to no type fails rather than excluding nothing quietly. Until then T11 runs with no exclusion term at all and the handler fixture of §4.2.2 step 3 is expected to be reported: a documented false positive is preferable to an exclusion nobody has verified.
- **The sanctioned factory is not exempted; it is not matched.** `ValueTask<TContext>.CreateAsync` returns a type parameter, and a type parameter is not a tenant context's full name. If `Aurora.Platform.Tenancy` ever declares a non-generic member returning a concrete context, T10 reports it (tenancy is outside every module's owning set) and T11 reports it too. That is the intended behaviour and it needs no extra clause.

**T12 (new) — a tenant `DbContext` is constructed only in `Aurora.Platform.Tenancy`.** Key: **the `newobj` opcode plus the exact assembly.**

- Population (counted): tenant `DbContext` types.
- Violation: a `newobj` naming a tenant context in any production assembly other than `Aurora.Platform.Tenancy`.
- This is the clause that closes the door *inside* the owning module, where T11 is silent by design, and it is also what catches `new SalesDbContext(options, null!)` — the one construction the C# nullable analysis will not stop.

#### 4.2.1 The honest boundary of Decision 2

Inside an owning assembly, a member that obtains a context **from the factory** and passes it to another type in the same assembly is reported by nothing — and it is not a bypass: to call the factory the caller must already hold a `TenantScope`, which ADR-0007 §3.4 makes unforgeable. The bypass inside an owning assembly is *construction*, and T12 covers it. This sentence exists so that no rule's documentation can later claim T11 covers the internal case; it does not, and the executed fixture `internal static class PublicFactoryDoor` must be recorded as a **T12** finding, never as a T11 one.

#### 4.2.2 What a reviewer does to prove these rules fire

1. Fixtures, in a population that spans **two assembly names** — the re-review's finding m-3 proved a single-assembly fixture cannot exercise a cross-assembly clause, and T10 is entirely a cross-assembly clause. Either a second fixture assembly or hand-constructed `ScannedType` records with two distinct `AssemblyName`s.
2. Positive fixtures: a public factory door outside the owning set (T10); `public static class Door { public static FixtureTenantDbContext Open() … }` (T11); `internal static class Door { public static FixtureTenantDbContext Open() => new(options, null!); }` (T12, and **not** T11).
3. Negative fixture, asserted as negative: a handler with an externally reachable member typed `ITenantDbContextFactory<FixtureTenantDbContext>` must **not** be reported by T11. Drop the factory-interface term and this assertion goes red — which is how the rule is shown not to over-report.
4. Faults that must turn a fixture red: make T10's owning set a prefix match (a `…Sales.Evil` fixture stops being reported); make T11 read declared accessibility instead of effective accessibility (the T12 fixture starts being reported by T11, and the negative assertion in step 3 fails); restrict T12 to a member-name key (the `newobj` fixture stops being reported).

---

### 4.3 Decision 3 — **the catalog context is identified by full name *and* assembly name, and a second type with the same simple name is a violation, not an exemption.**

- The exempt pair is exactly `("Aurora.Platform.Tenancy.Catalog.CatalogDbContext", "Aurora.Platform.Tenancy")` (B-05, `src/platform/Aurora.Platform.Tenancy/Catalog/CatalogDbContext.cs`).
- **T13 (new, and small) — the catalog exemption is unique and lands where it says.** Population (counted): production types whose base chain reaches `DbContext`. Two violations: (a) a type whose simple name is `CatalogDbContext` that is not the exempt pair; (b) after B-05 merges, the absence of the exempt pair itself. (b) is the clause that makes a rename fail loudly rather than silently turning every tenant-context rule's exemption into a no-op.
- Until B-05 merges the pair matches nothing, and the existing inertness guard already asserts that no `DbContext` exists; T13's row in the inventory names B-05 as the task that wakes it.
- **Demonstration:** the re-review's own fixture — a `…Violations.Attack.CatalogDbContext` deriving from `DbContext` with a public constructor — must be reported by T1 **and** by T13(a). Revert the exemption to simple-name matching and both reports disappear: that is the fault that must turn the fixture red. Rename the real catalog context in a scratch copy and T13(b) must go red.
- **Why not an attribute:** the scanner cannot read attributes (§1.1), and a self-applied mark is not a boundary (§2).

### 4.4 Decision 4 — **allow-lists match exact assembly names. Never a prefix. Every entry must name an assembly that exists.**

- T3's `TenancyAssemblyPrefix` becomes an exact set: `{ "Aurora.Platform.Tenancy" }`. T6's handle allow-list becomes an exact set: `{ "Aurora.Platform.Tenancy" }`.
- ADR-0027 §1's "the provisioning/migration runner" assemblies are **not** pre-entered. They do not exist; a guessed name is an entry nobody can verify. The task that creates such an assembly adds its exact name to the list **in its own diff**, which is what "extending it is a reviewable diff" was always supposed to mean.
- **T14 (new, and smaller) — every allow-list entry names an assembly in the production population.** Population (counted): allow-list entries across all allow-listed rules. A renamed, moved or deleted allow-listed assembly turns this red rather than silently widening the rule it guards. It also makes a typo in a new entry fail on the branch that adds it.
- **Demonstration:** hand-constructed `ScannedType` records in assembly `Aurora.Platform.TenancyBypass` — one implementing `ITenantDbContextFactory\`1`, one naming `TenantDatabaseHandle` — must be reported by T3 and T6 respectively. Restore `StartsWith` and both reports disappear. For T14, rename `Aurora.Platform.Tenancy` in a scratch copy and watch it go red.

---

## 5. Rule summary

| Id | Rule | Keys on | State when it lands |
|---|---|---|---|
| **T2** *(amended)* | The `AddDbContext` family is called only in `Aurora.Platform.Tenancy`, only for the catalog context; an unresolved generic argument is a violation | Call-site assembly + argument full name | Live once B-05 merges (floor ≥ 1: the catalog site) |
| **T9** *(new)* | No container-facing call or method names a tenant `DbContext` | Exact framework type names of the container surface | Live today (container-facing methods already exist); finds nothing until B-06 |
| **T10** *(new)* | A tenant `DbContext` is named only inside its owning assemblies | Assembly, exact, derived from the declaring assembly | Inert until the first tenant context; fixture + inventory row |
| **T11** *(new)* | No externally reachable member yields a tenant `DbContext` | Effective accessibility (member + enclosing type visibility) | Live today; floor is the count of externally reachable members |
| **T12** *(new)* | A tenant `DbContext` is constructed only in `Aurora.Platform.Tenancy` | `newobj` + exact assembly | Inert until the first tenant context |
| **T13** *(new)* | The catalog exemption is one exact (full name, assembly) pair and nothing else carries that simple name | Full name + assembly name | **Inert** until B-05 merges (its population is types deriving from `DbContext`, which is empty today) — inventory row names B-05, fixture is the `Attack.CatalogDbContext` type |
| **T14** *(new)* | Every allow-list entry names an assembly that exists | Exact assembly names | Live today |

Every rule above must carry an inventory row with a **non-zero floor** and a `SubjectKind` naming what it counted, per ADR-0030 and `RuleInventoryTests`. A rule that reports "no violations" without a count is the defect this project has rejected three times.

---

## 6. What this ADR does **not** decide

Named here so that nobody reads silence as permission, and so the follow-ups are not rediscovered:

1. **How `ITenantDbContextFactory<TContext>` actually constructs a context** whose constructor is `internal` to another assembly (reflection, `InternalsVisibleTo`, or a registered per-module construction delegate). B-06's decision; T12 holds whichever way it goes, because the factory is generic and names no concrete context.
2. **Whether a fitness rule confines connection-string and `NpgsqlDataSource` construction to the resolver** (`modules.md` §4 asserts it in prose; no rule id exists). That is the backstop for reflective construction, and it needs an id and an owner.
3. **The tenancy rule-id collision.** `testing-strategy.md` §5.3 lists T6 as "`TenantScope` appears in no serializable payload" while the implemented T6 is ADR-0027's `TenantDatabaseHandle` allow-list, and the document's T7/T8 (isolation-contract subclass; `IPlatformJob` references) are unimplemented. T9–T14 are claimed here; the serializable-payload rule needs a fresh id from whoever implements it.
4. **Tightening the rest of the simple-name subject matching** in `TenancyNames` (`TenantScope`, `TenantDatabaseHandle`, `TenantAccess`, both factory interfaces) to full names once B-06 fixes the namespaces. It is safe today for the reason in §4.2 — a wide subject match fails loudly — but it is a name-keyed match and it should not stay one forever. Needs an owner and a task id.
5. **Whether L6** ("no `public` type in a module's `Application` or `Infrastructure`") **subsumes part of T11** once it is implemented. It probably narrows T11's population; it does not replace it, because T11 also covers platform assemblies.

---

## 7. What is a convention here, and not a rule

Stated plainly, because the defect three reviews have found in this repository is documentation that reads as though a mechanism exists.

| Gap | Why no rule catches it | What actually stands behind it |
|---|---|---|
| **Reflective construction or resolution inside an owning assembly** — `Activator.CreateInstance(typeof(SalesDbContext), …)`, `ConstructorInfo.Invoke`, `Type.GetType("…SalesDbContext")` | A string-based lookup is a `ldstr` operand the scanner does not record; a `ldtoken` of the context *is* visible but is indistinguishable from EF Core's own legitimate uses inside the owning module | **Convention.** Reviewer's eye, Full tier. Runtime backstop: the constructor takes a non-nullable `TenantAccess` that only `Aurora.Platform.Tenancy` can construct, and the connection still has to come from somewhere (§6 item 2) |
| **A registration reached through a project-specific wrapper type** whose signature names no container-surface type, called from inside the owning module | T9's key is the container surface; a wrapper is not on it. T10 permits naming the context inside the owning module | **Convention**, with a real backstop: the container cannot activate a tenant context anyway — its only constructor is `internal` and takes a `TenantAccess` — so such a registration fails at resolution rather than handing out a context |
| **`null!` passed as the `TenantAccess` argument inside `Aurora.Platform.Tenancy`** | T12 permits construction there, which is the point of the assembly | **Convention.** Full-tier review of the tenancy assembly; ADR-0007 §4.1's "a reviewer will see it as a deliberate act" is the whole mechanism, and it is a human one |
| **The container-surface list itself** (§4.1.1) | It is a hand-kept list of framework type names; a container API outside those namespaces is not covered | **Half rule, half convention.** The list is data in the test and extending it is a diff, but nothing proves it is complete |

**None of these four is the guarantee.** The guarantee is ADR-0007 §4.1 layer 1: one `internal` constructor taking an unforgeable `TenantAccess`. T2 and T9–T12 are defence in depth over that, and their value is that they make an *attempt* visible in review — not that they are individually impassable.

---

## 8. Consequences

- **Positive.** The two questions blocking B-06 are answered with mechanisms, not prose: B-04's placeholder predicate becomes "call site assembly ≠ `Aurora.Platform.Tenancy`", and B-06 knows before it writes a line that `AddTenantDbContext<T>()` is not on the table and what replaces it.
- **Positive.** Four bypasses close with keys that cannot be minted by the code they judge; the executed attacks A1, A2, A3, A7 and A9 all become reported violations with fixtures.
- **Positive.** No scanner capability is added: every key is something `ScannedModel` already exposes. The ADR that needed attributes would have needed a scanner change first, and would have been weaker.
- **Negative.** Five new rules and one amendment, each needing a fixture, an inventory row and a floor. Sizing is the project-manager's call; T2, T13 and T14 are corrections to rules B-04 already owns, while T9–T12 are new and mostly inert until B-06.
- **Negative.** T10 and T13 hard-code full names from B-05 and from the module naming convention. A rename fails the build. That is the intended cost of a boundary over a convention, and it is why both have a clause that fails *loudly* rather than silently exempting nothing.
- **Negative for ergonomics.** Module composition code is slightly more verbose: a per-module configuration type instead of one `AddTenantDbContext<T>()` line. §4.1.3 is the written justification, per `CLAUDE.md`'s rule that novelty — and here, deliberate inconvenience — needs one.

## 9. Revisit when

- EF Core introduces a registration shape none of §4.1.3's alternatives cover (a compiled model that must be registered per context, or pooling that genuinely pays for itself in this architecture). Then a new ADR names that case and the exemption it needs, keyed on an exact assembly.
- A second `DbContext` legitimately becomes non-tenant (a second shared store). Decision 3's exemption becomes a set of exact pairs; the shape does not change.
- `ScannedType` gains custom attributes for some other rule's sake. Even then, do not revisit Decision 1 in favour of an attribute: §2's objection is about self-applied marks, not about scanner capability.
- The module naming convention in `solution-layout.md` §2.1 changes, which breaks T10's derivation of the owning set.
