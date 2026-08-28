#!/usr/bin/env bash
set -euo pipefail

CLI=/opt/gridpool-blake2b/knots-rc3/bin/bitcoin-cli
DATA_DIR=/var/lib/gridpool-blake2b/testnet4
CONFIG=/etc/gridpool-blake2b/knots-testnet4.conf
MIN_FREE_GIB="${MIN_FREE_GIB:-15}"

free_kib="$(df -Pk "$DATA_DIR" | awk 'NR == 2 { print $4 }')"
min_free_kib="$((MIN_FREE_GIB * 1024 * 1024))"
if (( free_kib < min_free_kib )); then
    printf 'CRITICAL: only %s KiB free; minimum is %s GiB\n' "$free_kib" "$MIN_FREE_GIB" >&2
    exit 2
fi

chain_info="$($CLI -datadir="$DATA_DIR" -conf="$CONFIG" getblockchaininfo)"
network_info="$($CLI -datadir="$DATA_DIR" -conf="$CONFIG" getnetworkinfo)"

jq -n \
    --argjson chain "$chain_info" \
    --argjson network "$network_info" \
    --arg free_kib "$free_kib" \
    '{
      chain: $chain.chain,
      blocks: $chain.blocks,
      headers: $chain.headers,
      initialblockdownload: $chain.initialblockdownload,
      verificationprogress: $chain.verificationprogress,
      pruned: $chain.pruned,
      connections: $network.connections,
      subversion: $network.subversion,
      free_kib: ($free_kib | tonumber)
    }'
