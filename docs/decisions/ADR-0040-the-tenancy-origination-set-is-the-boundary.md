# ADR-0040 — The tenancy origination set is the boundary: naming is not originating, and what governs the friend list

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** — (nothing is reversed)
- **Amends:**
  - **ADR-0007 §3.4 and §12.3** — the mechanisms are unchanged. What changes is the *statement* of what they guarantee: §3.4's "**No module, no component and no job can fabricate a `TenantScope`**" is unconditional as written and §5 below restates it with the two conditions under which it actually holds. Both sections carry a dated banner pointing here.
  - **ADR-0032 §4.2** — the population-versus-verdict inversion in §3 below applies to T16/T17/T18 as well as to B-06.1a's proof scan. §4.2's decisions are unchanged; §3.4 of this ADR records what a future revision of those rules should look like and why.
- **Generalises:** **ADR-0034 §6.1**, which is the only governance of an `[InternalsVisibleTo]` grant that exists today. Its three conditions for `Aurora.TestKit` are unchanged and remain the binding answer for that grant; §4 below is the standing rule they are an instance of.
- **Superseded by:** —
- **Related:** ADR-0007 (tenancy), ADR-0008 §9 (the package load seam), ADR-0029 (the `tid` claim and fail-closed permission evaluation), ADR-0030 (fitness rules read IL metadata), ADR-0031 §2 (the package reference allowlist), **ADR-0033 (the tenancy trust boundary is the process — read it before this one)**
- **Raised by:** `ARCH-Q-SCOPE-BOUNDARY`, from the second, third and fourth reviews of PR #13 (`task/B-06.1a`)

> Four reviewers found four different doors through one rule. This ADR does not build a fifth rule. It says what the rule is for, what the boundary underneath it actually is, what governs membership of that boundary, and — executed rather than argued — which of the things everyone has been calling "the boundary" the compiler actually enforces.

---

## 1. What is already decided, with section numbers

The question was routed with an explicit invitation to answer "the ADRs already say this" where they do. Two of its three parts are in that position, and saying so is most of the value.

**Sub-question 2 — *what is the control for a package that reflects past the friend set; does a package context get the tenancy contracts assembly at all?* — is fully answered by ADR-0033, and nothing here adds to it.**

| Asked | Answered at |
|---|---|
| Does a package's `AssemblyLoadContext` hand it `Aurora.Platform.Tenancy.Contracts`? | **ADR-0033 §3, links L3/L4**, and **§3.1**, which executed both halves: binding the assembly *by name* from inside the package context is refused (`FileLoadException`), and the package reaches the already-loaded instance through `AppDomain.CurrentDomain.GetAssemblies()` without resolving anything |
| What stops reflection there? | **ADR-0033 §4 items 1–4** — nothing does, and .NET 10 offers no mechanism that could. `AssemblyLoadContext` carries no privilege; there is no in-process sandbox; `DisablePrivateReflectionAttribute` is obsolete and was never enforced |
| So what is the control? | **ADR-0033 §5.2** — admission, not confinement. **§5.3** — admitting a package is a fleet-wide grant |
| And the residual? | **ADR-0033 §5.4 R1–R3**, with **§5.6 D2** requiring the mint to live in the repository as a test so it cannot be silently believed closed |

Anyone tempted to propose a countermeasure aimed at scope forgery should read **ADR-0033 §2's last paragraph** first: the forged scope is the most *legible* path, not the most privileged one, and code in this process can open its own `NpgsqlConnection` to any tenant database without naming a single tenancy type.

**Sub-question 3 — *should ADR-0007 §12.3's guarantee be restated as scoped?* — is half answered.** ADR-0033 §5.1 already attaches a banner to §12.3 scoping it against an in-process adversary. What no ADR scopes is the **other** condition, and it is the one three consecutive reviews were actually walking through: the guarantee also holds only while **no assembly inside the friend set publishes origination**. §5 below supplies that half.

**Sub-question 1 — *is the friend set the stated boundary, and what governs adding to it?* — is not answered anywhere.** ADR-0034 §6.1 is the only governance in the repository: three conditions attached to one anticipated grant on one assembly. §2 and §4 below are the answer.

---

