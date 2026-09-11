# Repository automation

What runs by itself, what it guarantees, and — for each claim — the mechanism that
produces it. Added 2026-09-11. Nothing here is aspirational: where a thing is not yet
verified, it says so.

## 1. Hooks — feedback at the moment of the edit

Wired in `.claude/settings.json`. They apply to every agent in the project, including
agents working in a worktree.

| Event | Script | What it does |
|---|---|---|
| `SessionStart` | `scripts/bootstrap-env.sh` | installs the .NET SDK if absent, starts Docker, pre-pulls `postgres:17-alpine` |
| `PreToolUse(Bash)` | `scripts/hooks/guard-bash.sh` | **blocks** the command (exit 2) and hands the reason back to the model |
| `PostToolUse(Write\|Edit)` | `scripts/hooks/guard-source-edit.sh` | reports project rules the new file content breaks |

### Why

A reviewer finding one of these costs a whole rejection round, and on the three tasks
measured so far the rejection round was roughly **half** of the task's wall clock
(B-01 44 min, B-02 74 min, B-03 96 min). A hook finding the same thing costs one
message inside the turn that caused it.

### The rules

`guard-bash.sh` — each blocks, with the rule id on stderr:

| Id | Blocks |
|---|---|
| `AURORA-BASH-01` | `git push --force`, `-f`, `--force-with-lease` |
| `AURORA-BASH-02` | `git push` to anything but the integration branch or a `task/*` branch |
| `AURORA-BASH-03` | `rm`/`git rm` under `docs/decisions/` |
| `AURORA-BASH-04` | staging or committing a `.env` |
| `AURORA-BASH-05` | `dotnet` without `scripts/dev-env.sh` sourced in the same call |

01–04 are hard limits from `CLAUDE.md`. 05 is not a rule but a turn saver: shell state
does not persist between Bash tool calls, so a `source` in an earlier call does not
carry over and the command would fail with `dotnet: command not found`.

`guard-source-edit.sh` — each reports; the write is not reverted, because `PostToolUse`
runs after the tool:

| Id | Reports | Scope |
|---|---|---|
| `AURORA-EDIT-01` | a country/jurisdiction compared against a string literal | `src/**`, except `src/Countries/**` and `Aurora.Countries.*` |
| `AURORA-EDIT-02` | a `TODO` with no `B-nn` / `TASK-nn` / `SPEC-nn` | `*.cs`, `*.razor`, `*.md`, `*.sh` |
| `AURORA-EDIT-03` | `DateTime(Offset).Now/UtcNow/Today` | `src/**`, except paths containing `Clock` |
| `AURORA-EDIT-04` | a declared `float`/`double` | `src/**` only |
| `AURORA-EDIT-05` | a credential-shaped literal (PEM private key, `AKIA…`, `ghp_…`, `sk-…`, a long literal password) | every file |

01, 03 and 04 duplicate architecture rules that B-04 enforces at build time. The
duplication is deliberate: the gate is authoritative and catches what a regex cannot,
the hook is immediate and catches it before a build is even run.

### These are proven, not assumed

`scripts/hooks/hooks-selftest.sh` asserts **each rule from both sides** — that the
violating input is blocked *naming that rule*, and that the legitimate case sitting
next to it is allowed. It prints its own case count, so it cannot report success
having measured nothing:

```
hook selftest: 27 passed, 0 failed (15 block cases, 12 allow cases, 27 total)
```

The allow cases matter as much as the block cases: `AURORA-EDIT-01` must not fire on a
Country Package's own `country == "SE"`, `AURORA-EDIT-03` must not fire on the single
`IClock` implementation, and `AURORA-EDIT-05` must not fire on a Testcontainers
`Password=postgres` fixture. A guard that blocks correct work is worse than none.

Run it after any change to a hook. It writes only to a temp directory.

**Both hooks were observed firing live** on 2026-09-11, not merely selftested:
`dotnet --version` was refused with `AURORA-BASH-05`, and a `Write` of B-03's
`double`-based `Percentage.Apply` was reported as `AURORA-EDIT-04`.

## 2. `scripts/dev-test.sh` — the quiet test runner

```
bash scripts/dev-test.sh [target] [-- extra dotnet test args]
```

Sources `dev-env.sh`, runs the tests, and prints **only** the per-assembly
executed/passed/failed counts and the failures themselves (name, assertion, and the
first `.cs:line` of the stack). Build errors are surfaced and the run stops.

Measured on the current suite, a passing run: **2 006 bytes of raw `dotnet test`
output → 236 bytes, an 89% reduction.** The saving is larger on a failing run, where
raw output dumps a full stack trace per failure.

It reports the executed count on success as well as failure, and **treats zero
executed tests as a failure** — the defect that shipped in `verify.sh` stage 6, where
a stage printed `PASS` having run nothing.

Proven from both sides on 2026-09-11: a deliberately failing `Assert.Equal(2, 1 + 2)`
was reported with its name, both values and its file and line, exit code 1; the clean
tree reports 208 executed, exit code 0.

`scripts/verify.sh` remains the authority on whether a branch may merge. `dev-test.sh`
is for the loop before that, and does not replace it.

## 3. Skills — `.claude/skills/`

| Skill | For | Why |
|---|---|---|
| `aurora-status` | resuming, or answering "where is the project" | injects `project-health.sh`, `agent-progress.sh`, the log and the worktree list in one invocation instead of five tool calls |
| `dispatch` | before starting any task | applies the size gate, tier, stacking rule, scope cap and slice handback rather than relying on memory |
| `integrate` | merging a reviewed branch | the five consistency updates, all of which have been skipped at least once |

A skill's body loads only when it is invoked, so an unused skill costs only its
description line. `dispatch` exists because dispatching an oversized task is the
orchestrator error that has cost this project the most: B-12 shipped 9 004 lines
across 69 files and B-04 4 540, and neither could report anything for the better part
of an hour.

Two things were established by observation rather than assumed. **A new skill is
picked up within the session that creates it**, not only at the next start — the first
invocation of `aurora-status` returned `Unknown skill`, the next one a minute later
ran. And **the `!`command`` injections do execute**: `aurora-status` returned live
`project-health.sh` output, the branch table and the worktree list, and that run
immediately exposed a defect in the skill itself (its `sed` for the next action
matched nothing, because the heading is `## Exact next action`). Fixed.

One name is taken: a skill called `status` is shadowed by the built-in `/status`
command and cannot be invoked at all. Hence `aurora-status`.

## 4. Agent definitions — `.claude/agents/`

`senior-developer` carries `memory: project` (pitfalls accumulate across sessions),
`maxTurns: 400` (a ceiling for a runaway — B-04, the largest task so far, used 205 tool
calls, so this is a backstop and not a routine cap) and `disallowedTools: Agent` (a
developer fanning out multiplies token cost invisibly).

**Not yet verified:** these were added 2026-09-11 and, like skills, are read at session
start. An unsupported frontmatter key is ignored rather than fatal, so the risk is that
one silently does nothing. Do not cite `maxTurns` as a guarantee until a run has been
seen to stop at it.

## 5. Ownership

`scripts/verify.sh`, `src/` and `tests/` stay senior-developer-owned. The orchestrator
owns `scripts/hooks/`, `scripts/dev-*.sh`, `scripts/bootstrap-env.sh`,
`scripts/project-health.sh`, `scripts/agent-progress.sh`, `.claude/` and this file.
