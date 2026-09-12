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
#
#     Two things beyond the dependency column decide dispatchability, and the second
#     version of this check read neither. It offered B-18.1 as dispatchable while
#     B-18.1's own notes say it may not run concurrently with B-19, which was in
#     flight at the time; and it offered three rows that already had branches. The
#     dependency column was the last link it followed, and the answer lived one link
#     further on.
#
#     The concurrency constraint is prose, so this reads it by a FIXED PHRASE LIST and
#     says so in its own output. A constraint worded any other way is invisible here.
#     That is why the phrase list and the scanned-cell count are printed rather than
#     kept in the script: a reader can see how much of the input was actually read.
#     FOLLOWUP-042 asks the project-manager for a machine-readable conflicts field,
#     which is the only thing that makes this exhaustive rather than best-effort.
echo "backlog readiness"
python3 - <<'READY'
import re, sys
import subprocess

# A row with a task branch ahead of the integration branch is started, not waiting.
# A row is started if its branch is ahead of the integration branch, OR if a worktree
# holds that branch at all. The commits-ahead signal alone reported B-09 dispatchable
# while an agent was actively building it: the agent had a locked worktree on
# task/B-09 but had not committed yet, so "ahead" was 0. Dispatching a second agent
# onto that row is exactly the collision this check exists to prevent, and the
# earlier signal — the worktree — was sitting in `git worktree list` the whole time.
INTEGRATION = 'claude/multi-tenant-saas-erp-pv2nap'
in_flight, started_by = set(), {}

for ref in subprocess.run(['git', 'for-each-ref', '--format=%(refname:short)', 'refs/heads/task'],
                          capture_output=True, text=True).stdout.split():
    row = ref.split('/', 1)[1] if '/' in ref else ref
    ahead = subprocess.run(['git', 'rev-list', '--count', f'{INTEGRATION}..{ref}'],
                           capture_output=True, text=True).stdout.strip()
    if ahead.isdigit() and int(ahead) > 0:
        in_flight.add(row)
        started_by[row] = f'{ahead} commit(s) ahead'

def _merged(ref):
    # A branch already contained in the integration branch is finished, whatever
    # still points at it. Without this the worktree arm below reported a MERGED
    # branch as in flight -- task/B-19 merged, its worktree outlived it, and
    # B-18.1 stayed held on a branch that no longer existed to conflict with.
    # That is the eighth form on CLAUDE.md's list, in this file: the worktree arm
    # was added to catch a row with no commits, and nothing re-checked what it did
    # to a row whose commits had all landed.
    return subprocess.run(['git', 'merge-base', '--is-ancestor', ref, INTEGRATION],
                          capture_output=True).returncode == 0

for line in subprocess.run(['git', 'worktree', 'list', '--porcelain'],
                           capture_output=True, text=True).stdout.split('\n'):
    if line.startswith('branch refs/heads/task/'):
        row = line[len('branch refs/heads/task/'):].strip()
        if row and row not in in_flight and not _merged(f'refs/heads/task/{row}'):
            in_flight.add(row)
            started_by[row] = 'a worktree holds it, no commits yet'

# The concurrency constraint is prose. These are the only phrases read; anything
# worded differently is not seen, which is why the list is printed below.
HOLD_PHRASES = ['never concurrently', 'must therefore be sequenced', 'no third row may',
                'not parallel-safe', 'must be sequenced']

# The id pattern reads EVERY row id, not just `B-`. It used to be `B-[0-9.]+[a-z]?`,
# which parsed 56 rows out of 133 and reported "1 row id the parser could not read" --
# because the blindness counter only looked at lines starting `| B-` too. So the check
# was blind to 77 rows AND wrong about how blind it was, which is worse than either.
# Invisible to it were every FOLLOWUP, every ARCH-*, DESIGN-001, the A11Y and GRID rows,
# PM-SPEC-TENANT-OPS, API-01-PRE -- and B-13-PRE, which blocks the first Country Package.
# That is the seventh failure form on CLAUDE.md's list, in the tool the orchestrator uses
# to decide what to dispatch.
ROW_ID = r'[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9.]+)+'
HEADER_CELLS = {'id', 'task', 'row', 'question'}

