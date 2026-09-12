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

> `TrustedPackageKey` gains `DevelopmentOnly` (default `false`). `CountryPackageHostOptions.Create` **refuses to start** when any key with `DevelopmentOnly = true` is configured and `EnvironmentName` is not `Development` — the same shape, the same failure mode and the same reason as the existing `AllowUnsigned` refusal, whose message is already written.

This puts the environment guard on **the credential** rather than on the floor, which is the right place: a floor is not a thing you can accidentally ship, and a key is.

**What it does not do, stated so nobody over-reads it.** It does not stop someone configuring a development key *without* the flag — the flag is a declaration by whoever adds the key, and an undeclared development key is indistinguishable from a release key. The control against that is ADR-0033 §5.3's: adding a trusted key is an **operator action** recorded in `catalog.operator_audit_event`, with the consequence stated next to the button. **That surface does not exist.** The chain here is: a flag in configuration → a startup refusal → and it stops there. It does not reach "only keys somebody vouched for are configured", and nothing today does.

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

> **The embedded manifest gains a `files` member: every file the package ships beside its manifest-bearing assembly, each with its SHA-256.** `CountryPackageLoadContext` resolves an assembly **only** if it is in that list and its hash matches, and admission **refuses a package directory containing any file not in the list**. A closed set, not an allowlist that tolerates extras.

**Why this and not a second signature.** The manifest is already embedded in the signed assembly and its bytes are already inside the signature (ADR-0031 §1). **A hash list placed in the manifest is covered by the existing signature with no new key material, no new signing step and no new trust anchor** — the signature's coverage extends from one file to the whole package for the cost of a manifest member. That is the cheapest available correction and it needs nothing this project does not already have.

**Refusing unlisted files is the load-bearing half.** Verifying the listed ones and ignoring the rest leaves the attack exactly as it was: drop a DLL the manifest does not mention, and the probe still finds it. The set must be closed.

### 4.3 What must demonstrate it

- **D8** — the hostile fixture package (ADR-0033 §5.6 D1 already builds one, with a module-initialiser witness) ships a **sibling** assembly with its own initialiser. Admission refuses the package, and **the sibling's witness flag is observed unset**. A refusal that happens after the sibling's initialiser has run is not a refusal — the same assertion shape D1 already uses, applied one file over.
- **D9** — a package whose sibling is listed but whose bytes were changed after signing is refused, with the mismatch named.
- **D10** — a package with an extra unlisted file is refused. This is the one that distinguishes a closed set from an allowlist and it is the one most likely to be dropped as pedantic.
- Each reports **files listed, files verified, files refused.**

### 4.4 What this changes elsewhere

- **`package.manifest.json` gains a member, which is a Country Package contract change** on its own SemVer clock (ADR-0008 §3.1). It is additive and no package ships today, so it is a MINOR at most; whoever implements it makes that call explicitly rather than by omission.
- **ADR-0008 §9.3's signature description is amended** from "the assembly file followed by the manifest bytes" to the same thing **plus** the manifest's file list, which transitively covers every shipped file.
- **ADR-0033 §5.4's residual list loses one entry** — R5's "the package directory is baked into the image and not writable at runtime" was doing work that this decision does properly. R5 stays as defence in depth; it stops being the only thing between an unsigned sibling and execution.

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

## 6. Consequences

**Positive**

- ADR-0033 §5.2 now says which rule is which, so the next reader does not have to choose between two readings of a security control.
- The escape hatch sits on the credential, where an environment guard makes sense, and the floor stays a property of the process.
- §4 turns "we signed the package" into a statement about what executes rather than about one file, and it does so with a manifest member rather than new cryptography.
- `FOLLOWUP-055` has a number and a decision instead of a narrowed claim in five doc comments.

**Negative, and owned**

- **B-21 merges with no development path, and that is fine only because no host loads packages.** The day one does, either a non-routing host or a signing command must exist first. §3.1 says so in the present tense and this is the sentence to check before wiring package loading into `Aurora.Web`.
- **`DevelopmentOnly` is a declaration, not a proof.** An undeclared development key is invisible to it. §3's chain stops there and the control beyond it — an operator surface that records key additions — does not exist.
- **§4 adds a build-ordering constraint** to package packaging, and build ordering is a class of thing that breaks quietly. D10 is the test most likely to be the one that catches it.
- **The manifest contract changes before any package ships**, which is the cheapest time and still a change to a versioned public contract.

---

## 7. Revisit when

- **The first host wires Country Package loading.** §3.1's "nothing is blocked today" expires that day, and the non-routing host or the signing command must land first.
- **A non-tenant-routing host appears.** ADR-0033 §9 already names it as the trigger to re-read §5.2, and it becomes the default package-development target.
- **A `Partner` package is proposed.** §4's closed file set is a precondition for taking partner code seriously, and ADR-0033 §5.5's out-of-process boundary is the other one.
- **Any new residual is found by executing ADR-0033 §5.6's demonstrations rather than by reading them.** That has now happened twice; it is the method working, and each time the residual was larger than written.
