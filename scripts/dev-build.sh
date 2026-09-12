#!/usr/bin/env bash
# Builds the solution and prints only the errors and warnings, with counts.
#
#   bash scripts/dev-build.sh [target] [-- extra dotnet build args]
#
# Exists because agents cannot always run `source scripts/dev-env.sh && dotnet build`
# as one compound command — the sandbox refuses some compound forms containing
# `source` — so every agent that needs a build first was writing its own wrapper in a
# scratch directory. This is that wrapper, once, in the repository.
#
# Prints a count on success as well as failure: a build reporting "succeeded" without
# saying how many projects it built cannot distinguish a real build from a no-op.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
# shellcheck disable=SC1091
source scripts/dev-env.sh >/dev/null 2>&1 || { echo "dev-build: scripts/dev-env.sh failed" >&2; exit 1; }

target="${1:-Aurora.sln}"; [[ $# -gt 0 ]] && shift
[[ "${1:-}" == "--" ]] && shift

log=$(mktemp); trap 'rm -f "$log"' EXIT
# -v minimal, not quiet: quiet suppresses the "Project -> /path/to.dll" lines,
# and without them the assembly count below is always zero — a count that cannot
# measure anything, in the script whose whole point is printing a real one.
dotnet build "$target" --nologo -v minimal "$@" >"$log" 2>&1
rc=$?

errors=$(grep -cE '(^|: )error [A-Z]+[0-9]+' "$log")
warnings=$(grep -cE '(^|: )warning [A-Z]+[0-9]+' "$log")
projects=$(grep -cE "^[[:space:]]*[A-Za-z0-9_.]+ -> /" "$log")

if (( errors > 0 )); then
    grep -E '(^|: )error [A-Z]+[0-9]+' "$log" | sort -u | head -30
    printf '\nBUILD FAILED — %d error line(s), %d warning(s)\n' "$errors" "$warnings"
    exit "${rc:-1}"
fi
if (( warnings > 0 )); then
    grep -E '(^|: )warning [A-Z]+[0-9]+' "$log" | sort -u | head -20
fi
printf 'build ok — %d assembly output(s), %d warning(s)\n' "$projects" "$warnings"
exit "$rc"
