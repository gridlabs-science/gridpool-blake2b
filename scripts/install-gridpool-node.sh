#!/usr/bin/env bash
set -euo pipefail

GRID_HOME="${GRID_HOME:-/opt/grid-pool}"
GRID_BOOT_IMAGE="${GRID_BOOT_IMAGE:-ghcr.io/gridlabs-science/gridpool-blake2b:sha-01255d6}"
GRID_FOUNDATION_PAYOUT_ADDRESS="${GRID_FOUNDATION_PAYOUT_ADDRESS:-bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3}"
GRID_TESTNET_PAYOUT_ADDRESS="${GRID_TESTNET_PAYOUT_ADDRESS:-mxt9bYPtfBdzoTeZcHr23QgL4Un45PVvF5}"
GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-}"
BITCOIN_NETWORK="${BITCOIN_NETWORK:-mainnet}"
BITCOIN_NOTIFICATION_MODE="${BITCOIN_NOTIFICATION_MODE:-attached-node}"
BITCOIN_TOPOLOGY="${BITCOIN_TOPOLOGY:-host-network}"
BITCOIN_HOST="${BITCOIN_HOST:-127.0.0.1}"
BITCOIN_DOCKER_NETWORK="${BITCOIN_DOCKER_NETWORK:-}"
BITCOIN_RPC_URL="${BITCOIN_RPC_URL:-}"
BITCOIN_RPC_USERNAME="${BITCOIN_RPC_USERNAME:-}"
BITCOIN_RPC_PASSWORD_FILE="${BITCOIN_RPC_PASSWORD_FILE:-}"
BITCOIN_RPC_COOKIE_FILE="${BITCOIN_RPC_COOKIE_FILE:-}"
BITCOIN_RPC_POLL_INTERVAL_SECONDS="${BITCOIN_RPC_POLL_INTERVAL_SECONDS:-5}"
BITCOIN_RPC_TIMEOUT_SECONDS="${BITCOIN_RPC_TIMEOUT_SECONDS:-3}"
BITCOIN_RPC_LAG_GRACE_SECONDS="${BITCOIN_RPC_LAG_GRACE_SECONDS:-5}"
BITCOIN_ZMQ_ENDPOINT="${BITCOIN_ZMQ_ENDPOINT:-auto}"
BITCOIN_ZMQ_RAWBLOCK_ENDPOINT="${BITCOIN_ZMQ_RAWBLOCK_ENDPOINT:-auto}"
BOOT_WEB_PORT="${BOOT_WEB_PORT:-5000}"
BOOT_DATUM_PORT="${BOOT_DATUM_PORT:-3008}"
BOOT_DATUM_PUBLIC_PORT_WAS_SET=0
if [[ -n "${BOOT_DATUM_PUBLIC_PORT+x}" ]]; then
    BOOT_DATUM_PUBLIC_PORT_WAS_SET=1
fi
BOOT_DATUM_PUBLIC_HOST="${BOOT_DATUM_PUBLIC_HOST:-}"
BOOT_DATUM_PUBLIC_PORT="${BOOT_DATUM_PUBLIC_PORT:-}"
BOOT_PUBLIC_BASE_URL="${BOOT_PUBLIC_BASE_URL:-}"
GRID_BOOT_BOOTSTRAP_PEERS="${GRID_BOOT_BOOTSTRAP_PEERS:-}"
GRID_BOOT_NETWORK_ID="${GRID_BOOT_NETWORK_ID:-}"
GRID_BOOT_STATE_FILE="${GRID_BOOT_STATE_FILE:-}"
GRID_POOL_COINBASE_TAG="${GRID_POOL_COINBASE_TAG:-Grid Pool}"
CONFIGURE_UFW="${CONFIGURE_UFW:-0}"
ASSUME_YES=0
DRY_RUN=0
OPEN_BROWSER=1

