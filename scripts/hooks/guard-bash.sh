#!/usr/bin/env bash
# PreToolUse(Bash) guard. Reads the hook payload on stdin, inspects the command
# the agent is about to run, and blocks the ones that violate a hard limit in
# CLAUDE.md or that are known to waste a turn.
#
# Exit 0  = no objection.
# Exit 2  = block; stderr is handed back to the calling model as the reason.
#
# Every rule prints a stable id (AURORA-BASH-nn) so scripts/hooks/hooks-selftest.sh
# can assert that a specific rule fired, not merely that something did.
set -uo pipefail

INTEGRATION_BRANCH='claude/multi-tenant-saas-erp-pv2nap'

payload=$(cat)

# Read the command with heredoc *bodies* removed. The guard reads raw command text,
# so without this a commit message that quotes a blocked command is analysed as if it
# were one — it was, and it refused this file's own commit twice. The heredoc's
# opening line survives, so a redirect target such as `cat > .env <<EOF` is still seen.
cmd=$(printf '%s' "$payload" | python3 "${CLAUDE_PROJECT_DIR:-.}/scripts/hooks/read-command.py" 2>/dev/null)

[[ -z "$cmd" ]] && exit 0

block() { printf 'AURORA-BASH-%s: %s\n' "$1" "$2" >&2; exit 2; }

# Collapse line continuations so a rule cannot be evaded by wrapping.
flat=$(printf '%s' "$cmd" | tr '\n' ' ')

# A command position is the start, after an operator, or after a shell keyword —
# `for … ; do git push …` put the push after `do`, where the operator-only pattern did
# not see it at all, so the whole rule silently did nothing on a loop.
CMD_POS='(^|[;&|(]|[[:space:]](do|then|else)[[:space:]])[[:space:]]*'
if [[ "$flat" =~ ${CMD_POS}git[[:space:]]+push ]]; then
    # Isolate the push invocation itself: everything from `git push` up to the first
    # shell operator. Every rule below reads this segment and not the whole command
    # line. Two false positives came from not doing that: `git push … | tail -3` read
    # the refspec as `tail`, and a `git worktree remove --force` elsewhere in the same
    # compound command was reported as a force-push.
    seg=$(printf '%s' "$flat" | sed -E 's/.*git[[:space:]]+push[[:space:]]*//' | sed -E 's/[|;&><].*//')

    if [[ "$seg" =~ --force([^-]|$) || "$seg" =~ --force-with-lease || "$seg" =~ (^|[[:space:]])-[a-zA-Z]*f([[:space:]]|$) ]]; then
        block 01 "force-push is a hard limit in CLAUDE.md ('never force-push, never rewrite history'). If the remote rejected a push, merge or rebase locally and push a new commit."
    fi
    # Drop flags; what remains is [remote] [refspec...].
    mapfile -t words < <(printf '%s' "$seg" | tr ' ' '\n' | grep -vE '^-' | grep -v '^$')
    if (( ${#words[@]} >= 2 )); then
        target="${words[1]}"
    else
        # `git push` with no refspec pushes the current branch.
        target=$(git rev-parse --abbrev-ref HEAD 2>/dev/null)
    fi
    target="${target#*:}"        # src:dst -> dst
    target="${target#refs/heads/}"
    if [[ "$target" == *'$'* || "$target" == *'`'* ]]; then
        # A variable refspec cannot be evaluated from command text. This rule guards a
        # hard limit, so it fails closed rather than guessing — but say that, instead
        # of claiming the branch named '$b' is wrong.
        block 02 "cannot evaluate '$target': the push target is a shell variable, and this guard reads command text, not shell state. Name the branch literally (one push per branch) so the target can be checked."
    fi
    if [[ -n "$target" && "$target" != HEAD && "$target" != "$INTEGRATION_BRANCH" && "$target" != task/* ]]; then
        block 02 "push target '$target' is neither the integration branch ($INTEGRATION_BRANCH) nor a task/* branch. CLAUDE.md: 'Never push to any other branch.'"
    fi
fi

if [[ "$flat" =~ (rm[[:space:]]+(-[a-zA-Z]+[[:space:]]+)*|git[[:space:]]+rm[[:space:]]+(-[a-zA-Z]+[[:space:]]+)*)[^[:space:]]*docs/decisions ]]; then
    block 03 "docs/decisions/ is append-only — ADRs are superseded by a new ADR, never deleted (CLAUDE.md hard limits)."
fi

if [[ "$flat" =~ git[[:space:]]+(add|commit) ]] && [[ "$flat" =~ (^|[[:space:]/])\.env($|[[:space:]\.]) ]]; then
    block 04 "refusing to stage a .env file: 'never commit secrets or .env files' (CLAUDE.md hard limits)."
fi

# dotnet needs scripts/dev-env.sh sourced in the *same* Bash call: shell state does
# not persist between tool calls, so a previous source does not carry over.
if [[ "$flat" =~ ${CMD_POS}dotnet[[:space:]] ]] \
   && [[ ! "$flat" =~ dev-env\.sh ]] && [[ ! "$flat" =~ /usr/share/dotnet ]]; then
    block 05 "'dotnet' will not be on PATH: shell state does not persist between Bash calls. Prefix the command with 'source scripts/dev-env.sh && '."
fi

exit 0
