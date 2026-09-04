#!/usr/bin/env bash
set -euo pipefail

base_url=http://127.0.0.1:5000
container=gridpool-blake2b-mainnet-private-soak

ready=false
for attempt in $(seq 1 60); do
    summary="$(curl --fail --silent --show-error --max-time 5 "$base_url/api/network/summary" 2>/dev/null || true)"
    if jq -e '
      .bitcoinNotification.mode == "attached-node" and
      .bitcoinNotification.authorityClass == "local-full-node" and
      .bitcoinNotification.miningSafe == true and
      .bitcoinNotification.rpc.synced == true and
      .bitcoinNotification.rpc.chainProfileAttested == true and
      (.configWarnings | length) == 0
    ' <<<"$summary" >/dev/null 2>&1; then
        ready=true
        break
    fi
    if (( attempt == 60 )); then
        docker logs --tail 100 "$container" >&2
        exit 1
    fi
    sleep 2
done

[[ "$ready" == true ]]

expected_image="${GRIDPOOL_BOOT_IMAGE:?GRIDPOOL_BOOT_IMAGE is required}"
actual_image="$(docker inspect --format '{{.Config.Image}}' "$container")"
[[ "$actual_image" == "$expected_image" ]]

ss -lntH "sport = :5000" | awk '{print $4}' | grep -Eq '^127\.0\.0\.1:'
if ss -lntH "sport = :5000" | awk '{print $4}' | grep -Evq '^127\.0\.0\.1:'; then
    echo "port 5000 is not loopback-only" >&2
    exit 1
fi

if ! ss -lntH "sport = :3008" | awk '{print $4}' | grep -Eq '^(0\.0\.0\.0|\*|\[::\]):3008$'; then
    echo "public DATUM beta port 3008 is not listening on a wildcard address" >&2
    exit 1
fi

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
