#!/usr/bin/env bash
set -euo pipefail

CLI=/opt/gridpool-blake2b/knots-rc4/bin/bitcoin-cli
DATA_DIR=/var/lib/gridpool-blake2b/mainnet
CONFIG=/etc/gridpool-blake2b/knots-mainnet.conf
SNAPSHOT_DIR="$DATA_DIR/snapshots"
SNAPSHOT="$SNAPSHOT_DIR/utxo-910000.dat"
SNAPSHOT_PART="$SNAPSHOT.part"
SNAPSHOT_URL="${SNAPSHOT_URL:-https://utxo.download/utxo-910000.dat}"
EXPECTED_BASE=0000000000000000000108970acb9522ffd516eae17acddcb1bd16469194a821

mkdir -p "$SNAPSHOT_DIR"
if [[ ! -s "$SNAPSHOT" ]]; then
    curl --fail --location --retry 8 --retry-all-errors --continue-at - \
        --output "$SNAPSHOT_PART" "$SNAPSHOT_URL"
    mv "$SNAPSHOT_PART" "$SNAPSHOT"
fi

until (( $("$CLI" -datadir="$DATA_DIR" -conf="$CONFIG" getblockchaininfo | jq -r .headers) >= 910000 )); do
    sleep 15
done

result="$("$CLI" -rpcclienttimeout=0 -datadir="$DATA_DIR" -conf="$CONFIG" loadtxoutset snapshots/utxo-910000.dat)"
jq -e --arg expected "$EXPECTED_BASE" \
    '.base_height == 910000 and .tip_hash == $expected' <<<"$result" >/dev/null
printf '%s\n' "$result"
