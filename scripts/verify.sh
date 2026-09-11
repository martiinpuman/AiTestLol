#!/usr/bin/env bash
#
# Aurora ERP - the single quality gate.
#
# Specification: docs/architecture/solution-layout.md 5 (contract, stage list,
# options, budget). This script is the only gate: the integration branch must
# always pass it (CLAUDE.md), and there is no separate CI variant.
#
# Implemented by B-02: stages 0-3, 6 and 11. Stages 4, 5, 7, 8, 9 and 10 are
# declared in STAGE_RUN with an empty handler and report as PENDING; task B-11
# turns each into a function. They are never reported as passing, and the
# summary says out loud that the gate is incomplete while any remains.
#
# Why stage 6 is here and not in B-11: B-01 shipped a solution on which
# `dotnet test Aurora.sln` aborted (a library was handed to VSTest and its test
# host could not start). Stages 0-3 cannot see that class of failure, so
# between B-02 and B-11 the integration branch would have been "green" with a
# broken test run. Stage 6 is implemented exactly as 5.2 specifies it - same
# number, same command - so B-11 inherits it instead of renumbering anything.

set -Eeuo pipefail

# now_ms reads EPOCHREALTIME, which bash 5.0 introduced; the stage tables are
# associative arrays (4.0). On an older bash - macOS ships 3.2 - the run would
# otherwise die on an unbound variable before it had named a single stage.
if (( BASH_VERSINFO[0] < 5 )); then
  printf 'verify: bash 5.0 or newer is required; this is bash %s.\n' "${BASH_VERSION}" >&2
  exit 2
fi

# ---------------------------------------------------------------------------
# Location. The gate always operates on the repository it lives in, whatever
# the caller's working directory is (5.1).
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd -P)"
cd -- "${REPO_ROOT}"

SOLUTION="${REPO_ROOT}/Aurora.sln"
VERIFY_DIR="${REPO_ROOT}/artifacts/verify"
SUMMARY_FILE="${VERIFY_DIR}/summary.txt"

# Stage 3 reads the MSBuild warning summary out of its log, so the CLI language
# must not depend on the developer's locale.
export DOTNET_CLI_UI_LANGUAGE=en

readonly FAIL_TAIL_LINES=40

# The number of unit tests stage 6 must see execute before it is allowed to
# report PASS. A stage that measures nothing must not be able to report PASS
# silently.
#
# The standing rule for this number: what the suite actually runs, rounded DOWN
# to the nearest ten. Not the exact count - an exact count turns every added test
# into an edit of this file and gets deleted in frustration. The floor is here to
# catch a whole assembly dropping out of Aurora.sln, a misspelled Category trait
# or a discovery failure, all of which move the count by tens or to zero. Re-round
# it whenever a test project joins or leaves Aurora.sln.
#
# 300 since B-04: Aurora.Architecture.Tests started hosting tests (97 of them) and
# the two kernel-local assembly tests it supersedes were deleted, taking the suite
# from 208 to 303. Leaving the floor at 200 would have let the whole architecture
# assembly drop out of the run without the gate noticing.
MIN_UNIT_TESTS="${AURORA_MIN_UNIT_TESTS:-300}"
readonly MIN_UNIT_TESTS

# ---------------------------------------------------------------------------
# The stage table (5.2). The order here is the execution order.
#   STAGE_RUN[id]   - handler function, or empty for "not implemented yet"
#   STAGE_OWNER[id] - the task that will implement a pending stage
#   STAGE_FLAGS[id] - space separated: docker (needs a daemon), slow (--fast skips)
# ---------------------------------------------------------------------------

STAGE_IDS=(0 1 2 3 4 5 6 7 8 9 10 11)

declare -A STAGE_NAME=(
  [0]="Preflight"
  [1]="Restore"
  [2]="Format & style"
  [3]="Build"
  [4]="Dependency licence gate"
  [5]="Vulnerability gate"
  [6]="Unit tests"
  [7]="Architecture fitness tests"
  [8]="Integration tests"
  [9]="UI component tests"
  [10]="Coverage report"
  [11]="Summary"
)

declare -A STAGE_SLUG=(
  [0]="preflight"
  [1]="restore"
  [2]="format"
  [3]="build"
  [4]="dependencies"
  [5]="vulnerabilities"
  [6]="unit-tests"
  [7]="architecture-tests"
  [8]="integration-tests"
  [9]="ui-tests"
  [10]="coverage"
  [11]="summary"
)

