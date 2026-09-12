#!/usr/bin/env bash
# At-a-glance view of every task branch in flight: how much has landed, how
# recently, and whether it currently builds. Answers "is that agent working or
# stuck?" without interrupting it.
set -Eeuo pipefail
cd "$(git rev-parse --show-toplevel)"
INTEGRATION="claude/multi-tenant-saas-erp-pv2nap"

printf '%-14s %7s %7s %9s %9s  %s\n' TASK COMMITS FILES LINES AGE LATEST
printf -- '---------------------------------------------------------------------------------------\n'
found=0
while read -r b; do
  [ -z "${b}" ] && continue
  git merge-base --is-ancestor "${b}" "${INTEGRATION}" 2>/dev/null && continue
  found=1
  commits=$(git rev-list --count "${INTEGRATION}..${b}")
  files=$(git diff --name-only "${INTEGRATION}...${b}" | wc -l | tr -d ' ')
  lines=$(git diff --shortstat "${INTEGRATION}...${b}" | grep -oE '[0-9]+ insertion' | grep -oE '[0-9]+' || echo 0)
  last=$(git log -1 --format=%ct "${b}")
  age=$(( ($(date +%s) - last) / 60 ))
  subject=$(git log -1 --format=%s "${b}" | cut -c1-46)
  printf '%-14s %7s %7s %9s %7sm  %s\n' "${b#task/}" "${commits}" "${files}" "${lines}" "${age}" "${subject}"
done < <(git branch --format='%(refname:short)' | grep -E '^task/' || true)
[ "${found}" -eq 0 ] && echo "  no task branches in flight"
printf -- '---------------------------------------------------------------------------------------\n'
echo "  AGE is minutes since that branch's last commit. A branch that has not moved in"
echo "  20+ minutes while its agent is still running is worth checking; everything else"
echo "  is an agent working. Push in-flight branches to watch them on GitHub."
