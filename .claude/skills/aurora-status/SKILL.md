---
name: aurora-status
description: Live snapshot of the Aurora project — integration branch, task branches in flight, agent progress, health-check failures and the next action. Use when resuming work, before dispatching, or when asked how the project is doing.
user-invocable: true
allowed-tools: Bash, Read
---

# Project status

Everything below is gathered fresh each time this skill runs. Read it instead of
issuing five separate `git` and `cat` calls.

## Health check

!`bash scripts/project-health.sh 2>&1 | tail -25`

## Branches in flight

!`bash scripts/agent-progress.sh 2>&1 | tail -25`

## Integration branch

!`git log --oneline -8 claude/multi-tenant-saas-erp-pv2nap`

## Worktrees

!`git worktree list`

## The next action

!`awk '/^## Exact next action/{f=1} f&&/^## /&&!/Exact next action/{exit} f' docs/STATE.md | head -30`

## How to read this

- A **health-check failure** is work, not noise: `project-health.sh` only reports
  things that are already inconsistent (a done task with no review, a merged branch
  still present, `STATE.md` disagreeing with `ITERATION_LOG.md`).
- A task branch with **no commit for more than ~20 minutes** while its agent is
  still running usually means the task was too large to report on. Check the brief
  against the size limit in `.claude/skills/dispatch/SKILL.md` before dispatching
  anything like it again.
- Worktrees under `/tmp/review-*`, `/tmp/rereview-*` and `/tmp/sec-*` are review
  checkouts and are expected while a review is open; remove them when it closes.
