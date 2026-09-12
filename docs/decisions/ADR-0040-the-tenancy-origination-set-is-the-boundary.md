# ADR-0040 — The tenancy origination set is the boundary: naming is not originating, accessibility is not a control, and what governs the friend list

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** — (nothing is reversed)
- **Amends:**
  - **ADR-0007 §3.4 and §12.3** — the mechanisms are unchanged. What changes is the *statement* of what they guarantee. §3.4's "**No module, no component and no job can fabricate a `TenantScope`**" and §12.3's framing of the guarantee as **compile-time** are both **falsified as written**, by a single `extern` declaration in an assembly on nobody's friend list (§2.1 probe A, executed). §5 restates both with the conditions under which they hold. Both sections carry a dated banner pointing here.
  - **ADR-0032 §4.2** — the population-versus-verdict inversion in §3 applies to T16/T17/T18 as well as to B-06.1a's proof scan. §4.2's decisions are unchanged; §3.4 records what a future revision of those rules should look like and why.
- **Generalises:** **ADR-0034 §6.1**, the only governance of an `[InternalsVisibleTo]` grant that exists today. Its three conditions for `Aurora.TestKit` are unchanged and remain the binding answer for that grant; §4 is the standing rule they are an instance of.
- **Superseded by:** —
- **Related:** ADR-0007 (tenancy), ADR-0008 §9 (the package load seam), ADR-0029 (the `tid` claim and fail-closed permission evaluation), ADR-0030 (fitness rules read IL metadata), ADR-0031 §2 (the package reference allowlist), **ADR-0033 (the tenancy trust boundary is the process — read it before this one)**
- **Raised by:** `ARCH-Q-SCOPE-BOUNDARY`, from the second, third, fourth and fifth reviews of PR #13 (`task/B-06.1a`)

> Seven doors through one rule across five reviews, and then two routes that are not doors in that rule at all: **`[UnsafeAccessor]` mints a `TenantScope` from an assembly no `[InternalsVisibleTo]` names**, and **`Unsafe.As<TenantScope>` gets a usable reference with no declaration to grep for** — both with no reflection, no friend and no compiler diagnostic. This ADR builds no eighth mechanism. It says what the friend set actually buys, what the guarantee may claim, and what governs membership — and it states no list as closed, because the last two times this document did, execution closed it for us.

---

## 1. What is already decided, with section numbers

The question was routed with an explicit invitation to answer "the ADRs already say this" where they do. Saying so is part of the value; the other part is that one of its premises is **too weak**, not too strong.

**Sub-question 2 — *what is the control for a package that reflects past the friend set; does a package context get the tenancy contracts assembly at all?* — is answered by ADR-0033, and nothing here adds a control.**

| Asked | Answered at |
|---|---|
| Does a package's `AssemblyLoadContext` hand it `Aurora.Platform.Tenancy.Contracts`? | **ADR-0033 §3, links L3/L4**, and **§3.1**, which executed both halves: binding the assembly *by name* from inside the package context is refused (`FileLoadException`); the package reaches the already-loaded instance through `AppDomain.CurrentDomain.GetAssemblies()` without resolving anything |
| What stops reflection there? | **ADR-0033 §4 items 1–4** — nothing does, and .NET 10 offers no mechanism that could |
| So what is the control? | **ADR-0033 §5.2** — admission, not confinement. **§5.3** — admitting a package is a fleet-wide grant |
| And the residual? | **ADR-0033 §5.4 R1–R3**, with **§5.6 D2** requiring the mint to live in the repository as a test |

**But the question's own framing understates it.** `ARCH-Q-SCOPE-BOUNDARY` says a package could *reflect* a scope into being. §2.1 probe A shows it does not need to. An ADR that answered only the reflection form would read as complete while leaving a shorter route open, so §2 and §5 are written against the stronger fact.

**Sub-question 3 — *should ADR-0007 §12.3's guarantee be restated as scoped?* — yes, and more than "scoped".** ADR-0033 §5.1 already banners §12.3 against an in-process adversary. Two further conditions have no record anywhere: the guarantee holds only while **no assembly in the friend set publishes origination** (§3.5), and — the one nobody had — **only against code that uses ordinary member lookup** (§2.1).

**Sub-question 1 — *is the friend set the stated boundary, and what governs adding to it?* — is not answered anywhere.** ADR-0034 §6.1 is the only governance in the repository: three conditions attached to one anticipated grant on one assembly. §2 and §4 are the answer.

---

## 2. Decision 1 — the boundary is **origination by ordinary lookup**, and it is stated as a boundary

### 2.1 Executed, 2026-09-12

All probes built against the `Aurora.Platform.Tenancy.Contracts.dll` compiled from `task/B-06.1a` (4f8b09b). Every probe assembly is named by no `[InternalsVisibleTo]` anywhere.

**Probe A — `[UnsafeAccessor]`.** `Aurora.Outsider.NotAFriend`, a `net10.0` console app referencing only the public contracts assembly. No reflection anywhere in the file; the whole route is one BCL attribute on one `extern` declaration:

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Constructor)]
private static extern TenantScope Mint(
    TenantId tenantId, TenantKey tenantKey, Region residencyRegion,
    SchemaVersion schemaVersion, InstalledPackages packages, TenantAccessReason reason);
```

```
### BUILD (does the C# compiler complain?)
Build succeeded.  0 Warning(s)  0 Error(s)
### RUN
this assembly                : Aurora.Outsider.NotAFriend
contracts grants internals to: [Aurora.Platform.Tenancy, Aurora.Platform.Tenancy.UnitTests]
am I a friend?               : False
UnsafeAccessor minted a scope: key=acme-trading active=True reason=OperatorSupport
                               tenant=019230b0-5c6a-7c3e-9a2f-0f1e2d3c4b5a region=nz schema=1
