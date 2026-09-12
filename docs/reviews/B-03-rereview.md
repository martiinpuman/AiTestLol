# Re-review — TASK B-03: `Aurora.SharedKernel` value objects

**Branch:** `task/B-03`, tip `343d6c4` (21 commits; 6 new since the reviewed `dd69589`)
**Base:** rebased onto `47a3b1c` · **Reviewer:** senior-reviewer (not the author) · **Date:** 2026-09-11

## Verdict: APPROVE

**B-1, M-1 and m-2 are all resolved.** The allocation arithmetic is now exact by construction rather than by care, and I could not break it in 200,000 randomised splits against an oracle built independently of the implementation. M-1's generator is genuinely arbitrary: I reintroduced the rounding-prone form myself and watched the property test falsify after one test with a concrete counterexample, and watched the postcondition guard fire on four of the five properties. The `MIN_UNIT_TESTS` work assigned by `docs/reviews/B-02-rereview.md` is complete, and the developer was right that the probe that re-review specified can no longer distinguish anything — their replacement probes do, and I reproduced both directions.

No blockers, no majors, no new minors against the branch. Four nits and one spec finding, all listed as follow-ups.

---

## Results as I observed them

Detached scratch worktree at `343d6c4`, removed afterwards. SDK `10.0.401`.

| # | Command | Observed |
|---|---|---|
| 1 | `./scripts/verify.sh` | **exit 0, `RESULT: PASS`, 16.3s.** Stage 6 row: `6 Unit tests PASS 2.7s 208 test(s) executed` |
| 2 | `dotnet test Aurora.sln -c Release` | **exit 0 — `Failed: 0, Passed: 208, Skipped: 0, Total: 208`** |
| 3 | `./scripts/verify-selftest.sh` | **16/16 cases behaved as specified, exit 0** |
| 4 | `git status --porcelain` | **empty** — before, after every probe I planted, and at the end |
| 5 | Second full `verify.sh` after all probes | exit 0, PASS, `208 test(s) executed`, tree clean |
| 6 | Fast-forwardable onto the integration tip | **no** — the branch moved again (docs only). `git merge-tree` **clean, no conflicts**; rebase at merge time, no re-run needed |

Acceptance criteria: all five rows met as before, and **ADR-0021 §6 — the decision the row does not mention and the one this branch previously failed — is now met and proven.**

---

## 1. Is the arithmetic exact by construction?

Yes. I read it line by line and then tried to break it.

**`ToCommonIntegerScale` (`Money.cs:481-497`) cannot lose digits or overflow.**

- `Digits` (`:508-516`) reads `decimal.GetBits` into four ints and reassembles `bits[2]<<64 | bits[1]<<32 | bits[0]` as a `BigInteger` through `uint` casts — the unsigned 96-bit significand, correctly ordered low→high, each limb zero-extended rather than sign-extended. The classic bug here would be `(int)` instead of `(uint)` on a limb with the high bit set, and it is not present.
- It ignores `bits[3]`'s sign bit, which is safe **only** because `AssertAllocatable` (called at `:319`) rejects every negative weight before `ToCommonIntegerScale` runs at `:342`. That ordering is load-bearing; it holds.
- **There is no negative scale exponent to mishandle.** `decimal.Scale` is a byte in `[0,28]` by construction, so `commonScale - weights[part].Scale` is always ≥ 0 and `BigInteger.Pow(10, …)` is never called with a negative exponent.
- **Trailing zeros are handled by construction.** `1.50m` is `(150, scale 2)`, `1.5m` is `(15, scale 1)`; at a common scale of 2 both become `150`. Verified: `Allocate([1.50m, 1m, 1m])` and `Allocate([1.5m, 1m, 1m])` return identical splits, as do `[1,1,2]`, `[25,25,50]` and `[0.0001,0.0001,0.0002]`.
- Nothing here can overflow: the exponent is bounded by 28 and `BigInteger` has no range.

**The two `decimal` operations that remain are exact-or-loud, not exact-or-silent.**

