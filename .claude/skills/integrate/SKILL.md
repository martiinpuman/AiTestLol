---
name: integrate
description: Merge a reviewed task branch into the integration branch and leave the repository consistent. Use when a review returns APPROVE, or after verifying the fixes from a rejected review.
user-invocable: true
argument-hint: "[task id, e.g. B-04]"
allowed-tools: Bash, Read, Edit, Write, Grep
---

# Integrating a reviewed task

The integration branch must always pass `scripts/verify.sh`. Everything here exists
because skipping one of these steps has already left the repository inconsistent once
— `STATE.md` said iteration 4 while `ITERATION_LOG.md` stopped at 2, and only
`project-health.sh` noticed.

## 1. The review must actually approve

- **Full tier:** an APPROVE from a reviewer who is not the author, and after rework a
  **second reviewer** re-review. Orchestrator verification is not a substitute.
- **Standard tier:** an APPROVE, or a REJECT whose fixes you verified yourself — open
  each finding and check the fix, do not take the author's word for it.
- **Light tier:** an APPROVE.

A reviewer may escalate the tier in its verdict. Honour it.

## 2. Verify the fix claims, one by one

For each finding the author says it fixed, find the mechanism that produces the fixed
behaviour. An author summary can badly understate or overstate what happened — B-05's
completion notice was one sentence over a branch holding 24 commits. The three shapes
worth re-checking by hand:

1. a claim in a comment, name or doc with nothing producing it;
2. a check that can report success without saying how many things it measured;
3. a test that cannot fail — break the code it guards and watch.

## 3. Gate on the rebased branch

```
git fetch origin claude/multi-tenant-saas-erp-pv2nap
git -C <worktree> rebase claude/multi-tenant-saas-erp-pv2nap
source scripts/dev-env.sh && ./scripts/verify.sh
```

Record the **verbatim summary and the executed-test count**. A PASS with a test count
equal to the previous task's is a PASS that measured nothing new.

## 4. Merge — through the pull request

```
git -C <worktree> merge claude/multi-tenant-saas-erp-pv2nap   # develop into the branch
# run the gate here, then:
git -C <worktree> push -u origin task/<ID>
```

Then merge the PR itself with `mcp__github__merge_pull_request`, merge-commit method.

Never rebase a pushed branch, never force-push, never push to `main`. A local
merge-and-push closes the PR only by ancestry and leaves no merge event on it; the
review trail is what makes the merge auditable.

## 4b. Transcribe the reviewer's routing list **before** you merge

A reviewer ends with items routed to other roles. **Every one of them becomes a
backlog row or a message to that role now**, while the review is in front of you.

This is not bookkeeping. B-05's third security reviewer reported three of its
routings as being made for the *third* time: the first and second reviews had
routed the same findings, nobody transcribed them, and each reviewer rediscovered
and re-reported them at full cost. One was a cluster on which a reviewer had made
the application role `SUPERUSER` and watched the migration apply without complaint.

A routing that lives only in a review expires when the review scrolls out of the
transcript. If the item needs design, message the architect; if it needs a task,
add a `FOLLOWUP-###` row naming the review and PR it came from; if it belongs to an
existing row, edit that row. Then merge.

## 5. Leave it consistent — all five, every time

1. `docs/reviews/<ID>.md` exists and records the verdict.
2. `docs/BACKLOG.md` — status set to done.
3. `docs/STATE.md` — done list, test counts, and the **next action**.
4. `docs/ITERATION_LOG.md` — one appended entry, with the iteration number matching
   `STATE.md`.
5. Worktrees and merged branches removed:
   ```
   git worktree remove <path> --force
   git worktree prune
   git branch -d task/<ID>
   ```

## 6. Confirm

!`bash scripts/project-health.sh 2>&1 | tail -20`

Anything it reports is work, not noise. Fix it before dispatching the next task.
