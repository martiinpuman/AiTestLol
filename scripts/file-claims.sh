#!/usr/bin/env bash
# Shows which files each in-flight task branch is changing, and flags any file more
# than one branch is changing.
#
#   bash scripts/file-claims.sh            # report every overlap
#   bash scripts/file-claims.sh <paths...> # would a task touching these collide?
#
# Why this exists: three architects were dispatched at once into docs/architecture
# and docs/decisions with no partition between them. Two numbered their ADR 0029,
# both created a section 6.2 in the same document, and reconciling it by hand
# introduced a defect into a third document. The collision was invisible until
# merge time, which is the most expensive moment to discover it.
#
# Exit 0 = no overlap, 1 = at least one file claimed twice.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)" || exit 1
INTEGRATION="claude/multi-tenant-saas-erp-pv2nap"

all=$(git branch --format='%(refname:short)' | grep -E '^task/' || true)
[ -z "${all}" ] && { echo "no task branches in flight"; exit 0; }

# Keep only branch tips. A rework branch is descended from the branch it reworks, so
# the two share ~every file — reporting that as contention buried six real collisions
# under 122 false ones, which is the "cannot tell the two apart" defect this whole
# repository keeps rediscovering. One lineage is one task.
branches=""
for b in ${all}; do
    tip=true
    for other in ${all}; do
        [ "${b}" = "${other}" ] && continue
        if git merge-base --is-ancestor "${b}" "${other}" 2>/dev/null; then tip=false; break; fi
    done
    ${tip} && branches="${branches} ${b}"
done

claims=$(mktemp); trap 'rm -f "$claims"' EXIT
for b in ${branches}; do
    git merge-base --is-ancestor "${b}" "${INTEGRATION}" 2>/dev/null && continue   # merged
    git diff --name-only "${INTEGRATION}...${b}" 2>/dev/null | while read -r f; do
        [ -n "$f" ] && printf '%s\t%s\n' "$f" "$b"
    done
done > "$claims"

# Asking about a specific set of paths: report which in-flight branch already has them.
if [ $# -gt 0 ]; then
    hits=0
    for want in "$@"; do
        owners=$(awk -F'\t' -v f="$want" '$1==f {print $2}' "$claims" | sort -u | paste -sd', ')
        if [ -n "$owners" ]; then
            printf '  CLAIMED  %s — already being changed by %s\n' "$want" "$owners"
            hits=$((hits+1))
        else
            printf '  free     %s\n' "$want"
        fi
    done
    printf '\n%d of %d path(s) already claimed.\n' "$hits" "$#"
    [ "$hits" -eq 0 ]
    exit $?
fi

branch_count=$(printf '%s\n' ${branches} | wc -l)
file_count=$(cut -f1 "$claims" | sort -u | wc -l)
printf 'file claims: %s in-flight branch(es), %s file(s) changed\n\n' "$branch_count" "$file_count"

overlaps=0
while read -r count file; do
    owners=$(awk -F'\t' -v f="$file" '$1==f {print $2}' "$claims" | sort -u | paste -sd', ')
    printf '  \033[31mCONTESTED\033[0m  %-52s %s branches: %s\n' "$file" "$count" "$owners"
    overlaps=$((overlaps+1))
done < <(cut -f1 "$claims" | sort | uniq -c | awk '$1>1 {print $1, $2}')

if [ "$overlaps" -eq 0 ]; then
    printf '  \033[32mok\033[0m  no file is claimed by more than one branch\n'
    exit 0
fi
printf '\n%d file(s) claimed by more than one branch. Each is a merge conflict you will\n' "$overlaps"
printf 'resolve by hand, and hand-resolution has already introduced a defect once.\n'
printf 'Partition them in the briefs, or sequence the tasks.\n'
exit 1
