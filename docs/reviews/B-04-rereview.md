# Re-review — B-04 `Aurora.Architecture.Tests` (second reviewer, security)

- **Verdict: REJECT (CHANGES_REQUESTED)** — the two majors from the first review are genuinely
  fixed, and M-2's fix is better than the fix that was asked for. The rejection is for four new
  findings in the tenancy rules themselves (T2, T5, T1/T3, and the population filter), each of which
  I executed: a tenancy control that can be bypassed by one line of ordinary code, or that silently
  covers less than its own documentation says, is the one defect this project exists to prevent.
- **Tier:** Full, unchanged. Not escalated — Full is already the top tier.
- **Reviewer:** security (second reviewer; did not author the branch, did not write `B-04.md`)
- **Branch:** `task/B-04` at `8d758ed`, 10 commits ahead of `claude/multi-tenant-saas-erp-pv2nap`
- **Reviewed in:** `/tmp/rereview-B-04` (detached, read-only). **Every experiment below ran in a
  copy** at `…/scratchpad/lab`; `git status` in the review worktree is empty and was never non-empty.

## Gate result — `scripts/verify.sh`, verbatim summary

```
---------------------------------------------------------------------
 #   Stage                       Result       Time  Note
---------------------------------------------------------------------
 0   Preflight                   PASS         0.1s
 1   Restore                     PASS         1.7s
 2   Format & style              PASS        13.4s
 3   Build                       PASS         2.7s
 4   Dependency licence gate     PENDING         -  owned by B-11
 5   Vulnerability gate          PENDING         -  owned by B-11
 6   Unit tests                  PASS         4.0s  315 test(s) executed
 7   Architecture fitness tests  PENDING         -  owned by B-11
 8   Integration tests           PENDING         -  owned by B-11
 9   UI component tests          PENDING         -  owned by B-11
 10  Coverage report             PENDING         -  owned by B-11
 11  Summary                     PASS         0.0s
---------------------------------------------------------------------
     Total                                   22.2s
---------------------------------------------------------------------
 Gate incomplete: stage(s) 4 5 7 8 9 10 are not implemented yet.
 RESULT: PASS
```

**315 tests executed**, matching the first review exactly. `Aurora.Architecture.Tests` alone: 109/109.

---

## 1. What I attacked, and what happened

I took the first reviewer's eleven planted violations as established and did not repeat them. I
attacked two things instead: the rework, and the question of what a *real* tenancy violation would
have to look like to walk past T1–T6.