ctor that ran was internal   : True
```

Three properties of that output decide most of this ADR:

1. **It is not reflection.** No `Activator`, no `GetUninitializedObject`, no API call, no permission. The runtime resolves the target and **deliberately skips the visibility check** — that is the attribute's documented purpose.
2. **The constructor ran.** `IsActive=True`, and the scope carries `OperatorSupport`, the most privileged member of `TenantAccessReason`. Unlike a hollow scope, nothing about it reads as absent. **B-06.1a's link 5 — the one link designed to catch a fabricated scope — does not apply to this route at all.**
3. **The declaration only names public types.** `TenantScope`, `TenantId`, `TenantKey`, `Region`, `SchemaVersion`, `InstalledPackages` and `TenantAccessReason` are all public and must be (ADR-0007 §4.5). **Naming is now demonstrably sufficient to construct.**

**Probes B and C — where the accessibility check actually lives.** A minimal two-assembly fixture (`SeedLib` / `SeedOutsider`, no grant), because the answer determines whether a design change could close probe A:

```
A. internal ctor, all-public signature        : p active=True
B. internal ctor, internal param via generic  : MissingMethodException: Method not found: 'SeedLib.Guarded..ctor'.
C. naming the internal parameter type         : error CS0122: 'Origin' is inaccessible due to its protection level
```

One sentence explains all three: **accessibility is enforced on the *signature of the accessor declaration*, and not on the *target it resolves to*.** An accessor whose signature names only public types compiles and binds (A). Substituting a type parameter for an inaccessible parameter type does not bind, because the runtime matches the signature by type identity (B). And an outsider cannot write the signature at all if one parameter type is inaccessible to it (C).

**Probe D — the ordinary route, for contrast.** The same outsider assembly compiles a cast of an `object` to `TenantScope` and reads every public property; it compiles a hold, a pass and a public re-export; and it fails only here:

```
Mint.cs(9,13): error CS1729: 'TenantScope' does not contain a constructor that takes 6 arguments
```

One source file was added to produce that error and removed to produce the successful build, so the demonstration fails in both directions rather than asserting one.

**Probe F — `Unsafe.As<TenantScope>(object)`, which is none of the above.** `Aurora.Outsider.FourthRoute`, also on nobody's friend list, referencing only the public contracts assembly. No attribute, no `extern`, no reflection, no friend involved — one line of public BCL API:

```
4a Unsafe.As<TenantScope>: compiled and ran in a non-friend assembly.
   static type             : TenantScope (the compiler accepts every member access below)
   runtime `is TenantScope`: False
   GetType()               : Impostor
   reading through it      : IsActive=False Reason=Request  (no exception)
   constructor executed    : no. None of TenantScope's six argument checks ran.
```

**What this establishes:** a non-friend obtains a reference the compiler treats as a `TenantScope` and reads members through it without throwing, with no declaration that probe E's signal or a reflection scan could find. The constructor never runs, so every guard in it is bypassed, and unlike `GetUninitializedObject` there is no link-5 defence to apply — link 5 is about an object of the *right* type left uninitialised.

**What this does not establish, in two attempts.** PR #21's reviewer reports `IsActive=True` from the same route. I could not reproduce controlled field values: a stand-in whose fields are declared in the reference assembly's declaration order raised `NullReferenceException` on read, because the CLR lays out auto-layout classes in an order of its own choosing. So the route is a **type-confusion** route, not a forgery route, and matching the physical layout is work I did not do. That distinction is worth keeping, because it is also the route's one cheap detection: `is TenantScope` and `GetType()` both give it away, where probe A's scope is indistinguishable from a real one. **Nothing in the design performs either check.**

**Probe E — the metadata signature of an `[UnsafeAccessor]` member**, read off probe A's own assembly, because §4.6's rule needs to know what it can key on:

```
Program.Mint  attrs=Private, Static, HideBySig  impl=IL  bodyless=True   abstract=False  pinvoke=False
              customAttrs=[NullableContextAttribute, UnsafeAccessorAttribute]
