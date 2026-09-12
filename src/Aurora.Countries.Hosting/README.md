# Aurora.Countries.Hosting

Discovery, manifest reading, signature verification, the core-contract compatibility gate and the
collectible load context (ADR-0008 §3.3, §5.1 steps 2–3, §9). Everything up to the point where a
package's own code runs.

The nine install steps of ADR-0008 §5.1 belong to **B-13**. This assembly owns steps 2 and 3 and the
loading machinery they protect; it touches no database and knows nothing about tenants.

## The order, which is the point

```
directory  →  metadata (no code runs)  →  reference rule  →  signature  →  admission floor  →  coreContractRange  →  load
```

Metadata, references, trust, the admission floor and compatibility are all settled while the
package is still an inert file. `MetadataLoadContext` reads an assembly the way a decompiler does:
no type initialisers, no module initialisers, no entry point. A package that fails any of these
checks has never been given a thread, so there is nothing to undo — which is why verification comes
before schema creation rather than after it. That "never been given a thread" is asserted, not
described: `PackageAdmissionFloorTests` loads a fixture package whose module initialiser records
that it ran, and observes the record absent after every refusal (ADR-0033 §5.6 D1).

| Type | What it does |
|---|---|
| `PackageMetadataReader` | Reads the manifest and the referenced assemblies without executing anything |
| `PackageAssemblyReferenceRule` | The allowlist of `Aurora.*` assemblies a package may reference, matched case-insensitively as the loader binds names |
| `PackageSignature`, `PackageSignatureVerifier`, `TrustedPackageKey` | ECDSA P-256 over SHA-256(assembly) ‖ manifest bytes, against thumbprint-pinned keys |
| `CoreContractGate` | Refuses an out-of-range or open-ended `coreContractRange`, naming both versions |
| `CountryPackageLoadContext` | One collectible ALC per (package, version) |
| `CountryPackageLoader`, `LoadedCountryPackage` | Inspect, gate, load, unload |
| `CountryPackageCatalogue` | What this deployment has available, with what it rejected and why |
| `CountryPackageHostOptions` | Where packages live, which keys are trusted, whether unsigned ones may load, and whether this host routes tenants — which sets its `AdmissionFloor` |

## The admission floor (ADR-0033)

