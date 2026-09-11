#!/usr/bin/env bash
# Checks the project's own bookkeeping invariants — the ones that drift silently
# when an orchestrator session is interrupted, and that nobody would notice for
# days because nothing fails.
#
# This is NOT the quality gate. scripts/verify.sh judges the code; this judges
# the repository's record of itself. Run it at the start and end of every
# iteration.
#
# Exit 0 = clean, 1 = at least one invariant broken.
set -Eeuo pipefail

cd "$(git rev-parse --show-toplevel)"
INTEGRATION="claude/multi-tenant-saas-erp-pv2nap"
problems=0

say()  { printf '  %s\n' "$*"; }
fail() { printf '  \033[31mBROKEN\033[0m  %s\n' "$*"; problems=$((problems + 1)); }
ok()   { printf '  \033[32mok\033[0m      %s\n' "$*"; }

echo "project health — $(date -u '+%Y-%m-%d %H:%M UTC')"
echo

# 1. Every task marked done must have a review carrying a verdict.
#    A task that merged without a recorded review breaks the Definition of Done
#    and leaves no evidence anyone looked at it.
echo "reviews"
done_ids="$(grep -oE '^\| (B-[0-9.]+[A-Za-z-]*|TASK-[0-9]+)[^|]*\|[^|]*\|[^|]*\|[^|]*\|[^|]*\|[^|]*\| *done *\|' docs/BACKLOG.md 2>/dev/null \
            | grep -oE '^\| [A-Za-z0-9.-]+' | tr -d '| ' || true)"
if [ -z "${done_ids}" ]; then
  say "no tasks marked done yet"
else
  for id in ${done_ids}; do
    review="$(ls docs/reviews/${id}.md docs/reviews/${id}-rereview.md 2>/dev/null | tail -1 || true)"
    if [ -z "${review}" ]; then
      fail "${id} is done but has no review in docs/reviews/"
    elif ! grep -qiE '^#+ *Verdict|Verdict: *(APPROVE|CHANGES_REQUESTED)|^## Verdict' "${review}"; then
      fail "${review} records no verdict"
    elif grep -qiE 'Verdict:? *CHANGES_REQUESTED' "${review}" \
         && ! ls "docs/reviews/${id}-rereview.md" >/dev/null 2>&1; then
      fail "${id} merged on a CHANGES_REQUESTED verdict with no re-review"
    else
      ok "${id} — $(basename "${review}")"
    fi
  done
fi
echo

# 2. Task branches that are fully merged are finished work left lying around;
#    they make `git branch` useless for seeing what is actually in flight.
echo "branches"
merged_left=0
while read -r b; do
  [ -z "${b}" ] && continue
  if git merge-base --is-ancestor "${b}" "${INTEGRATION}" 2>/dev/null; then
    fail "${b} is fully merged into ${INTEGRATION} and should be deleted"
    merged_left=$((merged_left + 1))
  else
    ok "${b} — in flight"
  fi
done < <(git branch --format='%(refname:short)' | grep -E '^task/' || true)
[ "${merged_left}" -eq 0 ] && [ -z "$(git branch --format='%(refname:short)' | grep -E '^task/' || true)" ] \
  && say "no task branches"
echo

# 3. A worktree whose branch is gone, or that points at merged work, is a trap:
#    the next agent to claim that branch hits --ignore-other-worktrees and may
#    silently work on a stale tree.
echo "worktrees"
stale=0
while read -r path; do
  [ -z "${path}" ] && continue
  [ "${path}" = "$(pwd)" ] && continue
  br="$(git -C "${path}" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
  if [ "${br}" = '?' ]; then
    fail "${path} — unreadable, prune it"
    stale=$((stale + 1))
  elif [[ "${path}" == /tmp/review-* || "${path}" == /tmp/rereview-* || "${path}" == /tmp/sec-* ]]; then
    # A reviewer's detached scratch worktree. Transient by design and removed by
    # the reviewer; only worth reporting, never a broken invariant.
    say "${path} — reviewer scratch, in use or awaiting cleanup"
  elif git merge-base --is-ancestor "${br}" "${INTEGRATION}" 2>/dev/null; then
    fail "${path} (${br}) — its work is merged, prune it"
    stale=$((stale + 1))
  else
    ok "${path##*/} (${br})"
  fi
done < <(git worktree list --porcelain | awk '/^worktree /{print $2}')
[ "${stale}" -eq 0 ] && say "none stale"
echo

# 4. Dangling document references. The architect once shipped forward references
#    to ADRs that did not exist yet; a reader following one finds nothing and
#    cannot tell whether the document is missing or the reference is wrong.
echo "cross-references"
dangling=0
for adr in $(grep -rhoE 'ADR-[0-9]{4}' docs/ --include='*.md' 2>/dev/null | sort -u); do
  ls docs/decisions/${adr}-*.md >/dev/null 2>&1 || { fail "${adr} is referenced but no such ADR exists"; dangling=$((dangling + 1)); }
done
for spec in $(grep -rhoE 'SPEC-[0-9]{3}' docs/ --include='*.md' 2>/dev/null | sort -u); do
  ls docs/product/specs/${spec}-*.md >/dev/null 2>&1 || { fail "${spec} is referenced but no such spec exists"; dangling=$((dangling + 1)); }
done
[ "${dangling}" -eq 0 ] && ok "every ADR and SPEC reference resolves"
echo

# 5. State freshness. STATE.md is what a fresh session reads first; if it is
#    behind the log, the next session starts from a false picture.
echo "state"
last_logged="$(grep -oE '^## Iteration [0-9]+' docs/ITERATION_LOG.md 2>/dev/null | grep -oE '[0-9]+' | sort -n | tail -1 || echo 0)"
last_state="$(grep -oE '\*\*Last iteration:\*\* *[0-9]+' docs/STATE.md 2>/dev/null | grep -oE '[0-9]+' | tail -1 || echo 0)"
if [ "${last_logged}" -eq 0 ]; then
  say "no iterations logged yet"
elif [ "${last_state}" != "${last_logged}" ]; then
  fail "STATE.md says iteration ${last_state}, ITERATION_LOG.md's latest is ${last_logged}"
else
  ok "STATE.md and ITERATION_LOG.md agree on iteration ${last_state}"
fi
echo

# 6. Uncommitted work in the main checkout. Uncommitted work is lost work.
echo "working tree"
if [ -n "$(git status --porcelain)" ]; then
  fail "uncommitted changes in the integration checkout:"
  git status --porcelain | sed 's/^/            /'
else
  ok "clean"
fi
echo

echo "-----------------------------------------------------------"
if [ "${problems}" -eq 0 ]; then
  printf ' \033[32mRESULT: healthy\033[0m\n'
else
  printf ' \033[31mRESULT: %d invariant(s) broken\033[0m\n' "${problems}"
fi
echo "-----------------------------------------------------------"
[ "${problems}" -eq 0 ]
