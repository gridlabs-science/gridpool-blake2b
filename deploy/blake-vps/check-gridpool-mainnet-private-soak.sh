#!/usr/bin/env bash
set -euo pipefail

base_url=http://127.0.0.1:5000
container=gridpool-blake2b-mainnet-private-soak

for attempt in $(seq 1 45); do
    if summary="$(curl --fail --silent --show-error --max-time 5 "$base_url/api/network/summary" 2>/dev/null)"; then
        break
    fi
    if (( attempt == 45 )); then
        docker logs --tail 100 "$container" >&2
        exit 1
    fi
    sleep 2
done

expected_image="${GRIDPOOL_BOOT_IMAGE:?GRIDPOOL_BOOT_IMAGE is required}"
actual_image="$(docker inspect --format '{{.Config.Image}}' "$container")"
[[ "$actual_image" == "$expected_image" ]]

jq -e '
  .bitcoinNotification.mode == "attached-node" and
  .bitcoinNotification.authorityClass == "local-full-node" and
  .bitcoinNotification.miningSafe == true and
  .bitcoinNotification.rpc.synced == true and
  .bitcoinNotification.rpc.chainProfileAttested == true and
  (.configWarnings | length) == 0
' <<<"$summary" >/dev/null

for port in 5000 3008; do
    ss -lntH "sport = :$port" | awk '{print $4}' | grep -Eq '^127\.0\.0\.1:'
    if ss -lntH "sport = :$port" | awk '{print $4}' | grep -Evq '^127\.0\.0\.1:'; then
        echo "port $port is not loopback-only" >&2
        exit 1
    fi
done

jq '{
  nodeId,
  releaseVersion,
  serviceStartedUtc,
  currentTipBlockHeight,
  currentTipBlockHash,
  currentStateId,
  candidateStateId,
  workSetCount,
  bitcoinNotification,
  configWarnings
}' <<<"$summary"