usage() {
    cat <<EOF
Usage: $(basename "$0") [options]

Install a GridPool node only. This is for miners who already have Bitcoin and
DATUM running, and only need the GridPool node that DATUM connects to.

Options:
  --payout-address ADDRESS      Slot-0 fallback payout address for this node
  --network mainnet|testnet4    Bitcoin network (default: $BITCOIN_NETWORK)
  --bitcoin-mode MODE           attached-node or external-fallback
  --bitcoin-topology TOPOLOGY   host-network, shared-bridge, host-gateway, remote, or node-less
  --bitcoin-host HOST           Bitcoin host/service name used by auto endpoints
  --bitcoin-docker-network NAME External Docker network for shared-bridge mode
  --bitcoin-rpc-url URL         Authenticated Bitcoin JSON-RPC endpoint
  --bitcoin-rpc-username USER   Bitcoin RPC username
  --bitcoin-rpc-password-file PATH  File containing the Bitcoin RPC password
  --bitcoin-rpc-cookie-file PATH    Bitcoin cookie file (host-network/native only)
  --bitcoin-zmq ENDPOINT        Bitcoin ZMQ endpoint, such as tcp://127.0.0.1:28332
  --bitcoin-zmq-rawblock ENDPOINT  Bitcoin rawblock ZMQ endpoint
  --external-fallback           Explicit node-less Mempool.Space mode
  --web-port PORT               Local WebUI port (default: $BOOT_WEB_PORT)
  --datum-port PORT             Local DATUM listener port (default: $BOOT_DATUM_PORT)
  --datum-public-host HOST      Advertised DATUM host (default: detected LAN IP)
  --datum-public-port PORT      Advertised DATUM port (default: --datum-port)
  --public-base-url URL         Advertised WebUI URL (default: http://LAN_IP:web-port)
  --bootstrap-peers URLS        Comma-separated bootstrap peers
  --home DIR                    Install directory (default: $GRID_HOME)
  --image IMAGE                 GridPool Docker image (default: $GRID_BOOT_IMAGE)
  --configure-ufw               Open WebUI, DATUM, and UDP relay ports if UFW is active
  --no-open-browser             Do not try to launch a local browser
  --yes                         Do not ask confirmation prompts
  --dry-run                     Print actions without changing the system
  -h, --help                    Show this help

Environment overrides mirror the option names:
  GRID_HOME, GRID_BOOT_IMAGE, GRID_POOL_PAYOUT_ADDRESS, BITCOIN_NETWORK,
  BITCOIN_NOTIFICATION_MODE, BITCOIN_TOPOLOGY, BITCOIN_HOST, BITCOIN_DOCKER_NETWORK,
  BITCOIN_RPC_URL, BITCOIN_RPC_USERNAME, BITCOIN_RPC_PASSWORD_FILE,
  BITCOIN_RPC_COOKIE_FILE, BITCOIN_ZMQ_ENDPOINT, BITCOIN_ZMQ_RAWBLOCK_ENDPOINT,
  BOOT_WEB_PORT, BOOT_DATUM_PORT,
  BOOT_DATUM_PUBLIC_HOST, BOOT_DATUM_PUBLIC_PORT, BOOT_PUBLIC_BASE_URL,
  GRID_BOOT_BOOTSTRAP_PEERS, GRID_BOOT_NETWORK_ID, GRID_BOOT_STATE_FILE
EOF
}

log() {
    printf '[gridpool-node] %s\n' "$*"
}

warn() {
    printf '[gridpool-node][warn] %s\n' "$*" >&2
}

fail() {
    printf '[gridpool-node][error] %s\n' "$*" >&2
    exit 1
}

run() {
    if (( DRY_RUN )); then
        printf '[gridpool-node][dry-run]'
        printf ' %q' "$@"
        printf '\n'
        return 0
    fi
    "$@"
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --payout-address)
                [[ $# -ge 2 ]] || fail "--payout-address requires a value"
                GRID_POOL_PAYOUT_ADDRESS="$2"
                shift 2
                ;;
            --network)
                [[ $# -ge 2 ]] || fail "--network requires a value"
                BITCOIN_NETWORK="$2"
                shift 2
                ;;
            --bitcoin-mode)
                [[ $# -ge 2 ]] || fail "--bitcoin-mode requires a value"
                BITCOIN_NOTIFICATION_MODE="$2"
                shift 2
                ;;
            --bitcoin-topology)
                [[ $# -ge 2 ]] || fail "--bitcoin-topology requires a value"
                BITCOIN_TOPOLOGY="$2"
                shift 2
                ;;
            --bitcoin-host)
                [[ $# -ge 2 ]] || fail "--bitcoin-host requires a value"
                BITCOIN_HOST="$2"
                shift 2
                ;;
            --bitcoin-docker-network)
                [[ $# -ge 2 ]] || fail "--bitcoin-docker-network requires a value"
                BITCOIN_DOCKER_NETWORK="$2"
                shift 2
                ;;
            --bitcoin-rpc-url)
                [[ $# -ge 2 ]] || fail "--bitcoin-rpc-url requires a value"
                BITCOIN_RPC_URL="$2"
                shift 2
                ;;
            --bitcoin-rpc-username)
                [[ $# -ge 2 ]] || fail "--bitcoin-rpc-username requires a value"
                BITCOIN_RPC_USERNAME="$2"
                shift 2
                ;;
            --bitcoin-rpc-password-file)
                [[ $# -ge 2 ]] || fail "--bitcoin-rpc-password-file requires a value"
                BITCOIN_RPC_PASSWORD_FILE="$2"
                shift 2
                ;;
            --bitcoin-rpc-cookie-file)
                [[ $# -ge 2 ]] || fail "--bitcoin-rpc-cookie-file requires a value"
                BITCOIN_RPC_COOKIE_FILE="$2"
                shift 2
                ;;
            --bitcoin-zmq)
                [[ $# -ge 2 ]] || fail "--bitcoin-zmq requires a value"
                BITCOIN_ZMQ_ENDPOINT="$2"
                shift 2
                ;;
            --bitcoin-zmq-rawblock)
                [[ $# -ge 2 ]] || fail "--bitcoin-zmq-rawblock requires a value"
                BITCOIN_ZMQ_RAWBLOCK_ENDPOINT="$2"
                shift 2
                ;;
            --no-bitcoin-zmq|--external-fallback)
                BITCOIN_NOTIFICATION_MODE="external-fallback"
                BITCOIN_TOPOLOGY="node-less"
                BITCOIN_ZMQ_ENDPOINT=""
                BITCOIN_ZMQ_RAWBLOCK_ENDPOINT=""
                shift
                ;;
            --web-port)
                [[ $# -ge 2 ]] || fail "--web-port requires a value"
                BOOT_WEB_PORT="$2"
                shift 2
                ;;
            --datum-port)
                [[ $# -ge 2 ]] || fail "--datum-port requires a value"
                BOOT_DATUM_PORT="$2"
                if (( ! BOOT_DATUM_PUBLIC_PORT_WAS_SET )); then
                    BOOT_DATUM_PUBLIC_PORT="$BOOT_DATUM_PORT"
                fi
                shift 2
                ;;
            --datum-public-host)
                [[ $# -ge 2 ]] || fail "--datum-public-host requires a value"
                BOOT_DATUM_PUBLIC_HOST="$2"
                shift 2
                ;;
            --datum-public-port)
                [[ $# -ge 2 ]] || fail "--datum-public-port requires a value"
                BOOT_DATUM_PUBLIC_PORT="$2"
                BOOT_DATUM_PUBLIC_PORT_WAS_SET=1
                shift 2
                ;;
            --public-base-url)
                [[ $# -ge 2 ]] || fail "--public-base-url requires a value"
                BOOT_PUBLIC_BASE_URL="$2"
                shift 2
                ;;
            --bootstrap-peers)
                [[ $# -ge 2 ]] || fail "--bootstrap-peers requires a value"
                GRID_BOOT_BOOTSTRAP_PEERS="$2"
                shift 2
                ;;
            --home)
                [[ $# -ge 2 ]] || fail "--home requires a value"
                GRID_HOME="$2"
                shift 2
                ;;
            --image)
                [[ $# -ge 2 ]] || fail "--image requires a value"
                GRID_BOOT_IMAGE="$2"
                shift 2
                ;;
            --configure-ufw)
                CONFIGURE_UFW=1
                shift
                ;;
            --no-open-browser)
                OPEN_BROWSER=0
                shift
                ;;
            --yes|--noninteractive)
                ASSUME_YES=1
                shift
                ;;
            --dry-run)
                DRY_RUN=1
                shift
                ;;
            -h|--help)
                usage
                exit 0
                ;;
            *)
                fail "unknown option: $1"
                ;;
        esac
    done
}

require_root() {
    (( DRY_RUN )) && return 0
    if [[ "$(id -u)" -ne 0 ]]; then
        fail "run with sudo/root, for example: curl -fsSL ... | sudo bash -s -- --payout-address ADDRESS"
    fi
}

primary_ipv4() {
    ip route get 1.1.1.1 2>/dev/null | awk '{for (i=1; i<=NF; i++) if ($i=="src") {print $(i+1); exit}}'
}

valid_port() {
    [[ "$1" =~ ^[0-9]+$ ]] && (( "$1" >= 1 && "$1" <= 65535 ))
}

normalize_network_defaults() {
    BITCOIN_NETWORK="${BITCOIN_NETWORK,,}"
    case "$BITCOIN_NETWORK" in
        mainnet|bitcoin)
            BITCOIN_NETWORK="mainnet"
            GRID_BOOT_NETWORK_ID="${GRID_BOOT_NETWORK_ID:-gridpool-blake2b-mainnet-v1}"
            GRID_BOOT_BOOTSTRAP_PEERS="${GRID_BOOT_BOOTSTRAP_PEERS:-https://blake.gridpool.net}"
            GRID_BOOT_STATE_FILE="${GRID_BOOT_STATE_FILE:-pool_state.json}"
            GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-$GRID_FOUNDATION_PAYOUT_ADDRESS}"
            ;;
        testnet4)
            GRID_BOOT_NETWORK_ID="${GRID_BOOT_NETWORK_ID:-gridpool-blake2b-testnet4-v1}"
            GRID_BOOT_BOOTSTRAP_PEERS="${GRID_BOOT_BOOTSTRAP_PEERS:-https://testnet4.blake.gridpool.net}"
            GRID_BOOT_STATE_FILE="${GRID_BOOT_STATE_FILE:-pool_state.testnet4.json}"
            GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-$GRID_TESTNET_PAYOUT_ADDRESS}"
            ;;
        *)
            fail "--network must be mainnet or testnet4"
            ;;
    esac
}

normalize_bitcoin_connectivity() {
    BITCOIN_NOTIFICATION_MODE="${BITCOIN_NOTIFICATION_MODE,,}"
    BITCOIN_TOPOLOGY="${BITCOIN_TOPOLOGY,,}"

    case "$BITCOIN_TOPOLOGY" in
        native|host-network)
            BITCOIN_HOST="${BITCOIN_HOST:-127.0.0.1}"
            ;;
        shared-bridge)
            [[ -n "$BITCOIN_DOCKER_NETWORK" ]] ||
                fail "--bitcoin-docker-network is required for shared-bridge topology"
            [[ "$BITCOIN_HOST" != "127.0.0.1" ]] ||
                fail "--bitcoin-host must be the Bitcoin service DNS name for shared-bridge topology"
            ;;
        host-gateway)
            BITCOIN_HOST="${BITCOIN_HOST:-host.docker.internal}"
            [[ "$BITCOIN_HOST" != "127.0.0.1" ]] || BITCOIN_HOST="host.docker.internal"
            ;;
        remote)
            [[ "$BITCOIN_HOST" != "127.0.0.1" ]] ||
                fail "--bitcoin-host must be the stable LAN hostname/IP for remote topology"
            ;;
        node-less)
            BITCOIN_NOTIFICATION_MODE="external-fallback"
            ;;
        *)
            fail "--bitcoin-topology must be host-network, shared-bridge, host-gateway, remote, or node-less"
            ;;
    esac

    case "$BITCOIN_NOTIFICATION_MODE" in
        attached-node)
            BITCOIN_RPC_URL="${BITCOIN_RPC_URL:-http://${BITCOIN_HOST}:8332}"
            [[ "$BITCOIN_ZMQ_ENDPOINT" != "auto" ]] ||
                BITCOIN_ZMQ_ENDPOINT="tcp://${BITCOIN_HOST}:28332"
            [[ "$BITCOIN_ZMQ_RAWBLOCK_ENDPOINT" != "auto" ]] ||
                BITCOIN_ZMQ_RAWBLOCK_ENDPOINT="tcp://${BITCOIN_HOST}:28333"
            [[ -n "$BITCOIN_RPC_COOKIE_FILE" ||
               (-n "$BITCOIN_RPC_USERNAME" && -n "$BITCOIN_RPC_PASSWORD_FILE") ]] ||
                fail "attached-node mode requires --bitcoin-rpc-cookie-file or both --bitcoin-rpc-username and --bitcoin-rpc-password-file"
            ;;
        external-fallback)
            BITCOIN_TOPOLOGY="node-less"
            BITCOIN_RPC_URL=""
            BITCOIN_ZMQ_ENDPOINT=""
            BITCOIN_ZMQ_RAWBLOCK_ENDPOINT=""
            ;;
        *)
            fail "--bitcoin-mode must be attached-node or external-fallback"
            ;;
    esac
}

