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
cmd=$(printf '%s' "$payload" | python3 -c 'import json,sys
try:
    print(json.load(sys.stdin).get("tool_input", {}).get("command", ""))
except Exception:
    print("")' 2>/dev/null)

[[ -z "$cmd" ]] && exit 0

block() { printf 'AURORA-BASH-%s: %s\n' "$1" "$2" >&2; exit 2; }

# Collapse line continuations so a rule cannot be evaded by wrapping.
flat=$(printf '%s' "$cmd" | tr '\n' ' ')

if [[ "$flat" =~ (^|[\;\&\|\(])[[:space:]]*git[[:space:]]+push ]]; then
    if [[ "$flat" =~ --force([^-]|$) || "$flat" =~ --force-with-lease || "$flat" =~ git[[:space:]]+push[[:space:]]+(-[a-zA-Z]*f) ]]; then
        block 01 "force-push is a hard limit in CLAUDE.md ('never force-push, never rewrite history'). If the remote rejected a push, merge or rebase locally and push a new commit."
    fi
    # Extract the refspec: the last bare word that is not a flag or a remote name.
    target=$(printf '%s' "$flat" | sed -E 's/.*git[[:space:]]+push[[:space:]]+//' | tr ' ' '\n' \
             | grep -v -E '^(-|origin$|HEAD$)' | grep -v '^$' | tail -1)
    target="${target#*:}"        # src:dst -> dst
    target="${target#refs/heads/}"
    if [[ -n "$target" && "$target" != "$INTEGRATION_BRANCH" && "$target" != task/* ]]; then
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
if [[ "$flat" =~ (^|[\;\&\|\(])[[:space:]]*dotnet[[:space:]] ]] \
   && [[ ! "$flat" =~ dev-env\.sh ]] && [[ ! "$flat" =~ /usr/share/dotnet ]]; then
    block 05 "'dotnet' will not be on PATH: shell state does not persist between Bash calls. Prefix the command with 'source scripts/dev-env.sh && '."
fi

exit 0
