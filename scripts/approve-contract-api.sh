#!/usr/bin/env bash
#
# Rewrite the approved public API surface of Aurora.Countries.Contracts.
#
# The build refuses (RS0016) any public member of that assembly that is not listed in
# PublicAPI.Shipped.txt, which is the gate ADR-0008 3.1 asks for: the contract surface cannot change
# without somebody running this script and reading the diff it produces. That diff is where the
# SemVer decision is made - a removed or changed line is MAJOR, an added one is MINOR.
#
# It is never run by verify.sh. A gate must not fix what it is measuring (solution-layout.md 5.1).
#
# How it works: PublicApiAnalyzers ships the "add to public API" code fix, and `dotnet format
# analyzers` applies it, writing into PublicAPI.Unshipped.txt. The fixer only emits the members it
# sees as missing, so the shipped file is emptied first and rebuilt whole - which also means a
# member that no longer exists disappears from the file instead of lingering as a stale line.
#
# Usage, from anywhere in the repository:
#
#   scripts/approve-contract-api.sh
#
set -Eeuo pipefail

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=dev-env.sh
AURORA_SKIP_DOCKER=1 source "${REPO_ROOT}/scripts/dev-env.sh"

PROJECT_DIR="${REPO_ROOT}/src/Aurora.Countries.Contracts"
PROJECT="${PROJECT_DIR}/Aurora.Countries.Contracts.csproj"
SHIPPED="${PROJECT_DIR}/PublicAPI.Shipped.txt"
UNSHIPPED="${PROJECT_DIR}/PublicAPI.Unshipped.txt"
HEADER='#nullable enable'

before=$(grep -c -v -x -e "${HEADER}" -e '' "${SHIPPED}" || true)

printf '%s\n' "${HEADER}" >"${SHIPPED}"
printf '%s\n' "${HEADER}" >"${UNSHIPPED}"

dotnet format analyzers "${PROJECT}" --diagnostics RS0016 --severity info

{
  printf '%s\n' "${HEADER}"
  grep -v -x -e "${HEADER}" -e '' "${UNSHIPPED}" | LC_ALL=C sort -u
} >"${SHIPPED}"

printf '%s\n' "${HEADER}" >"${UNSHIPPED}"

after=$(grep -c -v -x -e "${HEADER}" -e '' "${SHIPPED}" || true)

if (( after == 0 )); then
  printf 'approve-contract-api: the code fix produced no API lines at all.\n' >&2
  printf '  That is never right for a non-empty assembly - it means the fixer did not run.\n' >&2
  printf '  The shipped file has been left empty on purpose so the next build fails loudly.\n' >&2
  exit 1
fi

printf 'contract surface: %s line(s), was %s.\n' "${after}" "${before}"
printf 'Read the diff of %s and decide the SemVer bump (ADR-0008 3.1) before committing:\n' \
  "${SHIPPED#"${REPO_ROOT}"/}"
printf '  a removed or changed line is MAJOR, an added one is MINOR.\n'