ensure_docker() {
    if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
        return
    fi

    if ! command -v apt-get >/dev/null 2>&1; then
        fail "Docker with the Compose plugin is required. Install Docker first, then rerun this script."
    fi

    log "installing Docker packages from the OS repository"
    run apt-get update
    run apt-get install -y docker.io docker-compose-plugin ca-certificates curl
    run systemctl enable --now docker

    if ! docker compose version >/dev/null 2>&1; then
        fail "docker compose is still unavailable after package install"
    fi
}

ensure_python() {
    if command -v python3 >/dev/null 2>&1; then
        return
    fi

    if ! command -v apt-get >/dev/null 2>&1; then
        fail "python3 is required to write JSON config safely"
    fi

    log "installing python3 from the OS repository"
    run apt-get update
    run apt-get install -y python3
}

bootstrap_peers_json() {
    python3 - "$GRID_BOOT_BOOTSTRAP_PEERS" <<'PY'
import json
import sys
print(json.dumps([item.strip() for item in sys.argv[1].split(",") if item.strip()]))
PY
}

write_json_config() {
    local config_path="$1"
    local peers_json="$2"
    local notification_source="MempoolSpace"
    local zmq_endpoint="$BITCOIN_ZMQ_ENDPOINT"
    local rpc_password=""
    local rpc_cookie_path="$BITCOIN_RPC_COOKIE_FILE"
    if [[ "$BITCOIN_NOTIFICATION_MODE" == "attached-node" ]]; then
        notification_source="BitcoinZmq"
        if [[ -n "$BITCOIN_RPC_PASSWORD_FILE" ]]; then
            rpc_password="$(tr -d '\r\n' <"$BITCOIN_RPC_PASSWORD_FILE")"
        fi
        if [[ -n "$BITCOIN_RPC_COOKIE_FILE" ]]; then
            rpc_cookie_path="/run/gridpool/bitcoin.cookie"
        fi
    fi

    python3 - "$config_path" <<PY
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
data = {}
if path.exists():
    try:
        data = json.loads(path.read_text())
    except json.JSONDecodeError:
        data = {}

data.update({
    "NotificationSource": ${notification_source@Q},
    "bitcoin_notification_mode": ${BITCOIN_NOTIFICATION_MODE@Q},
    "bitcoin_rpc_url": ${BITCOIN_RPC_URL@Q},
    "bitcoin_rpc_username": ${BITCOIN_RPC_USERNAME@Q},
    "bitcoin_rpc_password": ${rpc_password@Q},
    "bitcoin_rpc_cookie_file": ${rpc_cookie_path@Q},
    "bitcoin_rpc_poll_interval_seconds": int(${BITCOIN_RPC_POLL_INTERVAL_SECONDS@Q}),
    "bitcoin_rpc_timeout_seconds": int(${BITCOIN_RPC_TIMEOUT_SECONDS@Q}),
    "bitcoin_rpc_lag_grace_seconds": int(${BITCOIN_RPC_LAG_GRACE_SECONDS@Q}),
    "WebUI_Port_http": int(${BOOT_WEB_PORT@Q}),
    "WebUI_Port_https": 0,
    "Datum_Port": int(${BOOT_DATUM_PORT@Q}),
    "public_base_url": ${BOOT_PUBLIC_BASE_URL@Q},
    "datum_public_host": ${BOOT_DATUM_PUBLIC_HOST@Q},
    "datum_public_port": int(${BOOT_DATUM_PUBLIC_PORT@Q}),
    "node_mode": "sovereign",
    "bitcoin_network": ${BITCOIN_NETWORK@Q},
    "chain_profile_id": "knots-blake2b-mainnet-rc4-activated" if ${BITCOIN_NETWORK@Q} == "mainnet" else "knots-rc3-afbe91c-testnet4-v1",
    "boot_network_id": ${GRID_BOOT_NETWORK_ID@Q},
    "boot_protocol_version": 23,
    "enable_peer_sync": True,
    "bootstrap_peers": json.loads(${peers_json@Q}),
    "enable_admin_api": False,
    "enable_peer_persistent_sessions": True,
    "enable_peer_udp_fast_relay": False,
    "peer_udp_bind_port": 5101 if ${BITCOIN_NETWORK@Q} == "mainnet" else 5102,
    "peer_udp_port": 5101 if ${BITCOIN_NETWORK@Q} == "mainnet" else 5102,
    "peer_udp_public_host": "",
    "peer_udp_max_datagram_bytes": 1200,
    "enable_pulse_proofs": False,
    "enable_optimistic_share_relay": False,
    "pool_payout_script": ${GRID_POOL_PAYOUT_ADDRESS@Q},
    "winners_list_size": 299,
    "grid_labs_support_fee_enabled": False,
    "coinbase_tag": ${GRID_POOL_COINBASE_TAG@Q},
    "min_diff": 300,
    "bitcoin_zmq_endpoint": ${zmq_endpoint@Q},
    "bitcoin_zmq_rawblock_endpoint": ${BITCOIN_ZMQ_RAWBLOCK_ENDPOINT@Q},
})

path.write_text(json.dumps(data, indent=2) + "\\n")
PY
}

