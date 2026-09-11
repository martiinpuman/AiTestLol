#!/usr/bin/env bash
#
# Move every line the PublicApiAnalyzers code fix put in PublicAPI.Unshipped.txt into
# PublicAPI.Shipped.txt, sorted, and empty the unshipped file.
#
# This is the deliberate human step of ADR-0008 3.1: the build refuses any public member of
# Aurora.Countries.Contracts that is not in the shipped file, and running this script is the moment
# somebody decides the change is intended and what SemVer bump it costs. It is never run by
# verify.sh - a gate must not fix what it is measuring - and the diff it produces is the thing a
# reviewer reads to see the contract surface change.
#
# Usage, from anywhere in the repository:
#
#   scripts/approve-contract-api.sh            # merge whatever the code fix has staged
#   scripts/approve-contract-api.sh --generate # run the code fix first, then merge
#
set -Eeuo pipefail

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=dev-env.sh
AURORA_SKIP_DOCKER=1 source "${REPO_ROOT}/scripts/dev-env.sh"

PROJECT="${REPO_ROOT}/src/Aurora.Countries.Contracts/Aurora.Countries.Contracts.csproj"
SHIPPED="${REPO_ROOT}/src/Aurora.Countries.Contracts/PublicAPI.Shipped.txt"
UNSHIPPED="${REPO_ROOT}/src/Aurora.Countries.Contracts/PublicAPI.Unshipped.txt"
HEADER='#nullable enable'

if [[ "${1:-}" == "--generate" ]]; then
  dotnet format analyzers "${PROJECT}" --diagnostics RS0016 --severity info
fi

added=$(grep -v -x -e "${HEADER}" -e '' "${UNSHIPPED}" | wc -l | tr -d ' ')

{
  printf '%s\n' "${HEADER}"
  cat "${SHIPPED}" "${UNSHIPPED}" | grep -v -x -e "${HEADER}" -e '' | LC_ALL=C sort -u
} >"${SHIPPED}.new"

mv "${SHIPPED}.new" "${SHIPPED}"
printf '%s\n' "${HEADER}" >"${UNSHIPPED}"

total=$(grep -c -v -x -e "${HEADER}" -e '' "${SHIPPED}" || true)
printf 'approved %s new API line(s); the contract surface is now %s line(s).\n' "${added}" "${total}"
printf 'Review the diff of %s and decide the SemVer bump (ADR-0008 3.1) before committing.\n' \
  "${SHIPPED#"${REPO_ROOT}"/}"