Program.Main  attrs=Private, Static, HideBySig  impl=IL  bodyless=False  abstract=False  pinvoke=False
```

A non-abstract, non-P/Invoke method **with no body** is a metadata signal today's scanner can reach without the custom-attribute support ADR-0032 §1.1 says it lacks.

### 2.2 The boundary, named — and what it is a boundary *of*

The fourth review's formulation — *"the set of assemblies permitted to **name** these types is the real boundary"* — is confirmed by probe A in the strongest possible way and is simultaneously unachievable: `TenantScope` is public and must stay public, and ADR-0034 §6 draws exactly this contrast when it keeps `TenantConnection` internal (*"`TenantScope` is in `.Contracts` because every application-service method takes one… `TenantConnection` is the opposite"*). Restricting who may name it was never available.

So the set that matters is not "who can", because after probe A that set is **every assembly in the process**. It is:

> **The tenancy origination set** is the set of assemblies that can originate a tenancy proof — a `TenantScope`, and by ADR-0027 §1 a `TenantDatabaseHandle` — **through the language's ordinary member lookup**: `new`, a call, a base-constructor chain.
>
> It is **(a)** the transitive `[InternalsVisibleTo]` closure of `Aurora.Platform.Tenancy.Contracts` and `Aurora.Platform.Tenancy`, **plus (b)** every assembly a member of that closure hands origination to through its own published API.
>
> **What it is a boundary of:** the line between origination that is **invisible in review** and origination that **announces itself**. Inside the set, `new TenantScope(...)` is ordinary code that no reader would question. Outside it, every route that works is one a reader can see is deliberate — an `[UnsafeAccessor]` declaration, a `ConstructorInfo.Invoke`, a `GetUninitializedObject`. **That is a real and valuable property and it is the only one the mechanism has.**

Clause **(b)** is the half that took the member-scan reviews, and it is why the closure alone is not the set: a public member of `Aurora.Platform.Tenancy` returning a scope adds every assembly that can call it, with no `.csproj` diff anywhere.

### 2.3 Accessibility is not worthless — it is a different thing than it was described as

Probe A does **not** license the conclusion that `internal` should be abandoned or that the compile-time work was wasted. It is the mechanism that keeps the honest routes honest: it is why every first-party module has to ask the factory, why no serializer can materialise a scope (link 4), and why a reviewer reading a diff sees the difference between code that was handed a scope and code that manufactured one.

What it cannot be is **the sentence the guarantee rests on**. ADR-0007 §3.4's "no module, no component and no job can fabricate a `TenantScope`" and §12.3's *compile-time* framing are both falsified by one `extern` declaration. §5 restates them.

---

## 3. Decision 2 — why seven reviews found seven doors, and the level to defend at

### 3.1 The pattern, classified

| # | Door | Defect class |
|---|---|---|
| 1 | direction inference — an `event`, and an `Action<TenantScope>` parameter | **verdict**: examined, and judged inbound |
| 2 | inherited members — `public sealed class OpenScopeCollection : List<TenantScope>` | **population**: the type declared no member |
| 3 | `protected` members on an unsealed public type | **population**: `BindingFlags.Public` never sees them |
| 4 | explicitly implemented non-generic interface members | **population**: emitted private |
| 5 | a **default interface member** explicitly implementing a proof-returning member | **population** |
| 6 | an explicit implementation **inherited from a base outside the scanned population** | **population** |
| 7 | erasure — `object`, `dynamic`, non-generic `IEnumerable` (§3.5) | **neither: invisible in principle** |

Two things found in the same reviews are **not** in this table, deliberately: `[UnsafeAccessor]` (§2.1 probe A) and `Unsafe.As` (probe F). Neither is a door through the member scan — neither involves a member of a friend assembly at all. They are origination routes, they belong to §5, and counting them here would make the member scan look responsible for something it was never positioned to see.

**Six of the seven were population defects.** The scan enumerated a *filtered* population and asked a correct question of each member in it; each review added one filter term. That is a blocklist over an open set — the CLI's member and accessibility model is not something anyone finishes enumerating from memory — and the examined count stayed healthy through every one. ADR-0032 §4.2.2 had already observed the same for a different clause: *"T17's examined count does not move… a healthy count is exactly what a missing clause hides behind."*

Door 1 was a **verdict** defect, and it was fixed by the right move: the verdict stopped classifying *direction* and became "mentions anywhere, allow-list the exceptions by exact key" — a complement. **Seven doors, seven fixes, each correct about the door it was shown.** That is the executed argument that shape-enumeration is the wrong level, and it is why this ADR adds no eighth term.

### 3.2 The rule that generalises

> **When a rule must hold over "everything reachable", do not enumerate the reachable ones. Enumerate everything, and make *unreachability* the thing that has to be proved — member by member, with an unclassified case throwing.**

This is not novel here; it is the shape of the **derivability check** B-06.1a already wrote, and it is why door 3 is closed *for good* rather than enumerated. `DerivableTypes` does not list which protected members are reachable. It proves the stronger, simpler fact that **no public type in the friend set can be derived from outside it**, after which no protected member is reachable whatever anyone declares later. Apply to the population the move already applied to the verdict.

### 3.3 What that means concretely

- Population: every member of every type in a friend assembly at **every accessibility** (`Public | NonPublic | Static | Instance | DeclaredOnly`), not the public ones plus hand-added side channels.
- Verdict: a computed `ExternalReachability` — `Public`, `ProtectedOnADerivableType`, `ExplicitInterfaceImplementation`, `DefaultInterfaceMember`, `InheritedFromOutsideThePopulation`, `NotExternallyReachable` — with a **throwing default arm**, the discipline `ProofMentionScan` already applies to member *kinds* and does not apply to member *reachability*.
- Report **members examined / reachable / not reachable / mentions**. Today it reports examined and mentions and **not the excluded count**, so a shrinking population is invisible. Doors 2–6 each moved members from the unexamined set into the examined set, and nothing ever printed the size of the unexamined set.
- The fault that must turn it red: remove one reachability arm and **`not reachable` rises by exactly that arm's members while `examined` is unchanged.** That is a number that moves when a door opens.

### 3.4 The same inversion applies to ADR-0032 §4.2

T17 has the identical shape and the identical exposure: its population is "externally reachable members", chosen by a filter, and its verdict enumerates the shapes that count. Nothing in §4.2's decisions changes — they are correct as decided — but the row that next revises T16/T17/T18 should carry §3.2's inversion, and the ADR-0032 banner says so.

### 3.5 Door 7: the class no member scan can close — executed

`ProofMentionScan`'s own remarks name it. That claim was executed rather than trusted, against the real scan on `task/B-06.1a`:

```
ARCH-Q-SCOPE-BOUNDARY probe: ProofMentionScan.Over(ErasureDoors) examined 14 public members,
  0 explicit implementations, over 1 types; mentions reported: 0 []
ARCH-Q-SCOPE-BOUNDARY probe: DerivableTypes(ErasureDoors) examined 1 types;
  derivable from outside: 0 []
