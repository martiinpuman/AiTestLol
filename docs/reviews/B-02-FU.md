# Review B-02-FU - quality-gate follow-ups

- **Verdict: APPROVE** (Light tier; no escalation - the change is confined to `scripts/verify.sh` and `scripts/verify-selftest.sh`)
- Reviewer: senior-reviewer (not the author)
- Branch `task/B-02-FU` @ `23f2044`, 5 commits, +288/-23 across two files. Merge-base `4850969`; the integration branch has moved only in `CLAUDE.md` since, so the branch is effectively rebased.

## Gate

Run in a detached worktree at `task/B-02-FU`:

| Command | Result |
|---|---|
| `./scripts/verify.sh` | exit 0, `6 Unit tests PASS 208 test(s) executed`, `RESULT: PASS` |
| `./scripts/verify-selftest.sh` | exit 0, `21/21 cases behaved as specified`, ~5 min, no skips on this machine |
| `git status --porcelain` after each | empty |

## Acceptance criteria

All five findings are closed, and each is closed by a mechanism that can fail.

- **m-6** - case 15 printed `probe 0210: 210 in base ten (> 208 executed), 136 in octal (<= 208)` and the gate failed stage 6. The derivation is correct: `octal_ambiguous_floor_above` returns the smallest `0d` with digits 0-7 whose base-ten reading exceeds N and whose octal reading does not, so the case discriminates for any N >= 8 instead of expiring at a literal. Printing both readings makes the choice checkable by the next reader; the cleverness is bounded to ten lines with a stated contract. Keep it.
- **n-6** - confirmed as a real hole, and the fix is sound in general, not only against the orchestrator's probe. I planted a test printing `<Counters total="1000" executed="1000" /> executed="1000"` and inspected the resulting TRX directly: the captured output is present as `&lt;Counters ... /&gt;`, and no file in `artifacts/verify/` contains a CDATA section. Captured output therefore cannot open a `<Counters ` element, and the run counted 209, not 1209. Case 16 exercises exactly this and is **not** vacuous - the probe's output verifiably reaches the file - so a future logger that switched to CDATA would fail the case loudly rather than over-count in silence.
- **n-7** - two cases, covering both halves of the guard: promotion to a failure from the exit handler when `--stage` leaves stage 11 unrun, and the advisory-beneath-the-table path when another stage already failed. Both assert the log the summary points at, not just the exit code.
- **n-8** - skips are tallied and named separately; `CASES_PASSED = RUN - FAILED - SKIPPED`, so a full score cannot absorb an unchecked property.
- **n-2** - **verified, not merely inspected.** I ran the script under `bash:3.2` in a container: `verify: bash 5.0 or newer is required; this is bash 3.2.57(1)-release.`, exit 2. The guard also fires ahead of option parsing (`--help` did not bypass it). No syntax above bash 3.2 precedes the check.

Substring vacuity: `assert_executed_count` anchors the number on leading whitespace, and I confirmed no numeric substring assertion of the old shape survives. The two remaining `assert_summary_row 6 "..." "test(s) executed"` calls are the baseline's presence check and are deliberate.

The new `assert_no_build_warnings` failure on a missing MSBuild summary line, plus its `N line(s) read, 0 warning(s)` note, is the right direction: the check now reports what it measured instead of passing on silence.

## Findings

No blockers. No majors.

### Minor / nit (follow-up candidates, not merge blockers)

- **nit** - `scripts/verify-selftest.sh:545,588` - the two new cases call `require_baseline_count`, so naming either one alone on the command line (the subset feature this task added) fails with "the baseline case did not record an executed-test count". It fails loudly and immediately, which is the right failure mode, but the header comment at `:15-19` warns only about the `--stage` cases. Add the baseline dependency to that sentence.
- **nit** - `scripts/verify-selftest.sh:608` - `octal_ambiguous_floor_above` is defined after its only call site. Legal in bash, mildly surprising; move it next to the case.

## Notes for the orchestrator

- **Harness runtime.** 299s for 21 cases, and B-11 owes six more for stage 4 alone; it will cross six minutes. Agreed that it should not stay one-gate-run-per-case - B-11 should look at sharing a single gate run across cases that only read the summary, and the harness must stay out of `verify.sh`.