## 2. Decision 1 — the boundary is **origination**, not naming, and it is stated as a boundary

PR #13's fourth review formulated it as *"the set of assemblies permitted to **name** `TenantScope`"*. That formulation is wrong in a way that changes what has to be governed, so it is corrected with evidence rather than with an argument.

### 2.1 Executed, 2026-09-12

`Outsider.NoGrant`, a `net10.0` library on **nobody's** `[InternalsVisibleTo]` list, referencing the `Aurora.Platform.Tenancy.Contracts.dll` built from `task/B-06.1a` (4f8b09b):

| What the outsider does with `TenantScope` | Result |
|---|---|
| names the type, declares a `private static TenantScope? held` field | **compiles** |
| `TenantScope scope = (TenantScope)handed;` where `handed` is an `object`, then reads `IsActive`, `TenantId`, `Packages` | **compiles** |
| takes one through `IAsyncDisposable`, casts, calls `DisposeAsync()` | **compiles** |
| re-exports it: `public static TenantScope? Held => held;` | **compiles** |
| `new TenantScope(id, key, region, version, packages, TenantAccessReason.Request)` | **`error CS1729: 'TenantScope' does not contain a constructor that takes 6 arguments`** |

```
### PROBE 1: outsider with NO grant, naming/casting/using/re-exporting TenantScope
Build succeeded.
    0 Warning(s)
    0 Error(s)
### PROBE 2: same outsider, calling the internal constructor
Mint.cs(9,13): error CS1729: 'TenantScope' does not contain a constructor that takes 6 arguments
Build FAILED.
```

One source file was added to the probe to produce the second row and removed to produce the first; the demonstration therefore fails in both directions rather than asserting one.

`TenantScope` is `public` and must stay public: every application-service method takes one as a parameter (ADR-0007 §4.5), and ADR-0034 §6 draws exactly this contrast when it keeps `TenantConnection` internal — *"`TenantScope` is in `.Contracts` because every application-service method takes one… `TenantConnection` is the opposite."* Restricting who may **name** the type is not available and was never the design.

### 2.2 The boundary, named

> **The tenancy origination set** is the set of assemblies that can bring a tenancy proof — a `TenantScope`, and by ADR-0027 §1 a `TenantDatabaseHandle` — into existence in code the C# compiler will accept.
>
> It is **(a)** the transitive `[InternalsVisibleTo]` closure of `Aurora.Platform.Tenancy.Contracts` and `Aurora.Platform.Tenancy`, **plus (b)** every assembly that a member of that closure hands origination to through its own published API.
>
> **This is the trust boundary ADR-0007 §4.1's structural guarantee rests on, and it is stated here as one.** It is a boundary against *compile-time reach by first-party code* — that is, against developer error. Against code executing in the process it is not a boundary at all, and ADR-0033 §5.1 is the record of that, not this ADR.

Clause **(b)** is the half that has taken four review rounds, and it is why the closure alone is not the set. A public member of `Aurora.Platform.Tenancy` that returns a scope adds every assembly that can call it to the origination set, with no `.csproj` diff anywhere.

---

## 3. Decision 2 — why four reviews found four doors, and the level to defend at

### 3.1 The pattern, classified

| # | Door | What was wrong |
|---|---|---|
| 1 | direction inference — an `event`, and an `Action<TenantScope>` parameter | **verdict**: the members were examined and judged inbound |
| 2 | inherited members — `public sealed class OpenScopeCollection : List<TenantScope>` | **population**: the type declared no member, so nothing was examined |
| 3 | `protected` members on an unsealed public type | **population**: `BindingFlags.Public` never sees them |
| 4 | explicitly implemented non-generic interface members | **population**: emitted private, so `BindingFlags.Public` never sees them |

Three of the four were **population** defects: the scan enumerated a *filtered* population and asked a correct question of each member in it. Each review added one filter term. That is a blocklist over an open set — the CLI's member and accessibility model is not something anyone finishes enumerating from memory — and the examined count stayed healthy through every one of them. ADR-0032 §4.2.2 had already observed the same thing for a different clause: *"T17's examined count does not move… a healthy count is exactly what a missing clause hides behind."*

