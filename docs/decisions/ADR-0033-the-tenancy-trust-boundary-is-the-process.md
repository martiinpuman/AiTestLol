# ADR-0033 — The tenancy trust boundary is the process: a loaded Country Package is inside it

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** — (no decision in any earlier ADR is reversed)
- **Amends:**
  - **ADR-0007 §4 and §12.3** — the four layers and the fitness tests that check them are unchanged in design. What this ADR adds is the threat model they were built against, and the statement that they are **not** the control against code executing inside the process. §4 calls layers 3–4 "defence in depth"; §5 of this ADR corrects that word for the routing-identity case.
  - **ADR-0008 §9.4** — "an `AssemblyLoadContext` is not a security boundary" is correct and stands. This ADR carries it the rest of the way: what that means for tenancy specifically, which control is load-bearing instead, what residual is left, and what must demonstrate all three.
- **Superseded by:** —
- **Amended by:** **[ADR-0039](ADR-0039-the-admission-floor-holds-in-every-environment.md) (2026-09-12)** — §5.2's floor is **unconditional and holds in Development too**, and the sentence that conflated it with ADR-0008 §9.3's separate `AllowUnsigned` rule is corrected; the escape hatch moves to a `DevelopmentOnly` trusted key that Production refuses in the same shape. §5.6 D3 gains an explicit Development case. §5.4's sibling-assembly residual is **closed rather than accepted**: the manifest gains a hash per shipped file and the load context resolves only from that list (`FOLLOWUP-055`).
- **Related:** ADR-0007 (tenancy), ADR-0008 (the Country Package contract), ADR-0025 (hosting and packaging), ADR-0027 (the DDL path), ADR-0031 §2 (the package reference allowlist), ADR-0034 (routing uniqueness and the stamp), `../architecture/modules.md` §4
- **Raised by:** the security review of PR #13 (`task/B-06.1a`)

> `task/B-06.1a` proves, with five tests, that nothing outside two named assemblies can *compile* a call to `TenantScope`'s constructor. That is true, it is worth having, and it is honestly scoped. This ADR is about the other half of the sentence, which until now no decision record owned: **ADR-0008 executes foreign code inside the same process**, and against that code the compile-time guarantee is not a control at all.

---

## 1. Context

Two decisions meet here and neither was written with the other in view.

**ADR-0007 §3.4 and §4** make "you cannot obtain a tenant `DbContext` without first holding a `TenantScope`, and you cannot manufacture a `TenantScope`" the structural guarantee of the whole tenancy model. `CLAUDE.md` demands it be "a compile-time or container-level guarantee, not a convention". §12.3 says how it is verified: *"The §4.1/§4.2 rules are verified by fitness tests over the assemblies, not by trying to compile bad code."* Those are statements about the compiler and about assembly metadata.

**ADR-0008 §9.2** loads Country Package assemblies into the host process and runs their code *"on the same thread and the same transaction as the caller"*. §9.4 already says plainly that an `AssemblyLoadContext` is version isolation and unloadability, not sandboxing, and that v1 therefore accepts first-party packages only.

Put together, they produce a claim nobody wrote down and nobody owns: a package assembly is code inside the process, `internal` is not enforced against reflection, so **a hostile or compromised Country Package can mint a `TenantScope` for any tenant id it likes** — and ADR-0007 §4.3's `TenantIdentityStamp` will agree with it, because the connection genuinely does reach the tenant the forged scope names.

The reviewer who raised this asked for the decision, the control, the residual and the demonstration. This is that record. It changes no mechanism that exists; it makes the claim each existing mechanism is entitled to make precise, and it names the one that was missing.

---

## 2. The threat, stated precisely

**Actor.** Managed code in a Country Package assembly, executing in the host process after admission. "Hostile" covers both a malicious package and a first-party package whose build was compromised.

**Capability sought.** A `TenantScope` naming a tenant the caller was never given, and through it a `DbContext` routed to that tenant's database.

