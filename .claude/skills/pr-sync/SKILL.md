---
name: pr-sync
description: Reconcile GitHub pull requests with the local branch state — open PRs for pushed task branches, refresh stale bodies, close PRs for merged work, delete merged remote branches. Use each iteration, and whenever branches have been pushed or merged.
user-invocable: true
allowed-tools: Bash, Read, Grep
---

# Keeping GitHub in sync

The repository is the team's memory; GitHub is where the product owner reads it.
A branch pushed with no pull request is invisible work, and a pull request whose
body describes a state three reworks old is worse than none.

## The branching model

Gitflow, under this project's names. **`main` is never pushed to** and
`claude/multi-tenant-saas-erp-pv2nap` is never force-pushed — both are hard limits
in `CLAUDE.md`, and `scripts/hooks/guard-bash.sh` blocks either.

| Gitflow | Here |
|---|---|
| `master` | `main` — PR #1 is the only path in |
| `develop` | `claude/multi-tenant-saas-erp-pv2nap` — must always pass `scripts/verify.sh` |
| `feature/*` | `task/<TASK-ID>` — one per backlog task, merged `--no-ff` after review |

Release and hotfix branches are unused: nothing is released yet. When the first
milestone ships, a `release/*` branch cut from the integration branch is the next
step — do not invent one before then.

## Current state

!`git fetch origin -q 2>/dev/null; for b in $(git branch -r --format='%(refname:short)' | grep 'origin/task/'); do n=${b#origin/}; a=$(git rev-list --count claude/multi-tenant-saas-erp-pv2nap..$b 2>/dev/null); f=$(git diff --name-only claude/multi-tenant-saas-erp-pv2nap...$b 2>/dev/null | wc -l); printf '%-26s %3s commits ahead  %3s files\n' "$n" "$a" "$f"; done`

A branch showing **0 commits ahead** is merged. Its PR should be closed or merged
and the remote branch deleted.

## The four checks, every iteration

1. **Every pushed `task/*` branch has an open draft PR** into the integration
   branch. Create one with `mcp__github__create_pull_request` (`draft: true`) if
   not. This is also a standing instruction in the session prompt, not optional.
2. **Every open PR's body matches reality.** After a review verdict or a rework,
   update it with `mcp__github__update_pull_request`. Say what the reviewers
   actually found — a body claiming a branch is ready when two blockers are open
   is the "claim with no mechanism" defect, aimed at the person who trusts it most.
3. **Merged work is closed out.** A branch at 0 commits ahead means merged: merge
   or close its PR, then `git push origin --delete task/<ID>` and
   `git branch -d task/<ID>`.
4. **PR #1 reflects the milestone.** It is what the product owner reads. Keep its
   table of open task PRs and its merged list current.

## Writing a PR body

Say what the change delivers, what reviewers found, and what is still open. Put
the evidence next to the claim: a gate result with its executed-test count, an
attack that was executed rather than reasoned about, a count of what a check
examined. Prefer the finding over the adjective — "a role could repoint another
tenant's database, executed as `aurora_app` with no DDL" beats "security issues
found".

Every PR is a **draft** until its task is merged into the integration branch.
Never mark one ready for review or merge it on your own authority: the Definition
of Done requires a reviewer who is not the author, and this environment refuses a
merge without one.
