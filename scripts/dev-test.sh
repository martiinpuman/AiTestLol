#!/usr/bin/env bash
# Runs the test suite and prints only what a reader needs: the executed/passed/failed
# counts per assembly, and the failures themselves. Nothing else.
#
#   bash scripts/dev-test.sh [target] [-- extra dotnet test args]
#
# `target` defaults to Aurora.sln. Exit code is dotnet's.
#
# Why this exists: a single implementation task cost 434 000 tokens, and the largest
# single contributor was raw `dotnet build` / `dotnet test` output echoed back into an
# agent's context dozens of times. The information in it that anyone acts on is the
# counts and the failures. This prints those and discards the rest, so the same run
# costs a few hundred tokens instead of several thousand.
#
# It always prints the executed count, including on success. A run that reports PASS
# without saying how many tests it executed cannot tell "all green" from "nothing ran"
# — that defect already shipped once, in verify.sh stage 6.
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
# shellcheck disable=SC1091
source scripts/dev-env.sh >/dev/null 2>&1 || { echo "dev-test: scripts/dev-env.sh failed" >&2; exit 1; }

target="${1:-Aurora.sln}"; [[ $# -gt 0 ]] && shift
[[ "${1:-}" == "--" ]] && shift

out=$(mktemp -d); trap 'rm -rf "$out"' EXIT
raw="$out/dotnet.log"

dotnet test "$target" \
    --nologo \
    --logger "trx;LogFileName=results.trx" \
    --results-directory "$out" \
    -v quiet \
    "$@" >"$raw" 2>&1
rc=$?

# Build errors never reach a TRX file, so surface them first and stop.
if grep -qE '(^|: )error [A-Z]+[0-9]+' "$raw"; then
    echo "BUILD FAILED"
    grep -E '(^|: )error [A-Z]+[0-9]+' "$raw" | sort -u | head -30
    echo "--- $(grep -cE '(^|: )error [A-Z]+[0-9]+' "$raw") error line(s); full log discarded"
    exit "${rc:-1}"
fi

shopt -s nullglob
trx=("$out"/*.trx "$out"/**/*.trx)
if (( ${#trx[@]} == 0 )); then
    echo "dev-test: no .trx produced — the run did not get as far as executing tests." >&2
    tail -30 "$raw" >&2
    exit "${rc:-1}"
fi

python3 - "$@" <<'PY' "${trx[@]}"
import sys, xml.etree.ElementTree as ET
from collections import OrderedDict

files = [a for a in sys.argv[1:] if a.endswith('.trx')]
NS = '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
total = OrderedDict(executed=0, passed=0, failed=0, skipped=0)
failures = []

for f in files:
    try:
        root = ET.parse(f).getroot()
    except ET.ParseError as e:
        print(f"dev-test: unreadable trx {f}: {e}", file=sys.stderr); continue
    c = root.find(f'{NS}ResultSummary/{NS}Counters')
    name = 'unknown'
    for t in root.iter(f'{NS}TestDefinitions'):
        for u in t.iter(f'{NS}UnitTest'):
            s = u.get('storage') or ''
            if s:
                name = s.replace('\\', '/').split('/')[-1]
                break
        break
    if c is not None:
        ex = int(c.get('executed', 0)); ps = int(c.get('passed', 0))
        fl = int(c.get('failed', 0)); sk = int(c.get('total', 0)) - ex
        total['executed'] += ex; total['passed'] += ps
        total['failed'] += fl; total['skipped'] += max(sk, 0)
        print(f"  {name:<48} {ex:>5} executed  {ps:>5} passed  {fl:>4} failed")
    for r in root.iter(f'{NS}UnitTestResult'):
        if r.get('outcome') == 'Failed':
            msg = r.find(f'{NS}Output/{NS}ErrorInfo/{NS}Message')
            stack = r.find(f'{NS}Output/{NS}ErrorInfo/{NS}StackTrace')
            failures.append((r.get('testName', '?'),
                             (msg.text or '').strip() if msg is not None else '',
                             (stack.text or '').strip() if stack is not None else ''))

print()
print(f"TOTAL {total['executed']} executed, {total['passed']} passed, "
      f"{total['failed']} failed, {total['skipped']} not run "
      f"({len(files)} assembly result file(s))")

if failures:
    print(f"\n{len(failures)} FAILURE(S):")
    for i, (n, m, st) in enumerate(failures[:25], 1):
        print(f"\n[{i}] {n}")
        for line in m.splitlines()[:12]:
            print(f"    {line}")
        first = next((l.strip() for l in st.splitlines() if '.cs:line' in l), '')
        if first:
            print(f"    at {first}")
    if len(failures) > 25:
        print(f"\n... and {len(failures) - 25} more failure(s)")
    sys.exit(1)

if total['executed'] == 0:
    print("\ndev-test: ZERO tests executed — this is a failure, not a pass.")
    sys.exit(1)
PY
prc=$?
(( rc != 0 && prc == 0 )) && exit "$rc"
exit "$prc"