- `BigInteger totalMinorUnits = (BigInteger)(Math.Abs(Amount) * scale)` (`:340`). Multiplying by a power of ten only shifts the scale down; the significand is reused unchanged when `Scale ≥ k`, and grows only when `Scale < k`, in which case it either fits or raises `OverflowException`. No path rounds. Probed the edge: `new Money(decimal.MaxValue, Nzd).IsInWholeMinorUnits` and a 29-digit scale-1 amount both throw rather than returning a wrong answer.
- `decimal amount = (decimal)partMinorUnits[part] / scale` (`:374`). Dividing an integer by 10^k for k ≤ 4 only raises the scale from 0 to k; exact.

**The largest-remainder computation.** `BigInteger.DivRem` on non-negative operands is floor division with a non-negative remainder, so `partMinorUnits[i] = ⌊T·wᵢ/W⌋` exactly and `leftover = Σrᵢ/W` is an exact integer in `[0, n)`. Because each `rᵢ < W`, `leftover < |{i : rᵢ > 0}|`, so the leftover queue can never reach a zero-remainder part — which is why a zero-weighted part takes nothing.

**Tie-break and total order.** `ByLargestRemainder` (`:604-621`) sorts descending by `BigInteger.CompareTo` and ascending by index on a tie, so the cent still goes to the earliest part. Indexes are distinct, so the composite comparer is antisymmetric, transitive and consistent — a genuine total order.

**Sign.** The split runs on `Math.Abs(Amount)` and re-applies one sign to every part at `:375`, so a credit note splits exactly like the invoice it reverses. No part ever pointed against its total.

**What I ran against it.** A standalone probe outside the repository, referencing the compiled kernel: **200,000 randomised splits** across NZD/JPY/BHD (2, 0 and 3 minor units), 1–12 parts, totals from −2×10¹⁰ to +2×10¹⁰ minor units, weights drawn from five shapes: `0`, `decimal` ratios of two 7-digit integers, `10⁻²⁰`…`10⁸`, round literals, and `n/3`. **The oracle derived each weight's exact integer form from the decimal's own invariant text, not from `GetBits`, so it is independent of the code under test.** Every split checked for: exact sum, whole minor units, each part equal to `⌊T·wᵢ/W⌋` or that plus one, total extras strictly below the part count, zero-weighted parts taking nothing, no sign change, and repeatability.

**0 failures.** Plus targeted cases, all correct: `10⁻²⁰` beside `10⁸`; `1e-28` beside `79228162514264337593543950335/3` (a full 96-bit significand — the case a misread of `bits[2]` would break); a single weight of `1/3`; one cent over three parts; a zero total; three cents over twelve parts; all-equal weights; all-but-one-zero weights; `±decimal.MaxValue` in JPY. Beyond `decimal`'s range the failure is `OverflowException` — loud, as the doc-comment now correctly says.

---

## 2. Does the postcondition guard actually guard?

Yes, and I proved both branches independently.

- **The whole-minor-units branch (`Money.cs:561-568`)** fires when the pre-fix arithmetic is put back with the guard in place: four of five property tests threw `InvalidOperationException: … gave part N … which is not a whole number of minor units and so cannot be paid. Refusing to return it (ADR-0021 §6)`, naming the exact weights.
- **The sum branch (`Money.cs:573-580`)** fires on the *fixed* code when I hand out one fewer leftover unit (`rank < leftover - 1`): parts stay whole minor units, the sum falls a cent short, and **11 of 24 allocation tests** threw `… gave parts adding up to 99.99, not 100.00. Refusing to return a split that loses or invents a minor unit (ADR-0021 §6)` — including the negative-total case and the BHD three-minor-unit case.

The guard is not decorative. `allocated != Amount` compares `decimal` values, so trailing zeros do not produce a false alarm, and because all parts share one sign with `Σ|parts| = |Amount|`, the running sum cannot overflow or round.

**On the guard having no permanent test: I agree with the developer, and I would have made the same call.** The guard is unreachable through the public API precisely because the fix succeeded; a test that reaches it would need a seam — an injectable arithmetic strategy, an `internal` overload taking pre-computed parts, or reflection — and every one adds a production affordance whose only consumer is a test, while weakening the "exact by construction" property that is the whole point of the fix. A seam here would make the code *less* trustworthy, not more. Checklist item 5 is satisfied by demonstration rather than by a standing test, and this review record is the durable evidence. See n-12.

I also checked what the guard does *not* cover: it verifies the sum, not proportionality. A split that adds up but distributes wrongly would pass it. That gap is covered elsewhere — zeroing the remainders failed 2 tests; reversing the scaled weights failed 4, including `Weights_give_each_line_its_share`. See n-10.

