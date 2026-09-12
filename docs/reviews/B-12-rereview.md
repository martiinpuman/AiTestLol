# B-12 — `Aurora.Countries.Contracts` and `Aurora.Countries.Hosting` — second-reviewer re-review

- **Verdict:** APPROVE
- **Tier:** Full (correct as dispatched; no escalation). Second reviewer, not the author of the code
  and not the author of either first review.
- **Branch:** `task/B-12-rework` @ `19c263e`, 8 rework commits on top of `996ed63`
- **Reworked against:** `docs/reviews/B-12.md` (M1–M4) and `docs/reviews/security-B-12.md`
  (H-1, M-1, L-1). All seven verified below.
- **Method:** every claim that carries the rework was re-run in a throwaway copy of the branch
  (`scratchpad/mut-B-12`), never on the branch. Mutations were injected into the copy, rebuilt
  `--no-incremental`, and reverted. The worktree at `/tmp/rereview-B-12` stayed clean apart from this
  file.

## Gate

```
/tmp/rereview-B-12 $ ./scripts/verify.sh
 0   Preflight                   PASS         0.1s
 1   Restore                     PASS         1.5s
 2   Format & style              PASS        15.1s
 3   Build                       PASS         2.9s
 4   Dependency licence gate     PENDING         -  owned by B-11
 5   Vulnerability gate          PENDING         -  owned by B-11
 6   Unit tests                  PASS         3.5s  440 test(s) executed
 7   Architecture fitness tests  PENDING         -  owned by B-11
 8   Integration tests           PENDING         -  owned by B-11
 9   UI component tests          PENDING         -  owned by B-11
 10  Coverage report             PENDING         -  owned by B-11
 11  Summary                     PASS         0.0s
 Gate incomplete: stage(s) 4 5 7 8 9 10 are not implemented yet.
 RESULT: PASS
```

```
/tmp/rereview-B-12 $ ./scripts/verify-selftest.sh
 ...
 21/21 cases behaved as specified.
 working tree: back to the state the run started from.
```