rows, notes = {}, {}
unread = []
for line in open('docs/BACKLOG.md'):
    if not line.startswith('|'):
        continue
    if set(line.replace('|', '').strip()) <= set('-: '):
        continue                      # separator row
    cells = [c.strip() for c in line.split('|')]
    if len(cells) < 9:
        continue
    ident = cells[1]
    if not ident or ident.lower() in HEADER_CELLS:
        continue                      # header row
    m = re.fullmatch(ROW_ID, ident.replace('**', ''))
    if not m:
        unread.append(ident)
        continue
    rid = m.group(0)
    rows[rid] = (cells[6], cells[8])
    notes[rid] = ' '.join(cells[9:]) if len(cells) > 9 else ''

def unmet(deps):
    # Dependencies are resolved against the KNOWN row ids, longest first, so
    # `B-13-PRE` is not read as `B-13` -- a different row with a different status,
    # which is what the old `B-[0-9.]+[a-z]?` pattern did.
    #
    # Matching known ids rather than a generic pattern also keeps prose out: the
    # notes and dependency cells are full of hyphenated words (`data-source`,
    # `parallel-safe`) that any id-shaped regex would read as dependencies.
    #
    # An id in the task namespace that matches NO row still blocks, and says so.
    # A typo'd dependency must not silently read as satisfied -- that is the
    # direction this check cannot afford to be wrong in.
    found, rest = [], deps
    for known in sorted(rows, key=len, reverse=True):
        if re.search(r'(?<![A-Za-z0-9.-])' + re.escape(known) + r'(?![A-Za-z0-9.-])', rest):
            found.append(known)
            rest = rest.replace(known, ' ')
    unknown = [t for t in re.findall(r'B-[0-9][A-Za-z0-9.-]*', rest) if t not in rows]
    return [d for d in found if rows[d][1] != 'done'] + [f'{t} (no such row)' for t in unknown]

# An explicit marker holds a row whatever else its notes say. The phrase list below
# can only hold a row that NAMES an in-flight B- row, so a hold waiting on an
# architecture decision, an ADR or anything outside the B- namespace was invisible to
# it -- B-07.1 is held on task/ARCH-SCOPE-RUNTIME and would have read dispatchable the
# moment its three dependencies merged. This marker needs no row id and no phrasing.
HOLD_MARKER = 'held - do not dispatch'   # compared against _norm(), which lowercases

def _norm(text):
    return text.replace('\u2014', '-').replace('\u2013', '-').replace('**', '').lower()

def held_by(row):
    # Only the sentences carrying a hold phrase are read for row ids: the notes cell
    # as a whole names every neighbour, so scanning all of it would hold every row.
    text = notes.get(row, '')
    if HOLD_MARKER in _norm(text):
        return ['an explicit HELD marker in its own row']
    blockers = set()
    for sentence in re.split(r'(?<=[.;])\s+', text):
        low = sentence.lower()
        if any(p in low for p in HOLD_PHRASES):
            blockers |= {d for d in re.findall(ROW_ID, sentence)
                         if d != row and d in in_flight}
    return sorted(blockers)

ready = [r for r, (d, st) in rows.items() if st == 'ready' and not unmet(d)]

# A row marked in-progress at dispatch time closes the window between "an agent was
# told to build this" and "that agent has put something on disk" -- repository state
# is the only thing this check can read, and a dispatch is not repository state until
# the agent acts. But an in-progress row with nothing on disk is also what an agent
# that DIED looks like, and usage limits have killed agents here five times. So report
# them, and say which of the two it is rather than letting the row go quiet.
in_progress = sorted(r for r, (d, st) in rows.items() if st == 'in-progress')
started = sorted(r for r in ready if r in in_flight)
held = sorted((r, held_by(r)) for r in ready if r not in in_flight and held_by(r))
dispatchable = sorted(r for r in ready
                      if r not in in_flight and not held_by(r))