declare -A STAGE_RUN=(
  [0]="stage_preflight"
  [1]="stage_restore"
  [2]="stage_format"
  [3]="stage_build"
  [4]=""
  [5]=""
  [6]="stage_unit_tests"
  [7]=""
  [8]=""
  [9]=""
  [10]=""
  [11]="stage_summary"
)

declare -A STAGE_OWNER=(
  [4]="B-11"
  [5]="B-11"
  [7]="B-11"
  [8]="B-11"
  [9]="B-11"
  [10]="B-11"
)

declare -A STAGE_FLAGS=(
  [8]="docker slow"
  [9]="slow"
  [10]="slow"
)

declare -A STAGE_RESULT=()
declare -A STAGE_MS=()
declare -A STAGE_NOTE=()

# ---------------------------------------------------------------------------
# Options (5.3)
# ---------------------------------------------------------------------------

OPT_FAST="${AURORA_VERIFY_FAST:-0}"
OPT_NO_DOCKER=0
OPT_FILTER=""
OPT_ONLY_STAGE=""

usage() {
  cat <<'USAGE'
Usage: scripts/verify.sh [options]

The single quality gate for Aurora ERP (docs/architecture/solution-layout.md 5).
Runs from any working directory; always operates on the repository it lives in.

Options:
  --fast             Skip the slow stages (8-10). Inner loop only - never a
                     substitute for a full run before review.
                     Equivalent to AURORA_VERIFY_FAST=1.
  --no-docker        Skip stage 0's Docker check and the integration stage (8).
  --filter <expr>    VSTest filter expression, ANDed into the test stages (6-9).
                     A filter narrow enough to select a handful of tests needs
                     AURORA_MIN_UNIT_TESTS=0 with it, or stage 6's floor fails
                     the run for having executed too few. That is the intended
                     trade: the floor cannot tell a deliberate filter from a
                     broken one.
  --stage <n>        Run one stage only. Assumes the stages before it have
                     already run in this working tree.
  -h, --help         Show this help.

Environment:
  AURORA_MIN_UNIT_TESTS   The number of tests stage 6 must see execute before
                          it may report PASS (default 300, the suite's count
                          rounded down to the nearest ten). The count is always
                          printed in the summary, whatever the floor is. Set it
                          to 0 when running a deliberately narrow --filter.

Exit codes:
  0  every stage that ran passed
  1  a stage failed - the summary names it
  2  usage error, or a bash older than 5.0

Output goes to artifacts/verify/: one log per stage, the .trx files, and
summary.txt. The script writes nowhere else and never modifies a tracked file:
stage 11 fails the run if anything in the working tree changed while it ran.
USAGE
}

fatal_usage() {
  printf 'verify: %s\n\n' "$1" >&2
  usage >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --fast) OPT_FAST=1; shift ;;
    --no-docker) OPT_NO_DOCKER=1; shift ;;
    --filter)
      [[ $# -ge 2 ]] || fatal_usage "--filter needs an expression"
      OPT_FILTER="$2"; shift 2 ;;
    --filter=*) OPT_FILTER="${1#--filter=}"; shift ;;
    --stage)
      [[ $# -ge 2 ]] || fatal_usage "--stage needs a stage number"
      OPT_ONLY_STAGE="$2"; shift 2 ;;
    --stage=*) OPT_ONLY_STAGE="${1#--stage=}"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) fatal_usage "unknown argument: $1" ;;
  esac
done

if [[ -n "${OPT_ONLY_STAGE}" ]]; then
  [[ -n "${STAGE_NAME[${OPT_ONLY_STAGE}]:-}" ]] \
    || fatal_usage "--stage ${OPT_ONLY_STAGE}: no such stage (0-11)"
  [[ -n "${STAGE_RUN[${OPT_ONLY_STAGE}]:-}" ]] \
    || fatal_usage "--stage ${OPT_ONLY_STAGE} (${STAGE_NAME[${OPT_ONLY_STAGE}]}) is not implemented yet; task ${STAGE_OWNER[${OPT_ONLY_STAGE}]:-?} owns it"
fi

# A non-numeric floor would be read as zero by the arithmetic in stage 6, which
# is the one value that silently disables the check it is meant to configure.
[[ "${MIN_UNIT_TESTS}" =~ ^[0-9]+$ ]] \
  || fatal_usage "AURORA_MIN_UNIT_TESTS must be a non-negative integer, got '${MIN_UNIT_TESTS}'"

# ---------------------------------------------------------------------------
# Presentation
# ---------------------------------------------------------------------------

