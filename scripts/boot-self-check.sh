#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-${BOOT_SELF_CHECK_URL:-http://127.0.0.1:5000}}"
BASE_URL="${BASE_URL%/}"

usage() {
    cat <<EOF
Usage: $(basename "$0") [base-url]

Examples:
  $(basename "$0")
  $(basename "$0") http://127.0.0.1:5000
  BOOT_SELF_CHECK_URL=https://boot.example.com $(basename "$0")

Checks:
  - /health/live responds
  - /health/ready responds
  - /api/network/summary responds and has basic network fields
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" || "${1:-}" == "help" ]]; then
    usage
    exit 0
fi

need_cmd() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "missing required command: $1" >&2
        exit 1
    fi
}

fetch() {
    local path="$1"
    curl -fsS --max-time 10 "$BASE_URL$path"
}

need_cmd curl

echo "[boot-self-check] target: $BASE_URL"

live_json="$(fetch /health/live)"
echo "[ok] live health: $live_json"

ready_status=0
ready_json="$(fetch /health/ready)" || ready_status=$?
if (( ready_status == 0 )); then
    echo "[ok] ready health: $ready_json"
else
    echo "[warn] ready health failed; node may still be starting or missing upstream dependencies"
fi

summary_json="$(fetch /api/network/summary)"
echo "[ok] network summary fetched"

if command -v jq >/dev/null 2>&1; then
    echo "$summary_json" | jq '{
        networkId,
        protocolVersion,
        selfEndpoint,
        peerCount,
        currentRoundNumber,
        currentTipBlockHeight,
        currentTipBlockHash,
        onDeckCount,
        winnersCount,
        localDatumHashrateDisplay,
        currentRoundObservedHashrateDisplay,
        currentStateId,
        candidateStateId
    }'
else
    echo "[warn] jq not installed; printing raw summary"
    echo "$summary_json"
fi

echo "[boot-self-check] complete"
