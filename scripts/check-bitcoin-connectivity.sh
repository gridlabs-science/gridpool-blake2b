#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/bitcoin-connectivity.sh
source "$SCRIPT_DIR/lib/bitcoin-connectivity.sh"

MODE="${BITCOIN_NOTIFICATION_MODE:-attached-node}"
RPC_URL="${BITCOIN_RPC_URL:-}"
RPC_USERNAME="${BITCOIN_RPC_USERNAME:-}"
RPC_PASSWORD_FILE="${BITCOIN_RPC_PASSWORD_FILE:-}"
RPC_COOKIE_FILE="${BITCOIN_RPC_COOKIE_FILE:-}"
ZMQ_HASHBLOCK="${BITCOIN_ZMQ_ENDPOINT:-}"
ZMQ_RAWBLOCK="${BITCOIN_ZMQ_RAWBLOCK_ENDPOINT:-}"
TIMEOUT_SECONDS="${BITCOIN_RPC_TIMEOUT_SECONDS:-3}"

usage() {
    cat <<'EOF'
Usage: check-bitcoin-connectivity.sh [options]

Options:
  --mode attached-node|external-fallback
  --rpc-url URL
  --rpc-username USER
  --rpc-password-file PATH
  --rpc-cookie-file PATH
  --zmq-hashblock tcp://HOST:PORT
  --zmq-rawblock tcp://HOST:PORT
  --timeout-seconds N

Passwords are read from files and are never printed.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode) MODE="${2:-}"; shift 2 ;;
        --rpc-url) RPC_URL="${2:-}"; shift 2 ;;
        --rpc-username) RPC_USERNAME="${2:-}"; shift 2 ;;
        --rpc-password-file) RPC_PASSWORD_FILE="${2:-}"; shift 2 ;;
        --rpc-cookie-file) RPC_COOKIE_FILE="${2:-}"; shift 2 ;;
        --zmq-hashblock) ZMQ_HASHBLOCK="${2:-}"; shift 2 ;;
        --zmq-rawblock) ZMQ_RAWBLOCK="${2:-}"; shift 2 ;;
        --timeout-seconds) TIMEOUT_SECONDS="${2:-}"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; usage >&2; exit 1 ;;
    esac
done

case "$MODE" in
    external-fallback)
        printf 'External-fallback mode selected; no attached Bitcoin node is required.\n'
        ;;
    attached-node)
        [[ -n "$RPC_URL" ]] || {
            printf 'Attached-node mode requires --rpc-url.\n' >&2
            exit 1
        }
        gridpool_check_bitcoin_connectivity \
            "$RPC_URL" \
            "$RPC_USERNAME" \
            "$RPC_PASSWORD_FILE" \
            "$RPC_COOKIE_FILE" \
            "$ZMQ_HASHBLOCK" \
            "$ZMQ_RAWBLOCK" \
            "$TIMEOUT_SECONDS"
        ;;
    *)
        printf 'Mode must be attached-node or external-fallback.\n' >&2
        exit 1
        ;;
esac