write_compose() {
    local compose_path="$1"
    local networking=""
    local cookie_mount=""
    case "$BITCOIN_TOPOLOGY" in
        native|host-network)
            networking="    network_mode: host"
            ;;
        shared-bridge)
            networking="    ports:
      - \"${BOOT_WEB_PORT}:${BOOT_WEB_PORT}\"
      - \"${BOOT_DATUM_PORT}:${BOOT_DATUM_PORT}\"
      - \"5001:5001/udp\"
    networks:
      - bitcoin"
            ;;
        host-gateway)
            networking="    ports:
      - \"${BOOT_WEB_PORT}:${BOOT_WEB_PORT}\"
      - \"${BOOT_DATUM_PORT}:${BOOT_DATUM_PORT}\"
      - \"5001:5001/udp\"
    extra_hosts:
      - \"host.docker.internal:host-gateway\""
            ;;
        remote|node-less)
            networking="    ports:
      - \"${BOOT_WEB_PORT}:${BOOT_WEB_PORT}\"
      - \"${BOOT_DATUM_PORT}:${BOOT_DATUM_PORT}\"
      - \"5001:5001/udp\""
            ;;
    esac
    if [[ -n "$BITCOIN_RPC_COOKIE_FILE" ]]; then
        cookie_mount="
      - ${BITCOIN_RPC_COOKIE_FILE}:/run/gridpool/bitcoin.cookie:ro"
    fi
    cat >"$compose_path" <<EOF