---

## 3. The reintroduction demonstration — M-1's evidence

Reproduced rather than taken on report. Two variants, each planted and reverted in the same step, tree empty afterwards.

**Variant A — pre-fix `Money.cs` restored (no guard).** `MoneyAllocationPropertyTests`: **Failed 2, Passed 3.**

```
Falsifiable, after 1 test (0 shrinks)
  -5460.2 NZD over [0, 0.3303076916184907320192446011, 0.0000000391372, 656.666,
                    0, 0.5, 0.7266484557956777996070726916]
  Money.Sum(parts, …) should be -5460.2 NZD but was -5460.1999999999999999999999999 NZD
```

`MoneyAllocationTests`: **Failed 3** — exactly the three B-1 reproductions. They exist as example tests, pass on `HEAD`, and fail against the code that produced the blocker. That is the shape a regression test is supposed to have.

**Variant B — pre-fix arithmetic with `AssertAddsUp` grafted on.** **4 of 5 property tests failed**, falsifying after 1, 2, 2 and 11 tests, every one through the guard rather than an assertion.

**The generator can now fail.** That was M-1's whole question, and the answer is demonstrated, not asserted. The class doc-comment now states which dimensions are arbitrary and which are a fixed list, and names the old generator as the reason the properties reported green while cents were vanishing.

---

## 4. The `MIN_UNIT_TESTS` changes

All four assignments present and correct.

| Assigned | Landed | Confirmed |
|---|---|---|
| Non-zero default, rounded down to the nearest ten | `verify.sh:52` — `${AURORA_MIN_UNIT_TESTS:-200}`, against 208 executed | Correct per the standing rule |
| **m-5** — `10#` on the comparison | `verify.sh:506` | Yes, and it bites |
| Stale comment replaced | `verify.sh:42-52` | Yes, and it states the re-round trigger |
| **n-9** — `--filter` / floor collision in `--help` | `verify.sh:150-154`, `:160-165` | Yes, names the escape hatch |

**The developer's reasoning about the octal probe is correct, and their replacements are better than the one I would have specified.**

| Probe | with `10#` | with `10#` removed |
|---|---|---|
| `=08 --stage 6` *(the specified probe)* | PASS, rc 0 | PASS, rc 0 + base error — **cannot distinguish** |
| **A:** `=08 --stage 6 --filter <one test>` | **FAIL, rc 1** | **PASS, rc 0** |
| **B:** `=0300 --stage 6` | **FAIL, rc 1** | **PASS, rc 0**, *silently* |

Probe B is the stronger and the one I would keep: `0300` is valid octal, so the old line reads it as 192, quietly concludes `208 ≥ 192` and prints `RESULT: PASS` **with no error output at all**. That is the failure mode m-5 was actually about — the noisy `08` case at least leaves a trace.

One consequence the developer did not raise: the harness still does not cover the base-ten reading. See m-6.

---

## Previous findings

| Finding | Status |
|---|---|
| **B-1** `Allocate` loses or invents minor units | **Resolved.** Exact by construction; 200,000-split independent oracle finds nothing; the three reproductions are permanent tests that fail against the old code |
| **M-1** the property generator cannot express the failure class | **Resolved.** Reintroduction falsifies after 1 test; the test documents which dimensions are arbitrary |
| **m-2** sign predicates answer for a `default(Money)` | **Resolved.** `Money.cs:73,84,95`, `Quantity.cs:56,66,76` |
| B-1 part 3 — the two false claims | **Resolved.** Both statements are now true |
| m-1, m-3, m-4, n-1 … n-6 | Deferred; unchanged |

---

## Ruling on the disclosed judgement calls

**1. Checklist item 5 has no permanent test.** **Accepted.** The guard is unreachable because the fix worked, and a seam built to reach it would cost more than it buys. Both branches verified by mutation instead.

**2. m-4 declined as instructed.** Correct — respecting a scope instruction over an earlier review's wish-list is the right instinct.

**3. `IsInWholeMinorUnits` left alone.** **Agreed, and independently verified.** Multiplying by a power of ten only shifts the scale: exact when `Scale ≥ k`, overflows loudly otherwise. Probed `decimal.MaxValue` and a 29-digit scale-1 amount — both raise, neither returns a wrong answer.