The fourth was a **verdict** defect, and it was fixed by the right move: the verdict stopped classifying *direction* and became "mentions anywhere, allow-list the exceptions by exact key". That is a complement — it stopped enumerating the safe cases and started enumerating the sanctioned ones.

### 3.2 The rule that generalises

> **When a rule must hold over "everything reachable", do not enumerate the reachable ones. Enumerate everything, and make *unreachability* the thing that has to be proved — member by member, with an unclassified case throwing.**

This is not a new idea on this project; it is the shape of the **derivability check** B-06.1a already wrote, and that is why door 3 is closed *for good* rather than enumerated. `DerivableTypes` does not list the protected members that are reachable. It proves the stronger and simpler fact that **no public type in the friend set can be derived from outside it**, after which no protected member is reachable whatever anyone declares later.

So the decision is: **apply to the population the move that has already been applied to the verdict.**

### 3.3 What that means concretely for the proof scan

- The population becomes every member of every type in a friend assembly at **every accessibility** (`Public | NonPublic | Static | Instance | DeclaredOnly`), not the public ones plus two hand-added side channels.
- Each member gets a computed verdict — `Public`, `ProtectedOnADerivableType`, `ExplicitInterfaceImplementation`, `NotExternallyReachable` — with a **throwing default arm**, the discipline `ProofMentionScan` already applies to member *kinds* and does not yet apply to member *reachability*.
- The scan reports **members examined / reachable / not reachable / mentions**. Today it reports examined and mentions and **not the excluded count** — so a population that shrinks is invisible. Each of doors 2, 3 and 4 moved members from the unexamined set into the examined set, and nothing ever printed the size of the unexamined set.

The fault that must turn it red: remove one reachability arm (say, explicit implementations) and the **`not reachable` count rises by exactly the members that arm classified** while `examined` is unchanged. That is a number that moves when a door opens, which is what the four reviews did not have.

### 3.4 The same inversion applies to ADR-0032 §4.2

T17 ("no externally reachable member yields a tenant `DbContext`") has the identical shape and the identical exposure: its population is "externally reachable members", chosen by a filter, and its verdict enumerates the shapes that count. Nothing in §4.2's decisions changes here — they are correct as decided — but the row that next revises T16/T17/T18 should carry §3.2's inversion, and the ADR-0032 banner says so.

### 3.5 The class of door no member scan can close — executed

`ProofMentionScan`'s own remarks name it: *"a return, field or parameter typed `object` or `dynamic`, a non-generic `IEnumerable`, a base type or interface that does not itself carry the proof type."* That claim was executed rather than trusted, against the real scan on `task/B-06.1a`:

```
ARCH-Q-SCOPE-BOUNDARY probe: ProofMentionScan.Over(ErasureDoors) examined 14 public members,
  0 explicit implementations, over 1 types; mentions reported: 0 []
ARCH-Q-SCOPE-BOUNDARY probe: DerivableTypes(ErasureDoors) examined 1 types;
  derivable from outside: 0 []
```

`ErasureDoors` is fourteen public members of which every one hands out or receives a scope: `object OpenAsObject()`, `IAsyncDisposable OpenAsDisposable()`, `IEnumerable OpenAsNonGenericSequence()`, `dynamic OpenAsDynamic()`, `object? Current { get; set; }`, `bool TryOpen(out object)`, `event Action<object> Opened`, `void OnOpen(Action<object>)`, `ValueTask<object> OpenAsync()`. Both scans report **zero**. The probe file was deleted after the run; it is not on any branch.

Combine that with §2.1 and the consequence is decisive: **`public static object Open()` in a friend assembly, and `(TenantScope)Friend.Open()` in an assembly with no grant, both compile, and no signature scan can ever report either.** The information is not in the metadata. This is not a fifth door of the same kind as the first four; it is proof that the first four were instances of a class that cannot be exhausted, because its last member is invisible in principle.

---

## 4. Decision 3 — what governs membership

The brief asked what makes the boundary **enforceable rather than asserted**, given that today it is a literal in `TenantAccessConstructionTests` and B-06.3 is about to want to edit it. The honest answer has two halves, and pretending otherwise would be the defect this project keeps catching.

> **A membership list cannot be made unfalsifiable. It can be made *complete* and *visible*, and today's mechanism is neither.** That — not the list itself — is the gap, and it is closable.