services:
  boot-portal:
    image: ${GRID_BOOT_IMAGE}
    container_name: boot-portal
    restart: unless-stopped
${networking}
    environment:
      BOOT_PORTAL_CONFIG_PATH: /data/boot_portal_config.json
      BOOT_PORTAL_LOCAL_CONFIG_PATH: /data/boot_portal_config.local.json
      BOOT_PORTAL_STATE_PATH: /data/${GRID_BOOT_STATE_FILE}
    volumes:
      - ./data:/data${cookie_mount}
$(if [[ "$BITCOIN_TOPOLOGY" == "shared-bridge" ]]; then cat <<NETWORKS
networks:
  bitcoin:
    external: true
    name: ${BITCOIN_DOCKER_NETWORK}
NETWORKS
fi)
EOF
}

preflight_bitcoin() {
    [[ "$BITCOIN_NOTIFICATION_MODE" == "attached-node" ]] || return 0
    (( DRY_RUN )) && {
        log "would run attached-node RPC/ZMQ connectivity preflight"
        return 0
    }
    if [[ "$BITCOIN_TOPOLOGY" == "shared-bridge" || "$BITCOIN_TOPOLOGY" == "host-gateway" ]]; then
        log "container-scoped Bitcoin connectivity will be verified after GridPool starts"
        return 0
    fi
    local helper
    helper="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/check-bitcoin-connectivity.sh"
    [[ -x "$helper" ]] ||
        fail "missing $helper; clone the repository and run the installer from scripts/ so attached-node preflight can run"

    local args=(
        --mode attached-node \
        --rpc-url "$BITCOIN_RPC_URL" \
        --zmq-hashblock "$BITCOIN_ZMQ_ENDPOINT" \
        --zmq-rawblock "$BITCOIN_ZMQ_RAWBLOCK_ENDPOINT" \
        --timeout-seconds "$BITCOIN_RPC_TIMEOUT_SECONDS"
    )
    [[ -n "$BITCOIN_RPC_USERNAME" ]] &&
        args+=(--rpc-username "$BITCOIN_RPC_USERNAME")
    [[ -n "$BITCOIN_RPC_PASSWORD_FILE" ]] &&
        args+=(--rpc-password-file "$BITCOIN_RPC_PASSWORD_FILE")
    [[ -n "$BITCOIN_RPC_COOKIE_FILE" ]] &&
        args+=(--rpc-cookie-file "$BITCOIN_RPC_COOKIE_FILE")
    "$helper" "${args[@]}"
}

