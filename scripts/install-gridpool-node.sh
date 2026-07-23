#!/usr/bin/env bash
set -euo pipefail

GRID_HOME="${GRID_HOME:-/opt/grid-pool}"
GRID_BOOT_IMAGE="${GRID_BOOT_IMAGE:-ghcr.io/gridlabs-science/boot-protocol:latest}"
GRID_FOUNDATION_PAYOUT_ADDRESS="${GRID_FOUNDATION_PAYOUT_ADDRESS:-bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3}"
GRID_TESTNET_PAYOUT_ADDRESS="${GRID_TESTNET_PAYOUT_ADDRESS:-mxt9bYPtfBdzoTeZcHr23QgL4Un45PVvF5}"
GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-}"
BITCOIN_NETWORK="${BITCOIN_NETWORK:-mainnet}"
BITCOIN_ZMQ_ENDPOINT="${BITCOIN_ZMQ_ENDPOINT:-auto}"
BITCOIN_ZMQ_RAWBLOCK_ENDPOINT="${BITCOIN_ZMQ_RAWBLOCK_ENDPOINT:-tcp://127.0.0.1:28333}"
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
  --bitcoin-zmq ENDPOINT        Bitcoin ZMQ endpoint, such as tcp://127.0.0.1:28332
  --bitcoin-zmq-rawblock ENDPOINT  Bitcoin rawblock ZMQ endpoint (default: tcp://127.0.0.1:28333)
  --no-bitcoin-zmq              Use MempoolSpace notifications instead of local ZMQ
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
  BITCOIN_ZMQ_ENDPOINT, BITCOIN_ZMQ_RAWBLOCK_ENDPOINT, BOOT_WEB_PORT, BOOT_DATUM_PORT,
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
            --no-bitcoin-zmq)
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
            GRID_BOOT_NETWORK_ID="${GRID_BOOT_NETWORK_ID:-mainnet-beta}"
            GRID_BOOT_BOOTSTRAP_PEERS="${GRID_BOOT_BOOTSTRAP_PEERS:-https://main.gridpool.net}"
            GRID_BOOT_STATE_FILE="${GRID_BOOT_STATE_FILE:-pool_state.json}"
            GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-$GRID_FOUNDATION_PAYOUT_ADDRESS}"
            ;;
        testnet4)
            GRID_BOOT_NETWORK_ID="${GRID_BOOT_NETWORK_ID:-testnet4-beta}"
            GRID_BOOT_BOOTSTRAP_PEERS="${GRID_BOOT_BOOTSTRAP_PEERS:-https://test.gridpool.net}"
            GRID_BOOT_STATE_FILE="${GRID_BOOT_STATE_FILE:-pool_state.testnet4.json}"
            GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-$GRID_TESTNET_PAYOUT_ADDRESS}"
            ;;
        *)
            fail "--network must be mainnet or testnet4"
            ;;
    esac
}

detect_bitcoin_zmq() {
    if [[ "$BITCOIN_ZMQ_ENDPOINT" != "auto" ]]; then
        return
    fi

    if timeout 1 bash -c '</dev/tcp/127.0.0.1/28332' >/dev/null 2>&1; then
        BITCOIN_ZMQ_ENDPOINT="tcp://127.0.0.1:28332"
    else
        BITCOIN_ZMQ_ENDPOINT=""
    fi
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
    if [[ -n "$BITCOIN_ZMQ_ENDPOINT" ]]; then
        notification_source="BitcoinZmq"
    else
        zmq_endpoint="tcp://127.0.0.1:28332"
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
    "WebUI_Port_http": int(${BOOT_WEB_PORT@Q}),
    "WebUI_Port_https": 0,
    "Datum_Port": int(${BOOT_DATUM_PORT@Q}),
    "public_base_url": ${BOOT_PUBLIC_BASE_URL@Q},
    "datum_public_host": ${BOOT_DATUM_PUBLIC_HOST@Q},
    "datum_public_port": int(${BOOT_DATUM_PUBLIC_PORT@Q}),
    "node_mode": "sovereign",
    "bitcoin_network": ${BITCOIN_NETWORK@Q},
    "boot_network_id": ${GRID_BOOT_NETWORK_ID@Q},
    "enable_peer_sync": True,
    "bootstrap_peers": json.loads(${peers_json@Q}),
    "enable_admin_api": False,
    "enable_peer_persistent_sessions": True,
    "enable_peer_udp_fast_relay": True,
    "peer_udp_bind_port": 5001,
    "peer_udp_port": 5001,
    "peer_udp_public_host": "",
    "peer_udp_max_datagram_bytes": 1200,
    "pool_payout_script": ${GRID_POOL_PAYOUT_ADDRESS@Q},
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
    cat >"$compose_path" <<EOF
services:
  boot-portal:
    image: ${GRID_BOOT_IMAGE}
    container_name: boot-portal
    restart: unless-stopped
    network_mode: host
    environment:
      BOOT_PORTAL_CONFIG_PATH: /data/boot_portal_config.json
      BOOT_PORTAL_LOCAL_CONFIG_PATH: /data/boot_portal_config.local.json
      BOOT_PORTAL_STATE_PATH: /data/${GRID_BOOT_STATE_FILE}
    volumes:
      - ./data:/data
EOF
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
  Block notify:      ${BITCOIN_ZMQ_ENDPOINT:-MempoolSpace}
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

    detect_bitcoin_zmq
    confirm
    ensure_docker
    ensure_python
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