if [[ -t 1 ]]; then
  C_RESET=$'\033[0m'; C_BOLD=$'\033[1m'; C_DIM=$'\033[2m'
  C_RED=$'\033[31m'; C_GREEN=$'\033[32m'; C_YELLOW=$'\033[33m'
else
  C_RESET=''; C_BOLD=''; C_DIM=''
  C_RED=''; C_GREEN=''; C_YELLOW=''
fi

readonly RULE="---------------------------------------------------------------------"

now_ms() {
  # EPOCHREALTIME renders its decimal separator in the caller's locale, so
  # strip every non-digit rather than splitting on '.'.
  local t="${EPOCHREALTIME}"
  t="${t//[^0-9]/}"
  printf '%s' "$(( 10#${t} / 1000 ))"
}

fmt_ms() {
  local ms="$1"
  if (( ms >= 60000 )); then
    printf '%dm%02ds' "$(( ms / 60000 ))" "$(( (ms % 60000) / 1000 ))"
  else
    printf '%d.%01ds' "$(( ms / 1000 ))" "$(( (ms % 1000) / 100 ))"
  fi
}

# ---------------------------------------------------------------------------
# Stage plumbing
# ---------------------------------------------------------------------------

STAGE_LOG=""

# Records the exact command in the stage log before running it, so a failing
# stage can be reproduced by copying one line out of artifacts/verify/.
run_cmd() {
  printf '\n$ %s\n' "$*" >>"${STAGE_LOG}"
  "$@" >>"${STAGE_LOG}" 2>&1
}

# Same, but hands the last line of stdout back to the caller.
run_capture() {
  local out rc=0
  printf '\n$ %s\n' "$*" >>"${STAGE_LOG}"
  out="$("$@" 2>>"${STAGE_LOG}")" || rc=$?
  printf '%s\n' "${out}" >>"${STAGE_LOG}"
  printf '%s' "${out}" | tail -n 1
  return "${rc}"
}

note() {
  printf '%s\n' "$*" >>"${STAGE_LOG}"
}

stage_is_implemented() { [[ -n "${STAGE_RUN[$1]:-}" ]]; }
stage_has_flag()       { [[ " ${STAGE_FLAGS[$1]:-} " == *" $2 "* ]]; }

# Which stages this invocation intends to run, after --fast/--no-docker/--stage.
SELECTED_STAGES=()
select_stages() {
  local id
  for id in "${STAGE_IDS[@]}"; do
    if [[ -n "${OPT_ONLY_STAGE}" && "${id}" != "${OPT_ONLY_STAGE}" ]]; then
      STAGE_RESULT[$id]="SKIP"; STAGE_NOTE[$id]="--stage ${OPT_ONLY_STAGE}"
      continue
    fi
    if [[ "${OPT_FAST}" == "1" ]] && stage_has_flag "${id}" slow; then
      STAGE_RESULT[$id]="SKIP"; STAGE_NOTE[$id]="--fast"
      continue
    fi
    if [[ "${OPT_NO_DOCKER}" == "1" ]] && stage_has_flag "${id}" docker; then
      STAGE_RESULT[$id]="SKIP"; STAGE_NOTE[$id]="--no-docker"
      continue
    fi
    SELECTED_STAGES+=("${id}")
  done
}

# Stage 0 checks the Docker daemon only when a stage that needs one will
# actually run (5.2). No implemented stage needs one today, so the check is
# inert; it arms itself the moment B-11 gives stage 8 a handler.
docker_is_needed() {
  local id
  for id in "${SELECTED_STAGES[@]}"; do
    stage_is_implemented "${id}" || continue
    stage_has_flag "${id}" docker && return 0
  done
  return 1
}

# ---------------------------------------------------------------------------
# Stage 0 - Preflight
# ---------------------------------------------------------------------------

# global.json is a handful of flat string properties, so reading it with sed
# is preferable to making the gate depend on jq being installed.
json_string_value() {
  sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$2" | head -n 1
}

sdk_major_minor() {
  local major minor rest
  IFS=. read -r major minor rest <<<"$1"
  [[ "${major}" =~ ^[0-9]+$ && "${minor}" =~ ^[0-9]+$ ]] || return 1
  printf '%s.%s' "${major}" "${minor}"
}

