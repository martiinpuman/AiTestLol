#!/usr/bin/env bash
#
# Aurora ERP - self-test for the quality gate.
#
# A gate nobody has seen fail is not known to work. For every stage that
# scripts/verify.sh implements, this script injects the violation that stage
# exists to catch, asserts that verify.sh exits non-zero and names that stage,
# and then puts the repository back exactly as it found it.
#
# It also asserts the properties of the gate that no single stage owns: that it
# runs from any working directory, that sourcing scripts/dev-env.sh is what puts
# the SDK on PATH, and that a failing stage leaves every later stage unrun
# (fail fast, docs/architecture/solution-layout.md 5.1).
#
# This is deliberately NOT a stage of verify.sh. It costs one full gate run per
# case, and it mutates the working tree while it runs - which is precisely what
# a gate must never do. Run it when verify.sh itself changes.
#
# Covers stages 0-3 and 6 (task B-02). Stages 4, 5 and 7-10 arrive with B-11;
# dependencies.md 1 rule 4 already demands six such cases for stage 4 alone, and
# they belong here rather than in a second harness.
#
# SAFETY. The injections below are real, if brief:
#   - the script refuses to start on a dirty working tree, because only on a
#     clean one is "put it back" unambiguous;
#   - every file it touches is copied aside first and restored by an
#     EXIT/INT/TERM trap, so an interrupted run still restores;
#   - after every case it asserts `git status --porcelain` is empty again and
#     stops the whole run if it is not;
#   - it never commits, never stashes and never touches the git index.

set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd -P)"
cd -- "${REPO_ROOT}"

VERIFY="${SCRIPT_DIR}/verify.sh"
VERIFY_DIR="${REPO_ROOT}/artifacts/verify"

WORK="$(mktemp -d)"
BACKUP_DIR="${WORK}/backup"
mkdir -p -- "${BACKUP_DIR}"

if [[ -t 1 ]]; then
  C_RESET=$'\033[0m'; C_BOLD=$'\033[1m'; C_DIM=$'\033[2m'
  C_RED=$'\033[31m'; C_GREEN=$'\033[32m'
else
  C_RESET=''; C_BOLD=''; C_DIM=''; C_RED=''; C_GREEN=''
fi

CASES_RUN=0
CASES_FAILED=0
CASE_NAME=""
CASE_LOG=""
RUN_RC=0
VERIFY_ENV=()

# Relative paths staged for restore, and whether each existed beforehand.
STAGED_PATHS=()
declare -A STAGED_EXISTED=()

# ---------------------------------------------------------------------------
# Restore
# ---------------------------------------------------------------------------

restore_all() {
  local rel
  for rel in ${STAGED_PATHS[@]+"${STAGED_PATHS[@]}"}; do
    if [[ "${STAGED_EXISTED[${rel}]}" == "1" ]]; then
      cp -p -- "${BACKUP_DIR}/$(path_key "${rel}")" "${REPO_ROOT}/${rel}"
    else
      rm -f -- "${REPO_ROOT}/${rel}"
    fi
  done
  STAGED_PATHS=()
  unset STAGED_EXISTED
  declare -gA STAGED_EXISTED=()
}

# A flat backup directory keyed by the path with '/' replaced, so no nested
# directories have to be recreated under ${BACKUP_DIR}.
path_key() { printf '%s' "${1//\//__}"; }

# Copy a file aside (or record that it does not exist yet) before a case
# mutates it. Every mutation in this script is preceded by one of these.
stage_file() {
  local rel="$1"
  STAGED_PATHS+=("${rel}")
  if [[ -e "${REPO_ROOT}/${rel}" ]]; then
    STAGED_EXISTED["${rel}"]=1
    cp -p -- "${REPO_ROOT}/${rel}" "${BACKUP_DIR}/$(path_key "${rel}")"
  else
    STAGED_EXISTED["${rel}"]=0
  fi
}

on_exit() {
  local rc=$?
  trap - EXIT
  restore_all
  rm -rf -- "${WORK}"
  exit "${rc}"
}
trap on_exit EXIT
trap 'trap - EXIT; restore_all; rm -rf -- "${WORK}"; exit 130' INT
trap 'trap - EXIT; restore_all; rm -rf -- "${WORK}"; exit 143' TERM

# ---------------------------------------------------------------------------
# Assertions
# ---------------------------------------------------------------------------

