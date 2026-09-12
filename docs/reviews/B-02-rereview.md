# Re-review — TASK B-02: `scripts/verify.sh` stages 0–3, 6 and 11

**Branch:** `task/B-02`, tip `6576781` (7 commits, 3 new since the reviewed `61d5687`)
**Base:** `claude/multi-tenant-saas-erp-pv2nap` (`f30493d`) · **Reviewer:** senior-reviewer (not the author) · **Date:** 2026-09-11

## Verdict: APPROVE

M-1 is closed, and closed better than the review specified. m-1 through m-4 and n-1 are resolved. Two of the three deviations are improvements on what the review asked for and I am adopting them; the third is a good change shipped with a false justification. One new minor and four nits, none blocking. One precondition the orchestrator must hold B-03 to at merge — see *The floor B-03 must set*.

---

## Results as I observed them

Detached scratch worktree at `6576781`, removed afterwards. SDK `10.0.401`, bash `5.2.21`.

| # | Command | Observed |
|---|---|---|
| 1 | `./scripts/verify.sh` | **exit 0, RESULT: PASS, 12.3s.** Stage 6 row: `6 Unit tests PASS 1.8s 0 test(s) executed` |
| 2 | `AURORA_MIN_UNIT_TESTS=1 ./scripts/verify.sh` | **exit 1, `RESULT: FAIL - stage 6 (Unit tests)`**, row `FAIL 1.6s 0 test(s) executed`, log names the filter verbatim and points at `MIN_UNIT_TESTS` |
| 3 | `./scripts/verify-selftest.sh` | **16/16 cases behaved as specified, exit 0, 2m53s.** Case 4 (offline) genuinely ran rather than skipping |
| 4 | `git status --porcelain` after each | **empty** every time; `git diff HEAD` empty |
| 5 | second consecutive full run, after all my probes | exit 0, PASS, tree clean |
| 6 | `./scripts/verify.sh --stage 6` | exit 0, PASS, `0 test(s) executed` |
| 7 | merge check | `git merge-tree` against the integration branch: **clean, no conflicts** |

### Probes the harness does not make, run by me

- **`on_exit` guard when stage 11 never runs.** Injected an MSBuild `AfterTargets="Build"` target writing an untracked file, ran `--stage 3`. Stage 11 was `SKIP`, the loop exited 0, and the exit handler promoted it: **exit 1**, row `11 Summary FAIL - working tree changed`, and `11-summary.log` created with the diff. **No path to a false PASS.**
- **Drift plus an earlier failure.** Same target with a `<Warning>` added, full run. Stage 3 failed; the summary printed the advisory block `Also: the working tree changed while the gate ran` and `RESULT: FAIL - stage 3 (Build)` stayed the headline. **Reported, not promoted; no double-report.**
- **Harness revert when an assertion fails mid-case.** Changed `case_summary_tree_guard`'s expectation to stage 9 in a copy and ran only that case. It reported the mismatch, exited 1, and afterwards the `.csproj` diff was **empty** and the probe file **gone**. `case_end` owns the restore, not the case body. **Yes, it reverts.**
- **Summary colours on a pty.** PASS green, PENDING yellow, **FAIL red**, `RESULT: FAIL` bold red. n-1 fixed.

---

## Previous findings

| Finding | Status |
|---|---|
| **M-1** stage 6 cannot report its own vacuity | **Resolved** — see below |
| **m-1** no self-test for the stage 11 tree guard | **Resolved** — case 14, observed failing for the right reason |
| **m-2** tree guard never runs when an earlier stage fails | **Resolved** — both new paths exercised by hand |
| **m-3** `case_offline` can cover stage 6 and the full run | **Resolved** — whole gate now runs under `unshare -n` with only loopback raised, and it really ran here |
| **m-4** documented options untested | **Resolved** — cases 15 and 16 |
| **n-1** summary result column never coloured | **Resolved**, verified on a pty |
| n-2 … n-5 | Explicitly deferred |
| **S-1** spec rows omit stage 6 | Architect/PM-owned, still outstanding |

### M-1 in detail — resolved, and better than specified

`verify.sh:451-518`. The count comes from the TRX `Counters` `executed` attribute, summed across `artifacts/verify/*.trx`, and `STAGE_NOTE[6]` is set **before** the `rc` check, so the count appears on the passing path, the test-failed path and the floor-failed path alike — confirmed on all three. `VERIFY_DIR` is wiped before the run, so stale `.trx` files cannot inflate the number.

**`executed` is the right attribute, and this is the part the review got wrong.** The review suggested "`total`/`executed`". I built a throwaway xUnit project with one passing and two `Skip=`'d facts and read the TRX:

```
<Counters total="3" executed="1" passed="1" ... notExecuted="0" ... />
```