stage_preflight() {
  if [[ ! -f "${SOLUTION}" ]]; then
    note "verify: ${SOLUTION} not found - is this the Aurora repository?"
    return 1
  fi

  if ! command -v dotnet >/dev/null 2>&1; then
    note "verify: 'dotnet' is not on PATH. scripts/dev-env.sh should have put it there;"
    note "        check that /usr/share/dotnet exists (CLAUDE.md, Local toolchain)."
    return 1
  fi

  # Run from the repository root, so this resolves through global.json. Under
  # the pinned rollForward of latestPatch the SDK muxer is what enforces the
  # pin, and a pinned-but-absent SDK fails right here with the SDK's own
  # diagnostic instead of somewhere deep inside stage 3.
  local resolved
  if ! resolved="$(run_capture dotnet --version)"; then
    note "verify: 'dotnet --version' failed - global.json pins an SDK this machine does not have."
    return 1
  fi
  note "SDK on PATH:      ${resolved}"

  local pinned roll
  pinned="$(json_string_value version "${REPO_ROOT}/global.json")"
  roll="$(json_string_value rollForward "${REPO_ROOT}/global.json")"
  roll="${roll:-latestPatch}"
  if [[ -z "${pinned}" ]]; then
    note "verify: could not read the SDK version out of global.json."
    return 1
  fi
  note "global.json pins: ${pinned} (rollForward: ${roll})"

  # The muxer's own enforcement is only as narrow as rollForward makes it:
  # widen that to latestFeature or latestMajor and it will happily build this
  # repository on a newer runtime. ".NET 10 (LTS)" is a parameter the product
  # owner locked (CLAUDE.md), so the major.minor is asserted here independently
  # of how rollForward is currently set.
  local mm_resolved mm_pinned
  mm_resolved="$(sdk_major_minor "${resolved}")" \
    || { note "verify: cannot parse the SDK version '${resolved}'."; return 1; }
  mm_pinned="$(sdk_major_minor "${pinned}")" \
    || { note "verify: cannot parse the global.json version '${pinned}'."; return 1; }
  if [[ "${mm_resolved}" != "${mm_pinned}" ]]; then
    note ""
    note "verify: SDK version mismatch - building on ${mm_resolved}, global.json pins ${mm_pinned}."
    note "        rollForward is '${roll}', which let the muxer cross that boundary."
    note "        The .NET major version is a locked product parameter (CLAUDE.md)."
    return 1
  fi
  note "major.minor:      ${mm_pinned} - match"

  if docker_is_needed; then
    if run_cmd docker info; then
      note "Docker:           available"
    else
      note ""
      note "verify: a selected stage needs a Docker daemon and 'docker info' failed."
      note "        Start Docker, or re-run with --no-docker to skip the stages that need it."
      return 1
    fi
  else
    note "Docker:           not required by any stage in this run"
  fi

  return 0
}

# ---------------------------------------------------------------------------
# Stage 1 - Restore
# ---------------------------------------------------------------------------

stage_restore() {
  # --locked-mode: a dependency change that did not update packages.lock.json
  # fails here, which is what makes the stage 4 licence gate enforceable
  # rather than aspirational (4, RestorePackagesWithLockFile).
  run_cmd dotnet restore "${SOLUTION}" --locked-mode || {
    note ""
    note "verify: restore failed. If a package was added, removed or bumped, commit the"
    note "        regenerated packages.lock.json files as part of the same change."
    return 1
  }
}

# ---------------------------------------------------------------------------
# Stage 2 - Format & style
# ---------------------------------------------------------------------------

stage_format() {
  # --verify-no-changes only. A gate must not silently fix what it measures
  # (5.1), so `dotnet format` is never allowed to write source here.
  run_cmd dotnet format "${SOLUTION}" \
    --verify-no-changes \
    --severity warn \
    --no-restore \
    --report "${VERIFY_DIR}/format-report.json" || {
    note ""
    note "verify: formatting or style deviations found. Fix them with"
    note "          dotnet format Aurora.sln --severity warn"
    note "        and commit the result. Machine-readable detail:"
    note "          artifacts/verify/format-report.json"
    return 1
  }
}

# ---------------------------------------------------------------------------
# Stage 3 - Build
# ---------------------------------------------------------------------------

stage_build() {
  run_cmd dotnet build "${SOLUTION}" -c Release --no-restore || return 1
  assert_no_build_warnings || return 1
}