A loaded package runs inside the process, and **the process is the tenancy trust boundary**
(ADR-0033 §5.1). .NET offers no in-process privilege boundary against loaded managed code — not a
weak one, none (§4, checked against .NET 10's own documentation) — so a package a tenant-routing
process loads can reach every tenant that process can, and confinement is not available as a
control. The only control is **admission**: deciding what executes at all.

- A host says what it is: `CountryPackageHostOptions.Create(..., routesTenants:)` has no default.
  A host that routes tenants has `AdmissionFloor == FirstParty`; one that does not has no floor
  above ADR-0008 §9.3's signature rules.
- `CountryPackageLoader.Load` refuses a package whose signature establishes less than the floor
  with `country_package.below_admission_floor`, after inspection and before any load context
  exists. `Inspect` is unchanged: a `Partner` package on a tenant-routing host is **listed and not
  loaded** — ADR-0033 §5.2 narrows *where* the level takes effect, it does not remove it, so a
  `Partner` key may still be configured beside a `FirstParty` one.
- The floor cannot be configured away. A tenant-routing host with `Packages:AllowUnsigned` refuses
  to start in every environment, Development included; so does one whose keys cannot establish the
  floor, because it could never load anything (§5.6 D3, `CountryPackageHostOptionsTests`).

**What the signature covers, and so what the floor admits.** The signature is over the
manifest-bearing assembly and the manifest (`PackageSignature.ContentToSign`), and the floor is a
judgement about *that* assembly. "A tenant-routing host loads only first-party packages" is
therefore true of the package and **not of everything that ends up executing in its context**: a
private dependency the package ships beside itself is resolved by the load context's dependency
probe on the strength of the package's admission and carries no signature of its own. Executed by
the reviewer of PR #17: an unsigned sibling DLL dropped into an admitted package's directory after
signing leaves the package verifying as `FirstParty`, is loaded through the normal probe, and runs
its module initialiser. That is bounded by the same write to the package directory ADR-0033 §5.4 R5
already accepts — but it bites with no attacker at all for any package that ships dependencies, and
it is raised to the architect as a further ADR-0033 residual.

**What none of this demonstrates.** That a package cannot reach tenant data. It cannot demonstrate
that, because it is not true: an admitted package can read the process's configuration and secret
material and open its own connection to any tenant database, naming no tenancy type at all
(ADR-0033 §5.4 R1–R3). The floor closes the admission gap only. The residual is real, it is written
down in the ADR, and `PackageAdmissionFloorTests` says so in its own documentation so that a green
suite cannot be read as a sandbox.

## What a package can and cannot reach

**Can:** the contract assembly and the two tier-0 assemblies it is expressed in
(`Aurora.SharedKernel`, `Aurora.Documents.Canonical`); its own private dependencies, at its own
versions, resolved inside its own load context; and whatever each extension point is handed as an
argument — a canonical document, a batch of payment instructions, an `IInterchangeSource` to pull
from, a `Stream` to write to.

**Cannot declare:** a reference to any other `Aurora.*` assembly. `PackageAssemblyReferenceRule`
refuses it from the package's metadata before any of its code runs, and that is the control that
matters: a package that statically references core's internals is never executed.
`CountryPackageLoadContext.Load` also throws `PackageReferenceRefusedException` when a package asks
for such an assembly by name through its own context, so an implicit bind does not quietly fall
through to the host's copy — but that override guards the implicit binding path only. Code that is
already running can call `AssemblyLoadContext.Default` directly, in one line, and get the host's
live assemblies; the override is a tripwire on the ordinary path, not a confinement boundary (see
the next paragraph). There is no `DbContext`, no connection string, no service provider and no
tenant anywhere in the contract, so a package cannot reach a tenant the caller did not open.

**Cannot, because it is not built yet, but is not prevented either:** everything else a process can
do. An `AssemblyLoadContext` is version isolation and unloadability, **not a sandbox**
(ADR-0008 §9.4). Loaded package code runs in-process with full trust: it can read any file this
process can read and open any socket. The controls that decide whether hostile code runs at all are
upstream — the signature, the reference rule, and the admission floor of ADR-0033 §5.2 (a
tenant-routing host loads first-party packages only). Nobody should read this file and conclude
there is a plugin sandbox here.

## What happens when signature verification fails

Nothing loads, and the failure is specific. The verifier returns a `Result` failure carrying
`country_package.untrusted` with a message that names the package, its version, and which of these
happened:

- **No `package.sig`.** Refused, unless `Packages:AllowUnsigned` is set — and
  `CountryPackageHostOptions.Create` refuses to build at all if that flag is set outside
  `Development`, so the host will not start rather than run with it. An unsigned package is admitted
  at `PackageTrustLevel.Unsigned`, never higher.
- **An empty `package.sig`.** Refused even where unsigned packages are allowed: an empty signature is
  a broken build, not the absence of a signature, and treating the two the same would let a
  truncated file through.
- **A signature that verifies against no trusted key.** Either the package was changed after signing
  or it was signed by a key this platform does not trust. The message says both, with the number of
  keys tried.
- **A manifest claiming more trust than the signature established.** The manifest is bytes inside the
  package, so `DeclaredTrust` proves nothing; the refusal names the claim and what was actually
  proved. This is the shape an attempt to pass a package off as first-party takes.

Discovery records the rejection against the directory rather than skipping it, so a package that
fails to appear is visible in the catalogue as a rejection with a reason — not as an absence.

## Version drift, and why the message names both versions

`CoreContractGate` is the gate Odoo does not have. Odoo's core and localization modules are versioned
semi-independently with no automated compatibility check, and the result is a named, recurring
support issue thrown at runtime that names neither version. Ours refuses at install and again at
every core upgrade, and names the package, its declared range, the running core contract version,
and — when the catalogue is offered — which version of the package would work instead.

The range must be bounded at both ends. NuGet reads a bare `1.0.0` as "1.0.0 or anything later",
which would declare a package compatible with every MAJOR core contract not yet written — including
the one that removes a member it calls — so the gate refuses `1.0.0`, `[1.0.0, )` and `(, )` even
when they admit the version running today, and names `[1.0.0, 2.0.0)` as the shape to use.

`CountryPackageLoader` takes the core contract version as a constructor parameter so the
release-build fleet compatibility report of ADR-0008 §5.2 can ask the same question about a *target*
version this process is not running, without a second implementation of the rule.