`total` counts skipped tests; `executed` does not. A suite in which every test has been `Skip=`'d — a plausible way for a suite to silently stop measuring anything — has `total=N, executed=0`. Counting `total` would let that pass; counting `executed` fails it. The developer chose the stricter and more honest of the two.

---

## Ruling on the three deviations

### 1. MSBuild-target injection instead of `sleep 6; touch` — **developer is right, adopted**

More robust *and* it tests more. The review's recipe raced a fixed 6-second sleep against stage timings that vary by machine and warmth of build; stages 0–3 complete in 9.9s here, so the window is real but not generous, and it would drift as B-11 adds stages. The MSBuild target fires deterministically at a point the case chooses, on any machine, at any speed.

It also tests the *right* thing. §5.1 names the threat as "a future stage that reformats source or regenerates a lock file" — a stage of the gate writing into the repository it measures. An external `touch` simulates a file appearing; the MSBuild target reproduces the actual failure mode.

### 2. Per-branch colour instead of `awk '{print $3}'` — **developer is right, adopted**

Field 3 is wrong. Rows are built with `printf ' %-3s %-27s %-8s %8s  %s'` and `awk` splits on whitespace, so the result lands on a different field per row:

```
 2   Format & style              PASS ...   ->  $2=Format  $3=&      $5=PASS
 3   Build                       PASS ...   ->  $2=Build   $3=PASS
```

No single field number is correct for all twelve rows — my fix would have coloured `Format & style` dim and everything else correctly, which is worse than uniformly dim because the inconsistency looks intentional. Naming the colour in each `case` branch removes the parse rather than fixing it.

### 3a. Anchoring the `.trx` grep — **change accepted, justification rejected**

The claim at `verify.sh:506-507` — *"`notExecuted="0"` is a different counter that an unanchored pattern would also match"* — is **false**. The attribute is camelCase `notExecuted`, so a case-sensitive grep for `executed="` does not match it. On a real TRX containing both, anchored and unanchored give identical output. Keep the anchor — it costs nothing and is defensible against captured test stdout — but the comment asserts a property the file does not have, which is the exact B-01 pattern. See n-6.

### 3b. Rejecting a non-numeric `AURORA_MIN_UNIT_TESTS` — **developer is right, and the concern is real**

Tested what happens without it, under `set -Eeuo pipefail`:

| Value | Without validation |
|---|---|
| `some` | `unbound variable`, rc=1 — loud, but an unexplained crash |
| `1.5` | `((: 1.5: syntax error`, **`if` takes the else branch, floor silently passes, rc=0** |
| `08` | `((: 08: value too great for base`, **floor silently passes, rc=0** |

A malformed floor silently disables the check it configures. Rejecting it at the boundary is correct, and case 15 asserts it. **But the fix has a residual hole of exactly that family** — see m-5.

---

## Findings

### Blockers
None.

### Major
None.

### Minor

**m-5 — A zero-padded floor passes validation and then silently disables the floor.**
`scripts/verify.sh:196` (the `^[0-9]+$` regex) and `scripts/verify.sh:489` (the comparison).

`^[0-9]+$` accepts `08`. The comparison `(( executed < MIN_UNIT_TESTS ))` then reads it as octal and fails. Reproduced against `6576781`:

```
$ AURORA_MIN_UNIT_TESTS=08 ./scripts/verify.sh --stage 6
./scripts/verify.sh: line 489: ((: 08: value too great for base (error token is "08")
 6   Unit tests                  PASS         1.7s  0 test(s) executed
 RESULT: PASS
EXIT=0
```

Zero tests executed, a floor of eight requested, and the gate says PASS — the precise outcome the validation was added to prevent, reachable *through* the validation. Contrast `AURORA_MIN_UNIT_TESTS=8`, which correctly exits 1.

The script already knows the idiom: `count_executed_tests` uses `10#${n}` at `:514` and `assert_no_build_warnings` at `:433`, both for this reason. Stage 6's comparison is the one place that does not.

**Fix** — one line: `if (( executed < 10#${MIN_UNIT_TESTS} )); then`. Preferred over tightening the regex because it matches the two existing uses and cannot be reintroduced by a future edit to the regex.

Not blocking — the default is `0` and every realistic value behaves correctly. It becomes load-bearing the moment the floor is non-zero, so **fix it in the same commit that raises the floor**.

### Nits

**n-6 — A comment claims a property the code does not have.** `verify.sh:506-507`. Reword to what the anchor actually buys, or make it genuinely load-bearing by scoping the match to the element.

**n-7 — The exit-handler branch of the tree guard has no self-test case.** `verify.sh:691-705`. It is the new code m-2 added, the only path that can turn an exit-0 run into a failure, and I verified it by hand — which by this harness's own house rule means it belongs in the harness. Two cases, both cheap because the injection already exists: (a) MSBuild target + `--stage 3`, asserting exit 1 and `RESULT: FAIL - stage 11`; (b) the same target with a `<Warning>`, asserting `RESULT: FAIL - stage 3` **and** the presence of the `Also:` block — that second assertion is what keeps the advisory from quietly regressing into a promotion.