**440 executed, reproduced project by project:** SharedKernel 208, Countries.Contracts 141,
Countries.Hosting 91 — exactly the sum in the stage 6 comment and exactly the new floor. The
self-test ran to completion (the peer review's merge precondition) and restored the tree.
`git status --porcelain` empty before and after both runs.

## The two claims that carry the rework

### M1 — the tax property generator: reproduced, and it can fail

- **Mutation.** `TaxRate.ApplyTo`'s integer path replaced by
  `decimal.Round(amount * (percent / 100m), currency.MinorUnits, midpoint)`, everything else intact:
  **3 of 141 red**, and the new property is one of them —
  `Tax_one_ulp_short_of_a_half_minor_unit_is_not_rounded_up_to_it`, falsified after 4 generated
  cases with the case named in the failure message:
  `-53469602.133599901587641440083 JPY @ 572.115137% → -305907688 JPY, should be -305907687 JPY`.
  (The other two are the pinned example and the unrelated `MidpointRounding` exception-type row the
  first review already noted.) Before the rework the same mutation left both properties green; the
  generator now reaches the region.
- **Statistics, my own seed, replicating `BoundaryCases`/`OneUlpShortOfTheHalf` verbatim:** 500 draws,
  **500 constructible, 0 throws, 188 phantom minor units (37.6%) against the naive implementation,
  0 against the shipped one.** The author reported 500/500 and 197 (39.4%); the difference is the
  seed, and the substance reproduces.
- **Arbitrary where it says arbitrary — checked against the code, not the comment.** `BoundaryCases`
  draws the rate through the same `Rates()` used by the random properties (`Gen.Choose(0,999)` percent
  plus `Gen.Choose(0,999_999)` millionths) and the target through `Gen.Choose(0, 999_999_999)`; sign,
  midpoint and currency are `Gen.Elements` over fixed lists. Measured over 500 draws: **500 distinct
  rates**, rate scales 4–6, targets spread over 4 086 267 … 998 759 413, all three currencies drawn.
  The remark's division into "arbitrary: rate and target" and "a fixed list: currency, midpoint, sign"
  is the code. This is not the first review's defect in a new costume.
- **The near-miss is genuinely closed.** The constructed amounts come back at scale 18–25 with
  magnitudes from 1.9e3 to 1.3e10 — decimals that exist, not the scale-28 construction that fitted
  nothing. And the invariant throws rather than skips: with `mantissa /= 2` injected after the ulp
  step so the construction lands outside its own region, the property goes **red** with
  `System.InvalidOperationException: The boundary construction is wrong: 5870231162784865313106456502E-23 BHD
  at 661.824567% taxes to at most 777012639 minor units, not just under 777012639 + ½`. An exception
  raised inside the FsCheck generator propagates as a test failure; it is not discarded and retried,
  so a draw cannot silently vanish and the property cannot go vacuous the way it did on the first try.
- **Extremes.** 120 hand-built cases across JPY (0 minor units), NZD (2) and BHD (3) × rates
  0.000001 %, 0.5 %, 15 %, 999.999999 % × targets 0, 1, 7, 999, 999 999 999 × both signs:
  **0 construction throws, 0 disagreements with the shipped implementation, 84 phantom minor units
  against the naive one** — including `JPY 15% of 3.3333333333333333333333333333 → ¥1 where the
  answer is ¥0`. The construction holds at both ends of the minor-unit range and at both ends of
  magnitude.

### H-1 — the casing bypass: reproduced, and no third spelling gets through

- **Against the old comparisons** (both `StartsWith(AuroraPrefix, …)` sites and the allowlist comparer
  put back to `Ordinal`, rebuilt): **7 of 91 red** — 3 `IsAllowed` rows, 3 load-context rows
  (`AURORA.Countries.Hosting` and `aurora.countries.hosting` each returning the host's real assembly,
  `aurora.Platform.Tenancy` failing with FileNotFound instead of a refusal), and
  `The_allowed_set_matches_names_the_way_the_loader_binds_them`. On the branch: **91/91 green.**
- **Third-spelling hunt.** I drove `CountryPackageLoadContext` with twelve spellings of a forbidden
  assembly that is really loaded in the host (`Aurora.Countries.Hosting`), building the request with
  `new AssemblyName { Name = … }` so the display-name parser could not normalise it first, and
  separately asked `AssemblyLoadContext.Default` what it binds:

  | spelling | metadata gate admits | through the package ALC |
  |---|---|---|
  | `Aurora.Countries.Hosting` / `AURORA.…` / `aurora.…` | no | refused |
  | trailing space, trailing tab, trailing dot, inner space | no | refused |
  | leading space | **yes** | not bound (`FileLoadException`) |
  | Turkish dotless `ı`, Turkish dotted `İ`, long `ſ` | no | refused |
  | `…, Version=…, Culture=…, PublicKeyToken=…` (any casing) | n/a | parses to the simple name → refused |

  The default context binds `Aurora.…`, `AURORA.…`, `aurora.…` **and** `aurora.countrıes.hostıng` to
  the host's real assembly, but every one of those is caught by the `Aurora.` prefix test — the prefix
  contains no `i`, so no culture-variant folding can make it miss, and `OrdinalIgnoreCase` is
  culture-invariant regardless. A version- or token-qualified display name reduces to the simple name
  before `Load` sees it. I found no spelling the loader treats as equivalent that either check misses
  (the leading-space case is nit 2 below: admitted at the metadata gate, but the runtime will not bind
  it, so nothing is reachable through it).
- **Nothing switched to a culture-sensitive comparison.** Every `StringComparison`/`StringComparer` in
  `src/Aurora.Countries.Hosting` and `src/Aurora.Countries.Contracts` is `Ordinal` or
  `OrdinalIgnoreCase`; there is no `CurrentCulture*`, no `ToLower()`/`ToUpper()` without
  `Invariant`. The two `OrdinalIgnoreCase` sites outside the reference rule are the environment-name
  comparison, which is correct there.

## The other five, verified briefly

Reverting **all** of `src/` to `996ed63` while keeping the reworked tests, rebuilt `--no-incremental`:
**16 of the new tests go red** — 3 in Contracts, 13 in Hosting. That is the check that each fix has a
test that fails without it.

- **M2, `coreContractRange` bounded at both ends.** All four rows fail against the old gate, so all
  four were genuinely green-as-passing before: `"1.0.0"`, `"[1.0.0, )"`, `"(, 2.0.0)"`, `"(, )"` each
  satisfied core contract 1.4.0 and were admitted. The refusal names which end is open and the bounded
  shape, `Remedy` no longer offers an open-ranged catalogue version (that test fails against the old
  code too), and every manifest fixture in the repo already declares a bounded range, so nothing else
  moves. The peer review's optional second gate in `PackageMetadataReader` was not added; the required
  fix was the gate, and `CountryPackageLoader.Load` runs it on every call.
- **M3, cycle detection.** `Two_accounts_that_roll_up_into_each_other…`, `A_box_that_sums_itself…` and
  `Two_boxes_that_sum_each_other…` all fail against the old validators.
  (`An_account_that_is_its_own_parent_is_refused` passes against the old code because that one check
  existed — it is a regression pin, and the commit does not claim otherwise.) I read both walks: the
  account walk memoises chains that reach the top so each account is followed once, `ToDictionary` is
  safe because duplicate codes are refused earlier in `Validate`, and a tail leading into a cycle
  (A→B→C→B) names the loop `B, C` rather than the tail. The report-box walk is an explicit-stack DFS
  with a grey set (`onPath`) and a black set (`settled`), the stack stays in step with the path on
  every branch, and both messages spell the loop out.
- **M4, README and `.csproj`.** Both now state the three-assembly allowlist, name
  `PackageAssemblyReferenceRule` as the mechanism, say it is matched case-insensitively and checked
  from package metadata before any package code runs, and the stale `CountryContractPublicApiTests`
  is corrected to the class that exists. The contract README also says plainly that ADR-0008 §3.1
  names only one assembly and why the enforced rule admits three — which is the honest form of a
  document that disagrees with an ADR it may not edit.
- **M-1, the signing curve.** `A_256_bit_key_on_a_curve_other_than_P_256_is_refused` fails against the
  old size-only check. I went further and tried explicit-parameter keys, since the new doc comment
  claims those are refused as well: explicit parameters taken from P-256, secp256k1 and P-384 are
  **all three refused** with `curve '<unnamed>'`, and a named secp256k1 key is refused. There is no
  explicit-encoding path back in. The comment is true.
- **L-1, the Hosting README's runtime claim.** "a tripwire on the ordinary path, not a confinement
  boundary" is accurate, and both halves have a mechanism I watched: the ALC override does refuse an
  implicit bind (the H-1 table above), and `AssemblyLoadContext.Default.LoadFromAssemblyName` does
  hand back the host's real `Aurora.Countries.Hosting` in one line. The paragraph now agrees with
  ADR-0008 §9.4 beside it and with the contract README. This is a replacement claim with a mechanism,
  not a softer sentence.

## Findings

**No blockers. No majors.** Minor and nit findings below are follow-ups, not merge conditions.

### m1 (minor, route to the architect — not this branch's to fix)

`docs/architecture/modules.md:76` still reads "The only core assembly a package may reference
(ADR-0008 §3.1)". After M4 that is inconsistent with `src/Aurora.Countries.Contracts/README.md:4-10`,
the `.csproj` header and `PackageAssemblyReferenceRule` on the same branch — and it already
contradicts modules.md's own Documents.Canonical row four lines above, which says packages depend on
that assembly. The developer correctly did not touch it: `docs/architecture/` is the architect's.
**Fix:** architect restates row 76 as the three-assembly allowlist, together with routing item 2 from
`docs/reviews/B-12.md` (the same sentence in ADR-0008 §3.1). Merging B-12 does not depend on it, but
it should not be lost.

### n1 (nit) — one count in a commit message is off by one

`f0eadab`: "Watched to fail first: **seven** case-varied rows and one set-membership fact were red
against the ordinal code (three IsAllowed rows returned true, two loads returned the host assembly,
one failed with FileNotFound)." The parenthetical adds to six rows, and six rows plus one fact is
what I measured: **7 tests red, not 8**. The breakdown in the same sentence is right and the fix is
right; only the headline number is wrong. History is not to be rewritten, so no action on the branch —
but anything carried forward (the orchestrator's log, a follow-up brief) should say 7.

### n2 (nit) — the metadata rule admits a leading-space assembly name

`src/Aurora.Countries.Hosting/PackageAssemblyReferenceRule.cs:70`.
`IsAllowed(" Aurora.Ledger.Infrastructure")` returns **true**: the leading space makes the `Aurora.`
prefix test miss, so the name is classified as "not one of ours". Not exploitable — I confirmed the
runtime will not bind a simple name with leading or trailing whitespace to the real assembly
(`FileLoadException` from both the package ALC and the default context), so a package whose reference
table carried that spelling would be admitted and then fail to load — but the rule is deciding on a
string shape the loader normalises differently, which is the same family as H-1.
**Fix (follow-up):** trim the name before comparing, and add `" Aurora.Ledger.Infrastructure"` to
`Every_other_aurora_assembly_is_forbidden`.

### n3 (nit) — the boundary generator never draws an everyday tax amount

`tests/unit/Aurora.Countries.Contracts.UnitTests/TaxRatePropertyTests.cs:252`.
`Gen.Choose(0, 999_999_999)` is uniform, so in 100 draws the target is never below about a million
minor units; over 500 draws I measured a minimum of 4 086 267. The region is reached either way, so
the property is sound — but the most vivid failures of the naive implementation live at the small end
(`JPY 15% of 3.3333333333333333333333333333` returning ¥1 where the answer is ¥0), and those are the
magnitudes a real invoice carries. **Fix (follow-up):** draw the target from a mixture — small values,
mid values and the full range — so the property exercises the amounts a tenant will actually post.

### n4 (nit) — a seed-dependent claim stated as general

`src/Aurora.Countries.Contracts/README.md:83` says the constructed-boundary property "fails the
obvious implementation on its first case". FsCheck reseeds per run; mine falsified on the fourth.
The test file's own remark is phrased as an observation ("Watched to fail … on its first generated
case") and is fine. **Fix (follow-up):** in the README, "within the first few cases".

### n5 (nit) — one crypto call outside the try/catch that turns failures into a `Result`

`src/Aurora.Countries.Hosting/CountryPackageHostOptions.cs:116`. `CurveOidOf` calls
`ExportParameters` after the `catch (CryptographicException)` block that converts import failures
into `HostConfiguration` results, so a key that imports but cannot export parameters would throw out
of `TrustedPackageKey.Create` instead of being refused by name. I could not construct such a key —
explicit-parameter keys export cleanly and are refused as documented — so this is theoretical and
about consistency, on operator-supplied configuration.

## To route

- **Architect:** m1 above (`docs/architecture/modules.md:76`), plus the five routing items already
  recorded in `docs/reviews/B-12.md` §"To route". Items 1–5 there are answered on
  `task/ARCH-CORRECTIONS` (ADR-0031); the bounded-range answer confirms M2 and is not reopened here.
- **B-13:** unchanged from the security review — do not cache what `GetExtension` returns in a
  long-lived singleton, or the collectible ALC never unloads.
- **B-11:** the architecture fitness test for ADR-0008 §3.1 should land with case-varied negative
  cases, so the build-time and host-time enforcement points do not share a blind spot. H-1's
  compensating control is still PENDING (verify.sh stage 7).
- **Orchestrator:** the peer review's eight minors (m1–m8) and this review's n1–n5 are follow-up
  material, not merge conditions.

## Re-review

None required. The two claims that carried the rework were reproduced independently, including the
mutation runs, and the gate and the self-test were run to completion on the branch.
