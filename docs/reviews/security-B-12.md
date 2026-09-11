# Security review — TASK B-12: Country Package loader and trust boundary

- Branch: `task/B-12` @ `996ed63`
- Scope (per brief): signature verification, the load context, what a package can reach, and the
  failure paths. Contract design and correctness are the general reviewer's; not re-reviewed here.
- Gate: `verify.sh` **PASS**, 419 unit tests. (Stages 7–9, including the architecture fitness test
  ADR-0008 §3.1 asks for, are PENDING and owned by B-11 — see finding H-1's blast radius.)
- Verdict: **CHANGES REQUESTED** — one high finding. Not approved.

## What I attacked and what happened

I built a hostile Country Package (`Aurora.Countries.HostilePackage`) with a `[ModuleInitializer]`,
a static type initialiser, and reflection methods that reach for forbidden assemblies, plus a stand-in
core-internal assembly (`Aurora.Ledger.Infrastructure`) carrying fake connection strings. I drove the
real `CountryPackageLoader` / `PackageSignatureVerifier` / `CountryPackageLoadContext` from a harness
and observed actual behaviour, not just the source.

1. **Is verification really before execution? (the vector the brief said matters most.)** CLEAN. I laid
   the hostile package on disk unsigned and ran `Inspect` and `Load` in a `Production` host. Both were
   refused (`country_package.untrusted`) and neither the module initialiser nor the type initialiser
   left its marker file — so no package byte executed before the signature verdict. The ordering in
   `CountryPackageLoader.Inspect`/`Load` (metadata → reference rule → signature → core-contract gate →
   `Activate`) holds: everything before `Activate` is `MetadataLoadContext`, which does not run type or
   module initialisers, custom-attribute constructors, or an entry point. I could not find a callback,
   resolver, satellite or native-library path that runs package code before the signature is checked.

2. **Can the metadata check and the runtime check disagree / be bypassed?** BROKEN — see **H-1**. The
   reference allowlist is compared case-sensitively (`StringComparison.Ordinal`) against a namespace the
   CLR binds case-insensitively. Both the pre-load metadata classifier and the runtime ALC refusal are
   defeated by changing the case of a forbidden assembly's simple name.

3. **`DeclaredTrust` self-assertion.** CLEAN. `PackageSignatureVerifier.Verify` decides on the *proved*
   level (`established.Level < metadata.Manifest.DeclaredTrust` → refused) and returns `established`,
   never the declared value; `InspectedPackage.Trust` is the proved value. The declared value appears
   only inside human-readable refusal messages, correctly labelled as a claim. The `Unsigned` path also
   refuses a package that claims more than `Unsigned` even when `AllowUnsigned` is set.

4. **Signature bytes / algorithm / curve / thumbprint.** MOSTLY CLEAN, one gap (**M-1**).
   `PackageSignature.ContentToSign` hashes the *whole* assembly file (`SHA256.HashData(FileStream)`),
   not a prefix, and appends the exact embedded manifest bytes; the 32-byte fixed hash makes the
   concatenation unambiguous. The package cannot negotiate algorithm or curve — verification uses the
   trusted key's own curve and a fixed `SHA256`. Thumbprint pinning normalises case on both sides
   (`ToHexStringLower` vs `expectedThumbprint.ToLowerInvariant()`, ordinal) and is computed over the
   SubjectPublicKeyInfo, so it cannot be defeated by case or encoding. **The gap:** the curve is *not*
   actually enforced — only the 256-bit key size is — so a non-P-256 256-bit curve is accepted (M-1).

5. **Unloadability as a resource concern.** CLEAN for B-12. Nothing in the loader/context pins the ALC:
   no static caches, no `AssemblyLoadContext.Unloading` capture, no timers, no retained delegates.
   `LoadedCountryPackage.Dispose` nulls its field and calls `Unload`. The collectibility test is honest
   (weak reference, work isolated in a `[MethodImpl(NoInlining)]` method). The real lifetime risk is
   downstream: `GetExtension` hands core live objects from the package's ALC, and whoever caches an
   `ITaxRuleProvider` etc. in a long-lived singleton will pin the context — that is B-13's to prove, and
   I flag it for routing, not as a B-12 defect.

6. **`PackageKey` → `pkg_<key>` DDL consequence.** CLEAN. `PackageKey` is `^[a-z][a-z0-9_]*\z`, max 16,
   and `SchemaName` is *derived* (`"pkg_" + value`), never read from the manifest; the manifest's own
   `schema` field is cross-checked to equal the derived value (`CountryPackageManifest.Create`). The
   character class admits nothing that could be read as SQL (no quotes, spaces, semicolons, hyphens),
   so even concatenated into DDL where no parameter can go, the value is inert. Downstream is depending
   on a real shape check, not a hope.

7. **Discovery records rejections rather than skipping them — can a rejected package be loaded another
   way?** CLEAN. `CountryPackageCatalogue.Scan` records rejections, but `CountryPackageLoader.Load` does
   not trust the catalogue: it re-runs `Inspect` (metadata + reference + signature) and the
   core-contract gate on every call. A later code path that enumerates the directory itself and calls
   `Load` on a rejected directory re-verifies from scratch, so a rejection cannot be laundered into a
   load.

**Regex-anchor completeness (the noted `\z` fix):** COMPLETE. Every shape regex in
`Aurora.Countries.Contracts` uses `\z` — `PackageKey`, `PackageId`, `PackageVersion`, `TaxCode`,
`AccountCode`, `Jurisdiction` (country/currency). No bare `$` end-anchor and no `new Regex(` anywhere.

**Secrets / PII in logs and messages:** CLEAN. Refusal messages name package id, version, assembly
names, trusted-key *thumbprints* (SHA-256 of a public key — appropriate for a post-compromise audit,
not a secret) and key counts. No connection string, credential, token or email in any message.

---

## Findings

### H-1 (high) — The referenced-assembly rule is bypassable by assembly-name casing

**Files:**
`src/Aurora.Countries.Hosting/PackageAssemblyReferenceRule.cs:40` (allowlist `HashSet` built with
`StringComparer.Ordinal`) and `:62` (`assemblyName.StartsWith(AuroraPrefix, StringComparison.Ordinal)`);
`src/Aurora.Countries.Hosting/CountryPackageLoadContext.cs:64` (`AllowedAuroraAssemblies.Contains(name)`)
and `:71` (`name.StartsWith(AuroraPrefix, StringComparison.Ordinal)`).

**The attack.** The .NET loader resolves assembly *simple names* case-insensitively, but every check
that decides "is this a forbidden `Aurora.*` assembly" uses ordinal (case-sensitive) matching. So the
same forbidden assembly, referenced or requested under a non-canonical casing of its simple name, is
classified as "not one of ours" and let through — at both gates:

- Metadata gate (before any code runs). Executed `PackageAssemblyReferenceRule.IsAllowed`:
  - `IsAllowed("Aurora.Ledger.Infrastructure")` → `False` (correctly refused)
  - `IsAllowed("AURORA.Ledger.Infrastructure")` → `True` (**passes the forbidden-reference gate**)
  - `IsAllowed("aurora.Ledger.Infrastructure")` → `True` (**passes**)
  A package whose PE reference table names the assembly under such a casing has `ForbiddenReferences`
  empty, so `Inspect` admits it and it goes on to load and execute.
- Runtime gate (`CountryPackageLoadContext.Load`). From inside a loaded, signed package, through its
  own ALC:
  - `Assembly.Load("Aurora.Countries.Hosting")` → refused (`PackageReferenceRefusedException`) — the
    control works for the canonical case.
  - `Assembly.Load("AURORA.Countries.Hosting")` → **returns the host's real
    `Aurora.Countries.Hosting`** (falls past the ordinal `StartsWith`, probe misses, runtime falls back
    to the default context, which binds case-insensitively).
  - `Assembly.Load("aurora.Countries.Hosting")` → **returns the host's real assembly** too.

Both gates fail identically, so they "agree" — on the wrong answer. This is the mechanism the whole
"a package *cannot*, by construction, reach any other `Aurora.*` assembly" claim (README, ADR-0008 §3.1)
rests on, and it is a case-sensitive string compare, i.e. a convention, not a mechanism.

**Why high, and the honest blast radius.** ADR-0008 §9.4 names the three real controls as "the
signature, the reference rule, and v1 accepting first-party packages only." This defeats one of the
three — the *pre-execution* gate whose entire value is refusing a bad package before it runs. Mitigating
context I want the orchestrator to weigh: (a) v1 loads first-party packages only, reviewed like core
code, so a *malicious* exploit is not the v1 threat; (b) §9.4 already concedes that loaded code runs
full-trust and can reach any assembly via `AssemblyLoadContext.Default` regardless (see the L-1 note),
so this grants no new *runtime* capability to code that already runs. What it does grant is admission:
a package that *statically declares* a forbidden reference is supposed to be refused at discovery and
never executed, and casing gets it admitted and run. It is rated high because it is a named security
control that can be bypassed by trivial, attacker-controlled input, and because the compensating
build-time architecture fitness test (ADR-0008 §3.1, verify.sh stage 7) is still PENDING (B-11) — so
this host-side check is currently the *only* thing enforcing the reference rule.

**Fix.** Compare assembly simple names the way the loader binds them — case-insensitively:
- Build `AllowedAuroraAssemblies` with `StringComparer.OrdinalIgnoreCase` (so a case-variant of an
  *allowed* assembly is still recognised and returns `null` → default context, and a case-variant of a
  *forbidden* one is not silently treated as non-Aurora).
- Change both `StartsWith(AuroraPrefix, StringComparison.Ordinal)` sites to
  `StringComparison.OrdinalIgnoreCase`.
- Add a test with `AURORA.*` / `aurora.*` casings to both `PackageAssemblyReferenceRuleTests` and
  `CountryPackageLoadContextTests` (the current tests only exercise the canonical casing, which is why
  this passed 419 green).

### M-1 (medium) — The signing curve is not enforced; only the 256-bit key size is

**File:** `src/Aurora.Countries.Hosting/CountryPackageHostOptions.cs:88` (`TrustedPackageKey.Create`,
`if (key.KeySize != PackageSignature.KeySizeInBits)`).

**The attack.** The docstring says it refuses "one that is not ECDSA on P-256" and ADR-0008 §9.3 says
"ECDSA P-256 ... and nothing else. Accepting a second curve would mean accepting the weakest one on the
list." The code only checks `key.KeySize == 256`. Executed: a freshly generated **secp256k1** key
(256-bit, a different curve) reports `KeySize == 256` and `TrustedPackageKey.Create` **accepts it**. So
an operator can pin a trusted key on any 256-bit curve the runtime supports, contrary to the stated
policy — a control that enforces less than it claims (CLAUDE.md self-check #1). This is on the operator
trust boundary and needs a misconfiguration to matter, and the package cannot itself choose the curve,
so it is defence-in-depth rather than a direct package-admission hole — hence medium.

**Fix.** Enforce the curve, not the bit length: compare `key.ExportParameters(false).Curve.Oid.Value`
against the P-256 OID `1.2.840.10045.3.1.7` (nistP256 / secp256r1 / prime256v1), and add a test that a
256-bit non-P-256 key is refused.

### L-1 (low) — README overstates the runtime reach guarantee

**File:** `src/Aurora.Countries.Hosting/README.md` ("Cannot, by construction: any other `Aurora.*`
assembly ... refused *again* by `CountryPackageLoadContext.Load` if a package asks for one by name at
runtime, because otherwise the default context would hand over the host's copy.")

The stated rationale is not quite right: the ALC `Load` override is only consulted on the *implicit*
binding path routed through the package's own context. A package that calls
`AssemblyLoadContext.Default.LoadFromAssemblyName(...)` directly bypasses the override entirely — I
confirmed a loaded package obtaining the host's live `Aurora.Countries.Hosting`, `Aurora.SharedKernel`
and `Aurora.Countries.Contracts` this way in one line. The file's *next* paragraph (full trust, "not a
sandbox", §9.4) is honest and correct, so a reader is not left believing in a sandbox; this is a
wording precision issue, and once H-1 is fixed the *metadata* gate (the one that matters, pre-execution)
holds. Recommend softening the "by construction ... refused again at runtime" bullet to say the runtime
override guards the implicit binding path only and is not a confinement boundary.

---

## Route elsewhere
- **B-13 (extension lifecycle):** whoever consumes `ICountryPackage.GetExtension` must not cache the
  returned objects (or delegates over them) in a static or long-lived singleton, or the collectible ALC
  never unloads. B-12 proves the context *can* be collected; keeping it collectible is B-13's to prove.
- **B-11 (architecture fitness tests):** stage 7 is pending, and ADR-0008 §3.1's arch test asserting no
  package references a forbidden `Aurora.*` assembly is the compensating control for H-1. It should
  land with a case-varied negative case, so the two enforcement points (build-time and host-time) do
  not both share H-1's blind spot.
