#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -n "${GRIDPOOL_HEALTH_NODE_BIN:-}" ]]; then
  exec "$GRIDPOOL_HEALTH_NODE_BIN" "$REPO_DIR/scripts/gridpool-health-monitor.mjs" "$@"
fi

node_major() {
  node --version 2>/dev/null | sed -E 's/^v([0-9]+).*/\1/'
}

if ! command -v node >/dev/null 2>&1 || [[ "$(node_major || echo 0)" -lt 18 ]]; then
  if [[ -s "${HOME}/.nvm/nvm.sh" ]]; then
    # shellcheck source=/dev/null
    source "${HOME}/.nvm/nvm.sh"
    nvm use --silent node >/dev/null 2>&1 || nvm use --silent 24 >/dev/null 2>&1 || true
  fi
fi

if ! command -v node >/dev/null 2>&1; then
  echo "node is required but was not found. Install Node 18+ or set GRIDPOOL_HEALTH_NODE_BIN." >&2
  exit 1
fi

if [[ "$(node_major || echo 0)" -lt 18 ]]; then
  echo "GridPool health monitor requires Node 18+. Found $(node --version)." >&2
  echo "Set GRIDPOOL_HEALTH_NODE_BIN in ~/.config/gridpool-health-monitor/monitor.env if needed." >&2
  exit 1
fi

exec node "$REPO_DIR/scripts/gridpool-health-monitor.mjs" "$@"
