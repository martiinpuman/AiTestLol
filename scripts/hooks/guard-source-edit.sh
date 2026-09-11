#!/usr/bin/env bash
# PostToolUse(Write|Edit) guard. Reads the hook payload on stdin, looks at the file
# that was just written, and reports project rules the new content breaks.
#
# Exit 0  = nothing found.
# Exit 2  = findings; stderr goes back to the model, which can fix them in the same
#           turn instead of losing a review round trip to them.
#
# The write itself is not reverted — PostToolUse runs after the tool. The point is
# feedback at the moment of the edit, where it costs one message instead of one
# rejected review (measured on B-01/B-02/B-03: the rejection round is roughly half
# of a task's wall clock).
#
# Each rule prints a stable id (AURORA-EDIT-nn) that scripts/hooks/hooks-selftest.sh
# asserts against, so a rule that stops matching is caught rather than assumed live.
set -uo pipefail

payload=$(cat)
file=$(printf '%s' "$payload" | python3 -c 'import json,sys
try:
    d = json.load(sys.stdin)
    print(d.get("tool_input", {}).get("file_path", ""))
except Exception:
    print("")' 2>/dev/null)

[[ -z "$file" || ! -f "$file" ]] && exit 0

# Path relative to the repository root. Derived from the last src/ or tests/ segment
# rather than from `git rev-parse`, so it is identical in the main checkout, in an
# agent worktree and in the selftest's scratch directory — and costs no subprocess.
case "$file" in
    */src/*)   rel="src/${file##*/src/}" ;;
    */tests/*) rel="tests/${file##*/tests/}" ;;
    *)         rel="$file" ;;
esac

findings=()
add() { findings+=("AURORA-EDIT-$1: $2"); }

is_cs=false
case "$rel" in *.cs|*.razor) is_cs=true ;; esac
in_src=false
case "$rel" in src/*) in_src=true ;; esac

if $is_cs && $in_src; then
    # A Country Package's own assemblies are where jurisdiction logic belongs.
    case "$rel" in
        src/Countries/*|*Aurora.Countries.*) country_exempt=true ;;
        *) country_exempt=false ;;
    esac
    if ! $country_exempt; then
        if grep -nE '(country|Country|CountryCode|countryCode|Jurisdiction)[A-Za-z]*[[:space:]]*[!=]=[[:space:]]*"' "$file" >/dev/null 2>&1; then
            add 01 "$rel compares a country/jurisdiction against a string literal. CLAUDE.md: 'The core must never contain an if (country == \"SE\")' — this belongs behind a Country Package extension point."
        fi
    fi

    case "$rel" in
        *Clock*|*clock*) clock_exempt=true ;;
        *) clock_exempt=false ;;
    esac
    if ! $clock_exempt; then
        if grep -nE '\b(DateTime|DateTimeOffset)\.(Now|UtcNow|Today)\b' "$file" >/dev/null 2>&1; then
            add 03 "$rel reads the ambient clock. Architecture rule S3 fails the gate on this — take an IClock/TimeProvider dependency instead."
        fi
    fi

    if grep -nE '(^|[[:space:](<,])(double|float)([[:space:]]+[A-Za-z_]|\[\]|\?|>|[[:space:]]*[,)])' "$file" \
       | grep -vE '^\s*[0-9]+:\s*(//|\*|///)' >/dev/null 2>&1; then
        add 04 "$rel declares a float/double. Architecture rule F1 fails the gate on any floating-point type in src/ — money is decimal amount + currency, and ratios are Percentage."
    fi
fi

if $is_cs || [[ "$rel" == *.md || "$rel" == *.sh ]]; then
    if grep -nE 'TODO' "$file" | grep -vE 'TODO[[:space:]]*[:(]?[[:space:]]*(B-[0-9]|TASK-[0-9]|SPEC-[0-9])' >/dev/null 2>&1; then
        add 02 "$rel has a TODO with no backlog id. CLAUDE.md: 'no TODO without a backlog ID' — write TODO(B-nn) or delete it."
    fi
fi

# Secrets, in any file type. Shapes only — a placeholder such as Password=postgres
# in a Testcontainers fixture is not a secret and must not be reported as one.
if grep -nE '\-\-\-\-\-BEGIN [A-Z ]*PRIVATE KEY\-\-\-\-\-|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|sk-[A-Za-z0-9]{20,}' "$file" >/dev/null 2>&1; then
    add 05 "$rel contains something shaped like a live credential. CLAUDE.md hard limit: no secrets in the repo."
fi
if grep -niE '(password|pwd)[[:space:]]*=[[:space:]]*"?[A-Za-z0-9!@#$%^&*_+-]{16,}"?' "$file" \
   | grep -viE '(postgres|password|changeme|example|placeholder|redacted|\$\{|<|your-)' >/dev/null 2>&1; then
    add 05 "$rel sets a long literal password. CLAUDE.md hard limit: no secrets in the repo — use configuration or a generated test value."
fi

if (( ${#findings[@]} > 0 )); then
    printf '%s\n' "${findings[@]}" >&2
    printf '%d project rule(s) broken by this edit.\n' "${#findings[@]}" >&2
    exit 2
fi
exit 0
