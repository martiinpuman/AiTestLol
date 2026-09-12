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
    # Reviews now live on the task's pull request. The repository's record of one is
    # its line in the iteration log naming the verdict and the PR — that is what a
    # session with no GitHub access reads. Either form counts.
    logged="$(grep -E "^\| *${id} *\|" docs/ITERATION_LOG.md 2>/dev/null | grep -cE 'APPROVE|#[0-9]+' || true)"
    if [ -z "${review}" ] && [ "${logged:-0}" -eq 0 ]; then
      fail "${id} is done but has neither a review in docs/reviews/ nor a verdict line in ITERATION_LOG.md"
    elif [ -z "${review}" ]; then
      ok "${id} — verdict recorded in ITERATION_LOG.md"
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
  if [ "$(git rev-list --count "${INTEGRATION}..${b}")" -eq 0 ] && git worktree list --porcelain | grep -q "^branch refs/heads/${b}$"; then
    # No commits of its own and a worktree holding it: an agent that has started and
    # not yet committed. Comparing tips instead was wrong the moment develop moved on,
    # which it did within the minute — the worktree is the signal that survives.
    say "${b} — checked out by a worktree, no commits yet"
  elif git merge-base --is-ancestor "${b}" "${INTEGRATION}" 2>/dev/null; then
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
  elif [[ "${br}" == worktree-agent-* ]] \
       && [ -n "$(find "${path}" -maxdepth 1 -mmin -60 -print -quit 2>/dev/null)" ]; then
    # An agent sits on its own worktree-agent-* branch until it checks out a task
    # branch, so by ancestry it is indistinguishable from merged leftover — reporting
    # it as one sent the orchestrator after a worktree that was simply starting up.
    # Recent modification is the signal that separates the two.
    say "${path} (${br}) — agent active in the last hour"
  elif [ "$(git rev-list --count "${INTEGRATION}..${br}" 2>/dev/null || echo 1)" -eq 0 ]; then
    # Tip equal to the integration tip means no commits yet — a branch that has not
    # started, not one whose work is merged. Ancestry alone cannot tell them apart,
    # and calling a freshly dispatched agent's worktree prunable sends the
    # orchestrator to delete work that is about to be written.
    say "${path} (${br}) — branched, no commits yet"
  elif git merge-base --is-ancestor "${br}" "${INTEGRATION}" 2>/dev/null; then
    fail "${path} (${br}) — its work is merged, prune it"
    stale=$((stale + 1))
  else
    ok "${path##*/} (${br})"
  fi
done < <(git worktree list --porcelain | awk '/^worktree /{print $2}')
[ "${stale}" -eq 0 ] && say "none stale"
echo

# 3b. Two in-flight branches changing the same file is a merge conflict scheduled for
#     later, and hand-resolution has already introduced a defect into a third document.
echo "file claims"
bash scripts/file-claims.sh 2>/dev/null | grep -E 'CONTESTED|no file is claimed' | sed 's/^/  /' || true
if ! bash scripts/file-claims.sh >/dev/null 2>&1; then
  fail "$(bash scripts/file-claims.sh 2>/dev/null | tail -3 | head -1)"
fi
echo

# 3c. `ready` in this backlog means "the spec is ready to build", not "dispatchable
#     now" — most ready rows are waiting on a predecessor, which is normal and not a
#     defect. So report what is *actually* dispatchable, and fail only on the real
#     inconsistency: a row marked done whose dependencies are not.
#     (The first version of this check failed on every ready-but-waiting row. Twenty
#     rows red at once is a check nobody reads — it fired on a convention, not a fault.)
echo "backlog readiness"
python3 - <<'READY'
import re, sys
rows = {}
for line in open('docs/BACKLOG.md'):
    m = re.match(r'\|\s*(B-[0-9.]+[a-z]?)\s*\|', line)
    if not m:
        continue
    cells = [c.strip() for c in line.split('|')]
    if len(cells) < 9:
        continue
    rows[m.group(1)] = (cells[6], cells[8])
def unmet(deps):
    return [d for d in re.findall(r'B-[0-9.]+[a-z]?', deps) if rows.get(d, ('', ''))[1] != 'done']
dispatchable = sorted(r for r, (d, st) in rows.items() if st == 'ready' and not unmet(d))
broken = [(r, unmet(d)) for r, (d, st) in rows.items() if st == 'done' and unmet(d)]
print(f"  dispatchable now: {', '.join(dispatchable) if dispatchable else '(none — every ready row waits on a predecessor)'}")
for r, u in broken:
    print(f"  {r} is done but depends on un-done {', '.join(sorted(set(u)))}")
unread = sum(1 for l in open('docs/BACKLOG.md')
             if re.match(r'\|\s*B-', l) and not re.match(r'\|\s*B-[0-9.]+[a-z]?\s*\|', l))
print(f"  {len(rows)} row(s) read" + (f", {unread} row id(s) the parser could not read" if unread else ""))
sys.exit(1 if broken else 0)
READY
[ $? -eq 0 ] || fail "a row is marked done while a dependency is not"
echo

# 4. Dangling document references. The architect once shipped forward references
#    to ADRs that did not exist yet; a reader following one finds nothing and
#    cannot tell whether the document is missing or the reference is wrong.
echo "cross-references"
dangling=0
for adr in $(grep -rhoE 'ADR-[0-9]{4}' docs/ --include='*.md' 2>/dev/null | sort -u); do
  if ! ls docs/decisions/${adr}-*.md >/dev/null 2>&1; then
    # An ADR cited here but living on an unmerged task branch is work in flight, not a
    # broken reference — B-12 merged citing three of them. Say which branch has it, so
    # the difference between "pending a merge" and "lost" is visible rather than guessed.
    on_branch=""
    for b in $(git branch -a --format='%(refname:short)' | grep -E '^(origin/)?task/' | sed 's|^origin/||' | sort -u); do
      if git ls-tree -r --name-only "${b}" -- docs/decisions 2>/dev/null | grep -q "${adr}-"; then
        on_branch="${b}"; break
      fi
    done
    if [ -n "${on_branch}" ]; then
      say "${adr} is referenced here but lives on ${on_branch}, not yet merged"
    else
      fail "${adr} is referenced but no such ADR exists"
      dangling=$((dangling + 1))
    fi
  fi
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
