#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAIN_URL="${BOOT_G2_MAIN_URL:-http://127.0.0.1:5000}"
PEER_URL="${BOOT_G2_PEER_URL:-http://100.96.249.123:5000}"
INTERVAL_SECONDS="${BOOT_G2_INTERVAL_SECONDS:-5}"
LOG_DIR="${BOOT_G2_LOG_DIR:-$ROOT_DIR/logs}"

usage() {
    cat <<EOF
Usage: $(basename "$0") <duration> [out.json]

Examples:
  $(basename "$0") 2h
  $(basename "$0") 12h logs/g2-monitor-overnight.json
  BOOT_G2_PEER_URL= $(basename "$0") 30m

Environment:
  BOOT_G2_MAIN_URL           Main node URL (default: ${MAIN_URL})
  BOOT_G2_PEER_URL           Peer node URL; empty disables peer comparison (default: ${PEER_URL})
  BOOT_G2_INTERVAL_SECONDS   Poll interval (default: ${INTERVAL_SECONDS})
  BOOT_G2_LOG_DIR            Output directory (default: ${LOG_DIR})
EOF
}

parse_duration_seconds() {
    local raw="$1"
    case "$raw" in
        *s) echo "${raw%s}" ;;
        *m) echo "$(( ${raw%m} * 60 ))" ;;
        *h) echo "$(( ${raw%h} * 60 * 60 ))" ;;
        *d) echo "$(( ${raw%d} * 24 * 60 * 60 ))" ;;
        ''|*[!0-9]*)
            echo "Invalid duration: $raw" >&2
            exit 1
            ;;
        *) echo "$raw" ;;
    esac
}

choose_window() {
    local seconds="$1"
    if (( seconds <= 3600 )); then
        echo "1h"
    elif (( seconds <= 43200 )); then
        echo "12h"
    elif (( seconds <= 86400 )); then
        echo "24h"
    else
        echo "7d"
    fi
}

case "${1:-}" in
    ""|-h|--help|help)
        usage
        exit 0
        ;;
esac

DURATION_SECONDS="$(parse_duration_seconds "$1")"
mkdir -p "$LOG_DIR"

timestamp="$(date -u +%Y%m%d-%H%M%S)"
out_path="${2:-$LOG_DIR/g2-monitor-$timestamp.json}"
summary_path="${out_path%.json}-summary.json"
start_timestamp="$(date +%s)"
summary_ran=0

monitor_cmd=(
    node "$ROOT_DIR/scripts/boot-g2-monitor.mjs"
    --main-url "$MAIN_URL"
    --duration-seconds "$DURATION_SECONDS"
    --interval-seconds "$INTERVAL_SECONDS"
    --out "$out_path"
)
summary_cmd=(
    node "$ROOT_DIR/scripts/boot-soak-report.mjs"
)

if [[ -n "$PEER_URL" ]]; then
    monitor_cmd+=(--peer-url "$PEER_URL")
fi

build_summary_cmd() {
    local elapsed_seconds="$1"
    local window
    window="$(choose_window "$elapsed_seconds")"

    summary_cmd=(
        node "$ROOT_DIR/scripts/boot-soak-report.mjs"
        --main-url "$MAIN_URL"
        --window "$window"
        --limit 5000
        --out "$summary_path"
    )

    if [[ -n "$PEER_URL" ]]; then
        summary_cmd+=(--peer-url "$PEER_URL")
    fi
}

run_summary() {
    if (( summary_ran )); then
        return
    fi

    summary_ran=1
    local end_timestamp elapsed_seconds
    end_timestamp="$(date +%s)"
    elapsed_seconds="$(( end_timestamp - start_timestamp ))"
    if (( elapsed_seconds < 1 )); then
        elapsed_seconds=1
    fi

    build_summary_cmd "$elapsed_seconds"
    echo "[boot-g2-soak] summary window: $(choose_window "$elapsed_seconds") (elapsed=${elapsed_seconds}s)"
    "${summary_cmd[@]}" || echo "[boot-g2-soak] summary generation failed"
}

trap run_summary EXIT

echo "[boot-g2-soak] main=$MAIN_URL peer=${PEER_URL:-none} duration=${DURATION_SECONDS}s interval=${INTERVAL_SECONDS}s"
echo "[boot-g2-soak] monitor output: $out_path"
echo "[boot-g2-soak] summary output: $summary_path"
echo "[boot-g2-soak] expected finish: $(date -d "+${DURATION_SECONDS} seconds" '+%Y-%m-%d %H:%M:%S %Z')"

"${monitor_cmd[@]}"
summary_ran=1
build_summary_cmd "$DURATION_SECONDS"
"${summary_cmd[@]}"
trap - EXIT
