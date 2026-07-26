#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER="$SCRIPT_DIR/install-gridpool-node.sh"

run_attached() {
    local topology="$1"
    local host="$2"
    shift 2
    "$INSTALLER" \
        --dry-run \
        --yes \
        --bitcoin-topology "$topology" \
        --bitcoin-host "$host" \
        --bitcoin-rpc-username test \
        --bitcoin-rpc-password-file /dev/null \
        --home "/tmp/gridpool-installer-${topology}" \
        "$@" >/dev/null
}

run_attached host-network 127.0.0.1
run_attached host-gateway host.docker.internal
run_attached remote bitcoin.lan
run_attached shared-bridge bitcoin --bitcoin-docker-network bitcoin
"$INSTALLER" \
    --dry-run \
    --yes \
    --external-fallback \
    --home /tmp/gridpool-installer-node-less >/dev/null

printf 'GridPool installer topology dry runs passed.\n'