configure_ufw() {
    (( CONFIGURE_UFW )) || return 0
    if ! command -v ufw >/dev/null 2>&1; then
        warn "ufw is not installed; skipping firewall updates"
        return 0
    fi
    if ! ufw status | grep -qi "Status: active"; then
        warn "ufw is not active; skipping firewall updates"
        return 0
    fi
    run ufw allow "${BOOT_WEB_PORT}/tcp"
    run ufw allow "${BOOT_DATUM_PORT}/tcp"
    run ufw allow "5001/udp"
}

wait_for_http() {
    local url="$1"
    local deadline=$((SECONDS + 120))
    until curl -fsS --max-time 5 "$url" >/dev/null 2>&1; do
        if (( SECONDS >= deadline )); then
            docker compose -f "$GRID_HOME/boot-node/docker-compose.yml" logs --tail 120 boot-portal >&2 || true
            fail "GridPool WebUI did not become healthy at $url"
        fi
        sleep 2
    done
}

wait_for_readiness() {
    local url="$1"
    local deadline=$((SECONDS + 60))
    until curl -fsS --max-time 5 "$url" >/dev/null 2>&1; do
        if (( SECONDS >= deadline )); then
            curl -sS --max-time 5 "http://127.0.0.1:${BOOT_WEB_PORT}/api/network/summary" \
                | jq '{bitcoinNotification, configWarnings}' >&2 || true
            fail "GridPool started, but attached Bitcoin readiness failed. Correct the reported RPC/ZMQ topology before mining."
        fi
        sleep 2
    done
}

