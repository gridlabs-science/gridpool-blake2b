#!/usr/bin/env bash
set -euo pipefail

# Mainnet public-beta Hydrapool launcher.
#
# This script intentionally reads the Umbrel bitcoind RPC cookie at process
# start instead of baking RPC credentials into a systemd unit.

HYDRAPOOL_DIR="${HYDRAPOOL_DIR:-/home/keegreil/Documents/GitHub/hydrapool}"
HYDRAPOOL_CONFIG="${HYDRAPOOL_CONFIG:-${HYDRAPOOL_DIR}/config.toml}"
HYDRAPOOL_BIN="${HYDRAPOOL_BIN:-${HYDRAPOOL_DIR}/target/debug/hydrapool}"
BITCOIN_CONTAINER="${BITCOIN_CONTAINER:-}"
BITCOIN_COOKIE_FILE="${BITCOIN_COOKIE_FILE:-/home/keegreil/.bitcoin/.cookie}"
BITCOIN_COOKIE_PATH="${BITCOIN_COOKIE_PATH:-/data/.bitcoin/.cookie}"
BITCOIN_RPC_URL="${BITCOIN_RPC_URL:-http://127.0.0.1:8334}"
BITCOIN_RPC_USERNAME="${BITCOIN_RPC_USERNAME:-}"
BITCOIN_RPC_PASSWORD="${BITCOIN_RPC_PASSWORD:-}"
BITCOIN_ZMQ_HASHBLOCK="${BITCOIN_ZMQ_HASHBLOCK:-tcp://127.0.0.1:28342}"
GRIDPOOL_PAYOUT_URL="${GRIDPOOL_PAYOUT_URL:-http://127.0.0.1:5000/api/mining/payouts}"
WAIT_TIMEOUT_SECONDS="${WAIT_TIMEOUT_SECONDS:-0}"

log() {
  printf '[hydrapool-gridpool-launcher] %s\n' "$*" >&2
}

wait_until() {
  local label="$1"
  shift
  local start now
  start="$(date +%s)"
  while true; do
    if "$@" >/dev/null 2>&1; then
      return 0
    fi
    now="$(date +%s)"
    if (( WAIT_TIMEOUT_SECONDS > 0 && now - start >= WAIT_TIMEOUT_SECONDS )); then
      log "timed out waiting for ${label}"
      return 1
    fi
    sleep 2
  done
}

bitcoin_container_running() {
  [[ "$(docker inspect -f '{{.State.Running}}' "$BITCOIN_CONTAINER" 2>/dev/null)" == "true" ]]
}

if [[ ! -x "$HYDRAPOOL_BIN" ]]; then
  log "Hydrapool binary is not executable: ${HYDRAPOOL_BIN}"
  exit 1
fi

if [[ ! -f "$HYDRAPOOL_CONFIG" ]]; then
  log "Hydrapool config not found: ${HYDRAPOOL_CONFIG}"
  exit 1
fi

wait_until "GridPool payout API ${GRIDPOOL_PAYOUT_URL}" \
  curl -fsS "$GRIDPOOL_PAYOUT_URL"

if [[ -n "$BITCOIN_CONTAINER" ]]; then
  wait_until "Docker container ${BITCOIN_CONTAINER}" bitcoin_container_running

  wait_until "bitcoind RPC cookie ${BITCOIN_CONTAINER}:${BITCOIN_COOKIE_PATH}" \
    docker exec "$BITCOIN_CONTAINER" sh -lc "test -s '${BITCOIN_COOKIE_PATH}'"

  cookie="$(docker exec "$BITCOIN_CONTAINER" sh -lc "cat '${BITCOIN_COOKIE_PATH}'")"
else
  if [[ -s "$BITCOIN_COOKIE_FILE" ]]; then
    cookie="$(cat "$BITCOIN_COOKIE_FILE")"
  elif [[ -n "$BITCOIN_RPC_USERNAME" && -n "$BITCOIN_RPC_PASSWORD" ]]; then
    cookie="${BITCOIN_RPC_USERNAME}:${BITCOIN_RPC_PASSWORD}"
  else
    echo "Neither BITCOIN_COOKIE_FILE nor BITCOIN_RPC_USERNAME/BITCOIN_RPC_PASSWORD is available" >&2
    exit 1
  fi
fi
rpc_user="${cookie%%:*}"
rpc_pass="${cookie#*:}"

if [[ -z "$rpc_user" || -z "$rpc_pass" || "$rpc_user" == "$rpc_pass" ]]; then
  log "failed to parse bitcoind RPC cookie"
  exit 1
fi

bitcoin_rpc_ready() {
  local response
  response="$(curl -fsS \
    --user "${rpc_user}:${rpc_pass}" \
    --data-binary '{"jsonrpc":"1.0","id":"health","method":"getblockchaininfo","params":[]}' \
    -H 'content-type: text/plain;' \
    "${BITCOIN_RPC_URL}/" 2>/dev/null)" || return 1

  jq -e '.error == null and .result.initialblockdownload == false' >/dev/null <<<"$response"
}

wait_until "bitcoind RPC ready and out of initial block download at ${BITCOIN_RPC_URL}" \
  bitcoin_rpc_ready

if [[ "${DRY_RUN:-0}" == "1" ]]; then
  log "dry-run ok: dependencies ready and RPC cookie parsed"
  exit 0
fi

cd "$HYDRAPOOL_DIR"

log "starting Hydrapool GridPool bridge on port ${P2POOL_STRATUM_PORT:-3333}"
exec env \
  P2POOL_BITCOINRPC_URL="$BITCOIN_RPC_URL" \
  P2POOL_BITCOINRPC_USERNAME="$rpc_user" \
  P2POOL_BITCOINRPC_PASSWORD="$rpc_pass" \
  P2POOL_STRATUM_ZMQPUBHASHBLOCK="$BITCOIN_ZMQ_HASHBLOCK" \
  P2POOL_STRATUM_HOSTNAME="${P2POOL_STRATUM_HOSTNAME:-0.0.0.0}" \
  P2POOL_STRATUM_PORT="${P2POOL_STRATUM_PORT:-3333}" \
  P2POOL_API_HOSTNAME="${P2POOL_API_HOSTNAME:-127.0.0.1}" \
  P2POOL_API_PORT="${P2POOL_API_PORT:-46884}" \
  P2POOL_LOGGING_LEVEL="${P2POOL_LOGGING_LEVEL:-info}" \
  "$HYDRAPOOL_BIN" --config "$HYDRAPOOL_CONFIG"