### 4.1 Why today's assertion is not yet a boundary

`TenantAccessConstructionTests` asserts `Grants(Contracts)` and `Grants(Tenancy)` as exact sets. Both assertions are good and both stay. What they cannot see is a grant **on a third assembly**: they read the two assemblies they name, so any `InternalsVisibleTo` elsewhere in `src/` is outside their population by construction. The repository holds **three** such grants today (executed, `task/B-06.1a`):

```
src/platform/Aurora.Platform.Tenancy.Contracts/…csproj:32: InternalsVisibleTo Aurora.Platform.Tenancy
src/platform/Aurora.Platform.Tenancy.Contracts/…csproj:33: InternalsVisibleTo Aurora.Platform.Tenancy.UnitTests
src/platform/Aurora.Platform.Tenancy/…csproj:46:            InternalsVisibleTo Aurora.Platform.Tenancy.UnitTests
src/platform/Aurora.Platform.Tenancy/…csproj:47:            InternalsVisibleTo Aurora.Platform.Tenancy.IntegrationTests
src/Aurora.Countries.Hosting/…csproj:34:                   InternalsVisibleTo Aurora.Countries.Hosting.UnitTests
```

Four on the two tenancy assemblies, one elsewhere. Nine production assemblies exist. Nothing in the repository asserts anything about the fifth grant or about the sixth, and a grant is the cheapest possible edit.

### 4.2 G1 — the repository asserts that it contains no grant outside a stated rule

> **Every `<InternalsVisibleTo>` in every `src/**/*.csproj` is either (a) to the declaring assembly's own `<name>.UnitTests` or `<name>.IntegrationTests`, or (b) to an assembly named in the rule's own pinned list.**
>
> The rule reports **grants found / self-test grants / pinned grants / unlisted**, and an unlisted grant fails.

This is a structural rule over a population the build already owns. It has no second copy of anything: the `.csproj` items are the single source of the grants, and the pinned list holds only the exceptions to clause (a) — which is **empty today**, because all five existing grants satisfy clause (a). A rule whose allow-list is empty and whose population is five is as close to a boundary as a list gets.

**It deliberately has no table of assembly names in this ADR.** `FOLLOWUP-058` is the record of what a hand-copied restatement of a machine-read value does on this project: `verify.sh`'s stage-6 floor and its `--help` text drifted twice in two merges, and neither drift could fail anything. A table of grant names here would be the third instance. What this ADR governs is the **criteria** (§4.3); the names live where the build reads them.

**Where G1 stops:** at the `.csproj` file. An `[assembly: InternalsVisibleTo("…")]` written in a `.cs` file, or injected by a `Directory.Build.props` import or an SDK, is not in its population. That gap is exactly what the two existing reflection assertions cover, because they read the *attribute* on the loaded assembly rather than the project file — so the two mechanisms are complements and both are required. G1's rule must additionally assert, for the assemblies the tenancy tests name, that **the attribute set observed by reflection equals the item set read from the project file**; a mismatch is a grant that arrived by a route neither mechanism reads alone. `Aurora.Architecture.Tests` already reads project files (`Solution/`), so this costs a glob and a comparison.

### 4.3 G2 — what a grant costs

Generalising ADR-0034 §6.1's three conditions into a standing rule. A new grant is admitted only when all four hold:

1. **The grantee must *originate* a proof, not *use* one.** §2.1 is executed evidence that a consumer needs no grant: naming, holding, casting, passing and re-exporting all compile without one. "It needs to see internals" is almost always "it needs to call something internal that is not a constructor", and that is a different request.
2. **The grantee's own export surface joins the proof scan the day the grant lands** — either it ships, and `ShippingFriends` picks it up transitively, or it is test-only and no project under `src/` references it (ADR-0034 §6.1 condition 2, unchanged).
3. **The grant is recorded in the same commit as the `.csproj` change**, in G1's pinned list, with the ADR clause that admitted it in the comment beside it.
4. **The task is reviewed at Full tier as a tenancy change**, whatever else it is. `CLAUDE.md`'s tier table already puts "tenancy and isolation" there; this makes it explicit for a one-line `.csproj` edit, which is the shape most likely to be filed as Light.