**4. `Percentage.IsZero`/`IsNegative` left alone.** **Agreed — and it is the only consistent choice, not merely defensible.** `Percentage.Zero` *is* `default`, documented as safe because "none of it" is a real answer. Making the predicates throw would make `Percentage.Zero.IsZero` throw, which is incoherent. The asymmetry with `Money` and `Quantity` is correct: those have a companion (currency, unit) whose absence makes the number meaningless; `Percentage` has none.

---

## Findings

### Blockers / Major
None.

### Minor (follow-ups)

**m-6 — The `10#` fix has no self-test case, so a future edit could remove it and still score 16/16.**
`verify-selftest.sh:457-465`. `case_unit_tests_vacuous` sets `AURORA_MIN_UNIT_TESTS=1`, not zero-padded, so it exercises the floor but not the base in which it is read. By this harness's own house rule, a property verified by hand belongs in the harness. **Fix:** one case — `AURORA_MIN_UNIT_TESTS=0300`, full run, assert `assert_fail_stage 6` and the log contains `below the required minimum of 0300`. Passes today; fails the moment `10#` is dropped. **Owner: B-11.**

### Nits

**n-10 — The property suite asserts the sum but never the shares.** A mutation permuting shares while preserving the sum was caught by the example tests, so this is coverage, not a hole. **Fix if taken:** one more property — each part is `⌊T·wᵢ/W⌋` or that plus one, and the number receiving the extra equals the leftover.

**n-11 — At the edge of `decimal`, `IsInWholeMinorUnits` surfaces a bare `OverflowException`.** `Money.cs:246`. Correct and loud, but the only exception this type raises that does not name `Money`, the amount or the fix.

**n-12 — The unreachable guard has no pointer to the evidence that it works.** `Money.cs:546-555`. Since there is deliberately no test, the next person to touch `Allocate` has nothing to check it against. **Fix:** one sentence naming this review as where both branches were demonstrated under mutation. That converts a defensive assertion into a documented one, which is what B-01's pattern asks of every claim here.

**n-13 — `Allocate(int parts)` has no upper bound.** `Allocate(int.MaxValue)` is an `OutOfMemoryException` rather than an `ArgumentOutOfRangeException`.

---

## Findings against the spec

**S-3 — the `decimal`-exhaustion rule belongs in ADR-0021.** *(Resolved before merge: the architect added it to ADR-0021 and ADR-0023 in `06b990a`, which landed while this review was running.)*

**S-1 and S-2** remain architect/PM-owned. S-2 is now more clearly worth doing: this branch was correct against its acceptance-criteria row while failing the ADR the row implements, twice over — once in the defect and once in the review that nearly missed it.

---

## Patterns noted for this codebase

**`--stage 6` does not rebuild, and a stale test binary will lie to you convincingly.** I briefly recorded a false result because a mutation I had reverted in source was still compiled into the test output directory. The stage *did* report `FAIL` with the right shape, from entirely the wrong cause. Any reviewer planting probes here must rebuild the whole solution after reverting, not just the project they edited, and must read the stage log rather than the summary row. The same trap will catch a developer bisecting a flaky test.

**A postcondition that becomes unreachable is not thereby untestable — it is testable only by mutation, and the mutation evidence must be written down.** The guard is the right engineering and there is no honest permanent test for it. That makes the review record the only place its behaviour is established, which is fine exactly once: the next change to `Allocate` must re-demonstrate it, and nothing in the repository currently says so. Where a guard is unreachable by construction, the code should name where the demonstration lives.

**Verify the discriminating power of a probe in both directions before believing either.** The B-02 re-review specified an octal probe that was discriminating when written and became vacuous the moment the suite grew past eight tests — a check that silently stopped checking, which is the exact defect that re-review was about. The developer noticed. The rule this repository keeps rediscovering: a probe, like a test and like a generator, must be shown to fail against the unfixed code, and "it failed before" is not evidence that it would fail now.

**The rest of this branch remains the standard.** Every type refuses to answer a question it cannot answer honestly, every exception names the cause and the fix, and the developer disclosed four judgement calls unprompted — three I agree with outright and one that is the only coherent option. The blocker existed because one method had to be clever about precision; the fix removed the cleverness rather than improving it, which is the right answer for this assembly.
