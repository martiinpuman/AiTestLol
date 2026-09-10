#!/usr/bin/env bash
# Rebuilds the development toolchain in a fresh container.
# Idempotent and safe to run on every session start.
set -uo pipefail

DOTNET_DIR="/usr/share/dotnet"
PG_IMAGE="postgres:17-alpine"

log() { echo "bootstrap-env: $*" >&2; }

# --- .NET SDK (LTS) ---------------------------------------------------------
if [ -x "$DOTNET_DIR/dotnet" ]; then
  log ".NET SDK already present ($("$DOTNET_DIR/dotnet" --version 2>/dev/null))"
else
  log "installing .NET SDK (LTS)..."
  tmp="$(mktemp -d)"
  if curl -sSL --max-time 180 https://dot.net/v1/dotnet-install.sh -o "$tmp/dotnet-install.sh"; then
    chmod +x "$tmp/dotnet-install.sh"
    "$tmp/dotnet-install.sh" --channel LTS --install-dir "$DOTNET_DIR" --no-path \
      && log ".NET SDK installed: $("$DOTNET_DIR/dotnet" --version 2>/dev/null)" \
      || log "ERROR: .NET SDK install failed"
  else
    log "ERROR: could not download dotnet-install.sh"
  fi
  rm -rf "$tmp"
fi

export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$PATH:$DOTNET_DIR"

# --- Docker daemon (Testcontainers needs it) --------------------------------
if docker info >/dev/null 2>&1; then
  log "Docker already running"
else
  log "starting Docker daemon..."
  (sudo -n dockerd >/tmp/dockerd.log 2>&1 &)
  for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 1; done
  docker info >/dev/null 2>&1 && log "Docker is up" || log "WARNING: Docker did not start"
fi

# --- PostgreSQL test image --------------------------------------------------
if docker info >/dev/null 2>&1; then
  if docker image inspect "$PG_IMAGE" >/dev/null 2>&1; then
    log "$PG_IMAGE already pulled"
  else
    log "pulling $PG_IMAGE..."
    docker pull "$PG_IMAGE" >/dev/null 2>&1 && log "pulled $PG_IMAGE" || log "WARNING: pull failed"
  fi
fi

log "ready."