**Where G2 stops:** at a reviewer. G1 makes an *unrecorded* grant fail the build; nothing makes a *recorded* one wise. That is the same stop ADR-0037 §6.2 names for `Reason` and ADR-0033 §5.3 names for "reviewed like core", and it is written at that size rather than dressed as a mechanism.

### 4.4 G3 — refused, with the reason

`CODEOWNERS` plus branch protection would make an edit to a grant-bearing `.csproj` mechanically require an architect review. **There is no `CODEOWNERS` file in this repository** (checked: `find . -name CODEOWNERS` returns nothing) and branch protection is a repository setting no agent on this project configures. Naming it as a control would be naming a control that rests on work nobody has scheduled — the defect ADR-0033 §5.5 spends a paragraph avoiding. It is recorded as the option and its prerequisite, and it is **not** part of this decision.

### 4.5 B-06.3 does not need a grant — it needs a door, and a door is the bigger widening

The brief's concern was that B-06.3 "is about to want to join that set for its factory". It does not. The scope factory lives in `Aurora.Platform.Tenancy`, which is already in the closure; B-06.3 changes no `.csproj`.

What B-06.3 adds is the **first entry in `SanctionedDoors`** — and that is the larger widening of the two, because a public `ITenantScopeFactory.OpenAsync` moves the effective origination set from "the closure" to "the closure, plus every assembly that can call that member". A grant is visible in a `.csproj`; a door is one line in an allow-list inside a test.

Therefore:

> **`SanctionedDoors` and `ProofTakingMembers` are governed by §4.3 exactly as the grant list is**, including condition 4. An addition to either is a Full-tier tenancy change.

And the thing that bounds a sanctioned door is **not accessibility** — the door is public by definition. It is authorization *at the door*: `ITenantScopeFactory.OpenAsync` must itself decide whether this caller may have *this* tenant, which is ADR-0029's `tid` claim and fail-closed permission evaluation, and ADR-0010's model. This is where the authority control that `TenantIdentityStamp` was never able to be (ADR-0033 §2; ADR-0034 §4) actually lives, and B-06.3's acceptance criteria must say so. **Present tense:** neither control exists. The stamp is asserted on **no path today** — the type ships with B-06.1a (in rework), the app-path initializer is B-06.2 and the DDL-path factory is B-07.1, neither built (ADR-0034 §5) — and `ITenantScopeFactory` is B-06.3, not built. Nothing in this ADR is a mitigation that rests on either.

---

## 5. Decision 4 — ADR-0007 §3.4 and §12.3, restated with their conditions

**As written (ADR-0007 §3.4):** *"No module, no component and no job can fabricate a `TenantScope`."*

**As it holds:**

> **No assembly outside the tenancy origination set (§2.2) can originate a `TenantScope` in code the C# compiler will accept.**
>
> Three things are outside that claim, each with its record:
> - **Reflection.** Any code in the process can invoke the internal constructor and obtain a live scope. Executed in ADR-0033 §3.1; no mechanism exists (ADR-0033 §4); the control is admission (ADR-0033 §5.2).
> - **A friend that publishes origination through a signature no scan can read.** Executed in §3.5 above. Bounded by the friend set being first-party code reviewed at Full tier, and by nothing else.
> - **Holding, casting, passing and re-exporting a scope somebody else originated.** Every assembly may do all four and that is the design, not a leak (ADR-0007 §4.5). Executed in §2.1.

### 5.1 The chain, and the link each part stops at