```

`ErasureDoors` is fourteen public members of which every one hands out or receives a scope: `object OpenAsObject()`, `IAsyncDisposable OpenAsDisposable()`, `IEnumerable OpenAsNonGenericSequence()`, `dynamic OpenAsDynamic()`, `object? Current { get; set; }`, `bool TryOpen(out object)`, `event Action<object> Opened`, `void OnOpen(Action<object>)`, `ValueTask<object> OpenAsync()`. Both scans report **zero**. The probe file was deleted after the run; it is on no branch.

Combined with probe D, the consequence is decisive: **`public static object Open()` in a friend assembly, and `(TenantScope)Friend.Open()` in an assembly with no grant, both compile, and no signature scan can ever report either.** The information is not in the metadata.

For contrast, the real scan on the same branch: `155 public members and 0 explicit interface implementations examined over 20 public types in 2 assemblies; 1 mention a proof, 1 allow-listed; floor 140`, and `20 public types examined for derivability; 0 derivable from outside`.

---

## 4. Decision 3 — what governs membership

The brief asked what makes the boundary **enforceable rather than asserted**, given that today it is a literal in `TenantAccessConstructionTests` and B-06.3 is about to want to edit it.

> **A membership list cannot be made unfalsifiable. It can be made *complete* and *visible*, and today's mechanism is neither.** That — not the list itself — is the gap, and it is closable.

### 4.1 Why today's assertion is not yet a boundary

`TenantAccessConstructionTests` asserts `Grants(Contracts)` and `Grants(Tenancy)` as exact sets. Both are good and both stay. What they cannot see is a grant **on a third assembly**: they read the two assemblies they name, so any `InternalsVisibleTo` elsewhere in `src/` is outside their population by construction. Five grants exist today across nine production assemblies (executed, `task/B-06.1a`):

```
Aurora.Platform.Tenancy.Contracts.csproj:32  -> Aurora.Platform.Tenancy
Aurora.Platform.Tenancy.Contracts.csproj:33  -> Aurora.Platform.Tenancy.UnitTests
Aurora.Platform.Tenancy.csproj:46            -> Aurora.Platform.Tenancy.UnitTests
Aurora.Platform.Tenancy.csproj:47            -> Aurora.Platform.Tenancy.IntegrationTests
Aurora.Countries.Hosting.csproj:34           -> Aurora.Countries.Hosting.UnitTests
```

Nothing in the repository asserts anything about the fifth grant or about the sixth.

**Read the branch label on that block.** All five exist on `task/B-06.1a`; the integration branch has **three** (the two `Aurora.Platform.Tenancy.Contracts` grants arrive with B-06.1a). Every count in §4.2 is on the five-grant tree, because that is the tree the rest of this ADR measures.

### 4.2 G1 — the repository asserts that it contains no grant outside a stated rule

**The first draft of this rule shipped red, and its own §4.1 evidence block was the counter-example.** Clause (a) said *"the **declaring assembly's** own `<name>.UnitTests`/`<name>.IntegrationTests`"*, and the declaring assembly of two of the five grants is `Aurora.Platform.Tenancy.Contracts`, whose own test projects would be `Aurora.Platform.Tenancy.Contracts.UnitTests`/`.IntegrationTests` — neither of which is what it grants to. Implemented verbatim and run over the tree §4.1 measures:

```
OLD clause (a): the DECLARING ASSEMBLY's own <name>.UnitTests/.IntegrationTests
  found=5  clause-a=3  pinned=0  unlisted=2
    UNLISTED: Aurora.Platform.Tenancy.Contracts -> Aurora.Platform.Tenancy
    UNLISTED: Aurora.Platform.Tenancy.Contracts -> Aurora.Platform.Tenancy.UnitTests
  VERDICT: FAIL (build would go red)
```

**The obvious repair is the wrong one and is refused here so nobody reaches for it under a red build.** Widening clause (a) to a prefix or a sibling match would admit both, and would also admit a grant from any assembly to any other whose name happens to start the same way — which turns *"every grant is either a module's own test project or a deliberate decision"* into *"every grant is fine"*. The distinction the rule exists to draw is **a test project versus a shipping assembly**, and the fix has to keep drawing it.

> **Every `<InternalsVisibleTo>` in every `src/**/*.csproj` is either**
>
> **(a)** to one of the **granting assembly's own module's** test projects — the module being the granting assembly's name with a trailing `.Contracts` removed, the grantee being exactly `<module>.UnitTests` or `<module>.IntegrationTests`, **and** the grantee's project file living under `tests/` and being referenced by no project under `src/`; **or**
>
> **(b)** to an assembly named in the rule's own pinned list, with the ADR clause that admits it beside the entry.
>
> The rule reports **grants found / module-test grants / pinned grants / unlisted**, and an unlisted grant fails.

**Why the location clause is not decoration.** ADR-0032 §2 is explicit that a rule may key only on an identity the violating code cannot mint for itself, and a `.UnitTests` suffix is a name the grantee chooses. *"Its project file is under `tests/` and no `src/` project references it"* is not — it is ADR-0034 §6.1 condition 2's own test, and `Aurora.Architecture.Tests/Solution/` already reads project files and their `ProjectReference`s. The name and the location are required together: a project named `…UnitTests` that lives under `src/` fails the location test, and one under `tests/` that ships fails the reference test.

**Executed, on the same five-grant tree:**

```
NEW clause (a): the granting assembly's MODULE's test projects, under tests/, unreferenced by src/
  found=5  module-test=4  pinned=1  unlisted=0
    pinned:   Aurora.Platform.Tenancy.Contracts -> Aurora.Platform.Tenancy
  VERDICT: PASS