tree_status() { git -C "${REPO_ROOT}" status --porcelain; }

fail_case() {
  printf '   %s%sFAILED%s %s\n' "${C_BOLD}" "${C_RED}" "${C_RESET}" "$*"
  if [[ -s "${CASE_LOG}" ]]; then
    printf '%s' "${C_DIM}"
    tail -n 25 -- "${CASE_LOG}"
    printf '%s' "${C_RESET}"
  fi
  CASES_FAILED=$(( CASES_FAILED + 1 ))
  return 1
}

case_begin() {
  CASE_NAME="$1"
  CASES_RUN=$(( CASES_RUN + 1 ))
  CASE_LOG="${WORK}/case-$(printf '%02d' "${CASES_RUN}").out"
  VERIFY_ENV=()
  printf ' %s%2d%s %s\n' "${C_BOLD}" "${CASES_RUN}" "${C_RESET}" "${CASE_NAME}"
}

# Restores the repository and insists it really is back to where it started.
# A case that cannot restore stops the run: continuing would inject the next
# defect on top of an unknown working tree.
case_end() {
  restore_all
  local dirty
  dirty="$(tree_status)"
  if [[ -n "${dirty}" ]]; then
    printf '   %s%sABORT%s working tree not restored after "%s":\n%s\n' \
      "${C_BOLD}" "${C_RED}" "${C_RESET}" "${CASE_NAME}" "${dirty}"
    exit 1
  fi
}

