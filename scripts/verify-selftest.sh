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
# the SDK on PATH, that it needs no network at all, that the options in
# solution-layout.md 5.3 do what the table says, and that a failing stage leaves
# every later stage unrun (fail fast, 5.1).
#
# This is deliberately NOT a stage of verify.sh. It costs one full gate run per
# case, and it mutates the working tree while it runs - which is precisely what
# a gate must never do. Run it when verify.sh itself changes.
#
# Covers stages 0-3, 6 and 11 (task B-02). Stages 4, 5 and 7-10 arrive with
# B-11; dependencies.md 1 rule 4 already demands six such cases for stage 4
# alone, and they belong here rather than in a second harness.
#
# SAFETY. The injections below are real, if brief:
#   - every file a case touches is copied aside before the case mutates it and
#     restored by an EXIT/INT/TERM trap, so even an interrupted run restores;
#   - after every case the working tree is compared against the state the run
#     started from, and a case that cannot be undone stops the whole run rather
#     than injecting the next defect on top of an unknown tree;
#   - it never commits, never stashes and never touches the git index.
#
# Uncommitted work is therefore safe, and the script runs with it in place. It
# says so when it starts, because the baseline case then measures that work too:
# a gate failure in the first case is far more likely to be yours than the
# harness's.

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

# Restores the repository and insists it really is back to where it started -
# which is BASELINE_STATUS, not necessarily a clean tree. A case that cannot be
# undone stops the run: continuing would inject the next defect on top of a
# working tree nobody can describe.
case_end() {
  restore_all
  local now
  now="$(tree_status)"
  if [[ "${now}" != "${BASELINE_STATUS}" ]]; then
    printf '   %s%sABORT%s working tree not restored after "%s":\n' \
      "${C_BOLD}" "${C_RED}" "${C_RESET}" "${CASE_NAME}"
    diff <(printf '%s\n' "${BASELINE_STATUS}") <(printf '%s\n' "${now}") || true
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

# 5.3's options are user-facing contract, and what they skipped has to be
# visible in the table rather than merely true. Read from summary.txt, which is
# the plain-text copy of the same table the developer sees.
assert_summary_row() {
  local id="$1" row fragment
  shift
  row="$(grep -E "^ ${id}[[:space:]]" -- "${VERIFY_DIR}/summary.txt" 2>/dev/null | head -n 1 || true)"
  [[ -n "${row}" ]] || fail_case "no summary row for stage ${id}" || return 1
  for fragment in "$@"; do
    [[ "${row}" == *"${fragment}"* ]] \
      || fail_case "stage ${id} row lacks '${fragment}':${row}" || return 1
  done
  printf '    summary row:%s\n' "${row}"
}

# A usage error is exit 2, distinct from a stage failure (exit 1), so a script
# that wraps the gate can tell "I called it wrong" from "the code is broken".
assert_usage_error() {
  local needle="$1"
  (( RUN_RC == 2 )) || fail_case "expected exit 2, got ${RUN_RC}" || return 1
  grep -qF -- "${needle}" "${CASE_LOG}" \
    || fail_case "expected '${needle}' in the output" || return 1
  printf '    exit 2: %s\n' "${needle}"
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

# The count in stage 6's row is asserted here rather than only in the case that
# makes it fail: it is printed on every run precisely so that a stage which
# measured nothing cannot look like a stage on which everything passed.
case_baseline() {
  case_begin "baseline: a clean tree passes, and stage 6 says how many tests ran"
  run_verify
  assert_pass || return 0
  assert_summary_row 6 "PASS" "test(s) executed" || return 0
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

# 5.1 allows exactly two network calls - the NuGet restore and one pull of
# postgres:17-alpine - and no stage implemented today makes either: the packages
# are already in the global cache and nothing here starts a container. Proven by
# running the whole gate in a network namespace with no route to anywhere,
# rather than by reading the commands and believing them.
#
# `unshare -n` hands back a namespace whose loopback interface exists but is
# *down*, and `dotnet test` reaches its test host over a TCP socket on
# loopback - so without the helper below stage 6 times out after 90s per
# assembly with "failed to connect to testhost", which is machine-local IPC
# failing rather than egress being denied. Raising an interface is normally
# iproute2's job and this image ships no `ip`; the helper performs the same
# ioctl directly. Loopback carries no traffic off the machine, so the case
# still proves what it claims.
LOOPBACK_HELPER="${WORK}/netns-loopback-up.py"

write_loopback_helper() {
  cat >"${LOOPBACK_HELPER}" <<'PY'
"""Raise loopback in the current network namespace, then exec the rest of argv.

This is what `ip link set lo up` ultimately performs: read the interface flags
with SIOCGIFFLAGS, set IFF_UP, write them back with SIOCSIFFLAGS. It opens no
route out of the namespace.
"""
import fcntl
import os
import socket
import struct
import sys

SIOCGIFFLAGS = 0x8913
SIOCSIFFLAGS = 0x8914
IFF_UP = 0x1

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
flags = struct.unpack("16sh", fcntl.ioctl(sock, SIOCGIFFLAGS, struct.pack("16sh", b"lo", 0)))[1]
fcntl.ioctl(sock, SIOCSIFFLAGS, struct.pack("16sh", b"lo", flags | IFF_UP))
sock.close()
os.execvp(sys.argv[1], sys.argv[1:])
PY
}

case_offline() {
  case_begin "5.1 the whole gate runs with no network egress at all"
  if ! unshare -n true >/dev/null 2>&1; then
    printf '    SKIP: this kernel or user cannot create a network namespace\n'
    return 0
  fi
  write_loopback_helper
  RUN_RC=0
  unshare -n -- python3 "${LOOPBACK_HELPER}" "${VERIFY}" >"${CASE_LOG}" 2>&1 || RUN_RC=$?
  assert_pass || return 0
  assert_summary_row 6 "PASS" "test(s) executed" || return 0
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
  assert_stage_log_contains 1 "NU1004" || return 0
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

# The other way stage 6 can be wrong: it measured nothing and said PASS. A
# dropped test project, a misspelled Category trait and a filter typo all look
# like this, so the floor is raised for the length of one run - which is also
# exactly what the developer who lands the first tests does permanently, which
# is why this case keeps working after MIN_UNIT_TESTS stops being zero.
case_unit_tests_vacuous() {
  case_begin "stage 6: a filter that matches nothing, against a floor of one"
  VERIFY_ENV=(AURORA_MIN_UNIT_TESTS=1)
  run_verify --filter 'FullyQualifiedName~ZzzNoSuchTestExists'
  assert_fail_stage 6 || return 0
  assert_stage_log_contains 6 "below the required minimum of 1" || return 0
  assert_stage_log_contains 6 "ZzzNoSuchTestExists" || return 0
  assert_summary_row 6 "FAIL" "0 test(s) executed" || return 0
}

# Stage 11 enforces 5.1's central promise - the gate never mutates what it
# measures - and B-11 adds the stages that read lock files and the dependency
# closure, which is the code most likely to regress it. The probe is an MSBuild
# target that writes an untracked file during stage 3's build, so the mutation
# lands at a point in the run this case decides rather than on a timer.
case_summary_tree_guard() {
  case_begin "stage 11: a stage that writes into the repository it measures"
  local proj="src/hosts/Aurora.Web/Aurora.Web.csproj"
  local probe="GATE_MUTATION_PROBE.txt"
  stage_file "${proj}"
  stage_file "${probe}"
  sed -i 's|^</Project>|  <Target Name="AuroraSelfTestMutation" AfterTargets="Build">\n    <WriteLinesToFile File="$(MSBuildThisFileDirectory)../../../GATE_MUTATION_PROBE.txt" Lines="verify-selftest" Overwrite="true" />\n  </Target>\n\n</Project>|' \
    -- "${REPO_ROOT}/${proj}"
  run_verify
  assert_fail_stage 11 || return 0
  assert_stage_log_contains 11 "${probe}" || return 0
  assert_summary_row 11 "FAIL" "working tree changed" || return 0
}

# 5.3. Each of these is a way of calling the gate wrongly, and each must be
# exit 2 with a message - not a silently skipped stage, and not exit 1, which
# would tell a wrapper script the repository is broken.
case_usage_errors() {
  case_begin "5.3 bad usage exits 2 and says what was wrong"
  run_verify --bogus
  assert_usage_error "unknown argument: --bogus" || return 0
  run_verify --stage 99
  assert_usage_error "no such stage" || return 0
  run_verify --stage 4
  assert_usage_error "B-11" || return 0
  run_verify --filter
  assert_usage_error "--filter needs an expression" || return 0
  VERIFY_ENV=(AURORA_MIN_UNIT_TESTS=some)
  run_verify
  assert_usage_error "AURORA_MIN_UNIT_TESTS must be a non-negative integer" || return 0
}

# The remaining half of 5.3: the flags that skip work must say in the table
# which flag skipped it, or a --fast run is indistinguishable from a full one
# in anything a reviewer reads afterwards.
case_option_skips_are_declared() {
  case_begin "5.3 --fast, --no-docker and --stage declare what they skipped"
  run_verify --stage 0
  assert_pass || return 0
  assert_summary_row 0 "PASS" || return 0
  assert_summary_row 6 "SKIP" "--stage 0" || return 0
  run_verify --fast
  assert_pass || return 0
  assert_summary_row 8 "SKIP" "--fast" || return 0
  assert_summary_row 9 "SKIP" "--fast" || return 0
  assert_summary_row 10 "SKIP" "--fast" || return 0
  run_verify --no-docker
  assert_pass || return 0
  assert_summary_row 8 "SKIP" "--no-docker" || return 0
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

printf '%sAurora ERP - verify self-test%s\n' "${C_BOLD}" "${C_RESET}"
printf '%srepository: %s%s\n\n' "${C_DIM}" "${REPO_ROOT}" "${C_RESET}"

# Every later comparison is against this, so the script restores uncommitted
# work rather than demanding its absence.
BASELINE_STATUS="$(tree_status)"

if [[ -n "${BASELINE_STATUS}" ]]; then
  printf '%sstarting from a dirty working tree - it will be restored, not cleaned:%s\n' \
    "${C_DIM}" "${C_RESET}"
  printf '%s\n' "${BASELINE_STATUS}"
  printf '%sthe baseline case measures those changes too.%s\n\n' "${C_DIM}" "${C_RESET}"
fi

CASES=(
  case_baseline
  case_any_cwd
  case_dev_env_is_sourced
  case_offline
  case_preflight_no_sdk
  case_preflight_sdk_mismatch
  case_restore_stale_lock
  case_format_violation
  case_build_compiler_warning
  case_build_msbuild_warning
  case_unit_test_failure
  case_unit_test_host_aborts
  case_unit_tests_vacuous
  case_summary_tree_guard
  case_usage_errors
  case_option_skips_are_declared
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
  printf '%sworking tree: back to the state the run started from.%s\n' \
    "${C_DIM}" "${C_RESET}"
  exit 0
fi

printf '%s%s%d of %d cases did not behave as specified.%s\n' \
  "${C_BOLD}" "${C_RED}" "${CASES_FAILED}" "${CASES_RUN}" "${C_RESET}"
exit 1
