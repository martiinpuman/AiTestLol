#!/usr/bin/env bash
# Source this before running any dotnet command:  source scripts/dev-env.sh
# Puts the .NET SDK on PATH and makes sure the Docker daemon is up for Testcontainers.

export DOTNET_ROOT="${DOTNET_ROOT:-/usr/share/dotnet}"
case ":$PATH:" in
  *":$DOTNET_ROOT:"*) ;;
  *) export PATH="$PATH:$DOTNET_ROOT" ;;
esac

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if ! docker info >/dev/null 2>&1; then
  echo "dev-env: starting Docker daemon..." >&2
  (sudo -n dockerd >/tmp/dockerd.log 2>&1 &)
  for _ in $(seq 1 30); do
    docker info >/dev/null 2>&1 && break
    sleep 1
  done
  docker info >/dev/null 2>&1 \
    && echo "dev-env: Docker is up." >&2 \
    || echo "dev-env: WARNING - Docker did not start; integration tests will fail." >&2
fi