run_verify() {
  RUN_RC=0
  if (( ${#VERIFY_ENV[@]} )); then
    env "${VERIFY_ENV[@]}" "${VERIFY}" "$@" >"${CASE_LOG}" 2>&1 || RUN_RC=$?
  else
    "${VERIFY}" "$@" >"${CASE_LOG}" 2>&1 || RUN_RC=$?
  fi
}

assert_pass() {
  (( RUN_RC == 0 )) || fail_case "expected exit 0, got ${RUN_RC}" || return 1
  grep -qF ' RESULT: PASS' -- "${CASE_LOG}" \
    || fail_case "expected 'RESULT: PASS' in the output" || return 1
  printf '    exit 0, RESULT: PASS\n'
}

# The gate must exit non-zero *and* say which stage failed: an exit code alone
# does not tell a developer where to look.
assert_fail_stage() {
  local id="$1" line
  (( RUN_RC != 0 )) || fail_case "expected a non-zero exit, got 0" || return 1
  line="$(grep -F ' RESULT: FAIL - stage ' -- "${CASE_LOG}" || true)"
  [[ "${line}" == *" RESULT: FAIL - stage ${id} ("* ]] \
    || fail_case "expected the summary to name stage ${id}, got: ${line:-<none>}" || return 1
  printf '    exit %s,%s\n' "${RUN_RC}" "${line}"
}

assert_stage_log_contains() {
  local id="$1" needle="$2" log
  log="$(ls -- "${VERIFY_DIR}/$(printf '%02d' "${id}")"-*.log 2>/dev/null | head -n 1 || true)"
  [[ -n "${log}" ]] || fail_case "no stage ${id} log in artifacts/verify/" || return 1
  grep -qF -- "${needle}" "${log}" \
    || fail_case "stage ${id} log does not mention '${needle}'" || return 1
  printf '    stage %s log says: %s\n' "${id}" "${needle}"
}

# Fail fast (5.1). verify.sh creates a stage's log only when it runs that stage,
# so the absence of every higher-numbered log is the evidence.
assert_nothing_ran_after() {
  local failed="$1" f base n
  for f in "${VERIFY_DIR}"/[0-9][0-9]-*.log; do
    [[ -e "${f}" ]] || continue
    base="$(basename -- "${f}")"
    n=$(( 10#${base:0:2} ))
    if (( n > failed )); then
      fail_case "stage ${n} ran after stage ${failed} failed - not fail-fast" || return 1
    fi
  done
  printf '    no stage after %s ran\n' "${failed}"
}

# ---------------------------------------------------------------------------
# Cases
# ---------------------------------------------------------------------------

case_baseline() {
  case_begin "baseline: a clean tree passes"
  run_verify
  assert_pass || return 0
}

case_any_cwd() {
  case_begin "5.1 runs from any working directory (/ and a subdirectory)"
  local dir rc_all=0
  for dir in / "${REPO_ROOT}/src/Aurora.SharedKernel" "${WORK}"; do
    RUN_RC=0
    ( cd -- "${dir}" && "${VERIFY}" ) >"${CASE_LOG}" 2>&1 || RUN_RC=$?
    if (( RUN_RC != 0 )) || ! grep -qF ' RESULT: PASS' -- "${CASE_LOG}"; then
      fail_case "cwd ${dir}: expected a passing run, got exit ${RUN_RC}" || rc_all=1
      break
    fi
    grep -qF "repository: ${REPO_ROOT}" -- "${CASE_LOG}" \
      || { fail_case "cwd ${dir}: did not report ${REPO_ROOT} as the repository" || rc_all=1; break; }
    printf '    cwd %-40s PASS, repository: %s\n' "${dir}" "${REPO_ROOT}"
  done
  (( rc_all == 0 )) || return 0
}

# Positive and negative halves of the same fact. With the SDK absent from PATH
# the gate still passes, because sourcing dev-env.sh is what puts it there;
# point DOTNET_ROOT somewhere empty and the same run fails at stage 0.
case_dev_env_is_sourced() {
  case_begin "5.1 sources dev-env.sh (passes with the SDK off PATH)"
  VERIFY_ENV=(-u DOTNET_ROOT PATH=/usr/bin:/bin)
  run_verify
  assert_pass || return 0
  assert_stage_log_contains 0 "SDK on PATH:" || return 0
}

case_preflight_no_sdk() {
  case_begin "stage 0: the SDK is not installed where DOTNET_ROOT points"
  VERIFY_ENV=(DOTNET_ROOT=/nonexistent/dotnet PATH=/usr/bin:/bin)
  run_verify
  assert_fail_stage 0 || return 0
  assert_stage_log_contains 0 "'dotnet' is not on PATH" || return 0
  assert_nothing_ran_after 0 || return 0
}

# The check this exercises is the reason global.json alone is not enough: with
# rollForward widened, the muxer happily resolves a different major version and
# reports success. Stage 0 asserts the major.minor independently.
case_preflight_sdk_mismatch() {
  case_begin "stage 0: global.json pins a different .NET major version"
  stage_file global.json
  cat >"${REPO_ROOT}/global.json" <<'JSON'
{
  "sdk": {
    "version": "9.0.100",
    "rollForward": "latestMajor",
    "allowPrerelease": false
  }
}
JSON
  run_verify
  assert_fail_stage 0 || return 0
  assert_stage_log_contains 0 "SDK version mismatch" || return 0
  assert_nothing_ran_after 0 || return 0
}

case_restore_stale_lock() {
  case_begin "stage 1: a dependency added without regenerating packages.lock.json"
  local proj="tests/Aurora.Architecture.Tests/Aurora.Architecture.Tests.csproj"
  stage_file "${proj}"
  sed -i 's|<PackageReference Include="xunit" />|<PackageReference Include="xunit" />\n    <PackageReference Include="Testcontainers.PostgreSql" />|' \
    -- "${REPO_ROOT}/${proj}"
  run_verify
  assert_fail_stage 1 || return 0
  assert_stage_log_contains 1 "packages.lock.json" || return 0
  assert_nothing_ran_after 1 || return 0
}

case_format_violation() {
  case_begin "stage 2: a formatting violation (AC-7)"
  local src="src/hosts/Aurora.Web/Program.cs"
  stage_file "${src}"
  sed -i 's|^        WebApplication app = builder.Build();|            WebApplication app = builder.Build();|' \
    -- "${REPO_ROOT}/${src}"
  run_verify
  assert_fail_stage 2 || return 0
  assert_stage_log_contains 2 "WHITESPACE" || return 0
  assert_nothing_ran_after 2 || return 0
}

# 5.2: stage 3 fails on any warning. A compiler warning gets there through
# TreatWarningsAsErrors, which fails the build outright.
case_build_compiler_warning() {
  case_begin "stage 3: a compiler warning (warnings are errors)"
  local src="src/hosts/Aurora.Web/Program.cs"
  stage_file "${src}"
  printf '#warning verify-selftest: deliberate compiler warning\n' \
    >>"${REPO_ROOT}/${src}"
  run_verify
  assert_fail_stage 3 || return 0
  assert_stage_log_contains 3 "CS1030" || return 0
  assert_nothing_ran_after 3 || return 0
}

# The other half of "any warning": an MSBuild-level warning is not a compiler
# warning, so TreatWarningsAsErrors does not touch it and the build succeeds.
# Only verify.sh's own reading of the MSBuild summary catches this one, which
# is why that check exists.
case_build_msbuild_warning() {
  case_begin "stage 3: an MSBuild warning the compiler never sees"
  local proj="src/hosts/Aurora.Web/Aurora.Web.csproj"
  stage_file "${proj}"
  sed -i 's|^</Project>|  <Target Name="AuroraSelfTestWarning" BeforeTargets="Build">\n    <Warning Text="verify-selftest: deliberate MSBuild warning" />\n  </Target>\n\n</Project>|' \
    -- "${REPO_ROOT}/${proj}"
  run_verify
  assert_fail_stage 3 || return 0
  assert_stage_log_contains 3 "warning(s). Warnings are errors" || return 0
  assert_nothing_ran_after 3 || return 0
}

case_unit_test_failure() {
  case_begin "stage 6: a unit test that asserts and loses"
  local src="tests/Aurora.Architecture.Tests/VerifySelfTestProbe.cs"
  stage_file "${src}"
  cat >"${REPO_ROOT}/${src}" <<'CSHARP'
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// Injected by scripts/verify-selftest.sh and deleted again in the same step.
/// </summary>
public sealed class VerifySelfTestProbe
{
    [Fact]
    public void DeliberateFailure()
    {
        Assert.Fail("verify-selftest: deliberate unit test failure");
    }
}
CSHARP
  run_verify
  assert_fail_stage 6 || return 0
  assert_stage_log_contains 6 "DeliberateFailure" || return 0
}

# The failure that put stage 6 in B-02 rather than B-11: a library under tests/
# handed to VSTest aborts the whole solution-wide run, and stages 0-3 cannot
# see it. Aurora.TestKit carries IsTestProject=false precisely for this.
case_unit_test_host_aborts() {
  case_begin "stage 6: a library under tests/ handed to the test runner"
  local proj="tests/Aurora.TestKit/Aurora.TestKit.csproj"
  stage_file "${proj}"
  sed -i 's|<IsTestProject>false</IsTestProject>|<IsTestProject>true</IsTestProject>|' \
    -- "${REPO_ROOT}/${proj}"
  run_verify
  assert_fail_stage 6 || return 0
  assert_stage_log_contains 6 "solution-wide test run failed" || return 0
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

printf '%sAurora ERP - verify self-test%s\n' "${C_BOLD}" "${C_RESET}"
printf '%srepository: %s%s\n\n' "${C_DIM}" "${REPO_ROOT}" "${C_RESET}"

if [[ -n "$(tree_status)" ]]; then
  printf '%sverify-selftest: the working tree is dirty.%s\n' "${C_RED}" "${C_RESET}" >&2
  cat >&2 <<'MSG'
This script injects real defects and restores the files afterwards. On a dirty
tree "restore" is ambiguous - it cannot tell your edits from its own - so it
refuses to start. Commit or set aside your work and run it again.
MSG
  exit 2
fi

CASES=(
  case_baseline
  case_any_cwd
  case_dev_env_is_sourced
  case_preflight_no_sdk
  case_preflight_sdk_mismatch
  case_restore_stale_lock
  case_format_violation
  case_build_compiler_warning
  case_build_msbuild_warning
  case_unit_test_failure
  case_unit_test_host_aborts
)

# case_end, not the case bodies, owns the restore: a case that fails an
# assertion half way through must still hand the next one a clean tree.
for case_fn in "${CASES[@]}"; do
  "${case_fn}" || true
  case_end
done

printf '\n'
if (( CASES_FAILED == 0 )); then
  printf '%s%s%d/%d cases behaved as specified.%s\n' \
    "${C_BOLD}" "${C_GREEN}" "${CASES_RUN}" "${CASES_RUN}" "${C_RESET}"
  printf '%sworking tree after the run: %s%s\n' \
    "${C_DIM}" "$(tree_status | wc -l) change(s)" "${C_RESET}"
  exit 0
fi

printf '%s%s%d of %d cases did not behave as specified.%s\n' \
  "${C_BOLD}" "${C_RED}" "${CASES_FAILED}" "${CASES_RUN}" "${C_RESET}"
exit 1
