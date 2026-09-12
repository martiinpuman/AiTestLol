# ADR-0039 — The admission floor holds in every environment, the escape hatch moves to the credential, and the signature must cover the whole package

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Amends:** **ADR-0033 §5.2** (the floor is confirmed as unconditional, and the sentence that made it ambiguous is corrected — §2), **ADR-0033 §5.6 D3** (what the demonstration asserts — §2.4), **ADR-0033 §5.4** (one residual is closed rather than accepted — §4), and **ADR-0008 §9.3** (the signature's coverage — §4.2).
- **Superseded by:** —
- **Related:** ADR-0008 §9.1–§9.4, §3.1, ADR-0031 §1, ADR-0033 §4, §5.1–§5.6, §9, ADR-0026 (supply chain)
- **Raised by:** B-21's developer through the orchestrator (the reading), and the reviewer of PR #17 (the sibling-assembly residual, carried as `FOLLOWUP-055`)

> B-21 read *"the admission floor cannot be configured away"* literally and refused `AllowUnsigned` on a tenant-routing host in **Development** too. That reading is confirmed. It is confirmed together with the thing that makes it safe rather than merely strict, because confirming it alone would move the escape hatch from a flag Production refuses loudly to a key Production has no reason to refuse at all.

---

## 1. Context

Two questions about ADR-0033's admission control, from two directions.

**The reading.** ADR-0033 §5.2 opens *"A process that can route tenants loads only packages whose signature establishes `FirstParty`. `Partner` and `Unsigned` are refused there."* — unconditional. Its next sentence says *"`AllowUnsigned` already cannot be set outside Development, and the host refuses to start rather than honour it"*, and §5.6 D3 says the floor's refusal is *"in the same shape and for the same reason as `AllowUnsigned` outside Development"*. A reader can take those two sentences as scoping the floor to non-Development environments. B-21 did not: it refuses `AllowUnsigned` on a tenant-routing host in every environment, Development included. **ADR-0033 does not say so in those words**, and the cost is real — local package development against `Aurora.Web` would then need a development key pinned as `FirstParty`.

**The residual.** PR #17's reviewer executed one of ADR-0033's own residuals and found it larger than written. The package signature covers **the manifest-bearing assembly only** (ADR-0008 §9.3 as superseded by ADR-0031 §1: a detached signature over the SHA-256 of the assembly file followed by the manifest bytes embedded in that assembly). An unsigned **sibling DLL** dropped into an admitted package's directory *after* signing loads through the normal dependency probe and runs its module initialiser. B-21 narrowed its own claim in five places and deliberately did not number the follow-up, because the number did not exist until this record. It is `FOLLOWUP-055`.

---

## 2. Decision 1 — the floor is unconditional, and ADR-0033 §5.2 is corrected to say so

### 2.1 Confirmed

> **A host that routes tenants loads only packages whose signature establishes `FirstParty`, in every environment, including Development. `Packages:AllowUnsigned` on such a host is refused at startup regardless of environment name.**

B-21's reading is right, and the textual argument is straightforward once the sentences are separated: §5.2's first two sentences state **the floor** and carry no environment qualifier. The third sentence is not about the floor at all — it restates **ADR-0008 §9.3's pre-existing, separate rule** about `AllowUnsigned`, which *is* environment-scoped. §5.6 D3's *"in the same shape and for the same reason as `AllowUnsigned` outside Development"* names the **shape of the refusal** — the host refuses to start rather than honour the setting — and not the floor's scope. Two rules of different scope, described in adjacent sentences, is why the ambiguity existed.

The substantive argument is §5.1's: a loaded package is inside the tenancy trust boundary and .NET offers no in-process privilege boundary against it. **That is a property of the process, not of the environment variable it was started with.** A Development host that routes tenants routes to real databases; they are a developer's databases, which is a statement about the value of the data and not about what the code can reach.

### 2.2 The correction to ADR-0033 §5.2

The sentence *"`AllowUnsigned` already cannot be set outside Development, and the host refuses to start rather than honour it"* is amended to read:

> **`Packages:AllowUnsigned` is refused in two independent ways, and conflating them is what made this section ambiguous.** ADR-0008 §9.3 refuses it **outside Development on any host**. This section refuses it **on any tenant-routing host in every environment, Development included**, because the floor is a property of what the process can reach and not of what it is called. A host that routes tenants and sets `AllowUnsigned` refuses to start in Development exactly as it does in Production.

### 2.3 Confirming it alone would have made things worse, which is why §3 is part of this decision

The strict reading pushes local package development toward a **development signing key pinned as `FirstParty`**. Compare the two escape hatches:

| Escape hatch | What refuses it in Production |
|---|---|
| `Packages:AllowUnsigned` | `CountryPackageHostOptions.Create` refuses to start, loudly, asserted by a configuration test. Built |
| A development key configured as a trusted `FirstParty` key | **Nothing.** A key is a key; Production has no way to know one of them was meant for a laptop |

Confirming the strict reading and stopping there would move the risk from a guarded flag to an unguarded credential, which is this project's eighth recurring form — *a fix that silently invalidates the evidence for the claim it was fixing* — committed deliberately. §3 is therefore not a follow-up; it is the other half of this decision.

### 2.4 ADR-0033 §5.6 D3, restated

D3's assertion becomes:

> **D3 — the admission floor cannot be configured away.** A host configured to route tenants **and** to admit non-`FirstParty` packages refuses to start. Asserted for **both** environments explicitly — a `Development` case and a `Production` case, as separate tests with the same expectation — because the single-environment version of this test is the one that would pass while the floor had a Development hole. The test reports which check refused.

**Why both cases and not one parameterised case with two rows:** a parameterised test is one mechanism, and if the environment is threaded into the assertion incorrectly both rows pass together. Two tests that must both be deleted to remove the guarantee is a smaller thing to get wrong. This is a preference, not a law, and the reason is written down so a reviewer can disagree with it on the merits.

---

## 3. Decision 2 — a development key is refused in Production in the same shape as `AllowUnsigned`

> `TrustedPackageKey` gains a `KeyScope` whose **zero member is `Unstated`**: `Unstated = 0`, `Release = 1`, `DevelopmentOnly = 2`. `CountryPackageHostOptions.Create` **admits a key only when its scope is exactly `Release` or exactly `DevelopmentOnly`, and refuses every other value**, naming the key's thumbprint. `CountryPackageHostOptions.Create` additionally refuses to start when any `DevelopmentOnly` key is configured and `EnvironmentName` is not `Development`, in the same shape, with the same failure mode and for the same reason as the existing `AllowUnsigned` refusal.

**Why the zero member, and why "required and undefaulted" was not enough.** The first draft wrote `DevelopmentOnly` as a `bool` defaulting to `false`, so **omission failed open**: a development key added by someone who never heard of the flag was silently a production-capable trust anchor. The second draft replaced it with a "required, undefaulted `KeyScope`" — and **that is not producible from a two-member enum.** A CLR enum always has a zero value; a missing JSON property, `IConfiguration.Bind` and a non-nullable parameter all land on it. With `Release = 0`, omission yields a production-capable anchor again — the same failure in a new coat, in the section written to end it. **This record committed `CLAUDE.md`'s fourth form twice in three drafts, in the same paragraph.**

**What makes omission safe is that the absent state and a refused state are the same number.** `Unstated = 0` means "no value was supplied" *is* refused, so there is no spelling of omission that lands anywhere else. The repository already does this three files away: `PackageTrustLevel.Unsigned = 0`, refused explicitly in `Create`. **And one line keeps it that way:** a test asserting `default(KeyScope) == KeyScope.Unstated`, because reordering enum members is exactly the edit nobody reviews as a security change.

**But the fourth draft's refusal was still deny-by-value, and a CLR enum is not a closed set.** Executed through `IConfiguration` and `Get<T>()` on .NET 10, with `KeyScope { Unstated = 0, Release = 1, DevelopmentOnly = 2 }`:

```
configuration value          bound   IsDefined   "refuse if Unstated" admits   "accept only Release/DevelopmentOnly" admits
absent                       (0)     True        no                            no
"Release"                    (1)     True        yes                           yes
"DevelopmentOnly"            (2)     True        yes                           yes
"Release, DevelopmentOnly"   (3)     False       YES  <-- the hole              no
"3"                          (3)     False       YES  <--                      no
"99"                         (99)    False       YES  <--                      no
```

`Enum.TryParse` accepts a comma-separated list and ORs it **even for a non-flags enum**, and a bare integer binds to whatever it says. `(KeyScope)3` is neither `Unstated` — so the scope check does not fire — nor `DevelopmentOnly` — so the environment guard does not fire. **A development key becomes a `FirstParty` trust anchor on a tenant-routing production host.**

**So the check is allow-by-value, and `Enum.IsDefined` is not the fix either.** `IsDefined` rejects `3` and `99` today and would start *accepting* `3` the day someone adds a third member — admitting a key under a scope nobody decided to admit. Accepting exactly the two named members means a new member is refused until somebody adds it to the check, which is a decision rather than a consequence.

**Three coats of one failure, and the shape is now stated rather than patched:** a `bool` defaulting to `false`; then an enum whose zero member was permissive; then a refusal that denied one value out of an open set. Each fix closed the case it was shown. **Where a check decides what is admitted, enumerate the admitted values — never the refused ones.** That is the same correction ADR-0036 §3 makes by replacing a character deny-list with a grammar, and ADR-0037 §2.1.1 makes by defaulting an unlisted `ALTER TABLE` sub-action to destructive: three instances on one branch, in three rounds of review, of one rule.

**What must demonstrate it.** A configuration test over **six** cases — absent, `"Release"`, `"DevelopmentOnly"`, `"Release, DevelopmentOnly"`, `"3"`, `"99"` — asserting that exactly the second and third start the host and the other four refuse it by thumbprint. The three undefined-value rows distinguish allow-by-value from deny-by-value, and they are the rows a test written from the fix rather than from the attack would have left out.

This puts the environment guard on **the credential** rather than on the floor, which is the right place: a floor is not a thing you can accidentally ship, and a key is.

**What it does not do, stated so nobody over-reads it.** Refusing everything but two named values stops *omission* and *undefined values*; it does not stop a **misdeclaration**. Someone who adds a development key and marks it `Release` gets a production-capable anchor and no refusal. The scope is a declaration by whoever adds the key, and nothing verifies it against the key's provenance. The control against that is ADR-0033 §5.3's: adding a trusted key is an **operator action** recorded in `catalog.operator_audit_event`, with the consequence stated next to the button. **That surface does not exist.** The chain here is: a flag in configuration → a startup refusal → and it stops there. It does not reach "only keys somebody vouched for are configured", and nothing today does.

### 3.1 What the development path actually is

**Today, nothing is blocked.** `CountryPackageHostOptions.Create` is called only from tests. `Aurora.Web`, `Aurora.Worker` and `Aurora.Composition` do not load Country Packages at all. **No developer is blocked by the strict reading today**, and B-21's branch needs no development path to merge. Present tense, and it will stop being true the day a host wires package loading.

When that day comes, two paths, in this order of preference:

1. **A host that routes no tenants** — the preferred path, and the cheapest. `RoutesTenants: false` gives `AdmissionFloor = Unsigned`, so `AllowUnsigned` is legal there in Development, **and the mechanism for it already exists in B-21's code**; what does not exist is a host project that passes `false`. ADR-0033 §9 already names such a host as the first place §5.2's distinction becomes operational and as "the natural first home" for an out-of-process package host. Most package development — manifest validation, identifier validators, formatters, rate tables, report definitions — needs no tenant database at all.
2. **A `DevelopmentOnly` key**, for the part of package development that genuinely needs a tenant-routing host: signed with a locally generated ECDSA P-256 key whose public SPKI is pinned in `appsettings.Development.json` with `DevelopmentOnly: true`, the private key outside the repository. **No developer-facing signing command exists** — `PackageOnDisk` signs packages inside the test fixtures and nothing else does. That is work, and it is why path 1 comes first.

---

## 4. Decision 3 — the manifest covers a hash per shipped file, and the load context resolves only from that list

### 4.1 What the residual actually is

ADR-0033 §5.2 says admission is the control. Admission verifies **one file** and then executes **a directory**. A sibling DLL added after signing is resolved by the normal probe, loads into the package's `AssemblyLoadContext`, and runs its module initialiser — before anything the package's own code does, and therefore before any check that inspects the package.

Named in this project's own vocabulary: the chain is *signature → the code that runs*, and it stops at *the manifest-bearing assembly*. Every break on this project has lived on a link nothing was reading, and this is one: the admission control's premise (ADR-0033 §5.3 — a Country Package is reviewed at the same bar as core code) is about **what executes**, and the signature is about **one file**.

It is not a privilege escalation — ADR-0033 §5.1 is unchanged, and a sibling can do exactly what the package itself can do, no more. It is a **provenance** break, and provenance is the entire control.

### 4.2 The decision

> **The embedded manifest gains a `files` member: every file in the package directory, recursively, each with its path relative to the directory root and its SHA-256.** Admission **refuses a package directory containing any file not in the list, or any listed file whose hash does not match**. The load context resolves a path **only** if it is inside the package directory **and** in the list. A closed set over the whole tree, not an allowlist of siblings.

**"Beside the manifest-bearing assembly" was the wrong population, and the first draft of this record said exactly that.** The resolution surface `CountryPackageLoadContext` actually has is wider than a flat directory, and D8–D10 as first written would have passed against an implementation that still loads unlisted files:

| Reachable by | Where it reads from | Covered by "beside"? |
|---|---|---|
| `Load` → `_resolver.ResolveAssemblyToPath` (`:81-84`) | Anything the package's `.deps.json` names, including `runtimes/<rid>/lib/…` | **No** |
| `Load` → the flat probe (`:86-87`) | `<dir>/<name>.dll` | Yes |
| **`LoadUnmanagedDll` → `_resolver.ResolveUnmanagedDllToPath` (`:90-95`)** | **`runtimes/<rid>/native/…` — native code, which no managed check ever sees** | **No** |
| Satellite assemblies | Culture subdirectories (`<dir>/fr/…`), which ADR-0008's *"additional locales"* extension point requires a package to ship | **No** |

Three consequences follow, and each is load-bearing:

1. **The set is over the directory *tree*, not the directory.** Recursive, with relative paths, so `runtimes/linux-x64/native/libfoo.so` and `fr/Pkg.resources.dll` are members with the same standing as the main assembly.
2. **`.deps.json` must be in the list.** It is the file that *decides where the resolver looks*; an unlisted or unhashed `.deps.json` can redirect every other resolution, so leaving it out re-opens the whole hole through the one file that controls the rest.
3. **Resolution is confined to the package directory.** `AssemblyDependencyResolver` can return paths outside it — a NuGet fallback folder, a shared framework location — from a `.deps.json` that names them. A resolved path that escapes the directory root is refused even if its hash would match something, because a hash list cannot describe files the package does not own.
4. **A refusal must throw. Returning `IntPtr.Zero` is not a refusal**, and the second draft of this record specified one that was not. Executed on .NET 10, an `AssemblyLoadContext` whose `LoadUnmanagedDll` returns `IntPtr.Zero` for **every** name:

   ```
   LoadUnmanagedDll("libm.so.6") -> IntPtr.Zero  (the override refuses)
   LoadUnmanagedDll("libc.so.6") -> IntPtr.Zero  (the override refuses)
   RESULT: libm cos(0)=1  libc getpid()=4608
   VERDICT: native code EXECUTED despite the override refusing every load.
   ```

   `IntPtr.Zero` means *"I decline; fall back to the default behaviour"*, and the default behaviour is the OS loader. The same override changed to **throw** denies:

   ```
   LoadUnmanagedDll("libm.so.6") -> THROW (the override refuses)
   RESULT: threw DllNotFoundException: unlisted native library 'libm.so.6'
   VERDICT: throwing actually denied the load.
   ```

   This matters most for the packages v1 ships: a **single-assembly package has no `.deps.json`**, so `_resolver` is null and *every* unmanaged load takes the fallback path.

### 4.2.1 Even throwing is provenance, not confinement — the honest size of §4

Executed, same harness: package code calling `NativeLibrary.Load("libc.so.6")` **directly** never reaches the override.

```
RESULT: NativeLibrary.Load("libc.so.6") returned handle 0x7f7fa84b8000
        without consulting the ALC override
```

No refusal line printed — the override was not invoked — and a handle came back. `dlopen` through a `DllImport` on `libdl` is the same story. **A host cannot deny native loading to managed code in its own process**, which is ADR-0033 §5.1's conclusion arriving somewhere new. So §4's claim is bounded to exactly this:

> **The closed set controls what the loader binds *on the package's behalf* — its implicit P/Invoke resolution and its managed assembly resolution. It does not control what native code a package chooses to load, and it never could.** That is provenance — *what ran is what was signed, for everything the runtime resolved for the package* — and it is not containment.

**This un-demotes R5.** §4.4's earlier draft said ADR-0033 §5.4 R5 — the package directory is baked into the image and not writable at runtime — "stops being the only thing between an unsigned sibling and execution". True for the managed path, false for the native one and false again in §4.2.2. **R5 is a co-equal control**: the hash list governs what the loader *binds*, R5 governs what files *exist to be opened at all*, including by `NativeLibrary.Load`, which the list cannot reach.

### 4.2.2 Two more holes in "confined", named rather than assumed away

- **Confinement was specified for resolver-returned paths only.** A manifest entry is also a path, and a manifest is attacker-influenced up to the moment it is signed. Every entry must be validated as **relative, normalised, containing no `..` segment, not rooted, and not a symlink whose target escapes the root** — the last resolved at the moment the file is opened, because a symlink is the one a textual check cannot see.
- **Hashes are verified at admission and the files are used at load.** That window is a TOCTOU and **R5 is its only control.** Where R5 holds — a read-only image — the filesystem closes it. Where it does not — a writable package directory, which ADR-0033 §9 already names as a revisit trigger — the window is real, and the mitigation is to hash **at open**, from the same handle that is read, rather than in a directory walk beforehand. Stated rather than solved: v1 relies on R5.

**Why this and not a second signature.** The manifest is already embedded in the signed assembly and its bytes are already inside the signature (ADR-0031 §1). **A hash list placed in the manifest is covered by the existing signature with no new key material, no new signing step and no new trust anchor** — the signature's coverage extends from one file to the whole package for the cost of a manifest member. That is the cheapest available correction and it needs nothing this project does not already have.

**Refusing unlisted files is the load-bearing half.** Verifying the listed ones and ignoring the rest leaves the attack exactly as it was: drop a file the manifest does not mention, and the resolver still finds it. The set must be closed, and it must be closed over every path the resolver can reach — which is the correction above.

### 4.3 What must demonstrate it

- **D8** — the hostile fixture package (ADR-0033 §5.6 D1 already builds one, with a module-initialiser witness) ships a **managed sibling** assembly with its own initialiser. Admission refuses the package, and **the sibling's witness flag is observed unset**. A refusal that happens after the sibling's initialiser has run is not a refusal — D1's assertion shape, one file over.
- **D9** — a package whose listed file's bytes were changed after signing is refused, with the mismatch naming the relative path.
- **D10** — a package with an extra **unlisted** file is refused. This distinguishes a closed set from an allowlist, and it is the one most likely to be dropped as pedantic.
- **D11 — the subdirectory cases, which are where the first draft of this record was wrong.** Three packages, each refused: one with an unlisted **native** library under `runtimes/<rid>/native/`; one with an unlisted **satellite** assembly under a culture directory; one whose **`.deps.json`** is altered after signing. D11 is the test that would have failed against the first draft's specification while D8–D10 passed, which is why it is named separately rather than folded into D10.
- **D12** — a `.deps.json` naming a path **outside** the package directory resolves to nothing and the package is refused; likewise a manifest entry containing `..`, an absolute path, or a symlink whose real path escapes the root.
- **D13 — the refusal is a refusal.** A package that P/Invokes an unlisted native library fails with the loader's refusal reaching the caller. The assertion is on the **`DllNotFoundException`**, not on the resolver having been consulted: a test asserting "the override was called" passes against the `IntPtr.Zero` implementation that ran the code anyway, which is exactly how the second draft of this record shipped a refusal that was not one.
- **D14 — the residual is pinned, in ADR-0033 §5.6 D2's style.** A fixture package calls `NativeLibrary.Load` directly for a library outside the list and the test asserts it **succeeds**. It is an executable statement of §4.2.1's boundary, with two failure modes that both matter: .NET gains a way to intercept it (good news, and this section must be re-read), or someone adds a control that makes the assertion pass for a different reason.
- Each reports **files listed, files verified, files refused, and paths refused for escaping the directory.**

### 4.4 What this changes elsewhere

- **`package.manifest.json` gains a member, which is a Country Package contract change** on its own SemVer clock (ADR-0008 §3.1). It is additive and no package ships today, so it is a MINOR at most; whoever implements it makes that call explicitly rather than by omission.
- **ADR-0008 §9.3's signature description is amended** from "the assembly file followed by the manifest bytes" to the same thing **plus** the manifest's file list, which transitively covers every shipped file.
- **ADR-0033 §5.4's R5 is *not* demoted.** An earlier draft said R5 "stops being the only thing between an unsigned sibling and execution". True for the managed path, false for native code a package loads itself (§4.2.1) and false for the admission-to-load window (§4.2.2). **R5 and the hash list are co-equal: the list governs what the loader binds, R5 governs what exists to be opened.**

---

## 5. Options considered

### 5.1 The floor in Development (§2)

| Option | Pros | Cons |
|---|---|---|
| **A. Unconditional floor, escape hatch on the credential** *(chosen)* | The floor is a property of what the process can reach, which does not vary by environment name; the escape hatch keeps a loud, environment-guarded refusal; developers get a path that is not "turn the control off" | Needs `DevelopmentOnly` and, eventually, a signing command or a non-routing host. Neither exists |
| B. Floor applies outside Development only | Zero new work; keeps the existing, tested `AllowUnsigned` guard as the only hatch; the threat to a developer's own throwaway tenants is negligible | Package developers then build against a configuration Production will never run, and the first time anything is signed is the release pipeline. It also makes `ASPNETCORE_ENVIRONMENT` — a string from configuration — the thing standing between a production host and unsigned code, on the *tenant-routing* path specifically |
| C. Unconditional floor, no escape hatch at all | Simplest to state and to verify | Every package developer must produce a release-key signature to run anything locally, which means either sharing the release private key (unacceptable) or not developing packages |
| D. Leave ADR-0033 ambiguous and let each host decide | No decision to make | The ambiguity is the finding. Two readings of one sentence in a security control is the defect, whichever reading is right |

### 5.2 The sibling-assembly residual (§4)

| Option | Pros | Cons |
|---|---|---|
| **A. Per-file hashes in the manifest; the load context resolves only from the list** *(chosen)* | Reuses the existing signature with no new key material; closes the set rather than widening an allowlist; the build already produces the manifest | Build ordering: the manifest must be written after the siblings are built and before the manifest-bearing assembly is signed. A manifest contract change |
| B. Sign every file separately | Conceptually simple | N signature files, N verifications, and it still needs a list to know what N is — so it is option A plus extra crypto |
| C. Ship each package as a single assembly with no dependencies | Nothing to enumerate | Forbids a package having any dependency, including a shared helper library, forever. ADR-0008 §3.1's contract does not promise that and packages will want it |
| D. Load packages from a content-addressed store rather than a directory | Provenance by construction | A new storage mechanism and a new distribution story for a problem a manifest member solves |
| E. Accept it, as ADR-0033 §5.4 R5 does (the directory is baked into the image) | No work | It makes the image build the trust boundary rather than the signature, which is a control with no verification step and no record. And it is exactly the residual PR #17's reviewer executed |

---

## 6. Where each rule in this record stops

| Rule | Last link it follows | What is on the other side, unchecked |
|---|---|---|
| §2 the floor is unconditional | `CountryPackageHostOptions.Create`, at startup, on `RoutesTenants` | **Whether a host that routes tenants says so.** `RoutesTenants` is a constructor argument with no default (B-21's choice, and the right one); nothing derives it from the fact that a tenant `DbContext` is registered |
| §3 `KeyScope` admitted by value | An **allow-list of two members**. Omission lands on `Unstated` (pinned by a test); `"Release, DevelopmentOnly"`, `"3"` and `"99"` bind to undefined values and are refused because they are not the two — executed | **Misdeclaration.** A development key marked `Release` is admitted. Provenance is not verified and cannot be, from configuration alone |
| §3 operator recording of key additions | ADR-0033 §5.3 | **Nothing.** The operator surface that records a key addition does not exist |
| §4 the closed file set | The manifest's `files` list, inside the existing signature, over the directory tree | **Build ordering** — the manifest must be written after every sibling is built and before the bearing assembly is signed. D11 is the test most likely to catch a break here |
| §4.2 the loader refusal | A **throw** from `LoadUnmanagedDll` / `Load`. Executed: `IntPtr.Zero` is not a refusal and the code ran | Nothing for the implicit path, *provided* the refusal throws. D13 asserts the exception rather than the call |
| §4.2.1 what the set controls | What the loader binds **on the package's behalf** | **`NativeLibrary.Load` and `dlopen`**, which never consult the context — executed. No in-process way to deny these exists, per ADR-0033 §5.1. D14 pins it |
| §4.2.2 confinement | The real path at open, inside the root, for resolver-returned **and** manifest-derived entries | **The admission-to-load window.** R5 is its only control; where the directory is writable the window is real and unclosed |

## 7. Consequences

**Positive**

- ADR-0033 §5.2 now says which rule is which, so the next reader does not have to choose between two readings of a security control.
- The escape hatch sits on the credential, where an environment guard makes sense, and the floor stays a property of the process.
- §4 turns "we signed the package" into a statement about what executes rather than about one file, and it does so with a manifest member rather than new cryptography.
- `FOLLOWUP-055` has a number and a decision instead of a narrowed claim in five doc comments.

**Negative, and owned**

- **B-21 merges with no development path, and that is fine only because no host loads packages.** The day one does, either a non-routing host or a signing command must exist first. §3.1 says so in the present tense and this is the sentence to check before wiring package loading into `Aurora.Web`.
- **`DevelopmentOnly` is a declaration, not a proof.** An undeclared development key is invisible to it. §3's chain stops there and the control beyond it — an operator surface that records key additions — does not exist.
- **§4 adds a build-ordering constraint** to package packaging, and build ordering breaks quietly. D11 is the test most likely to catch it.
- **§4 is smaller than it first read.** It is provenance for what the loader resolves, not a boundary. Anyone reaching for it as containment should read §4.2.1 and ADR-0033 §5.1 instead.
- **The closed set makes a package's shape part of its signed identity.** Adding a locale, a native dependency or a `.deps.json` entry is now a re-sign, not a file copy. That is the intended cost and it will be felt first by whoever ships the first reference package.
- **The manifest contract changes before any package ships**, which is the cheapest time and still a change to a versioned public contract.

---

## 8. Revisit when

- **The first host wires Country Package loading.** §3.1's "nothing is blocked today" expires that day, and the non-routing host or the signing command must land first.
- **A non-tenant-routing host appears.** ADR-0033 §9 already names it as the trigger to re-read §5.2, and it becomes the default package-development target.
- **A `Partner` package is proposed.** §4's closed file set is a precondition for taking partner code seriously, and ADR-0033 §5.5's out-of-process boundary is the other one.
- **Any new residual is found by executing ADR-0033 §5.6's demonstrations rather than by reading them.** That has now happened twice; it is the method working, and each time the residual was larger than written.
