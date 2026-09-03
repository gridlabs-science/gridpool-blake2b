#!/usr/bin/env bash
set -euo pipefail

bitcoin_cli="${GRIDPOOL_BITCOIN_CLI:-/opt/gridpool-blake2b/knots-rc4/bin/bitcoin-cli}"
bitcoin_datadir="${GRIDPOOL_BITCOIN_DATADIR:-/var/lib/gridpool-blake2b/mainnet}"
bitcoin_conf="${GRIDPOOL_BITCOIN_CONF:-/etc/gridpool-blake2b/knots-mainnet.conf}"
soak_script="${GRIDPOOL_SOAK_SCRIPT:-/opt/gridpool-blake2b/mainnet-private-soak/scripts/boot-g2-soak.sh}"
soak_output="${GRIDPOOL_SOAK_OUTPUT:-/opt/gridpool-blake2b/mainnet-private-soak/soak-logs/post-validation-soak.json}"
poll_seconds="${GRIDPOOL_VALIDATION_POLL_SECONDS:-300}"
soak_duration="${GRIDPOOL_POST_VALIDATION_SOAK_DURATION:-12h}"

while true; do
    chainstates="$($bitcoin_cli -datadir="$bitcoin_datadir" -conf="$bitcoin_conf" getchainstates)"
    active_chainstates="$(jq -r '.chainstates | length' <<<"$chainstates")"
    all_validated="$(jq -r '[.chainstates[].validated] | all' <<<"$chainstates")"
    background_height="$(jq -r '.chainstates[0].blocks // 0' <<<"$chainstates")"

    printf '[validation-wait] chainstates=%s all_validated=%s background_height=%s\n' \
        "$active_chainstates" "$all_validated" "$background_height"

    if [[ "$active_chainstates" == "1" && "$all_validated" == "true" ]]; then
        break
    fi

    sleep "$poll_seconds"
done

printf '[validation-wait] full validation complete; starting %s soak at %s\n' \
    "$soak_duration" "$(date -u +%Y-%m-%dT%H:%M:%SZ)"

exec "$soak_script" "$soak_duration" "$soak_output"
