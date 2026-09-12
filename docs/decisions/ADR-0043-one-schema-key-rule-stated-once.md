# ADR-0043 — The `pkg_` schema key's rule is stated once, and the bound is the one a column enforces

- **Status:** Accepted (2026-09-12)
- **Deciders:** architect
- **Supersedes:** —
- **Amends:** — (no accepted decision is changed; ADR-0008 §4.1's ambiguity is **not** resolved here, see §4)
- **Related:** ADR-0008 §4.1 R1 (`pkg_<key>` schemas), ADR-0035 §2 (why `InstalledPackages` carries catalog **text** and not Country Package contract types), **ADR-0038 §2.1–§2.2 and §2.6** (`task/ARCH-MIGRATION-IDENTIFIERS`, PR #19, unmerged), ADR-0040 §4.2 (the same drift shape, one level up), `FOLLOWUP-058`, `B-13-PRE`
- **Raised by:** PR #13's fifth review, routed through the orchestrator

> Two types in two assemblies each say they are the stem of `pkg_<…>`, each justify their length bound by PostgreSQL's 63-byte identifier limit, and they disagree. A 20-character key is accepted by the catalog and refused by the Countries contract. This decides the one thing that does not depend on `B-13-PRE`.

---

## 1. The facts, read off the code

| | `Aurora.Countries.Contracts.PackageKey` | `Aurora.Platform.Tenancy.Contracts.PackageIdFormat` (on `task/B-06.1a`) |
|---|---|---|
| Character rule | `^[a-z][a-z0-9_]*\z` | `^[a-z][a-z0-9_]*$` |
| Length bound | `MaxLength = 16` | `MaxLength = 32` |
| Claims to be | *"the schema the package may create objects in: `pkg_nz`"* | *"the stem of the package's `pkg_<id>` schema (ADR-0008 §4.1)"* |
| Justifies its bound by | *"`pkg_` plus the key must stay inside PostgreSQL's 63-byte identifier limit"* | *"what keeps `pkg_` plus the id clear of PostgreSQL's 63-byte identifier truncation"* |
| Enforced by anything else? | **No.** The literal is enforced by `PackageKey.Create` and nothing else | **Yes.** `InstalledPackageConfiguration` builds *both* the check constraint and `HasMaxLength` from it (ADR-0038 §2.2), and the catalog column is `package_id varchar(32)` with `ck_installed_package_id_well_formed` |

**The live consequence, today, on merged code plus one branch in review:** a 20-character key — `my_long_package_name` — satisfies the character rule, fits `varchar(32)`, passes `ck_installed_package_id_well_formed`, and is accepted by `PackageIdFormat.Create`. **`PackageKey.Create` refuses it.** The refusal lands at the Countries boundary on a value the catalog has already stored, which is to say after install, not at it.

**On the 63-byte justification.** `pkg_` is four bytes, so the limit implies a ceiling of 59. Neither 16 nor 32 is that number; both are *sufficient* choices under it, and ADR-0038's own reasoning is explicit about which mechanism produces its number (`4 + 32 = 36`, safely clear). So the sentence in each type is a true statement about a constraint neither bound is derived from — which is why two authors writing it independently arrived at different literals and neither could tell.

---

## 2. Decision 1 — they are two statements of one rule, and that is not a `B-13-PRE` question

`B-13-PRE` is about a **global package id** (`aurora.country.nz`, dotted, 128 characters) versus a **schema key** (`nz`). This is not that. Both types above describe the **schema key**: `PackageKey`'s own summary names `pkg_nz`, and **ADR-0038 §2.6 has already ruled that `PackageIdFormat`'s pattern is the schema key's** — *"What is decided is that §2.3's composer takes the schema key, whatever the resolution calls it, and that §2.1's pattern is the schema key's."*

> **Ruling: `PackageKey` and `PackageIdFormat` state the same rule about the same identifier, and must therefore agree.** Nothing about that conclusion waits on `B-13-PRE`, which decides only whether a *second*, global identifier exists beside this one and which column holds which.

---

## 3. Decision 2 — the bound is **32**, and `PackageKey` stops declaring one

> **The schema key's length bound is 32. `PackageKey.MaxLength` is removed; `PackageKey.Create` validates the character rule and defers the length to the one bound a column enforces.**

Three reasons, in increasing order of how much each would still hold if the previous were answered:

1. **32 is the bound with a mechanism.** `InstalledPackageConfiguration` builds the check constraint *and* `HasMaxLength` from `PackageIdFormat.MaxLength`, so the database and the type cannot drift (ADR-0038 §2.2). `16` is enforced by `PackageKey.Create` and by nothing else. Choosing the number a stored column enforces is the only choice that cannot reproduce "accepted, then refused".
2. **Widening 16 → 32 accepts strictly more and rejects nothing that works today.** The reverse — narrowing 32 → 16 — would refuse values `catalog.installed_package` can already hold, which is a `Contract` under ADR-0007 §7.2 with no migration to carry it and no installed package to migrate.
3. **Neither literal is derived from the 63-byte sentence** (§1), so there is no correctness argument for 16 over 32 to weigh against either of the above.

**And the justification sentence is corrected in both places rather than copied a third time.** `pkg_` plus the key must stay inside 63 bytes; that is a **ceiling of 59 that 32 is comfortably inside**, not the derivation of 32. A doc-comment that states a reason which does not produce the number beside it is `CLAUDE.md` self-check #1, and it is what let these two drift silently.

---

## 4. Decision 3 — two declarations plus a rule, not one shared constant

The obvious fix is one constant in a place both assemblies see. Both `Aurora.Countries.Contracts` and `Aurora.Platform.Tenancy.Contracts` reference `Aurora.SharedKernel` (checked), so it would fit without touching layering rule L2.

> **It is refused.** `Aurora.SharedKernel` is the domain kernel. Putting a PostgreSQL schema-naming rule in it re-couples the Country Package contract and the tenancy contract *through* the kernel — which is precisely what ADR-0035 §2 spent a decision avoiding when it made `InstalledPackages` carry the catalog's **text** rather than the Country Package contract types, *"so that a package-contract MAJOR bump is not a tenancy change"*. A shared constant in the kernel would make the schema key's rule exactly that kind of shared surface.
>
> **Instead: the two declarations stay, and a fitness rule asserts they are equal.** Population: the declarations found. Report **declarations found / compared / equal**, and fail if the count is not the number the rule expects — a comparison that finds one declaration and reports "no disagreement" is the shape this project has shipped once.

**Why "two copies plus a rule" is acceptable here when `FOLLOWUP-058` says it is not.** `FOLLOWUP-058`'s drift *could not fail anything* — `verify.sh`'s `--help` text is never asserted against the floor it describes, so both drifts survived every gate and were found by a human reading the file. Its recommended fix, in its own words, is *"assert it against the value in `verify-selftest.sh` so the drift is a failure rather than a reading."* That is exactly this. The distinction is not the number of copies; it is whether a machine compares them.

**Where this stops:** at the two declarations the rule knows to compare. A third statement of the same rule — a regex in a migration, a literal in a validator, a package manifest schema — is outside its population until somebody adds it, and nothing discovers one. The mitigation is the report's count, not a claim of completeness.

---

## 5. What this ADR does not decide

- **`B-13-PRE`** — whether a global package id exists beside the schema key, and which column holds which. Unchanged, still blocked on B-13's installer shape, still the right call to leave undecided (ADR-0038 §2.6).
- **`Aurora.Countries.Contracts.PackageId`'s** own rule (`^[a-z0-9]+(?:\.[a-z0-9]+)+$`, 128 characters). If `B-13-PRE` resolves as "store both", that is a *different* identifier with a legitimately different rule and §2's ruling does not reach it. If it resolves as "derive the key from the id", the derivation is B-13's to specify.
- **Whether 32 is the right ceiling at all.** It is the one that is enforced; §3 chooses between two existing numbers and does not re-open the question. Raising it later is a catalog migration and an ADR.

---

## 6. Consequences and revisit when

**Positive**

- A package key that the catalog accepts is a package key the Countries contract accepts. The failure mode this removes is the worst-shaped one available: a value stored successfully and refused later by a different layer.
- One statement of the rule is enforced by a column; the other defers to it; a machine compares them.
- The false justification is removed rather than propagated, which is what would otherwise produce a third literal.

**Negative, and owned**

- **Two declarations remain**, and the rule that compares them does not exist yet. Until it does, this ADR is a specification and the drift is still only a reading.
- **The comparison's population is what somebody thought to compare.** §4's last paragraph is the honest limit.
- **`PackageKey` losing its bound makes it depend on a rule stated in an assembly it cannot reference.** That is the cost of ADR-0035's decoupling and is paid deliberately; the fitness rule is what keeps it honest.
- **`PackageIdFormat` is on an unmerged branch** (`task/B-06.1a`, in rework). Nothing here can be implemented before it merges.

**Revisit when**

- **`B-13-PRE` resolves.** If it produces two identifiers, re-read §2: the premise that these two types describe the same thing is the one that would change.
- **A third statement of the schema-key rule appears** — §4's population stops being complete, and the rule's count is what should show it.
- **`catalog.installed_package.package_id` is widened or renamed.** §3's reason 1 points at that column; if it moves, the bound moves with it.