```

**The pinned list holds exactly one entry, and it is the right one.** `Aurora.Platform.Tenancy.Contracts → Aurora.Platform.Tenancy` is not an exception to the rule; it **is** §2.2 clause (a)'s closure — the grant that constitutes the origination set. Admitted by **ADR-0007 §3.4** (*"only `Aurora.Platform.Tenancy` may construct one"*). A rule whose pinned list contains the single most load-bearing grant in the repository, named with the decision that admits it, is a better artefact than one whose list is empty because the clause was widened until nothing needed pinning.

**And a sixth grant, injected four ways, each run separately:**

```
Aurora.SharedKernel        -> Aurora.Platform.Tenancy            UNLISTED  FAIL
Aurora.Platform.Tenancy    -> Aurora.TestKit                     UNLISTED  FAIL
Aurora.Countries.Hosting   -> Aurora.Platform.Tenancy.UnitTests   UNLISTED  FAIL
Aurora.Platform.Tenancy    -> Aurora.Platform.Tenancy.SmokeTests  UNLISTED  FAIL
```

The second matters most: it is **exactly the grant ADR-0034 §6.1 says must be a decision**, and clause (a) refuses it rather than absorbing it, because `Aurora.TestKit` is not the granting module's test project. The fourth shows the clause names two exact projects rather than "any test project of the module".

**What clause (a) does not stop, stated rather than left to be discovered.** A grant from `X` or `X.Contracts` to `X.UnitTests`/`X.IntegrationTests` is admitted silently, and for `X = Aurora.Platform.Tenancy` that means both tenancy test assemblies are in the mint set with no entry anywhere. That is the intended design (the contracts `.csproj` says so in as many words) and it is separately pinned by link 2's two exact-set assertions — so G1 is the **completeness** mechanism and link 2 is the **tenancy-specific** one, and neither replaces the other. The residual is a derivation that widens without a diff to any rule's data, which is the same weakness ADR-0032 §4.2 records for its owning-set derivation and contains the same way: creating a project inside an existing module's name is itself a reviewable event.

Structural, over a population the build already owns, with **no second copy of anything**: the `.csproj` items are the single source of the grants, and the pinned list holds only what clause (a) does not reach.

**It deliberately has no table of assembly names in this ADR.** `FOLLOWUP-058` is the record of what a hand-copied restatement of a machine-read value does here: `verify.sh`'s floor and its `--help` text drifted twice in two merges and neither drift could fail anything. A table of grant names here would be the third instance. This ADR governs the **criteria** (§4.3); the names live where the build reads them.

**Where G1 stops:** at the `.csproj` file. An `[assembly: InternalsVisibleTo("…")]` in a `.cs` file, or injected by a `Directory.Build.props` import or an SDK, is not in its population. That is exactly what the two existing reflection assertions cover, because they read the *attribute* on the loaded assembly. The two are complements and both are required, and G1 must additionally assert — for the assemblies the tenancy tests name — that **the attribute set observed by reflection equals the item set read from the project file**. `Aurora.Architecture.Tests` already reads project files (`Solution/`), so this costs a glob and a comparison.

### 4.3 G2 — what a grant costs

Generalising ADR-0034 §6.1's three conditions. A new grant is admitted only when all four hold:

1. **The grantee must *originate* a proof, not *use* one.** Probe D is executed evidence that a consumer needs no grant.
2. **The grantee's export surface joins the proof scan the day the grant lands** — it ships, and `ShippingFriends` picks it up transitively, or it is test-only and no project under `src/` references it (ADR-0034 §6.1 condition 2, unchanged).
3. **The grant is recorded in the same commit as the `.csproj` change**, in G1's pinned list, with the ADR clause that admitted it beside it.
4. **The task is reviewed at Full tier as a tenancy change**, whatever else it is.

**Where G2 stops:** at a reviewer. G1 makes an *unrecorded* grant fail the build; nothing makes a *recorded* one wise. Same stop ADR-0037 §6.2 names for `Reason` and ADR-0033 §5.3 names for "reviewed like core", written at that size rather than dressed as a mechanism.

### 4.4 G3 — refused, with the reason

`CODEOWNERS` plus branch protection would make an edit to a grant-bearing `.csproj` mechanically require an architect review. **There is no `CODEOWNERS` file in this repository** (checked: `find . -name CODEOWNERS` returns nothing) and branch protection is a repository setting no agent here configures. Naming it as a control would be naming a control that rests on unscheduled work — the defect ADR-0033 §5.5 spends a paragraph avoiding. Recorded as the option and its prerequisite; **not** part of this decision.

### 4.5 B-06.3 does not need a grant — it needs a door, and a door is the bigger widening

Its factory lives in `Aurora.Platform.Tenancy`, already in the closure; B-06.3 changes no `.csproj`. What it adds is the **first entry in `SanctionedDoors`**, and that is the larger widening: a public `ITenantScopeFactory.OpenAsync` moves the effective origination set from "the closure" to "the closure, plus every assembly that can call that member". A grant is visible in a `.csproj`; a door is one line in an allow-list inside a test.

> **`SanctionedDoors` and `ProofTakingMembers` are governed by §4.3 exactly as the grant list is**, condition 4 included.

What bounds a sanctioned door is **not accessibility** — the door is public by definition. It is authorization *at the door*. And here the honest answer is the short one:

> **No authority control exists at issuance today, and no ADR specifies one.**

The first draft of this section handed that job to ADR-0029's `tid` cross-check. It cannot do it, for three reasons that are all structural rather than schedule-related:

1. **It is upstream of the door, not at it.** ADR-0029 §4's checkpoint table, row 1: *"Every HTTP request carrying an authenticated principal, in the tenant-resolution middleware, **after** host/path resolution and **before** `ITenantScopeFactory.OpenAsync`"*. A check positioned before the door is bypassed by every caller that reaches the door another way — and "carried by middleware ordering" is the exact class of thing ADR-0029's own A1.2 rework existed to remove.
2. **It covers one of six reasons.** `TenantAccessReason` is `Request | Job | Outbox | Provisioning | Migration | OperatorSupport`. `AccessSubject.ForSystemJob` carries *"no `tid` claim at all"*, deliberately (ADR-0029 §4 and A2.2), so for the five system reasons there is no `tid` to compare. Worse for three of them: A2.2 item 3 has `ForSystemJob` **refuse unless `scope.Reason` is `Job` or `Outbox`**, so `Provisioning`, `Migration` and `OperatorSupport` have no `AccessSubject` factory at all.
3. **The cited layer consumes the proof, so it cannot gate it.** Both factories take a scope as a parameter — `FromPrincipal(ClaimsPrincipal, TenantScope)` and `ForSystemJob(TenantScope, SystemPrincipalId)`. A mechanism whose input is a `TenantScope` is downstream of origination by construction.

`OperatorSupport` is the most privileged member of the enum and is the reason §2.1 probe A's forged scope carried. Specifying its authorization as "ADR-0029's `tid` check" would have written a cross-tenant escalation path into B-06.3's acceptance criteria.

**What a control at issuance would have to do**, stated as a requirement on B-06.3 rather than as a mechanism this ADR invents — a mechanism is B-06.3's to design:

> `OpenAsync` decides, **inside the factory**, from inputs that do not include a `TenantScope`. Per reason: for **`Request`**, ADR-0029's `tid`-versus-resolved-tenant comparison, moved into the factory rather than trusted from the middleware that ran before it. For **`Job`** and **`Outbox`**, the tenant set is fixed by the job's *registration*, not chosen at call time, and the named system principal is ADR-0010 rule 9's (*"A job never runs 'as nobody'"*). For **`Provisioning`**, **`Migration`** and **`OperatorSupport`**, a named system principal with an explicitly enumerated permission set, and — for `OperatorSupport` — an entry in `catalog.operator_audit_event` (ADR-0007 §9.2) written before the scope is returned, because it is the one reason whose legitimate use is a human reaching into a customer's data.

**Present tense.** `ITenantScopeFactory` is B-06.3 and is not built. ADR-0029's checkpoint 1 is B-18.4 and is not built. `TenantIdentityStamp` is asserted on **no path today** — the type ships with B-06.1a (in rework), the app-path initializer is B-06.2 and the DDL-path factory is B-07.1, neither built (ADR-0034 §5) — and in any case it is a routing control, not an authority one (ADR-0033 §2; ADR-0034 §4). **Nothing in this ADR is a mitigation that rests on any of them**, and until B-06.3 lands, a sanctioned door is bounded by review and by nothing else.

### 4.6 G4 — `[UnsafeAccessor]` is refused in `src/`, and this is the rule probe A creates

An `[UnsafeAccessor]` declaration is silent to the compiler and **loud in source**. That asymmetry is the whole of §2.2's property, and it is worth a mechanism:

> **No type in `src/` names an API whose documented purpose is to step around the type system or its accessibility rules.** Population: every method in every production assembly, and every type they reference. Report **methods examined / body-less non-P/Invoke methods found / bypass-API references found / allow-listed**. The allow-list is **empty**, and an entry in it is a Full-tier architecture decision, not a task's choice.

**The population is a named family, not a single attribute, and that is the correction probe F forced.** The first draft of G4 said *"`[UnsafeAccessor]`"*, and `Unsafe.As` walks straight past it. Enumerating *routes* is the same mistake §3 diagnoses in the member scan one level up; the fix is the same move ADR-0037 §2.3 makes for limb B — enumerate the **surfaces the platform provides for the purpose**, not the uses people find for them:

| # | Surface | Why it is in the family |
|---|---|---|
| F1 | `System.Runtime.CompilerServices.UnsafeAccessorAttribute` | Documented to skip the visibility check. Probe A |
| F2 | `System.Runtime.CompilerServices.Unsafe` (`As`, `AsRef`, `NullRef`, …) | Reinterprets a reference without a type check. Probe F |
| F3 | `System.Reflection` — `Activator`, `ConstructorInfo.Invoke`, `FieldInfo.SetValue`, `Type.GetType` | ADR-0033 §3.1 |
| F4 | `System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject` | Allocates without a constructor. Link 5's subject |
| F5 | `System.Runtime.InteropServices.MemoryMarshal` and pointer `unsafe` blocks | Same capability as F2 by a different door |
| F6 | `System.Reflection.Emit` / `System.Linq.Expressions.Expression.Compile` | Emits IL that the C# accessibility rules never saw |

**Where the family's enumeration stops: at human knowledge of the BCL.** That is the identical stop ADR-0037 §2.3 takes and names, and the mitigation is the same — the question becomes *"which bypass surfaces have we walked?"*, which has a finite answer somebody can be held to, rather than *"did we think of every trick?"*, which does not. **This list is the routes known on 2026-09-12 and is not claimed complete**; §5 says what would find another.

**Why repository-wide rather than "…targeting a tenancy proof".** These surfaces reach every `internal` member of every assembly — `TenantConnection`'s credential-carrying constructor (ADR-0034 §6), `CatalogDbContext`, a Country Package's internals. A rule scoped to tenancy would have to be rewritten the first time somebody points the same API somewhere else.

**The chain, and where it stops.**

| Link | Established by | Stops at |
|---|---|---|
| The declaration exists in the assembly | `UnsafeAccessorAttribute` on the method | **ADR-0032 §1.1: the scanner cannot read custom attributes today.** Either extend it, or use the metadata proxy below |
| The metadata proxy for F1: a **non-abstract, non-P/Invoke method with no body** | Executed, probe E: `Mint` is `bodyless=True`, `abstract=False`, `pinvoke=False`; `Main` is `bodyless=False` | The proxy is a *superset*, and its one systematic false-positive class is a **`delegate`** — `Invoke`/`BeginInvoke`/`EndInvoke` are non-abstract, non-P/Invoke and carry no IL body. **Executed: `delegate` declarations in `src/` today: 0**, so the allow-list is genuinely empty and will gain its first entry the day somebody declares one. A false positive is loud and fixable, which is the safe direction (ADR-0032 §4.2's subject-versus-exclusion rule). The implementing row reports which signal it used |
| F2–F6 are caught as **type and member references**, which ADR-0032 §1.1 says the scanner already reads (declaring type, member name, signature per instruction) | — | **Not executed.** I did not build this half. The implementing row must demonstrate that a `Unsafe.As<T>` call site is reported, because that is the case that motivated the widening |
| …therefore no first-party assembly steps around accessibility **through a surface on this list** | — | **The list is not complete** (F1–F6 above), and **a Country Package's source is not in `src/`** — G4 cannot see it and does not reduce ADR-0033's residual by one bit. The first draft of this row said *"no first-party assembly steps around accessibility silently"*, full stop; probe F falsified it |

### 4.7 A design change considered and refused: an inaccessible parameter on the constructor

Probe C shows a real mitigation exists. Give `TenantScope`'s internal constructor one parameter of an `internal` type — say `TenantScopeOrigin` — and a non-friend cannot write the accessor signature at all (`CS0122`), and cannot substitute a type parameter for it (probe B, `MissingMethodException`). Probe A's route closes and the adversary is pushed back to reflection.

**It is refused, and the reason is this project's own decision rather than taste.** ADR-0033 §2's final paragraph:

> *"A countermeasure aimed at scope forgery would not reduce this threat. Anyone tempted to 'fix' §2 by hardening `TenantScope` should read this paragraph twice."*

**And it buys less than it looks.** The seed parameter works by making the *accessor declaration's signature* unwritable (probe C, `CS0122`) or unbindable (probe B, `MissingMethodException`). Both only bite on routes that **resolve and call the constructor**. Against §5's four known routes:

| Route | Closed by the seed parameter? |
|---|---|
| (a) `[UnsafeAccessor]` | **yes** |
| (b) reflection | no — `GetUninitializedObject` never calls a constructor |
| (c) a friend's erasure door | no — the friend has the internal type |
| (d) `Unsafe.As` | no — it allocates nothing and calls no constructor, so no constructor signature can stop it |

**One of four**, and the three it misses include the two that produce a scope reading as live. ADR-0033 §5.4 R2 records that the same package can read the process's secret material and open its own `NpgsqlConnection` to any tenant database while naming no tenancy type at all. Adding a structural defence against a threat the architecture has explicitly accepted elsewhere, which moves the adversary one line sideways, buys nothing and produces an inconsistent claim.

**Revisit trigger, narrowed so the expiry test is true.** The first draft said *"if the out-of-process host is built, `[UnsafeAccessor]` becomes the top of the residual list"* — but in that world `Unsafe.As` sits at the top alongside it and the seed parameter still does not reach it. The honest trigger is: **the seed parameter earns its keep only when something else closes type-identity forgery** — an out-of-process host (ADR-0033 §5.5), or a runtime that checks reference types on reinterpretation. Until then it is a contract change that closes one of four.

---

## 5. Decision 4 — ADR-0007 §3.4 and §12.3, restated with their conditions

**As written (ADR-0007 §3.4):** *"No module, no component and no job can fabricate a `TenantScope`."* **False** — probe A.

**§12.3's framing, that the guarantee is a compile-time one verified by fitness tests over assembly metadata:** the tests are correct about what they measure. The framing is **false** as a statement about origination — probe A compiles clean, with zero warnings, in an assembly no grant names.

**As it holds:**

> **No assembly outside the tenancy origination set obtains a `TenantScope` through ordinary member lookup.** `new` is refused (`CS1729`, probe D), no parameterless constructor exists at any accessibility, and both in-box serializers refuse (link 4).
>
> **Every other route runs through §4.6's bypass family, and the routes known on 2026-09-12 are these four:**
>
> | | Route | Constructor runs? | Reads as live? | Executed |
> |---|---|---|---|---|
> | (a) | `[UnsafeAccessor]` in the obtaining assembly | **yes** | **yes** | §2.1 probe A |
> | (b) | reflection in the obtaining assembly | no (`GetUninitializedObject`) or yes (`ConstructorInfo.Invoke`) | inert, or **yes** | ADR-0033 §3.1; probe D |
> | (c) | a *friend* assembly's member whose signature erases the type | yes — the friend's own call | yes | §3.5 |
> | (d) | `Unsafe.As<TenantScope>(object)` in the obtaining assembly | **no** | type-confused; `is TenantScope` is **False** | §2.1 probe F |
>
> **This list is not claimed exhaustive, and the claim that it was is what this sentence replaces.** The first draft said *"exactly three routes work… nothing else works"*; route (d) falsified it within one review. §3 of this ADR argues that the class of ways to reach a type cannot be enumerated; a closed list here would contradict that argument two sections later.
>
> **What would find a fifth.** Not a proof, but a method with a finite answer, and it is the one §4.6 already uses: **walk the platform surfaces whose documented purpose is to step around the type system or its accessibility rules** (F1–F6), rather than walking the uses people find for them. A route that is not one of those is a runtime bug, not a design gap. Every route found so far — `[UnsafeAccessor]`, reflection, `Unsafe.As` — is an F-row, and the two that were missed were missed because nobody had walked the list.
>
> **And for a Country Package, all four are available and nothing in this repository can see any of them**, because a package's source is not in `src/`. That is ADR-0033's residual, unchanged and not reduced by anything here.

### 5.1 The chain, and the link each part stops at

| # | Link | Enforced by | Stops at |
|---|---|---|---|
| O1 | Every constructor of `TenantAccess` and `TenantScope` is `internal`; `TenantScope` is sealed; no parameterless constructor at any accessibility | the C# compiler, on **ordinary member lookup**; asserted by B-06.1a links 1 and 4 | **`[UnsafeAccessor]`.** Executed, probe A. The check is on the accessor's *signature*, not its *target* — probes B and C |
| O1a | No assembly in `src/` names a bypass surface (§4.6 F1–F6) | **Nothing today.** §4.6 proposes it | **The family's enumeration**, which stops at human knowledge of the BCL; and a package's source, which is not in `src/` |
| O2 | `internal` reaches exactly the assemblies the two grant lists name | `[InternalsVisibleTo]`; exact-set assertions at link 2 | **A list a task edits**, seen by two assemblies only. §4.2 closes the second half; §4.3 prices the first |
| O3 | No assembly in the closure publishes origination through a member signature, an inheritance, an explicit implementation, a default interface member or a protected member on a derivable type | `ProofMentionScan` + `DerivableTypes`. Executed: `155 public members … 1 mention, 1 allow-listed; floor 140`; `20 public types … 0 derivable` | **Erasure.** Executed: 14 doors, 0 reported (§3.5) |
| O4 | …therefore no assembly outside the closure can originate a scope | — | **Broken four ways known today**: `[UnsafeAccessor]` (O1), reflection (ADR-0033 §3.1), erasure (O3), `Unsafe.As` (probe F). **This is where the chain stops**, and §5's list is open |
| O5 | The code that could exercise O1a's, O3's or O4's gaps is first-party, in the friend set, and Full-tier reviewed | `CLAUDE.md` review tiers; ADR-0033 §5.3 | **A human.** It degrades quietly, as ADR-0033 §8 already says of its own control |

### 5.2 What the proof scan is, and what it is not

> **The proof scan is a design-integrity rule over the origination set's export surface. It is not a security control and must not be cited as one.**

An assembly that wants a scope without a grant needs neither a missed member shape nor reflection — it needs one `extern` declaration (probe A). A friend that wants to publish one needs `public static object Open()` (§3.5). What the scan buys is real and narrower: it keeps the *sanctioned door count* at the number the design says, so origination stays legible and first-party code cannot acquire a scope by accident or by idiom. Against developer error — the threat model ADR-0033 §5.1 assigns to every ADR-0007 §4 layer — an incomplete enumeration is a useful guard rail.

**Two consequences, and they are the operative part of this ADR for reviewers:**

1. **A newly found member shape is a normal finding on the rule**, at the tier its owning task carries. It is not a re-opening of ADR-0007 §3.4's guarantee, and it does not block a branch *on the guarantee's account*. Five consecutive reviews escalated on that reading, and the reading was reasonable because nothing said otherwise. Now something does.
2. **No further shape-enumeration closes O3, and none of it touches O1 or route (d).** A review proposing an eighth term should propose §3.2's inversion instead, or accept the residual.

### 5.3 Present tense

- **O1, O3 and probes A–E are executed and current** — but against `task/B-06.1a`, which is **in rework and unmerged**. `TenantScope`, `TenantAccess` and `TenantIdentityStamp` are on no merged branch.
- **O1a does not exist.** **O2's repository-wide rule (§4.2) does not exist.** **§3.2's inversion does not exist.** **B-06.3's sanctioned door does not exist**, which is why §6 declines to specify its allow-list entry.

Until those land, §4 is a **specification and nothing enforces it**. Written in the present tense on purpose.

---

## 6. What this ADR does not decide

- **Whether `Aurora.TestKit` gets a grant.** ADR-0034 §6.1 owns it; its three conditions stand.
- **The exact allow-list entry B-06.3 adds.** It depends on the factory's signature, which does not exist.
- **Whether an out-of-process package host is built.** ADR-0033 §5.5 and its four triggers are unchanged and unscheduled — but §4.7 now hangs a second decision off the same trigger.
- **Whether assemblies should be strong-named** (§7 option E).

---

## 7. Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Name the origination set as the boundary, govern it repository-wide, refuse `[UnsafeAccessor]` in `src/`, demote the member scan, restate the guarantee** *(chosen)* | States what every mechanism actually rests on; the `[UnsafeAccessor]` rule is repository-wide and reaches every internal member, not just tenancy's; costs two structural rules over populations of five and "all methods" | The residual (§5) is permanent, and governance's last link is a reviewer. Both written at that size |
| B. Build an eighth member-scan term and keep the guarantee unconditional | — | It cannot close erasure (§3.5) and does not touch `[UnsafeAccessor]` at all. A rule believed complete is more dangerous than one known not to be. §3.2 is adopted as a rule improvement, not as a repair of the guarantee |
| C. Make `TenantScope` internal and publish an `ITenantScope` interface | Non-friends could not name the concrete type | Strictly worse: a module could *implement* `ITenantScope` and fabricate a proof with no attribute and no reflection; link 5's hollow-scope reading is a property of the concrete sealed class; and it contradicts ADR-0007 §4.5 |
| D. An inaccessible parameter on the internal constructor | Executed to work for the route it addresses (probes B and C): `CS0122` on the declaration, `MissingMethodException` on a generic stand-in | §4.7. It closes **one of §5's four known routes** — not (b), not (c), not (d), and the two it misses produce a scope reading as live. ADR-0033 §2's final paragraph forbids exactly this trade. **Refused with a narrowed expiry test, not on taste** |
| E. Strong-name the assemblies and qualify the grants with a public key | Stops an assembly *named* `Aurora.Platform.Tenancy.UnitTests` receiving internals | Defends a build-time impostor who could equally edit the `.csproj`; moot against an in-process adversary; costs signing, key custody and a key blob in every grant |
| F. `CODEOWNERS` + branch protection as the governance | The only mechanical answer to "who may widen the list" | §4.4: neither exists and neither is agent-configurable here |
| G. Leave it unowned (the state before this ADR) | Nothing to write | Five reviews, one question, no record. Each reviewer correctly read §3.4's guarantee as unconditional and correctly found it was not |

---

## 8. Consequences

**Positive**

- The falsification is written down where a reader of §3.4 and §12.3 will meet it, with the code that produces it, instead of being rediscovered by a sixth review.
- `[UnsafeAccessor]` gets a repository-wide rule rather than a tenancy-shaped one, so the next internal member somebody reaches for is covered before anyone reaches for it.
- The member scan's status is decided, so an eighth shape costs a rework on one rule rather than a re-litigation of ADR-0007 §4.
- §4.2 turns "two assemblies assert their own grant lists" into "the repository asserts it contains no grant outside a stated rule", with an empty allow-list.
- §4.5 catches the widening that was about to happen unremarked: B-06.3's public factory member, not a `.csproj` edit.
- Five probe results are in the ADR with what they printed, and §4.7's refusal rests on two of them rather than on an intuition.

**Negative, and owned**

- **The guarantee gets substantially weaker on paper and stays exactly as strong in fact.** ADR-0007 §3.4's sentence read better. It was false, and five people spent five rounds finding that out one door at a time.
- **G4's rule does not exist**, its F1 signal is a metadata proxy rather than the attribute itself (ADR-0032 §1.1), and **its F2–F6 half is specified but not executed** — I did not build it, and §4.6's chain table says so.
- **§5's route list is open**, which is correct and is also less comfortable than the closed list it replaces. Two of the four routes were added by reviewers, one of them after this document had already been rewritten once around the first.
- **G4 buys nothing against a Country Package** and must never be cited as though it does.
- **§3.2's inversion is work that does not exist**; until it lands the proof scan's population is still a filter.
- **§4.3's last link is a reviewer**, and §4.4 refuses the one mechanism that would replace it.
- **§4.7 refuses a mitigation that demonstrably works.** If that refusal is wrong, it is wrong in the safe direction — the change remains available and costs one type and one parameter.

---

## 9. Revisit when

- **ADR-0033 §5.5's out-of-process host is scheduled.** Two decisions turn on that day, not one: the package boundary, and §4.7's seed parameter.
- **A grant is requested for an assembly that ships.** First time §4.3 condition 2 does real work.
- **A second sanctioned door is proposed.** One factory member is a design; two is a pattern.
- **`Aurora.TestKit` reaches B-18.5** — ADR-0034 §6.1's conditions meet §4.3's criteria there for the first time.
- **.NET changes what `[UnsafeAccessor]` may reach**, or ships an in-process isolation mechanism, or begins checking reference types on reinterpretation. §2.1 probes A and F are the tests that would go red and bring someone here.
- **A fifth origination route is found.** §5's list is open by construction; what should be checked first is whether it is an F-row §4.6 already names — if it is, G4 covers it and only §5's table needs the line. If it is not, §4.6's family is what needs revisiting, not §5.
- **An eighth member shape is found *after* §3.2's inversion has landed.** Not a trigger before then (§5.2); after, it is a serious one, because it would mean reachability is not decidable from metadata the way §3.3 assumes.
- **Any assembly becomes strong-named** for an unrelated reason — option E becomes nearly free.