| # | Attack | Result |
|---|---|---|
| A1 | A tenant `DbContext` with a **public constructor** named `CatalogDbContext` in an unrelated namespace | **Evades T1, T2 and the T1/T2 inertness guard.** m-1 |
| A2 | Register a tenant context with `AddScoped<SalesDbContext>()` instead of `AddDbContext` | **Evades T2.** H-2 |
| A3 | Register it through a one-line generic helper: `Register<TContext>(s) => s.AddDbContext<TContext>()` | **Evades T2** — the call *is* counted as a subject, its generic argument is `!!0`. H-2 |
| A4 | A hosted service (`AddHostedService<T>`, a container singleton) with a `TenantScope` field | **Evades T5.** H-3 |
| A5 | A singleton holding `ConcurrentDictionary<string, TenantScope>` | Caught. Generic arguments are flattened into `TypeUse.Names`. Defence holds |
| A6 | A tenant context two hops from `DbContext` through a **generic** base (`ModuleContextBase<TSelf>`) | Caught — the base walk resolves a generic-instantiation base. Defence holds |
| A7 | A **public static factory method** returning a tenant context, ctor left `internal` | **Evades T1 and T3 both**, and each rule's doc says the other one covers it. H-4 |
| A8 | A production `.csproj` under any directory named `Fixtures/` | **Removed from the population silently**; the anti-vacuity guard cannot see it. H-1 |
| A9 | An assembly named `Aurora.Platform.TenancyBypass` implementing the factory / naming the handle | Allow-listed by both T3 and T6. m-2 |
| A10 | A type emitted by a **source generator** | Caught — the rules read the compiled `.dll`, so generated code is indistinguishable from hand-written. Worth saying out loud: this is a real advantage of IL scanning over a Roslyn rule |
| A11 | Four fault injections inside T1, T2 and M1 (M-2's crux) | All four turn the inventory test red. Reproduced, verbatim output in §3 |
| A12 | Restricting the base-type walk to a single assembly | **All 109 tests stay green** while T1/T2 go blind to the exact shape B-06 will ship. §3 |
| A13 | Secrets/PII sweep of the whole branch diff (`password`, `secret`, `connectionstring`, `api_key`, `token=`) | Clean. No credential, no connection string, no personal data; the branch adds no logging at all |

Two boundaries were **not** touched by this change and I did not review them: tenant↔platform
(no catalog code here) and core↔Country Package (no package loading here).

---

## 2. M-1 — fixed, but it left the load-bearing half behind (see H-1)

The `ItemGroup` is gone (`2312fd9`). I verified the fix is complete on its own terms:

- `grep -rn ViolatingProjects` over the whole tree returns **nothing** — the string no longer exists.
- Nothing in the build depended on the removed items: `dotnet build Aurora.sln` succeeds, stage 2
  (format/style) passes, and the remaining `.csproj` comment is the EF Core / ASP.NET half, which
  `FixtureFidelityTests` does mechanise. That comment is now true in full.

The residue the first reviewer tied to it (n-6) was left in place deliberately. That decision is the
problem, and it is now a finding with an exploit rather than a tidiness note — **H-1**.

---

## 3. M-2 — the fix is right, the author's reasoning holds, and here is what it still misses

### The four faults, reproduced

I did not accept the author's table. Each fault was applied to the rule source in the scratch copy,
rebuilt, and `Each_inert_rule_still_fires_against_its_fixture` run alone. All four:

| Fault injected | Test output |
|---|---|
| T1's `where` made unsatisfiable (`Accessibility == PrivateScope`) | RED — *"rule T1 is recorded as inert but did not fire on its own deliberately-violating fixture. It is not asleep, it is broken: T1 …: examined 3 tenant DbContext types, found 0 violation(s)."* |
| T2's violation clause silenced (`named.Length > 1000`) | RED — *"rule T2 … examined 3 AddDbContext* calls, found 0 violation(s)."* |
| M1's matrix-refusal branch made unreachable for a `.Contracts` provider | RED — *"rule M1 … examined 1 cross-module project references, found 0 violation(s)."* |
| T1 reports `SubjectsExamined = 0` while still finding violations | RED at the count — *"rule T1 examined nothing in its fixture, so it could not have fired: … examined 0 …, found 2 violation(s)."* |

Restored, 109/109 green. **The test can fail, it fails for the right reason, and it names the rule
and the count when it does.** That is a materially better mechanism than the one the first reviewer
asked for.

### The author's disputed reasoning — I agree, with one caveat

- *"Deleting the six per-rule fixture tests leaves this test green, and that is correct."* **Agreed.**
  The test now calls `TenantDbContextConstructorRule.Check(...)` itself; there is no longer a
  dependency on `TenancyRuleTests` for it to be red when a rule stops firing. The first reviewer's
  proposed experiment was a valid probe of the *old* test and is simply not a probe of the new one.
- *"Inverting T1's accessibility check stayed green, correctly, because the rule then fired against a
  different fixture."* **Agreed as far as it goes.** The caveat: it shows the test asserts *some*
  violation, not *the right* violation. A rule that fires on the compliant fixture and stays silent
  on the violating one satisfies this test. That is covered by
  `T1_fires_on_a_public_constructor_and_on_a_protected_one` and
  `T1_stays_silent_on_an_internal_constructor_two_inheritance_steps_from_DbContext`, so the suite is
  fine — but those are now the *only* thing covering it, which is worth one sentence in the test's
  comment so a future pruner knows they are load-bearing.

### The question the author did not ask — yes, there is one, and it is the important one

**Is there a change to a rule that is genuinely broken in production and leaves this test green?**
Yes, and it is not a contrived one. Every inert row runs its rule over
`TypeIndex.Of(FixtureAssembly.AllViolations)` — **one assembly**. Inside one assembly, every hop of
the base-type walk either matches `Microsoft.EntityFrameworkCore.DbContext` by name or resolves
within the same `.dll`. The hop that requires `TypeIndex.Find` to succeed *across* assemblies is
never exercised by any fixture — and it is the one `TypeIndex`'s own doc calls "the point":

> *"Cross-assembly is the point: `SalesDbContext` in one assembly derives from `DbContext` in
> another, and a rule that gave up at the assembly boundary would never recognise a tenant context at
> all - and would report no violations, in green."*

I made `TypeIndex.DerivesFrom` (`tests/Aurora.Architecture.Tests/Rules/TypeIndex.cs:58-78`) abandon
the walk when the next base type lives in a different assembly — the shape of a plausible
"optimisation" — and ran the suite:

```
Passed!  - Failed: 0, Passed: 109, Skipped: 0, Total: 109
```

Then I put the production shape B-06 will actually ship through the faulted rule: `SalesDbContext`
(assembly `Aurora.Modules.Sales.Infrastructure`) → `ModuleDbContext` (assembly
`Aurora.Platform.Tenancy`) → `DbContext`, with a **public** constructor on the module context:

```
unfaulted: recognised tenant contexts: …SalesDbContext, …ModuleDbContext
           T1: examined 2 tenant DbContext types, found 1 violation(s).
faulted:   recognised tenant contexts: …ModuleDbContext
           T1: examined 1 tenant DbContext types, found 0 violation(s).
```

T1 and T2 are blind to every module context in the solution, and the entire architecture suite is
green. **That is what the test is still missing:** a fixture whose base chain crosses an assembly
boundary at a hop the index has to resolve. Cheapest honest fix — a tiny second fixture assembly is
not needed: add one inert-row check (or a `TypeIndexTests` case) built from hand-constructed
`ScannedType` records with two different `AssemblyName`s, exactly as in the probe above. ~15 lines.
**Severity medium, but it is part of M-2 and must land with it.**

---

## Blockers

None in the sense of "the build stops for a live cross-tenant read" — no tenant code exists yet. The
four findings below are all **high**: each is a tenancy control that can be bypassed, or one whose
documentation claims coverage the mechanism does not have. Under the review rules that is a REJECT.

---

## High

### H-1 — any production project under a directory named `Fixtures/` is silently deleted from the entire architecture gate

`tests/Aurora.Architecture.Tests/Solution/SolutionLayout.cs:31-35` and `:150-156`:

```csharp
public const string FixtureMarker = "/Fixtures/";
…
.Where(static path => !path.Replace('\\', '/').Contains(FixtureMarker, StringComparison.Ordinal))
```

**The attack (A8, executed).** A/B, same `.csproj`, same content, only the path differs:

- `src/Aurora.Ghost/Aurora.Ghost.csproj` → the anti-vacuity guard fires, as designed:
  *"These projects under src/ produced no assembly, so the architecture rules would have run over an
  incomplete population…"*
- `src/Fixtures/Aurora.Ghost/Aurora.Ghost.csproj` → **42/42 green.** Nothing is reported. The project
  is invisible to `ProductionProjects`, therefore to L1, L1-L5 and M1; and because
  `ProductionAssemblies` is built from the same filtered list, its types never enter
  `ProductionTypes`, so F1, S3 and **T1–T6 never see them either**. The guard cannot catch it: it
  only checks that the projects it *discovered* produced assemblies.
  `Every_project_under_src_is_compiled_and_in_the_population` compares two numbers derived from the
  same filtered list, so it cannot catch it either.

This is the exact bypass the population guard exists to prevent, and it is reachable by naming a
folder. Worse, it is undiscoverable from the docs: `README.md` §4 states

> *"`SolutionLayout` builds the population from **every `.csproj` under `src/`** — not a hand-kept
> list, so a module added tomorrow is covered without anyone remembering"*

which is false, and `FixtureMarker`'s own doc comment describes the exclusion as covering "the
violating `.csproj` files the project-graph rules are proven against" — **the same claim M-1 was
rejected for**, in the copy that actually does something. M-1 deleted the harmless copy (two MSBuild
items that globbed nothing) and kept the load-bearing one. This was raised as n-6 and consciously
left; raised twice and still live, it is now a finding about the change, not a tidiness note.

**Fix (two lines):** delete `FixtureMarker` and the `.Where(...)` filter. No `.csproj` lives under a
`Fixtures/` folder, `FixtureProjectTree` writes to `Path.GetTempPath()`, and the temp path cannot
collide with `RepositoryRoot`. If the filter is kept for defence in depth, then it must not be
silent: assert in `ProductionPopulationTests` that no `.csproj` under `src/` was excluded by it, and
correct README §4 and the `FixtureMarker` comment in the same change.

### H-2 — T2 is defeated by any registration API not literally named `AddDbContext*`, including a one-line generic helper

`tests/Aurora.Architecture.Tests/Rules/TenantDbContextRules.cs:86-88` and `:112-125`.

**The attack (A2/A3, executed).** Fixture in the violations assembly, scanned by the real scanner:

```csharp
public static void RegisterViaAddScoped(IServiceCollection services) =>
    services.AddScoped<SalesDbContext>();                       // member name is not AddDbContext*

public static void RegisterViaHelper(IServiceCollection services) =>
    RegisterContext<SalesDbContext>(services);                  // member name is not AddDbContext*

private static void RegisterContext<TContext>(IServiceCollection services)
    where TContext : DbContext => services.AddDbContext<TContext>();   // generic argument is !!0
```

Result: `T2: examined 4 AddDbContext* calls, found 2 violation(s)` — the two original fixtures, and
**neither attack**. The helper's `AddDbContext<TContext>` call is counted as a *subject*, which makes
the population floor look healthy while the violation is unreachable.

This matters now, not later: a platform-level `AddTenantDbContext<TContext>()` helper is the most
natural thing B-06 will write, and the moment it exists every module registers its context through a
member whose name T2 does not match. ADR-0007 §4.2 says *nothing* registers a tenant context; the
rule enforces "nothing registers one through a member whose name begins `AddDbContext`". The rule's
"What it cannot see" paragraph lists two other limits and not these two, so the documentation is
narrower than the defect.

**Fix:** match on the argument, not the API name. Flag any call whose declaring type is in
`Microsoft.Extensions.DependencyInjection` (or whose member name starts `Add`/`TryAdd`) and whose
generic arguments name a tenant context; and treat a generic argument that is a *type parameter*
(`!!0`/`!0`) at such a call site as unresolved — report it, or resolve it at the call site that
supplies the concrete type. Add both fixtures.

### H-3 — T5 does not see a hosted service, which is the singleton most likely to hold a tenant proof

`tests/Aurora.Architecture.Tests/Rules/TenantScopeRules.cs:105-116`:
`IsSingletonRegistration(memberName) => memberName.Contains("Singleton")`.

**The attack (A4, executed).** A background worker holding the tenant it last ran for:

```csharp
internal sealed class TenantWarmupService : IHostedService, IAsyncDisposable
{
    private TenantScope? _scope;                       // assigned in StartAsync
    …
}
… services.AddHostedService<TenantWarmupService>();
```

`AddHostedService` contains no "Singleton", so `TenantWarmupService` never enters
`SingletonRegisteredTypeNames` and T5 reports nothing:
`singleton-registered type names: …Attack.ScopeCache` only. `IHostedService` registrations *are*
singletons — and `CLAUDE.md` names background jobs explicitly as a place tenant isolation must be
proven, while B-07's provisioning saga and B-08's migration runner are both going to be hosted
services holding a `TenantDatabaseHandle`.

The same hole covers `AddSingleton<IFoo>(sp => new EvilImpl())`, where only `IFoo` appears as a
generic argument and the implementation type is never checked.

**Fix:** treat every type whose base chain reaches `Microsoft.Extensions.Hosting.BackgroundService`
or whose interface list names `IHostedService` as a singleton holder, regardless of how it is
registered — that needs no registration call at all and is the same trick the static half already
uses. Optionally also match `AddHostedService` by name. Fixture: the one above.

### H-4 — a public factory method reaching a tenant context is covered by neither T1 nor T3, and both docs say the other one has it

`tests/Aurora.Architecture.Tests/Rules/TenantDbContextRules.cs:26-29`:

> *"**What it cannot see:** a context reachable through a public factory method that is not the
> tenant factory. That is T3's job, not this rule's."*

T3 (`Rules/TenantScopeRules.cs:9-31`) inspects **the implemented-interface list**. A static method
implements no interface, so T3's job is not that job.

**The attack (A7, executed).**

```csharp
internal sealed class SalesDbContext : DbContext { internal SalesDbContext(DbContextOptions o) : base(o) { } }

internal static class PublicFactoryDoor
{
    public static SalesDbContext Open(DbContextOptions options) => new(options);
}
```

T1: silent (the constructor is `internal`, which the rule is right about). T3: silent (no interface).
Nothing else looks. This is precisely the "second door" ADR-0007 §4.1 closes — a caller outside the
tenancy assembly obtains a tenant context without a `TenantScope`, without the §4.3 connection
identity check — and the rule set says, in two places, that it is covered.

**Fix, either:** (a) add the rule — any `public`/`protected` member outside
`Aurora.Platform.Tenancy` whose return type names a tenant context, with `PublicFactoryDoor` as the
fixture; or (b) delete the sentence from T1's doc and record the gap in README §6 with a backlog id
(`CLAUDE.md` self-check #1: if you cannot point at the mechanism, delete the claim). (a) is ~20 lines
and closes a real door; (b) is honest. Silently leaving the claim is neither.

---

## Medium

- **m-1 — the catalog exemption is matched by simple name, so any type anywhere called
  `CatalogDbContext` is exempt from T1, T2 *and* the inertness guard.**
  `Rules/TenancyNames.cs:79-88`. **Executed (A1):** a fixture `…Violations.Attack.CatalogDbContext`
  deriving from `DbContext` with a **public** constructor does not appear in
  `TenancyNames.TenantContextsIn`, draws no T1 violation, and would not trip
  `No_tenant_DbContext_exists_yet_which_is_why_T1_and_T2_are_inert`. In an ERP, "catalog" is an
  overloaded word — a product-catalog context in the Products module is a plausible accident, not a
  contrived one. `The_only_context_exempt_from_the_tenancy_rules_is_the_catalog` asserts the *list
  contents*, which does not constrain how many types match. The doc does disclose simple-name
  matching, which is why this is medium and not high. *Fix:* once B-05 lands, exempt the catalog by
  **full name**; until then, assert that at most one production type carries the simple name and
  that it lives in the catalog assembly. This becomes high the day B-05 merges.
- **m-2 — T3's and T6's allow-lists are assembly-name *prefixes*, so a new assembly authorises
  itself.** `Rules/TenantDatabaseHandleRule.cs:45-55`, `Rules/TenantScopeRules.cs:49`. **Executed
  (A9):** `IsAllowed("Aurora.Platform.TenancyTools") = True`,
  `IsAllowed("Aurora.Platform.TenancyBypass") = True`,
  `IsAllowed("Aurora.Platform.Tenancy.Sales.Infrastructure") = True`. The doc comment says *"If
  either lands under a different assembly name, add it here rather than widening the rule — the
  point of an allow-list is that extending it is a reviewable diff."* With a prefix, extending it is
  a **naming choice**, not a diff. ADR-0027 §1 names three holders. *Fix:* exact assembly names, and
  add B-07's and B-08's when those projects exist (the inventory's "expires loudly" pattern is the
  right shape here too); or keep prefixes and require the segment boundary (`name == prefix ||
  name.StartsWith(prefix + ".")`), which at least stops `TenancyBypass`.
- **m-3 — the inert-rule test cannot see a rule broken only across an assembly boundary.** §3 above.
  109 green while T1/T2 go blind to B-06's shape. Part of M-2's rework.

---

## Low

- **l-1 — `Each_inert_rule_still_fires_against_its_fixture` asserts *a* violation, not *the*
  violation** (`RuleInventoryTests.cs:212-220`). An inverted rule that fires on the compliant fixture
  passes it. The per-rule tests cover this; one sentence in the comment saying so would stop a future
  pruner from deleting the thing that does.
- **l-2 — the dangling `FixtureIsolationTests` citation (first review n-2) is still there**
  (`Fixtures/FixtureAssembly.cs:24`); the assertions live in `ProductionPopulationTests`. Trivial,
  but it is the same "name a thing that does not exist" family as M-1.

## Carried forward and still unfixed

- **n-6** (first review) — now **H-1**. Raised twice, left in place, and it turns out to be a live
  bypass rather than dead code. Worth noting in the iteration log as a process signal: the minor that
  was deferred was the half of M-1 that had teeth.
- **n-1, n-3, n-5, n-7, n-8** — unaddressed, still valid, still not merge blockers.
- **Route to the architect (unchanged from the first review, nothing on this branch touched it):**
  ADR-0020 still names ArchUnitNET; `testing-strategy.md` §5, `dependencies.md` and
  `Directory.Packages.props` still carry it with no consumer.
- **New, route to the architect:** ADR-0007 §4.2 says "nothing registers a tenant context" and §4.1
  says the factory is the only door. H-2 and H-4 show both sentences are broader than any mechanism
  can be made without a decision: is *any* DI registration naming a tenant context banned (not just
  `AddDbContext`), and is a public member *returning* a tenant context outside
  `Aurora.Platform.Tenancy` a violation? Both answers are cheap to enforce once stated, and B-06
  needs them stated before it writes the registration helpers.
- **Route to the project-manager:** first review's n-8 — the two deferred ADR-0027 rules still have
  no backlog id; and if H-4 is fixed by option (b), that deferral needs one too.

## What held (so the negative result is on the record)

`Aurora.SharedKernel`'s value objects, the `.csproj`/lock-file state and the whole branch diff are
free of secrets, connection strings and personal data; the branch adds no logging. Generic-collection
fields on singletons, generic base classes, `protected internal` constructors, base-class fields
inherited by a singleton, IL-body-only mentions of the handle, and source-generated types are all
caught — I tried each. The staleness and missing-assembly guards behave exactly as the first review
recorded (I re-ran the missing-assembly one as the control for H-1). Reading compiled IL rather than
source is the right choice for this rule set, and A10 is the argument for it: a Roslyn rule would not
see generated code, and this one cannot tell the difference.