broken = [(r, unmet(d)) for r, (d, st) in rows.items() if st == 'done' and unmet(d)]

print(f"  dispatchable now: {', '.join(dispatchable) if dispatchable else '(none — every ready row is started, held or waiting on a predecessor)'}")
for r in started:
    print(f"  already started: {r} — {started_by.get(r, 'in flight')}")
for r in in_progress:
    if r in in_flight:
        print(f"  {r} in progress — {started_by.get(r, 'in flight')}")
    else:
        print(f"  {r} in progress — dispatched, nothing on disk yet; if this persists its agent died")
for r, b in held:
    if b == ['an explicit HELD marker in its own row']:
        print(f"  {r} held — its row carries an explicit HELD marker")
    else:
        print(f"  {r} held — its row forbids running concurrently with {', '.join(b)}, in flight")
for r, u in broken:
    print(f"  {r} is done but depends on un-done {', '.join(sorted(set(u)))}")

scanned = sum(1 for r in rows if notes.get(r))
print(f"  {len(rows)} row(s) read of {len(rows) + len(unread)} data rows found"
      + (f"; {len(unread)} id(s) the parser could not read: {', '.join(unread)}" if unread else "; none unreadable"))
print(f"  {scanned} notes cell(s) scanned for a hold: the marker '{HOLD_MARKER}', or a sentence matching {'; '.join(HOLD_PHRASES)}")
print(f"  a hold worded any other way is not seen here — FOLLOWUP-042 asks for a machine-readable field")
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

# 6. The main checkout is on the integration branch, and its work is committed.
#
# Both halves are here because of one incident on 2026-09-12. An agent was
# dispatched without being told to make a worktree, so it checked a task branch
# out in the main checkout. The orchestrator then committed a backlog note,
# which landed on that task branch, and `git push origin <integration>` reported
# SUCCESS while pushing an unchanged ref - the note was reported as recorded and
# was not on the integration branch at all. Nothing failed; the push was a real
# push of a real branch that simply had nothing new on it.
#
# That is why the branch is checked before the working tree. A dirty tree is
# visible the moment you look; the wrong branch is invisible precisely because
# every command still succeeds.
echo "working tree"
# Where is the integration branch checked out, and is that where you are about to commit?
#
# The original form of this check asserted the MAIN checkout was on the integration
# branch. That was right about the hazard and wrong about the remedy: agents legitimately
# check task branches out in the main checkout, so the check sat permanently red, and a
# permanently red check is one nobody reads - which is the same failure it exists to catch.
#
# The invariant that actually matters is that SOME worktree holds the integration branch
# and you know which one, because a commit made anywhere else is pushed by
# `git push origin <integration>` with a SUCCESS message and an unchanged ref. That
# happened twice on 2026-09-12. Nothing failed; every command returned zero.
integration_wt="$(git worktree list --porcelain \
  | awk -v b="refs/heads/${INTEGRATION}" '/^worktree /{wt=$2} $0=="branch "b{print wt}')"
checked_out="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '(detached)')"
if [ -z "${integration_wt}" ]; then
  fail "no worktree holds the integration branch '${INTEGRATION}'"
  say "       Integration work has nowhere to land. Create one and commit there:"
  say "           git worktree add <path> ${INTEGRATION}"
else
  ok "integration branch checked out at ${integration_wt}"
  if [ "${checked_out}" != "${INTEGRATION}" ]; then
    say "       this checkout is on '${checked_out}' - commit integration work in the worktree above,"
    say "       and verify every push landed rather than trusting its output:"
    say "           git merge-base --is-ancestor HEAD origin/${INTEGRATION}"
  fi
fi
if [ -n "$(git status --porcelain)" ]; then
  fail "uncommitted changes in this checkout:"
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