| # | Link | Enforced by | Stops at |
|---|---|---|---|
| O1 | Every constructor of `TenantAccess` and `TenantScope` is `internal`; `TenantScope` is sealed; there is no parameterless constructor at any accessibility | the C# compiler; asserted by B-06.1a links 1 and 4, and by `GetUninitializedObject` reading as inert at link 5 | **Holds.** Executed at §2.1 row 5 |
| O2 | `internal` reaches exactly the assemblies the two grant lists name | `[InternalsVisibleTo]`; asserted as exact sets by link 2 | **A list a task edits**, and a list that sees only two assemblies. §4.2 closes the second half; §4.3 prices the first |
| O3 | No assembly in the closure publishes origination through a member signature, an inheritance, an explicit implementation, or a protected member on a derivable type | `ProofMentionScan` + `DerivableTypes` (link 3). Executed on `task/B-06.1a`: `155 public members and 0 explicit interface implementations examined over 20 public types in 2 assemblies; 1 mention a proof, 1 allow-listed; floor 140` and `20 public types examined for derivability; 0 derivable from outside` | **Erasure.** `object`, `dynamic`, non-generic `IEnumerable`, any interface the proof implements. Executed: **14 doors, 0 reported** (§3.5) |
| O4 | …therefore no assembly outside the closure can originate a scope | — | **Broken for reflection** (ADR-0033 §3.1) and **broken for erasure** (O3). This is where the chain stops |
| O5 | The code that could exercise O3's or O4's gaps is first-party, in the friend set, and Full-tier reviewed | `CLAUDE.md` review tiers; ADR-0033 §5.3 | **A human.** It degrades quietly, as ADR-0033 §8 already says of its own control |

### 5.2 What the proof scan is, and what it is not

> **The proof scan is a design-integrity rule over the origination set's export surface. It is not a security control and must not be cited as one.**

An assembly that wants a scope without a grant does not need a member shape the scan missed — it needs eight lines of reflection (ADR-0033 §3.1). A friend that wants to publish one does not need a shape the scan missed either — it needs `public static object Open()` (§3.5). What the scan buys is real and it is narrower: it keeps the *sanctioned door count* at the number the design says, so that origination stays legible and so that first-party code cannot acquire a scope by accident or by idiom. Against developer error — which is exactly the threat model ADR-0033 §5.1 assigns to every ADR-0007 §4 layer — an incomplete enumeration is a useful guard rail.

**Two consequences follow, and they are the operative part of this ADR for reviewers:**

1. **A newly found member shape is a normal finding on the rule**, at the tier its owning task carries. It is not a re-opening of ADR-0007 §3.4's guarantee, and it does not block a branch *on the guarantee's account*. Three consecutive reviews escalated on exactly that reading, and the reading was reasonable because nothing said otherwise. Now something does.
2. **No amount of further shape-enumeration closes O3**, so a review that proposes a fifth term should propose §3.2's inversion instead, or accept the residual.

---

### 5.3 Present tense

Everything §5.1 measures exists; everything it relies on for the future does not, and the distinction is kept because this project has had to withdraw a control that rested on unscheduled work.

- **O1 and O3 are executed and current** — but on `task/B-06.1a`, which is **in rework and unmerged**. `TenantScope`, `TenantAccess` and `TenantIdentityStamp` are on no merged branch. The integration branch's `Aurora.Platform.Tenancy.Contracts` holds the registry identifiers and nothing else.
- **O2's repository-wide rule (§4.2) does not exist.** The two per-assembly exact-set assertions do, on that same unmerged branch.
- **§3.2's inversion does not exist.**
- **The sanctioned door B-06.3 will add does not exist**, which is why §6 declines to specify its allow-list entry.

Until those land, §4 is a **specification and nothing enforces it**. Written in the present tense on purpose.

---

## 6. What this ADR does not decide

Stated in the present tense, because deciding against no requirement is how this project has produced its worst clauses.

- **Whether `Aurora.TestKit` gets a grant.** ADR-0034 §6.1 owns that decision and its three conditions stand unchanged. §4.3's criteria are the general form and do not overrule them.
- **The exact allow-list entry B-06.3 adds for the scope factory.** It depends on the factory's signature, which does not exist. §4.5 decides what *governs* the entry; the entry itself is B-06.3's, and the `MemberKey` shape (type, member, generic arity, parameter list) is already specified in B-06.1a.
- **Whether an out-of-process package host is built.** ADR-0033 §5.5 and its four trigger conditions are unchanged and unscheduled.
- **Whether the `.Contracts` grants should be strong-name-qualified** (`InternalsVisibleTo("X, PublicKey=…")`). See §7 option E: it defends a threat model this project does not have, and no assembly here is strong-named. Revisit only if assemblies are signed for another reason.

---

