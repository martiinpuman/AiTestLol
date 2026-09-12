---
name: dispatch
description: Size, brief and dispatch a backlog task to an agent. Use before starting any task so the size limit, tier, scope cap and slice handback are applied rather than remembered.
user-invocable: true
argument-hint: "[backlog id, e.g. B-09]"
allowed-tools: Bash, Read, Grep, Agent
---

# Dispatching a task

Dispatching an oversized or under-specified task is an **orchestrator** error, and it
is the one that has cost this project the most: B-12 shipped 9 004 lines across 69
files and B-04 4 540, so neither could report anything for the better part of an
hour. Work the four gates below in order. Do not skip the first.

## 1. Size gate — before anything else

Read the backlog row and count what it actually demands.

Send it back to the **project-manager to split** if it implies any of:
- more than roughly **400 changed lines**,
- more than about **six acceptance criteria**,
- more than **one subsystem**.

Three patterns that catch it early:
- a row listing **ten of anything** is ten tasks, or one task with a scope cap;
- a row whose verbs include both *define* and *host* / *load* / *execute* is two tasks;
- a row naming **a contract and its implementation** is two tasks.

## 2. Tier — from `CLAUDE.md`, named in the brief

| Tier | Covers | Process |
|---|---|---|
| **Full** | Money, ledger, tax, tenancy and isolation, authn/authz, migrations, anything a Country Package can influence | implement → review → rework → **re-review by a second reviewer** → merge |
| **Standard** | Other shipping behaviour: modules, screens, APIs, background jobs | implement → review → rework → orchestrator verifies → merge |
| **Light** | Scaffolding, build config, tooling, docs | implement → review → orchestrator verifies → merge |

When in doubt go **up** a tier. Name the tier in the brief — the reviewer sets its
depth and length from it.

## 3. Stacking — may this start before its predecessor merges?

**Allowed** when the dependent task is **Standard or Light** and the predecessor's
branch is gate-green. Branch from the predecessor's `task/<ID>` and say so in the
first commit message. **Never** when either task is **Full**.

Whoever starts early **owns the rebase**. Absent an explicit instruction in the
brief, the agent waits for the merge.

## 4. The brief

Choose the model: **fable** for implementation, **opus** for review, architecture and
security. Choose the agent: `senior-developer` for anything under `src/`, `tests/`,
`scripts/`; `senior-reviewer` or `security-reviewer` for review; never the author for
its own review.

Every brief carries these **five** parts. The last two are not optional — they are what
turns one silent 50-minute run into two 25-minute runs with feedback in between.

1. **The bounded deliverable** — the acceptance criteria, quoted, and the tier.
2. **Where to work — say "create your own worktree", in those words.** Give the branch,
   then: `git worktree add <path> <branch>`, work there, and `source scripts/dev-env.sh`
   in the same shell call before any `dotnet`.
   **Never let an agent work in `/home/user/AiTestLol`.** Roles that get a worktree
   automatically are fine; the ones that do not — architect, researcher, project-manager,
   ui-designer — will use the main checkout unless told otherwise, and a brief that says
   only "work on branch X" is such a brief.
   One root cause produced four distinct failures on 2026-09-12, none of which announced
   itself as the same bug:
   - an orchestrator commit landed on the agent's branch, **twice**, because the agent had
     checked that branch out in the shared checkout — and `git push origin <integration>`
     then reported SUCCESS while pushing an unchanged ref;
   - `verify.sh` reported **1708 tests, exactly 2 × 854**, because two concurrent runs share
     one `artifacts/verify` directory and stage 6 sums every `.trx` in it. The floor is a
     minimum, so an inflated count can never fail (`FOLLOWUP-062`);
   - a review's gate run failed `stage 11, working tree changed` for a change the branch
     did not make;
   - **a PR's head moved under a reviewer mid-review**, so its findings were drafted against
     a tree that no longer existed.
   Every one of those was green or plausible at the moment it happened. That is why this is
   a numbered requirement and not advice.
2b. **Point the agent at `docs/DEVELOPER_BRIEF.md` first**, by name, as the first thing
   it reads — then name the specific sections its task needs. `docs/` is ~91 000 words
   and `DEVELOPER_BRIEF.md` exists solely to route an agent out of it in about three
   minutes. A brief that lists documents without sending the agent there first makes
   every agent either over-read (the product owner's stated cost concern) or guess.
   This step was missed on B-09's dispatch, which is why it is a numbered part here
   rather than something to remember.
3. **The scope cap**, verbatim:
   > Deliver exactly these. **If a further improvement, hardening or abstraction
   > suggests itself, do not build it — name it in your summary as a follow-up.**
   > This brief is deliberately narrow.
4. **The slice handback**, verbatim:
   > When you have one complete, working, tested vertical slice — not the whole task
   > — commit it and report. Do not carry on to the next piece without handing back.

For any task that adds a check, a gate or a property test, add the demonstration ask:

> Prove the check can fail: break the code it guards, watch it go red, then revert.
> State in the test which input dimensions are genuinely arbitrary and which are a
> fixed list. A mechanism that cannot fail is not a check.

## 5. After dispatch — do this immediately, not at the end of the turn

Set the row's status to **`in-progress`** in `docs/BACKLOG.md` in the same turn you
dispatch, and record it in `docs/ITERATION_LOG.md`. Do not dispatch a second agent
against the same files.

Why immediately: `project-health.sh` reads **repository state**, and a dispatch is not
repository state until the agent puts something on disk. Between the `Agent` call and
the agent's first commit or worktree, the row still reads `dispatchable now:` — and a
check-in firing in that window will dispatch it again. This was observed live: B-20 and
B-21 both read dispatchable seconds after their agents were launched.

The status flip is the only signal available at dispatch time, which is why it is a step
and not a courtesy. The readiness check now also reports in-progress rows and says
whether anything is on disk yet, because an in-progress row with nothing on disk is
*also* what an agent that died looks like, and usage limits have killed agents here five
times.

## 0. Before any of the above — is the file free?

```
bash scripts/file-claims.sh <paths the task will touch>
```

Two agents in one file is a merge conflict scheduled for later, and it has already
cost this project real defects: two architects dispatched at once both numbered their
ADR **0029** and both created a **§6.2** in `solution-layout.md`, and reconciling it by
hand corrupted a dozen `ADR-0008 §6.2` references in a third document before it was
caught and redone.

With no arguments the script lists every file claimed by more than one in-flight
branch. It collapses a rework branch into the task it reworks — those share nearly
every file by design and are one task, not two.

**When two tasks need the same file, do one of these, in order of preference:**
1. **Partition it in the briefs** — name in each brief which sections, which ADR
   numbers, and which headings are that agent's. `docs/architecture/` and
   `docs/decisions/` need this every time more than one architect is running.
2. **Sequence them** — dispatch the second when the first merges. Cheaper than a
   hand-resolved conflict in a document nobody can diff by eye.
3. **Merge the first, then rebase the second** — only when the second has barely
   started.

Never dispatch two agents into the same file and plan to sort it out at merge time.
