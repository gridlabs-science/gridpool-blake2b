#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="$(mktemp -d)"
HTTP_PORT="${BOOT_PORTAL_SMOKE_HTTP_PORT:-35100}"
DATUM_PORT="${BOOT_PORTAL_SMOKE_DATUM_PORT:-33100}"
CONFIG_PATH="$TMP_DIR/boot_portal_config.json"
LOG_PATH="$TMP_DIR/boot_portal.log"
SERVER_PID=""

cleanup() {
    if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" >/dev/null 2>&1; then
        kill "$SERVER_PID" >/dev/null 2>&1 || true
        wait "$SERVER_PID" >/dev/null 2>&1 || true
    fi
    rm -rf "$TMP_DIR"
}

trap cleanup EXIT

cat > "$CONFIG_PATH" <<JSON
{
  "NotificationSource": "MempoolSpace",
  "enable_peer_sync": false,
  "public_base_url": "http://127.0.0.1:${HTTP_PORT}",
  "datum_public_host": "127.0.0.1",
  "Datum_Port": ${DATUM_PORT},
  "WebUI_Port_http": ${HTTP_PORT},
  "WebUI_Port_https": 0,
  "bootstrap_peers": [],
  "boot_network_id": "smoke-test",
  "boot_protocol_version": 21,
  "enable_admin_api": false,
  "admin_api_key": "",
  "max_share_request_bytes": 1024,
  "max_coinbase_hex_chars": 200,
  "max_merkle_path_entries": 4,
  "testing_round_reset_mode": "none",
  "testing_round_reset_low_nibble_threshold": 0
}
JSON

BOOT_PORTAL_CONFIG_PATH="$CONFIG_PATH" \
BOOT_PORTAL_LOCAL_CONFIG_PATH= \
dotnet run --project "$ROOT_DIR/boot_portal/boot_portal.csproj" --no-build \
    >"$LOG_PATH" 2>&1 &
SERVER_PID="$!"

for _ in $(seq 1 60); do
    if curl -fsS "http://127.0.0.1:${HTTP_PORT}/api/network/summary" >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

if ! kill -0 "$SERVER_PID" >/dev/null 2>&1; then
    echo "GridPool server exited during startup."
    cat "$LOG_PATH"
    exit 1
fi

if ! curl -fsS "http://127.0.0.1:${HTTP_PORT}/api/network/summary" >/dev/null 2>&1; then
    echo "GridPool server did not become ready in time."
    cat "$LOG_PATH"
    exit 1
fi

expect_status() {
    local expected_status="$1"
    local url="$2"
    local payload="$3"
    local actual_status

    actual_status="$(curl -sS -o /dev/null -w '%{http_code}' \
        -H 'Content-Type: application/json' \
        -X POST \
        --data "$payload" \
        "$url")"

    if [[ "$actual_status" != "$expected_status" ]]; then
        echo "Expected HTTP ${expected_status} from ${url}, got ${actual_status}."
        echo "Payload: $payload"
        cat "$LOG_PATH"
        exit 1
    fi
}

valid_header="$(printf '0%.0s' $(seq 1 160))"
valid_coinbase="$(printf 'a%.0s' $(seq 1 200))"
valid_merkle="$(printf 'b%.0s' $(seq 1 64))"
oversized_coinbase="$(printf 'a%.0s' $(seq 1 3000))"

expect_status 400 \
    "http://127.0.0.1:${HTTP_PORT}/api/mining/share" \
    "{\"minerAddress\":\"\",\"headerHex\":\"\",\"coinbaseHex\":\"${valid_coinbase}\",\"merklePath\":[\"${valid_merkle}\"]}"

expect_status 400 \
    "http://127.0.0.1:${HTTP_PORT}/api/mining/share" \
    "{\"minerAddress\":\"\",\"headerHex\":\"${valid_header}\",\"coinbaseHex\":\"zz\",\"merklePath\":[\"${valid_merkle}\"]}"

expect_status 400 \
    "http://127.0.0.1:${HTTP_PORT}/api/mining/share" \
    "{\"minerAddress\":\"\",\"headerHex\":\"${valid_header}\",\"coinbaseHex\":\"${valid_coinbase}\",\"merklePath\":[\"bad\"]}"

expect_status 413 \
    "http://127.0.0.1:${HTTP_PORT}/api/mining/share" \
    "{\"minerAddress\":\"\",\"headerHex\":\"${valid_header}\",\"coinbaseHex\":\"${oversized_coinbase}\",\"merklePath\":[\"${valid_merkle}\"]}"

expect_status 400 \
    "http://127.0.0.1:${HTTP_PORT}/api/peer/share" \
    "{\"senderEndpoint\":\"https://peer.example\",\"protocolVersion\":1,\"networkId\":\"wrong-network\",\"share\":{\"shareId\":\"\",\"minerAddress\":\"\",\"username\":\"\",\"headerHex\":\"${valid_header}\",\"coinbaseHex\":\"${valid_coinbase}\",\"merklePath\":[\"${valid_merkle}\"],\"prevBlockHash\":\"${valid_merkle}\",\"source\":\"peer\"}}"

summary_time="$(curl -sS -o /dev/null -w '%{time_total}' "http://127.0.0.1:${HTTP_PORT}/api/network/summary")"
if ! awk "BEGIN { exit !($summary_time < 2.0) }"; then
    echo "Summary endpoint was too slow after malformed requests: ${summary_time}s"
    cat "$LOG_PATH"
    exit 1
fi

if ! kill -0 "$SERVER_PID" >/dev/null 2>&1; then
    echo "GridPool server exited after malformed requests."
    cat "$LOG_PATH"
    exit 1
fi

if grep -Eq "BackgroundService failed|Unhandled exception|UnhandledException|StopHost" "$LOG_PATH"; then
    echo "Detected fatal/unhandled error signatures in smoke-test log."
    cat "$LOG_PATH"
    exit 1
fi

echo "Malformed request smoke test passed."