read_pubkey() {
    docker logs boot-portal 2>&1 \
        | sed -n 's/.*Server Public Key (Hex): //p' \
        | tail -n 1
}

maybe_open_browser() {
    (( OPEN_BROWSER )) || return 0
    [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]] || return 0

    local user="${SUDO_USER:-}"
    if [[ -n "$user" && "$user" != "root" ]] && command -v runuser >/dev/null 2>&1; then
        runuser -u "$user" -- xdg-open "$BOOT_PUBLIC_BASE_URL" >/dev/null 2>&1 || true
    elif command -v xdg-open >/dev/null 2>&1; then
        xdg-open "$BOOT_PUBLIC_BASE_URL" >/dev/null 2>&1 || true
    fi
}

confirm() {
    (( ASSUME_YES || DRY_RUN )) && return 0
    cat <<EOF

GridPool node-only install
  Install dir:       ${GRID_HOME}/boot-node
  Docker image:      ${GRID_BOOT_IMAGE}
  Web UI:            ${BOOT_PUBLIC_BASE_URL}
  DATUM host:        ${BOOT_DATUM_PUBLIC_HOST}
  DATUM port:        ${BOOT_DATUM_PUBLIC_PORT}
  Bitcoin network:   ${BITCOIN_NETWORK}
  Bitcoin mode:     ${BITCOIN_NOTIFICATION_MODE}
  Bitcoin topology: ${BITCOIN_TOPOLOGY}
  RPC endpoint:     ${BITCOIN_RPC_URL:-not used}
  Block notify:     ${BITCOIN_ZMQ_ENDPOINT:-MempoolSpace}
  Bootstrap peers:   ${GRID_BOOT_BOOTSTRAP_PEERS:-none}
  Payout fallback:   ${GRID_POOL_PAYOUT_ADDRESS}

EOF
    read -r -p "Continue? [y/N] " answer
    [[ "$answer" =~ ^[Yy]$ ]] || fail "aborted"
}