**Why it works.** `internal` is an accessibility the C# compiler enforces on early-bound IL. `System.Reflection` performs no such check for fully trusted code, and **all managed code in a .NET process is fully trusted** — there is no partial trust to be in (§4). `RuntimeHelpers.GetUninitializedObject` is the route B-06.1a already names and defends against by making a hollow scope read as inert; the route it correctly does *not* claim to block is `ConstructorInfo.Invoke` on the real internal constructor, which produces a fully-initialised, valid scope.

**Why `TenantIdentityStamp` does not catch it.** The stamp answers *"which database did this physical connection actually reach?"* and compares the answer with the tenant the scope names. A forged scope naming tenant B, resolved to tenant B's database, is **consistent** — the stamp confirms the forgery rather than detecting it.

> **`TenantIdentityStamp` is a routing check, not an authorization check.** It exists to catch a mis-routed connection string: a catalog bug, a bad restore, a database renamed by hand, a failover pointing at a stale replica (ADR-0007 §4.3). It has never been a control over *who asked*. ADR-0034 §4 makes this its named role.

**And the part that matters most.** The forged scope is the *most legible* path, not the most privileged one. Code executing in this process can read the configuration and the secret material the process can read, and open its own `NpgsqlConnection` to any tenant database on any cluster — without naming a single tenancy type. **A countermeasure aimed at scope forgery would not reduce this threat.** Anyone tempted to "fix" §2 by hardening `TenantScope` should read this paragraph twice.

---

## 3. The chain, and the link we stop at

`CLAUDE.md`'s standing rule: name the chain from the assertion to the behaviour it claims, and say which link you stop at.

**Assertion:** *a Country Package cannot reach tenant data it was not given.*

| # | Link | Enforced by | Status |
|---|---|---|---|
| L1 | Only a package whose signature verifies against a pinned trusted key is admitted | `PackageSignatureVerifier`, ADR-0008 §9.3, ADR-0031 §1 | **Holds.** Demonstrated by `PackageSignatureVerifierTests`, including a package changed after signing |
| L2 | The bytes that were verified are the bytes that execute | Signature over the assembly file plus the embedded manifest, then `LoadFromAssemblyPath` of that same file | **Weak, named.** Verify and load are two reads of one path; nothing makes them atomic. Bounded, not closed — see §5.4 R5 |
| L3 | A package may name only `Aurora.Countries.Contracts`, `Aurora.SharedKernel`, `Aurora.Documents.Canonical` | `PackageAssemblyReferenceRule` at metadata time, and `CountryPackageLoadContext.Load` at runtime | **Holds — for binding by name.** Demonstrated in §3.1 below |
| L4 | …therefore the package cannot reach `Aurora.Platform.Tenancy.Contracts` | — | **Broken.** This is where the chain stops |
| L5 | …therefore it cannot invoke `TenantScope`'s internal constructor | — | Not enforced. .NET offers no mechanism (§4) |
| L6 | …and a forged scope would be refused downstream | — | Not enforced. The stamp agrees with it (§2) |

**L3 covers binding. It does not cover reaching.** An `AssemblyLoadContext`'s `Load` override is consulted when an assembly is resolved *by name*. Assemblies already loaded in the process are reachable without resolving anything: `AppDomain.CurrentDomain.GetAssemblies()`, `AssemblyLoadContext.All`, `AssemblyLoadContext.Default.Assemblies`, or simply `anyObjectTheHostHandedUs.GetType().Assembly`. None of those calls the override.

### 3.1 Executed, not reasoned about (2026-09-12)

Built against `task/B-06.1a`'s own compiled `Aurora.Platform.Tenancy.Contracts.dll`, loaded into the default context by a host, with a hostile assembly loaded into a collectible `AssemblyLoadContext` whose `Load` refuses every `Aurora.*` name exactly as `CountryPackageLoadContext` does:

```
LINK A — package binds the tenancy assembly by name:
  refused: FileLoadException — Could not load file or assembly
           'Aurora.Platform.Tenancy.Contracts, Culture=neutral, PublicKeyToken=null'.

LINK B — package never binds; it takes what is already loaded:
  MINTED: tenant victim-tenant (019230b0-5c6a-7c3e-9a2f-0f1e2d3c4b5a) | IsActive=True
        | TenantId=019230b0-5c6a-7c3e-9a2f-0f1e2d3c4b5a | ctor.IsAssembly=True
        | my ALC=CountryPackage:hostile@1.0.0 | victim ALC=Default
```

Link B enumerated `AppDomain.CurrentDomain.GetAssemblies()`, took the `TenantScope` type from the assembly the host had already loaded, called the single instance constructor (`IsAssembly == true` — it is the `internal` one), and got back a scope that reports `IsActive == true` for a tenant of its choosing. No `[InternalsVisibleTo]` grant was involved; the hostile assembly is on nobody's friend list.

**This is the residual, and it is real.** §5.6 D2 requires it to exist in the repository as a test, because a residual that lives only in an ADR is one the next person assumes was closed.

---

## 4. What .NET actually offers — checked, not assumed

The brief said to check rather than assume in either direction. Verified against Microsoft Learn on 2026-09-12, for .NET 10:

