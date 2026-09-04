#!/usr/bin/env bash
set -euo pipefail

CLI=/opt/gridpool-blake2b/knots-rc4/bin/bitcoin-cli
DATA_DIR=/var/lib/gridpool-blake2b/mainnet
CONFIG=/etc/gridpool-blake2b/knots-mainnet.conf
ACTIVATION_HEIGHT=961640
ACTIVATION_HASH=0000000000000050c1e5f69672f459293be14f46e5a494e7a8c8541396f18eeb
EXPECTED_SUBVERSION=/Satoshi:29.4.1/Knots:20260508rc4/
MIN_FREE_GIB="${MIN_FREE_GIB:-15}"

free_kib="$(df -Pk "$DATA_DIR" | awk 'NR == 2 { print $4 }')"
min_free_kib="$((MIN_FREE_GIB * 1024 * 1024))"
if (( free_kib < min_free_kib )); then
    printf 'CRITICAL: only %s KiB free; minimum is %s GiB\n' "$free_kib" "$MIN_FREE_GIB" >&2
    exit 2
fi

chain_info="$("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getblockchaininfo)"
network_info="$("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getnetworkinfo)"
height="$(jq -r .blocks <<<"$chain_info")"

jq -e --arg expected_subversion "$EXPECTED_SUBVERSION" --argjson network "$network_info" '
  .chain == "main" and
  .initialblockdownload == false and
  .blocks == .headers and
  $network.subversion == $expected_subversion and
  $network.connections > 0
' <<<"$chain_info" >/dev/null

if (( height >= ACTIVATION_HEIGHT )); then
    actual="$("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getblockhash "$ACTIVATION_HEIGHT")"
    [[ "$actual" == "$ACTIVATION_HASH" ]]
fi

jq -n --argjson chain "$chain_info" --argjson network "$network_info" --arg free_kib "$free_kib" '{
  chain: $chain.chain,
  blocks: $chain.blocks,
  headers: $chain.headers,
  initialblockdownload: $chain.initialblockdownload,
  verificationprogress: $chain.verificationprogress,
  pruned: $chain.pruned,
  chainstates: $chain.chainstates,
  connections: $network.connections,
  subversion: $network.subversion,
  free_kib: ($free_kib | tonumber)
}'
