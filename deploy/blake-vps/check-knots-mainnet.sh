#!/usr/bin/env bash
set -euo pipefail

CLI=/opt/gridpool-blake2b/knots-rc4/bin/bitcoin-cli
DATA_DIR=/var/lib/gridpool-blake2b/mainnet
CONFIG=/etc/gridpool-blake2b/knots-mainnet.conf
ACTIVATION_HEIGHT=961640
ACTIVATION_HASH=0000000000000050c1e5f69672f459293be14f46e5a494e7a8c8541396f18eeb

chain_info="$("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getblockchaininfo)"
network_info="$("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getnetworkinfo)"
height="$(jq -r .blocks <<<"$chain_info")"

if (( height >= ACTIVATION_HEIGHT )); then
    actual="$("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getblockhash "$ACTIVATION_HEIGHT")"
    [[ "$actual" == "$ACTIVATION_HASH" ]]
fi

jq -n --argjson chain "$chain_info" --argjson network "$network_info" '{
  chain: $chain.chain,
  blocks: $chain.blocks,
  headers: $chain.headers,
  initialblockdownload: $chain.initialblockdownload,
  verificationprogress: $chain.verificationprogress,
  pruned: $chain.pruned,
  chainstates: $chain.chainstates,
  connections: $network.connections,
  subversion: $network.subversion
}'