1. **`AssemblyLoadContext` gives no privilege property at all.** *"AssemblyLoadContext does not provide any security features. All code has full permissions of the process."* — [AssemblyLoadContext class remarks](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-runtime-loader-assemblyloadcontext) (the page carries a `net-10.0` moniker).
2. **Its isolation is name resolution, nothing more.** *"There's no binary isolation between these dependencies. They're only isolated by not finding each other by name."* — [About AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext). That sentence is the whole of §3's L3/L4 boundary, in Microsoft's own words.
3. **There is no in-process sandbox to opt into.** *"Sandboxing, which relies on the runtime or the framework to constrain which resources a managed application or library uses or runs, isn't supported on .NET Framework and therefore is also not supported on .NET 6+… Use security boundaries provided by the operating system, such as virtualization, containers, or user accounts, for running processes with the minimum set of privileges."* — [.NET Framework technologies unavailable on .NET 6+](https://learn.microsoft.com/en-us/dotnet/core/porting/net-framework-tech-unavailable). Code access security and security transparency are both gone, and both are described there as no longer security boundaries even where they exist.
4. **There is no way to switch private reflection off.** `DisablePrivateReflectionAttribute` has had no effect since .NET Core 2.1 and is obsolete from .NET 6 ([SYSLIB0015](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib0015)); the documentation says the runtimes do not consistently enforce it and that it must not be relied on to restrict access to non-public members.

**Conclusion, stated plainly: .NET offers no in-process privilege boundary against a loaded assembly.** Not a weak one — none. Any design that needs one must put a process or an OS principal between the two pieces of code.

**And the collectible context we do have.** B-12's `CountryPackageLoadContext` is collectible. That buys exactly two things: one version of one assembly name per context, and the ability to *attempt* an unload. It buys no privilege. Its unloadability is best-effort too: an unload completes only once no reference into the context's assemblies survives, and package code can keep itself alive by handing an object to anything long-lived in the default context. **Do not describe it as isolation without saying which of the two properties is meant, and do not describe the unload as guaranteed.**

---

## 5. Decision

### 5.1 The tenancy trust boundary is the OS process, and it is named as such

Core code, module code and loaded Country Package code are all **inside** one trust boundary. Everything ADR-0007 §4 builds is a control *within* that boundary. Its threat model is **developer error and mis-routing**, and against those it is excellent. It is not a control against code that executes in the process, and no amount of additional accessibility work will make it one.

| ADR-0007 §4 layer | Stops | Does not stop |
|---|---|---|
| §4.1 type system | A developer writing data access that forgets the tenant; a serializer or `new T()` materialising a scope | Reflection from any assembly in the process |
| §4.2 container | Any DI registration handing out a tenant `DbContext`, whatever method spells it (ADR-0032 §4.1) | Code that never asks the container |
| §4.3 identity stamp | A **mis-routed connection**: catalog bug, bad restore, renamed database, stale failover target | A correctly-routed connection obtained under a forged identity — the stamp confirms it (§2) |
| §4.4 least privilege | A compromised *connection string* reaching a database it has no `CONNECT` on | A compromised *process*, which holds every credential it can resolve |

ADR-0007 §12.3's fitness tests, and `task/B-06.1a`'s five-link chain, are evidence about the compiler and about assembly metadata. They are **not** evidence about an in-process adversary, and the file a reader reaches from §12.3 must say so.

### 5.2 The control is admission, not confinement

Since confinement is unavailable (§4), the only control is deciding what is allowed to execute at all.

**A process that can route tenants loads only packages whose signature establishes `FirstParty`.** `Partner` and `Unsigned` are refused there.

> **Amended by [ADR-0039](ADR-0039-the-admission-floor-holds-in-every-environment.md) §2.2 (2026-09-12).** The sentence that stood here — *"`AllowUnsigned` already cannot be set outside Development, and the host refuses to start rather than honour it"* — described a **different rule** beside this one and made this section readable as though the floor had a Development hole. It does not. **`Packages:AllowUnsigned` is refused in two independent ways:** ADR-0008 §9.3 refuses it **outside Development on any host**; this section refuses it **on any tenant-routing host in every environment, Development included**, because the floor is a property of what the process can reach and not of what it is called. A host that routes tenants and sets `AllowUnsigned` refuses to start in Development exactly as in Production. The environment-scoped escape hatch moves to the *credential*: a trusted key marked `DevelopmentOnly`, which Production refuses in the same shape (ADR-0039 §3). Both refusals live in `CountryPackageHostOptions.Create` and are asserted by configuration tests.

This **narrows where** ADR-0008 §9.3's trust levels take effect; it does not remove them. A `Partner` key may still be configured, a partner package may still be signed, inspected and listed. What this ADR decides is that such a package is not *loaded* by a tenant-routing process until §5.5's boundary exists.

**This gate is not built.** `CountryPackageLoader.Load` today admits any package whose signature establishes any configured trust level, because no host distinguishes "routes tenants" from "does not". §5.6 D3 says what must demonstrate it and §6 asks for the row.

### 5.3 Admitting a package is a fleet-wide grant, and is treated as one

It follows from §5.1 that installing a Country Package grants that code access to **every tenant the process can reach**, not to the tenants that installed it. Two consequences, both binding:

- **A Country Package is reviewed at the same bar as core code** — `CLAUDE.md`'s **Full** tier, because a Country Package is "anything a Country Package can influence" in the most literal sense available.
- **"Add a trusted package key" is an operator action, not a configuration edit.** It is recorded in `catalog.operator_audit_event` (ADR-0007 §9.2), and the operator surface that offers it states the consequence in the sentence next to the button: *this key can admit code that reads every tenant in this deployment.* A trust decision presented as a settings field is a trust decision nobody made.

### 5.4 The residual, written down rather than claimed away

| # | Residual | Bounded by |
|---|---|---|
| R1 | A loaded package can mint a `TenantScope` for any tenant id, and the stamp will agree | Admission (§5.2), review (§5.3). **Demonstrated in §3.1** |
| R2 | A loaded package can read the process's configuration and secret material and open its own connection to any tenant database, naming no tenancy type | Same. Nothing technical |
| R3 | ADR-0007 §3.5 stage 2 (a login role per tenant database) does **not** bound R2: the process resolves and holds every such credential | Same |
| R4 | Deactivating a package attempts an unload; package code can prevent it completing | Restart. Not a security property in the first place |
| R5 | L2's verify-then-load is two reads of one path, not one atomic read | The package directory ships inside the container image (ADR-0025) and is writable only by whoever can write the application binaries. **Accepted** — an attacker with that write can replace core, so closing L2 alone buys nothing. It stops being acceptable the moment packages are delivered to a running host at runtime rather than baked into the image |

**When R1–R3 stop being acceptable.** Any one of these turns §5.5 from "not scheduled" into "required before that feature ships":

1. A package this team did not write and review is admitted to a tenant-routing process.
2. A customer or partner can cause a package to be installed without an Aurora code review.
3. A commitment is made to a customer, an auditor or a certification that package code cannot reach other tenants' data.
4. A package needs to execute tenant-authored logic — a formula, a script, a report expression, a rule DSL evaluated as code.

Condition 4 is the one that arrives by accident, through a feature nobody thinks of as "loading code".

### 5.5 The path to a real boundary — described, and explicitly not built

If a condition in §5.4 fires, the answer is the one Microsoft's own guidance gives (§4 item 3): an OS boundary. Two properties are required, and anything short of both is theatre:

1. The package executes as a **different OS principal in a different process or container** from the tenant-routing host.
2. It **holds no database credential** and reaches data only through a narrow, serialised contract the host mediates and authorises per call.

**The cost, named:** ADR-0008 §9.2's *"same thread and the same transaction as the caller"* ends. Every extension point that participates in the caller's transaction must become request/response over a canonical document, which is a change to the contract surface — a MAJOR core-contract bump under ADR-0008 §3.1, with the deprecation window that implies.

**No decision in this ADR depends on that host existing.** It is not built, it is not scheduled, and it is not a mitigation for anything today. Naming a control that rests on unscheduled work is the defect this project has already had to withdraw once, and this paragraph exists so that it cannot be read that way.

Two options were considered and are not chosen; §7 records why.

### 5.6 What must demonstrate it

Three mechanisms. Each can fail, and each says what it measured.

**D1 — the control fires, and fires *before* execution.** A package whose signature does not verify, or whose established trust is below the host's admission floor, is refused *without its code running*. The assertion is not "a failed `Result` came back"; it is that a **module initialiser in the deliberately-hostile fixture package did not execute** — a static flag the fixture sets, observed to be unset. A refusal that happens after the type system has run the package's initialisers is not a refusal. Test count and which check refused are reported.

**D2 — the residual is pinned, so it cannot be silently believed closed.** One test loads a hostile fixture package into a real `CountryPackageLoadContext` and asserts **both** halves of §3:

- (a) `Assembly.Load("Aurora.Platform.Tenancy.Contracts")` from inside the package's context is **refused** — L3 firing;
- (b) the same package, reaching `AppDomain.CurrentDomain.GetAssemblies()`, **succeeds** in invoking `TenantScope`'s internal constructor and obtaining `IsActive == true` for an arbitrary tenant id — L4 broken.

Part (b) asserts a success deliberately. It is an executable statement of R1, and it has two failure modes that both matter: the mint stops working (good news, and this ADR must be re-read and amended), or (a) regresses and the name-binding path opens too. A test that can only go red for a reason we want to hear about is still a test that can go red.

**Where D2 may not live.** Not in `Aurora.Platform.Tenancy.UnitTests`, which is on the contracts assembly's `[InternalsVisibleTo]` list. A test proving an outsider can reach an internal constructor proves nothing if the test *is* an insider. D2's assembly must not appear in that grant list, and **the test must assert that fact about its own assembly** — B-06.1a already reads the grant list as an exact set, so the mechanism exists.

**D3 — the admission floor cannot be configured away.** A host configured to route tenants and to admit non-`FirstParty` packages **refuses to start**, in the same shape and for the same reason as `AllowUnsigned` outside Development (ADR-0008 §9.3). Asserted by a configuration test, because a flag that only a comment prevents from reaching production will reach production.

> **Sharpened by [ADR-0039](ADR-0039-the-admission-floor-holds-in-every-environment.md) §2.4.** "In the same shape as `AllowUnsigned` outside Development" names the **shape of the refusal**, not the floor's scope. D3 asserts **both** environments explicitly — a `Development` case and a `Production` case, as two tests with the same expectation — because the single-environment version of this test is exactly the one that would pass while the floor had a Development hole.

**What none of the three demonstrates:** that a package cannot reach tenant data. Nothing demonstrates that, because it is not true (§5.4). D1 and D3 demonstrate the admission control; D2 demonstrates that the residual is still exactly the size this ADR says it is.

---

## 6. Work this creates

No backlog row is edited here — `docs/BACKLOG.md` belongs to the project-manager. The row this decision needs is described in the handback, and it carries D1, D2 and D3 plus the §5.2 admission floor. Until that row lands:

- §5.2's floor is a **specification and nothing enforces it**; and
- §5.4's residual is recorded here and nowhere a build can see.

Both sentences are written in the present tense on purpose.

---

## 7. Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. In-process, admission-controlled, residual written down** *(chosen)* | Keeps ADR-0008 §9.2's same-thread/same-transaction contract, which every effective-dated tax and account-role extension point is designed around; costs one gate and three tests; matches the honest state of the runtime (§4) | The residual is real and permanent for as long as it is chosen. It depends on a human review process, which is a control that degrades quietly |
| B. Out-of-process package host now | The only thing that is actually a boundary (§4 item 3) | Large, unscheduled, and it changes the contract surface for every extension point (§5.5). Building it before a partner package exists would be building it against no requirement — and ADR-0008 §12 already names the partner request as the trigger |
| C. WebAssembly sandbox (Wasmtime or similar) for pure extension points | A genuine in-process-ish boundary for identifier validators, formatters and rate tables | Novelty, which `CLAUDE.md` requires be justified in writing; a third-party runtime with its own licence and supply-chain surface (ADR-0026); and it cannot host any extension point that must participate in the caller's transaction, which is most of ADR-0008 §7's ten. Reconsider only together with option B |
| D. Leave the gap unowned (the state before this ADR) | Nothing to write | Every reader of ADR-0007 §4 draws a stronger conclusion than the mechanism supports. That is the failure mode `CLAUDE.md`'s self-check #1 describes, at ADR scale |

---

## 8. Consequences

**Positive**

- The strength of ADR-0007 §4 is now stated where a reader of §4 will find it, with a table of what each layer does and does not stop. Nobody has to re-derive it from two ADRs written months apart.
- The threat has a named control (admission) rather than an implied one (accessibility), and the control is the one that can actually be shown working.
- §5.4's four conditions turn "we accepted this risk" into a statement with an expiry test. A future team can check whether the acceptance still holds without re-running this analysis.
- D2 makes the residual a fact the build knows. The day .NET or our design changes it, a test says so.

**Negative, and owned**

- **v1 is first-party-only for a reason that is now load-bearing, not a preference.** The partner-package roadmap acquires a real prerequisite (§5.5), and that prerequisite is large.
- **The control degrades quietly.** Code review is a human process; "reviewed like core" is worth exactly what the reviews are worth. The technical mechanisms (D1, D3) only prove that *some* trusted key admitted the package, never that the review happened.
- **§5.3's operator wording is a UI requirement in an architecture ADR.** It is here because putting it only in `docs/design/` would separate the consequence from the decision that created it.
- **§5.2's gate does not exist today** (§6). Until it does, this ADR's control is a specification.

---

## 9. Revisit when

- Any condition in §5.4 fires — that is the trigger for option B, and this ADR is superseded rather than amended.
- .NET ships an in-process isolation or capability mechanism for managed code. §4 says none exists as of .NET 10; if one appears, D2 part (b) is the test that will go red and bring someone here.
- A Country Package needs to evaluate tenant-authored expressions as code (§5.4 condition 4), even if the package itself is first-party.
- The package directory becomes writable at runtime rather than baked into the image — R5's acceptance ends there.
- A host appears that loads packages but routes no tenants (a validation or report-rendering worker). That is the first place §5.2's distinction becomes operational rather than theoretical, and it is also the natural first home for option B.