## 7. Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Name the origination set as the boundary, govern it repository-wide, and demote the member scan to a design-integrity rule** *(chosen)* | States the thing every mechanism already rests on; costs one structural rule over a five-item population and one allow-list policy; stops the same question being re-routed by making the scan's status a decision rather than an inference | The scan's residual (§3.5) is permanent and the governance's last link is a reviewer. Both are written at that size |
| B. Build a fifth, more complete member scan and keep the guarantee unconditional | Closes doors 2–4's *class* by inversion (§3.2 is worth doing on its own merits) | It cannot close erasure (executed, §3.5), so the unconditional guarantee would still be false — and a rule that is believed complete is more dangerous than one known not to be. §3.2 is adopted *as a rule improvement*, not as a repair of the guarantee |
| C. Make `TenantScope` internal and publish an `ITenantScope` interface from `.Contracts` | Non-friends could not name the concrete type | Strictly worse: a module could *implement* `ITenantScope` and fabricate a proof with no reflection at all, and link 5's `GetUninitializedObject` reading is a property of the concrete sealed class. It also contradicts ADR-0007 §4.5, which requires every application-service method to take one |
| D. `CODEOWNERS` + branch protection as the governance | The only mechanical answer to "who may edit the list" | No `CODEOWNERS` exists and branch protection is not an agent-configurable setting here. §4.4 |
| E. Strong-name the assemblies and qualify the grants with a public key | Stops an assembly *named* `Aurora.Platform.Tenancy.UnitTests` from receiving internals | Defends a build-time impostor. An attacker who can add a project to this solution can also edit the `.csproj`; and against an in-process adversary it is moot (ADR-0033 §4). Cost — signing, key custody, every `InternalsVisibleTo` carrying a key blob — exceeds the benefit |
| F. Leave it unowned (the state before this ADR) | Nothing to write | Three reviews, three rounds, one question, no record. Each reviewer correctly read §3.4's guarantee as unconditional and correctly found it was not |

---

## 8. Consequences

**Positive**

- The thing four reviewers were circling has a name, a definition and a governance rule, and the question stops being re-routed.
- The member scan's status is decided, so the next found shape costs a rework on one rule rather than a re-litigation of ADR-0007 §4.
- §4.2 turns "two assemblies assert their own grant lists" into "the repository asserts it contains no grant outside a stated rule" — which is the actual difference between a list and a boundary, and its allow-list is empty today.
- §4.5 catches the widening that was about to happen unremarked: B-06.3's public factory member, not a `.csproj` edit.
- The three probes are in the ADR with what they printed, so §2.1, §3.5 and §4.1 can be re-run by anyone who doubts them.

**Negative, and owned**

- **The guarantee gets weaker on paper and stays exactly as strong in fact.** ADR-0007 §3.4's unconditional sentence read better. It was not true, and four people spent four rounds discovering that one at a time.
- **§3.2's inversion is work that does not exist**, and until it lands the proof scan's population is still a filter. §5.2 makes that a bounded cost rather than an open guarantee, but the rule is weaker than it should be in the meantime.
- **§4.3's last link is a reviewer**, and §4.4 refuses the one mechanism that would replace it. That is the honest state, not a preference.
- **`SanctionedDoors` becoming Full tier adds review weight to B-06.3**, which is already a Full-tier row. The marginal cost is small; it is named so nobody files the allow-list line as a nit.

---

## 9. Revisit when

- **A grant is requested for an assembly that ships** (not a test assembly). That is the first time §4.3 condition 2 does real work and the first time the closure grows a shipping member since B-06.
- **A second sanctioned door is proposed.** One factory member is a design; two is a pattern, and the second request is the signal that origination is leaking into convenience.
- **`Aurora.TestKit` reaches B-18.5** — ADR-0034 §6.1's conditions meet §4.3's criteria there for the first time.
- **.NET ships an in-process isolation or capability mechanism.** ADR-0033 §9 already carries this trigger; it would change O4, not O2.
- **Any assembly in this repository becomes strong-named** for an unrelated reason — option E becomes nearly free at that point and should be reconsidered.
- **A reviewer finds a fifth member shape.** Not a trigger to re-open the guarantee (§5.2), but if §3.2's inversion has landed and a shape still escapes, that *is* a trigger: it would mean reachability is not decidable from metadata the way §3.3 assumes.