main() {
    parse_args "$@"
    require_root
    normalize_network_defaults

    local detected_ip
    detected_ip="$(primary_ipv4 || true)"
    detected_ip="${detected_ip:-127.0.0.1}"
    BOOT_DATUM_PUBLIC_HOST="${BOOT_DATUM_PUBLIC_HOST:-$detected_ip}"
    BOOT_DATUM_PUBLIC_PORT="${BOOT_DATUM_PUBLIC_PORT:-$BOOT_DATUM_PORT}"
    BOOT_PUBLIC_BASE_URL="${BOOT_PUBLIC_BASE_URL:-http://${detected_ip}:${BOOT_WEB_PORT}}"

    valid_port "$BOOT_WEB_PORT" || fail "--web-port must be between 1 and 65535"
    valid_port "$BOOT_DATUM_PORT" || fail "--datum-port must be between 1 and 65535"
    valid_port "$BOOT_DATUM_PUBLIC_PORT" || fail "--datum-public-port must be between 1 and 65535"

    normalize_bitcoin_connectivity
    confirm
    ensure_docker
    ensure_python
    preflight_bitcoin
    configure_ufw

    local boot_dir="$GRID_HOME/boot-node"
    local data_dir="$boot_dir/data"
    local config_path="$data_dir/boot_portal_config.local.json"
    local compose_path="$boot_dir/docker-compose.yml"
    local peers_json
    peers_json="$(bootstrap_peers_json)"

    run mkdir -p "$data_dir"
    if (( ! DRY_RUN )); then
        write_json_config "$config_path" "$peers_json"
        write_compose "$compose_path"
        chown -R 1000:1000 "$data_dir"
        chmod 0750 "$data_dir"
        chmod 0600 "$config_path"
    else
        log "would write $config_path"
        log "would write $compose_path"
    fi

    log "pulling and starting ${GRID_BOOT_IMAGE}"
    run docker compose -f "$compose_path" --project-directory "$boot_dir" pull boot-portal
    run docker compose -f "$compose_path" --project-directory "$boot_dir" up -d

    if (( DRY_RUN )); then
        cat <<EOF

Dry run complete.

Expected local UI:
  ${BOOT_PUBLIC_BASE_URL}

Expected DATUM settings:
  Pool Host: ${BOOT_DATUM_PUBLIC_HOST}
  Pool Port: ${BOOT_DATUM_PUBLIC_PORT}
EOF
        return 0
    fi

    wait_for_http "http://127.0.0.1:${BOOT_WEB_PORT}/health/live"
    if [[ "$BITCOIN_NOTIFICATION_MODE" == "attached-node" ]]; then
        wait_for_readiness "http://127.0.0.1:${BOOT_WEB_PORT}/health/ready"
    fi

    local pubkey
    pubkey="$(read_pubkey || true)"
    maybe_open_browser

    cat <<EOF

GridPool node install complete.

Open the local UI:
  ${BOOT_PUBLIC_BASE_URL}

Configure DATUM with:
  Pool Host:   ${BOOT_DATUM_PUBLIC_HOST}
  Pool Port:   ${BOOT_DATUM_PUBLIC_PORT}
  Pool Pubkey: ${pubkey:-see the local UI Connect DATUM panel}

Then point ASICs at DATUM, not directly at GridPool.

Useful commands:
  cd ${boot_dir}
  sudo docker compose logs -f boot-portal
  sudo docker compose ps
EOF
}

main "$@"