**n-8 — A skipped self-test case is counted as a passing one.** `verify-selftest.sh:294-297, 568-570`. `case_offline` prints `SKIP:` and returns 0 after `case_begin` has incremented `CASES_RUN`, so the tally still reads `16/16`. On a kernel forbidding user namespaces the harness would report a perfect score while the only proof of the offline property did not run. Same family as M-1, far lower stakes.

**n-9 — `--filter` and a raised floor will collide, by design, and the help text does not say so.** `verify.sh:143`. Once B-03 sets a non-zero floor, the documented §5.3 debugging flow `./scripts/verify.sh --filter SomeOneTest` fails at stage 6. This is the correct trade — weakening the floor under `--filter` would gut the mechanism — and the failure message names the escape hatch. Add one clause to the help line.

---

## The floor B-03 must set

The floor stays vacuous until someone raises it, and a floor of zero also makes the counter itself unfalsifiable: if a future SDK renames the TRX attribute, `count_executed_tests` returns 0 and the row keeps printing `0 test(s) executed`, which is currently the truth. Raising the floor is what makes the counter self-checking as well as the stage.

**What B-03's landing commit must contain, in the same commit as the first tests:**

1. `MIN_UNIT_TESTS` changed from `${AURORA_MIN_UNIT_TESTS:-0}` to a **non-zero default**.
2. **The value: the number stage 6 actually executes on that branch, rounded down to the nearest ten, minimum 10.** If B-03 lands 87 tests, set `80`. Not the exact count — that turns every added test into a `verify.sh` edit and will be deleted in frustration within a fortnight. The floor's job is to catch a whole assembly dropping out of `Aurora.sln`, a `Category` trait typo, or a discovery failure, all of which move the number by tens or to zero; it is not to police churn of one or two.
3. The `10#` fix for **m-5**, since that line is being edited anyway.
4. The comment at `verify.sh:42-45` updated — it says "Zero today … raise this in the same commit that adds them", stale the moment B-03 merges. Replace with the standing rule: *round down to the nearest ten of what the suite actually runs; re-round whenever a test project is added to or removed from `Aurora.sln`.*

`case_unit_tests_vacuous` sets `AURORA_MIN_UNIT_TESTS=1` explicitly, so it keeps proving the mechanism at any default. Nothing in the harness needs to change when the floor moves.

For the architect, alongside S-1: **`AURORA_MIN_UNIT_TESTS` is new user-facing contract and belongs in `solution-layout.md` §5.3's option table.**

---

## Follow-ups for the backlog (not blocking this branch)

| Item | Where | Who |
|---|---|---|
| m-5 — `10#` on the floor comparison | `scripts/verify.sh:489` | **B-03**, same commit as the floor |
| n-6 — correct the `.trx` grep comment | `scripts/verify.sh:506-507` | B-11 or a tidy-up |
| n-7 — self-test cases for the exit-handler guard path | `scripts/verify-selftest.sh` | **before B-11 starts** |
| n-8 — skipped cases counted as passing | `verify-selftest.sh:568-570` | B-11 |
| n-9 — `--filter` / floor interaction in `--help` | `verify.sh:143` | B-03 |
| S-1 — §6 and BACKLOG rows omit stage 6; add `AURORA_MIN_UNIT_TESTS` to §5.3 | `solution-layout.md`, `docs/BACKLOG.md` | architect + PM |

n-7 is worth scheduling before B-11, for the same reason m-1 and m-2 were: B-11 adds the stages that read lock files and the dependency closure, and the exit-handler path is now half of what makes the tree guard trustworthy on a failing run.

---

## Patterns noted for this codebase

**A validator that rejects bad input can still admit the one bad input that matters.** M-1 was "PASS without a count". The guard added to protect the floor from a non-numeric value is right, but `^[0-9]+$` admits `08`, and `08` is precisely the value that makes the comparison fail open. When you add a guard against silent vacuity, enumerate the inputs that pass the guard and *then* break the thing being guarded — for shell arithmetic that is leading zeros, decimals, whitespace and signs. This repository already has the answer in two other places (`10#${n}`); reuse the idiom the file already contains rather than writing a new gate in front of the old arithmetic.

**Injecting a defect from inside the system under test beats injecting it from outside on a timer.** Deterministic, machine-independent, and it reproduces the threat the spec names instead of a proxy for it. For any future "does the gate notice X happening mid-run" case, prefer a hook in the build over a race against the clock.

**Field-number parsing of a formatted table is wrong by construction when any column can contain spaces.** `awk '{print $N}'` over `printf '%-27s'` output fails on `Format & style` and will fail again on the first stage name B-11 adds with a space in it. If a `case` pattern has already classified the line, name the value; do not re-derive it.