# TreatWarningsAsErrors in Directory.Build.props promotes compiler, analyzer
# and code-style warnings to errors, but not every MSBuild- or NuGet-level
# warning. 5.2 says stage 3 fails on *any* warning, so the MSBuild summary
# line is checked too - and it has to be there. A log without one is not a
# build without warnings but a build this check could not read: MSBuild's
# terminal logger prints no such line, and a build that warned still exits 0
# under it, so a missing line would leave this check reporting nothing and
# passing.
assert_no_build_warnings() {
  local n summaries=0
  while read -r n; do
    [[ -n "${n}" ]] || continue
    summaries=$(( summaries + 1 ))
    if (( 10#${n} > 0 )); then
      note ""
      note "verify: the build reported ${n} warning(s). Warnings are errors in this"
      note "        repository (Directory.Build.props); a build that warns fails stage 3."
      return 1
    fi
  done < <(grep -Eo '^[[:space:]]*[0-9]+ Warning\(s\)' "${STAGE_LOG}" | grep -Eo '[0-9]+' || true)
  if (( summaries == 0 )); then
    note ""
    note "verify: the build log has no MSBuild warning summary ('N Warning(s)'), so stage 3"
    note "        cannot tell a clean build from one whose warnings it never saw. The console"
    note "        logger prints that line and the terminal logger does not: check"
    note "        MSBUILDTERMINALLOGGER and any -tl/--tl option reaching dotnet build."
    return 1
  fi
  note ""
  note "MSBuild warning summary: ${summaries} line(s) read, 0 warning(s)"
  return 0
}

# ---------------------------------------------------------------------------
# Stage 6 - Unit tests
# ---------------------------------------------------------------------------

# Solution-wide, so a project that cannot start a test host fails here rather
# than being discovered by a reviewer running a command nobody specified.
# Category-filtered, so the stage keeps the property testing-strategy.md 11
# requires of it: it passes with the Docker daemon stopped.
stage_unit_tests() {
  local filter='Category!=Integration&Category!=Ui'
  if [[ -n "${OPT_FILTER}" ]]; then
    filter="(${filter})&(${OPT_FILTER})"
  fi

  local rc=0
  run_cmd dotnet test "${SOLUTION}" \
    -c Release \
    --no-build \
    --filter "${filter}" \
    --logger "trx" \
    --results-directory "${VERIFY_DIR}" || rc=$?

  # A stage that reports PASS without a count cannot report its own vacuity. A
  # test project dropped from Aurora.sln, a misspelled Category trait, a filter
  # typo and a discovery failure all exit zero here and look exactly like
  # "everything passed". So the number of tests that actually ran is put in the
  # summary row on every path, passing or failing, and MIN_UNIT_TESTS turns it
  # into a floor that is raised by editing one line.
  local executed
  executed="$(count_executed_tests)"
  STAGE_NOTE[6]="${executed} test(s) executed"
  note ""
  note "tests executed:   ${executed} (minimum required: ${MIN_UNIT_TESTS})"
  note "filter applied:   ${filter}"

  if (( rc != 0 )); then
    note ""
    note "verify: the solution-wide test run failed. Two unrelated causes look alike here:"
    note "          - a test asserted and lost, or"
    note "          - a project in Aurora.sln was handed to the test runner and its test"
    note "            host could not start ('Test Run Aborted'), which fails the whole run."
    note "        A library under tests/ that hosts no tests of its own needs"
    note "        <IsTestProject>false</IsTestProject> in its .csproj."
    return 1
  fi

  # Base ten, explicitly. The ^[0-9]+$ validation above accepts "08", which bash
  # otherwise reads as octal: the arithmetic errors, the comparison is false, and
  # the stage reports PASS having executed nothing - the exact failure the floor
  # exists to prevent, reached through the check that was meant to prevent it.
  if (( executed < 10#${MIN_UNIT_TESTS} )); then
    note ""
    note "verify: stage 6 executed ${executed} test(s), below the required minimum of ${MIN_UNIT_TESTS}."
    note "        The filter applied was:"
    note "          ${filter}"
    note "        A test run that matched nothing is not a passing test run. Either that"
    note "        filter excludes everything, a test project has left Aurora.sln, or a"
    note "        Category trait is misspelled."
    note "        The floor is MIN_UNIT_TESTS in scripts/verify.sh (AURORA_MIN_UNIT_TESTS)."
    return 1
  fi
}

# Stage 6's cardinality. `dotnet test --logger trx` writes one .trx per test
# assembly into --results-directory, each carrying a <Counters executed="N"/>
# element; an assembly whose filter matched nothing still writes one, with zero.
# Read with grep, so the gate keeps its "nothing beyond sed/grep/awk/diff/git"
# property.
#
# The match is scoped to the <Counters element, and that scope is what makes
# the count trustworthy: the same file carries captured test output verbatim
# in <StdOut>, so a test that printed executed="1000" would otherwise be summed
# as a counter. Captured output cannot open an element - its '<' is escaped to
# &lt; - so only the element itself begins with '<Counters '. Within it, the
# leading space anchors the attribute name. (notExecuted="0" was never the
# concern: it is camelCase, and a case-sensitive executed=" does not match it.)
count_executed_tests() {
  local trx n total=0
  for trx in "${VERIFY_DIR}"/*.trx; do
    [[ -f "${trx}" ]] || continue
    while read -r n; do
      [[ -n "${n}" ]] || continue
      total=$(( total + 10#${n} ))
    done < <(grep -o '<Counters [^>]*' -- "${trx}" | grep -o ' executed="[0-9]*"' | grep -o '[0-9]*' || true)
  done
  printf '%s' "${total}"
}

# ---------------------------------------------------------------------------
# Stage 11 - Summary
# ---------------------------------------------------------------------------

TREE_SNAPSHOT=""
TREE_DRIFT=""
TREE_GUARD_DONE=0

snapshot_working_tree() {
  if git -C "${REPO_ROOT}" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    TREE_SNAPSHOT="$(git -C "${REPO_ROOT}" status --porcelain)"
  else
    TREE_SNAPSHOT="<not a git work tree>"
  fi
}

# The 5.1 promise that the gate never mutates what it measures. Compares the
# tree against the snapshot taken before stage 0 and leaves the difference in
# TREE_DRIFT; memoised, because both stage 11 and the exit handler ask.
#
# The exit handler is why this is not simply stage 11's body: fail-fast leaves
# the stage loop, so a mutation made by stage 1 or 2 on a run that then fails
# at stage 3 would never be compared at all - and that is the run on which a
# developer, already reading a failure, is least likely to notice it.
compare_working_tree() {
  (( TREE_GUARD_DONE == 0 )) || return 0
  TREE_GUARD_DONE=1

  if [[ "${TREE_SNAPSHOT}" == "<not a git work tree>" ]]; then
    return 0
  fi

  local after
  after="$(git -C "${REPO_ROOT}" status --porcelain)" || return 0
  if [[ "${after}" != "${TREE_SNAPSHOT}" ]]; then
    TREE_DRIFT="$(diff <(printf '%s\n' "${TREE_SNAPSHOT}") <(printf '%s\n' "${after}") || true)"
  fi
  return 0
}

# Appended to the stage 11 log whether stage 11 reached the guard itself or the
# exit handler had to reach it on stage 11's behalf: the summary points the
# reader at that file either way, so it has to exist and explain itself.
write_tree_drift_log() {
  {
    printf '\nverify: the working tree changed while the gate ran. A gate must not modify\n'
    printf '        what it measures (solution-layout.md 5.1). Difference, before vs after:\n\n'
    printf '%s\n' "${TREE_DRIFT}"
  } >>"$1" 2>/dev/null || true
}

# The timing table is printed by the exit handler, so that it appears whatever
# went wrong. What stage 11 *does* is report the working-tree guard: a future
# stage that reformats source or regenerates a lock file fails here instead of
# being noticed weeks later in someone's `git status`.
stage_summary() {
  compare_working_tree

  if [[ "${TREE_SNAPSHOT}" == "<not a git work tree>" ]]; then
    note "working-tree guard: skipped (not a git work tree)"
    return 0
  fi

  if [[ -z "${TREE_DRIFT}" ]]; then
    note "working-tree guard: no tracked or untracked file changed during the run"
    return 0
  fi

  STAGE_NOTE[11]="working tree changed"
  write_tree_drift_log "${STAGE_LOG}"
  return 1
}

# ---------------------------------------------------------------------------
# Summary rendering
# ---------------------------------------------------------------------------

SUMMARY_ARMED=0
RUN_START_MS=0
FAILED_STAGE=""

result_colour() {
  case "$1" in
    PASS)    printf '%s' "${C_GREEN}" ;;
    FAIL)    printf '%s' "${C_RED}" ;;
    PENDING) printf '%s' "${C_YELLOW}" ;;
    *)       printf '%s' "${C_DIM}" ;;
  esac
}

print_summary() {
  local exit_code="$1"
  local total_ms=$(( $(now_ms) - RUN_START_MS ))
  local id result note_text time_text line plain=""

  plain+="${RULE}"$'\n'
  plain+="$(printf ' %-3s %-27s %-8s %8s  %s' "#" "Stage" "Result" "Time" "Note")"$'\n'
  plain+="${RULE}"$'\n'

  for id in "${STAGE_IDS[@]}"; do
    result="${STAGE_RESULT[$id]:--}"
    note_text="${STAGE_NOTE[$id]:-}"
    if [[ -n "${STAGE_MS[$id]:-}" ]]; then
      time_text="$(fmt_ms "${STAGE_MS[$id]}")"
    else
      time_text="-"
    fi
    line="$(printf ' %-3s %-27s %-8s %8s  %s' \
      "${id}" "${STAGE_NAME[$id]}" "${result}" "${time_text}" "${note_text}")"
    plain+="${line}"$'\n'
  done

  plain+="${RULE}"$'\n'
  plain+="$(printf ' %-3s %-27s %-8s %8s' "" "Total" "" "$(fmt_ms "${total_ms}")")"$'\n'
  plain+="${RULE}"$'\n'

  local pending=""
  for id in "${STAGE_IDS[@]}"; do
    [[ "${STAGE_RESULT[$id]:-}" == "PENDING" ]] && pending+="${id} "
  done
  if [[ -n "${pending}" ]]; then
    plain+=" Gate incomplete: stage(s) ${pending% } are not implemented yet."$'\n'
  fi

  # Drift found on a run that already failed for another reason is reported,
  # not promoted: the stage the developer has to fix stays the headline. When
  # stage 11 is the failure, it has already said this in its own log.
  if [[ -n "${TREE_DRIFT}" && "${FAILED_STAGE}" != "11" ]]; then
    plain+=" Also: the working tree changed while the gate ran (solution-layout.md 5.1)."$'\n'
    plain+="$(printf '%s\n' "${TREE_DRIFT}" | sed 's/^/       /')"$'\n'
  fi

  if [[ "${exit_code}" -eq 0 ]]; then
    plain+=" RESULT: PASS"$'\n'
  elif [[ -n "${FAILED_STAGE}" ]]; then
    plain+=" RESULT: FAIL - stage ${FAILED_STAGE} (${STAGE_NAME[${FAILED_STAGE}]})"$'\n'
    plain+=" Log:    artifacts/verify/$(printf '%02d' "${FAILED_STAGE}")-${STAGE_SLUG[${FAILED_STAGE}]}.log"$'\n'
  else
    plain+=" RESULT: FAIL - the run was interrupted (exit ${exit_code})"$'\n'
  fi

  mkdir -p "${VERIFY_DIR}" 2>/dev/null || true
  printf '%s' "${plain}" >"${SUMMARY_FILE}" 2>/dev/null || true

  # The same table on stdout, with the result column coloured.
  printf '\n'
  while IFS= read -r line; do
    case "${line}" in
      " RESULT: PASS")
        printf '%s%s%s%s\n' "${C_BOLD}" "${C_GREEN}" "${line}" "${C_RESET}" ;;
      " RESULT: FAIL"*)
        printf '%s%s%s%s\n' "${C_BOLD}" "${C_RED}" "${line}" "${C_RESET}" ;;
      # Which column holds the result cannot be found by field number: a stage
      # name is one, two or three words ("Build", "Unit tests", "Format &
      # style"). The patterns below have already identified it, so each branch
      # names it rather than re-parsing the row.
      *" PASS "*)    printf '%s%s%s\n' "$(result_colour PASS)"    "${line}" "${C_RESET}" ;;
      *" FAIL "*)    printf '%s%s%s\n' "$(result_colour FAIL)"    "${line}" "${C_RESET}" ;;
      *" PENDING "*) printf '%s%s%s\n' "$(result_colour PENDING)" "${line}" "${C_RESET}" ;;
      *" SKIP "*)    printf '%s%s%s\n' "$(result_colour SKIP)"    "${line}" "${C_RESET}" ;;
      *)
        printf '%s\n' "${line}" ;;
    esac
  done <<<"${plain}"
}

on_exit() {
  local code="$1"
  set +e
  trap - EXIT
  if [[ "${SUMMARY_ARMED}" == "1" ]]; then
    compare_working_tree
    # Nothing else failed, yet the tree moved: stage 11 never got to run - the
    # loop was cut short by --stage or by an interrupt - so the guard fails the
    # run from here, and writes the log the summary is about to point at.
    if [[ -n "${TREE_DRIFT}" && "${code}" -eq 0 ]]; then
      local log="${VERIFY_DIR}/11-${STAGE_SLUG[11]}.log"
      mkdir -p -- "${VERIFY_DIR}" 2>/dev/null
      printf '=== stage 11: %s (working-tree guard, run from the exit handler) ===\n' \
        "${STAGE_NAME[11]}" >>"${log}" 2>/dev/null
      write_tree_drift_log "${log}"
      STAGE_RESULT[11]="FAIL"
      STAGE_NOTE[11]="working tree changed"
      FAILED_STAGE=11
      code=1
    fi
    print_summary "${code}"
  fi
  exit "${code}"
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

select_stages

# Sourcing dev-env.sh is the gate's first real action (5.1). Argument parsing
# has to precede it, because --no-docker decides whether dev-env.sh should
# spend its 30-second timeout starting a daemon that nothing in this run needs.
if ! docker_is_needed; then
  export AURORA_SKIP_DOCKER=1
fi
set +e
# shellcheck source=scripts/dev-env.sh
. "${REPO_ROOT}/scripts/dev-env.sh"
set -e

rm -rf -- "${VERIFY_DIR}"
mkdir -p -- "${VERIFY_DIR}"

snapshot_working_tree

RUN_START_MS="$(now_ms)"
SUMMARY_ARMED=1
trap 'on_exit $?' EXIT
trap 'trap - EXIT; on_exit 130' INT
trap 'trap - EXIT; on_exit 143' TERM

printf '%s\n' "${C_BOLD}Aurora ERP - verify${C_RESET}"
printf '%s\n' "${C_DIM}repository: ${REPO_ROOT}${C_RESET}"
printf '%s\n' "${C_DIM}logs:       artifacts/verify/${C_RESET}"
printf '\n'

for stage_id in "${STAGE_IDS[@]}"; do
  if [[ "${STAGE_RESULT[$stage_id]:-}" == "SKIP" ]]; then
    printf ' %s%-3s %-27s skipped (%s)%s\n' \
      "${C_DIM}" "${stage_id}" "${STAGE_NAME[$stage_id]}" "${STAGE_NOTE[$stage_id]}" "${C_RESET}"
    continue
  fi

  if ! stage_is_implemented "${stage_id}"; then
    STAGE_RESULT[$stage_id]="PENDING"
    STAGE_NOTE[$stage_id]="owned by ${STAGE_OWNER[$stage_id]:-?}"
    printf ' %s%-3s %-27s pending (%s)%s\n' \
      "${C_YELLOW}" "${stage_id}" "${STAGE_NAME[$stage_id]}" "${STAGE_OWNER[$stage_id]:-?}" "${C_RESET}"
    continue
  fi

  STAGE_LOG="${VERIFY_DIR}/$(printf '%02d' "${stage_id}")-${STAGE_SLUG[$stage_id]}.log"
  : >"${STAGE_LOG}"
  printf '=== stage %s: %s ===\n' "${stage_id}" "${STAGE_NAME[$stage_id]}" >>"${STAGE_LOG}"

  printf ' %-3s %-27s ' "${stage_id}" "${STAGE_NAME[$stage_id]}"

  stage_start_ms="$(now_ms)"
  stage_rc=0
  "${STAGE_RUN[$stage_id]}" || stage_rc=$?
  STAGE_MS[$stage_id]=$(( $(now_ms) - stage_start_ms ))

  if [[ "${stage_rc}" -eq 0 ]]; then
    STAGE_RESULT[$stage_id]="PASS"
    printf '%sPASS%s  %s\n' "${C_GREEN}" "${C_RESET}" "$(fmt_ms "${STAGE_MS[$stage_id]}")"
    continue
  fi

  STAGE_RESULT[$stage_id]="FAIL"
  FAILED_STAGE="${stage_id}"
  printf '%sFAIL%s  %s\n' "${C_RED}" "${C_RESET}" "$(fmt_ms "${STAGE_MS[$stage_id]}")"

  printf '\n%s%s%s\n' "${C_RED}" "${RULE}" "${C_RESET}"
  printf '%s%s STAGE %s FAILED: %s%s\n' "${C_BOLD}" "${C_RED}" \
    "${stage_id}" "${STAGE_NAME[$stage_id]}" "${C_RESET}"
  printf '%s%s%s\n' "${C_RED}" "${RULE}" "${C_RESET}"
  printf '%slast %s lines of %s:%s\n\n' \
    "${C_DIM}" "${FAIL_TAIL_LINES}" "${STAGE_LOG#"${REPO_ROOT}/"}" "${C_RESET}"
  tail -n "${FAIL_TAIL_LINES}" "${STAGE_LOG}"
  printf '\n%sfull log: %s%s\n' "${C_DIM}" "${STAGE_LOG#"${REPO_ROOT}/"}" "${C_RESET}"

  # Fail fast (5.1): every later stage stays unrun and says so in the table.
  exit 1
done

exit 0
