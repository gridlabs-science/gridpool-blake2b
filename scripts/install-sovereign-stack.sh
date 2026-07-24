#!/usr/bin/env bash
set -euo pipefail

SCRIPT_NAME="$(basename "$0")"

GRID_HOME="${GRID_HOME:-/opt/grid-pool}"
GRID_BOOT_REPO_URL="${GRID_BOOT_REPO_URL:-https://github.com/gridlabs-science/boot-protocol.git}"
GRID_BOOT_REPO_REF="${GRID_BOOT_REPO_REF:-main}"
GRID_BOOT_IMAGE="${GRID_BOOT_IMAGE:-ghcr.io/gridlabs-science/boot-protocol:latest}"
GRID_BOOT_LOCAL_BUILD="${GRID_BOOT_LOCAL_BUILD:-0}"
GRID_DATUM_REPO_URL="${GRID_DATUM_REPO_URL:-https://github.com/OCEAN-xyz/datum_gateway.git}"
GRID_DATUM_REPO_REF="${GRID_DATUM_REPO_REF:-master}"

GRID_POOL_PAYOUT_ADDRESS_WAS_SET="${GRID_POOL_PAYOUT_ADDRESS+x}"
GRID_BOOT_BOOTSTRAP_PEERS_WAS_SET="${GRID_BOOT_BOOTSTRAP_PEERS+x}"
GRID_BOOT_NETWORK_ID_WAS_SET="${GRID_BOOT_NETWORK_ID+x}"
GRID_FOUNDATION_PAYOUT_ADDRESS="${GRID_FOUNDATION_PAYOUT_ADDRESS:-bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3}"
GRID_POOL_PAYOUT_ADDRESS="${GRID_POOL_PAYOUT_ADDRESS:-$GRID_FOUNDATION_PAYOUT_ADDRESS}"
GRID_POOL_COINBASE_TAG="${GRID_POOL_COINBASE_TAG:-Grid Pool}"
GRID_BOOT_BOOTSTRAP_PEERS="${GRID_BOOT_BOOTSTRAP_PEERS:-https://main.gridpool.net}"
GRID_BOOT_NETWORK_ID="${GRID_BOOT_NETWORK_ID:-mainnet-beta}"
GRID_BOOT_NODE_MODE="${GRID_BOOT_NODE_MODE:-sovereign}"
BITCOIN_NETWORK="${BITCOIN_NETWORK:-mainnet}"
GRID_BOOT_STATE_FILE="${GRID_BOOT_STATE_FILE:-pool_state.json}"

BITCOIN_CORE_VERSION="${BITCOIN_CORE_VERSION:-31.0}"
BITCOIN_PRUNE_MB="${BITCOIN_PRUNE_MB:-1100}"
BITCOIN_DBCACHE_MB="${BITCOIN_DBCACHE_MB:-auto}"
BITCOIN_MAX_MEMPOOL_MB="${BITCOIN_MAX_MEMPOOL_MB:-150}"
BITCOIN_ASSUMEVALID="${BITCOIN_ASSUMEVALID:-}"
BITCOIN_ASSUMEUTXO_SNAPSHOT="${BITCOIN_ASSUMEUTXO_SNAPSHOT:-}"
BITCOIN_ASSUMEUTXO_MIN_HEADERS="${BITCOIN_ASSUMEUTXO_MIN_HEADERS:-800000}"
BITCOIN_DELETE_ASSUMEUTXO_SNAPSHOT="${BITCOIN_DELETE_ASSUMEUTXO_SNAPSHOT:-1}"
BITCOIN_ASSUMEUTXO_STREAM="${BITCOIN_ASSUMEUTXO_STREAM:-auto}"
BITCOIN_DATA_DIR="${BITCOIN_DATA_DIR:-/var/lib/bitcoind}"
BITCOIN_CONF_DIR="${BITCOIN_CONF_DIR:-/etc/bitcoin}"
BITCOIN_RPC_USER="${BITCOIN_RPC_USER:-datum}"
BITCOIN_RPC_PASSWORD="${BITCOIN_RPC_PASSWORD:-}"
BITCOIN_RPC_URL="${BITCOIN_RPC_URL:-http://127.0.0.1:8332}"
GRID_ALLOW_LOW_RESOURCE_BITCOIN="${GRID_ALLOW_LOW_RESOURCE_BITCOIN:-0}"

GRID_SWAP_MB="${GRID_SWAP_MB:-auto}"
GRID_SWAP_FILE="${GRID_SWAP_FILE:-/swapfile}"

BOOT_WEB_PORT="${BOOT_WEB_PORT:-5000}"
BOOT_DATUM_PORT="${BOOT_DATUM_PORT:-3008}"
BOOT_DATUM_PUBLIC_PORT="${BOOT_DATUM_PUBLIC_PORT:-$BOOT_DATUM_PORT}"
DATUM_STRATUM_PORT="${DATUM_STRATUM_PORT:-23334}"
DATUM_API_PORT="${DATUM_API_PORT:-7152}"
DATUM_VARDIFF_MIN="${DATUM_VARDIFF_MIN:-1024}"
DATUM_TARGET_SHARES_MIN="${DATUM_TARGET_SHARES_MIN:-30}"
DATUM_MAX_CLIENTS_PER_THREAD="${DATUM_MAX_CLIENTS_PER_THREAD:-4}"
DATUM_MAX_THREADS="${DATUM_MAX_THREADS:-1}"
DATUM_MAX_CLIENTS="${DATUM_MAX_CLIENTS:-4}"
DATUM_WORK_UPDATE_SECONDS="${DATUM_WORK_UPDATE_SECONDS:-5}"
DATUM_POOLED_MINING_ONLY="${DATUM_POOLED_MINING_ONLY:-false}"
DATUM_POOL_PASS_WORKERS="${DATUM_POOL_PASS_WORKERS:-false}"
DATUM_POOL_PASS_FULL_USERS="${DATUM_POOL_PASS_FULL_USERS:-false}"
DATUM_ADMIN_PASSWORD="${DATUM_ADMIN_PASSWORD:-}"

INSTALL_BITCOIN=1
INSTALL_BOOT=1
INSTALL_DATUM=1
ASSUME_YES=0
DRY_RUN=0
NONINTERACTIVE=0
CONFIGURE_UFW="${CONFIGURE_UFW:-0}"

usage() {
    cat <<EOF
Usage: $SCRIPT_NAME [options]

Installs a sovereign GridPool stack on Debian/Ubuntu-style Linux:
  - pruned Bitcoin Core with ZMQ and DATUM blocknotify
  - GridPool from Docker Compose
  - DATUM Gateway from upstream source

Options:
  --payout-address ADDRESS      Bitcoin payout address for DATUM and GridPool slot-0 fallback
                                (default: 256 Foundation donation address)
  --home DIR                    Install root (default: $GRID_HOME)
  --boot-ref REF                GridPool repo branch/tag/commit (legacy flag name; default: $GRID_BOOT_REPO_REF)
  --datum-ref REF               DATUM repo branch/tag/commit (default: $GRID_DATUM_REPO_REF)
  --bitcoin-version VERSION     Bitcoin Core release version (default: $BITCOIN_CORE_VERSION)
  --bitcoin-network NETWORK     Bitcoin network: mainnet or testnet4 (default: $BITCOIN_NETWORK)
  --prune-mb MB                 Bitcoin prune target in MiB (default: $BITCOIN_PRUNE_MB)
  --dbcache-mb MB|auto          Bitcoin dbcache in MiB (default: $BITCOIN_DBCACHE_MB)
  --assumevalid HASH            Optional trusted recent block hash for Bitcoin Core assumevalid
  --assumeutxo-snapshot PATH    Optional local/HTTP(S) UTXO snapshot to load with loadtxoutset
  --assumeutxo-stream auto|1|0  Stream HTTP(S) snapshots through a FIFO instead of saving first
  --bitcoin-rpc-url URL         Bitcoin RPC URL for DATUM (default: $BITCOIN_RPC_URL)
                                Use with --no-bitcoin for edge/proxy installs.
  --bootstrap-peers LIST        Comma-separated GridPool peer URLs
  --swap-mb MB|auto|0           Swapfile size for low-RAM devices (default: $GRID_SWAP_MB)
  --no-bitcoin                  Skip Bitcoin Core install/config
  --no-boot                     Skip GridPool install/config
  --no-datum                    Skip DATUM install/config
  --configure-ufw               Open ports if UFW is active
  --yes                         Do not ask confirmation prompts
  --noninteractive              Fail instead of prompting for missing values
  --dry-run                     Print actions without changing the system
  -h, --help                    Show this help

Useful environment overrides:
  GRID_BOOT_REPO_URL, GRID_DATUM_REPO_URL
  BITCOIN_NETWORK, GRID_BOOT_NETWORK_ID, GRID_BOOT_STATE_FILE, GRID_POOL_COINBASE_TAG
  BITCOIN_RPC_USER, BITCOIN_RPC_PASSWORD, BITCOIN_RPC_URL, BITCOIN_ASSUMEVALID
  BITCOIN_ASSUMEUTXO_SNAPSHOT, BITCOIN_ASSUMEUTXO_STREAM, BITCOIN_ASSUMEUTXO_MIN_HEADERS
  BITCOIN_DBCACHE_MB, BITCOIN_MAX_MEMPOOL_MB
  BOOT_WEB_PORT, BOOT_DATUM_PORT, BOOT_DATUM_PUBLIC_PORT, DATUM_STRATUM_PORT, DATUM_API_PORT
  DATUM_MAX_CLIENTS, DATUM_MAX_CLIENTS_PER_THREAD, DATUM_MAX_THREADS
  DATUM_POOL_PASS_WORKERS, DATUM_POOL_PASS_FULL_USERS

Examples:
  sudo $SCRIPT_NAME
  sudo $SCRIPT_NAME --payout-address bc1q...
  BITCOIN_ASSUMEVALID=000000... sudo -E $SCRIPT_NAME --payout-address bc1q...
EOF
}

log() {
    printf '[grid-install] %s\n' "$*"
}

warn() {
    printf '[grid-install][warn] %s\n' "$*" >&2
}

fail() {
    printf '[grid-install][error] %s\n' "$*" >&2
    exit 1
}

run() {
    if (( DRY_RUN )); then
        printf '[dry-run] '
        printf '%q ' "$@"
        printf '\n'
        return 0
    fi

    "$@"
}

write_file() {
    local path="$1"
    local mode="${2:-0644}"
    local owner="${3:-root:root}"
    local tmp
    tmp="$(mktemp)"
    cat >"$tmp"
    if (( DRY_RUN )); then
        log "would write $path"
        sed 's/^/[dry-run-file] /' "$tmp"
        rm -f "$tmp"
        return 0
    fi

    install -D -m "$mode" -o "${owner%%:*}" -g "${owner##*:}" "$tmp" "$path"
    rm -f "$tmp"
}

backup_if_exists() {
    local path="$1"
    if [[ -e "$path" ]]; then
        local backup="${path}.bak.$(date -u +%Y%m%d%H%M%S)"
        warn "backing up existing $path to $backup"
        run cp -a "$path" "$backup"
    fi
}

random_secret() {
    if command -v openssl >/dev/null 2>&1; then
        openssl rand -hex 24
    else
        tr -dc 'A-Za-z0-9' </dev/urandom | head -c 48
        printf '\n'
    fi
}

read_existing_install_secret() {
    local key="$1"
    local record="/etc/grid-pool/install.env"

    [[ -r "$record" ]] || return 0
    sed -n "s/^${key}=//p" "$record" | tail -n 1
}

primary_ipv4() {
    local route
    route="$(ip -4 route get 1.1.1.1 2>/dev/null || true)"
    awk '
        {
            for (i = 1; i <= NF; i++) {
                if ($i == "src") {
                    print $(i + 1)
                    exit
                }
            }
        }' <<<"$route"
}

mem_total_mb() {
    awk '/MemTotal:/ { printf "%d\n", $2 / 1024 }' /proc/meminfo 2>/dev/null || printf '0\n'
}

swap_total_mb() {
    awk '/SwapTotal:/ { printf "%d\n", $2 / 1024 }' /proc/meminfo 2>/dev/null || printf '0\n'
}

disk_available_mb() {
    df -Pm "${1:-/}" 2>/dev/null | awk 'NR == 2 { print $4 }'
}

resolve_bitcoin_dbcache_mb() {
    if [[ "$BITCOIN_DBCACHE_MB" != "auto" ]]; then
        printf '%s' "$BITCOIN_DBCACHE_MB"
        return 0
    fi

    local mem_mb
    mem_mb="$(mem_total_mb)"
    if (( mem_mb >= 7000 )); then
        printf '4096'
    elif (( mem_mb >= 3500 )); then
        printf '2048'
    elif (( mem_mb >= 1800 )); then
        printf '768'
    else
        printf '256'
    fi
}

resolve_swap_mb() {
    if [[ "$GRID_SWAP_MB" != "auto" ]]; then
        printf '%s' "$GRID_SWAP_MB"
        return 0
    fi

    local mem_mb swap_mb
    mem_mb="$(mem_total_mb)"
    swap_mb="$(swap_total_mb)"
    if (( swap_mb > 0 )); then
        printf '0'
    elif (( mem_mb < 1200 )); then
        printf '4096'
    elif (( mem_mb < 2400 )); then
        printf '1024'
    else
        printf '0'
    fi
}

should_stream_assumeutxo() {
    local source="$1"
    if ! [[ "$source" =~ ^https?:// ]]; then
        return 1
    fi

    case "$BITCOIN_ASSUMEUTXO_STREAM" in
        1|true|TRUE|yes|YES)
            return 0
            ;;
        0|false|FALSE|no|NO)
            return 1
            ;;
        auto|"")
            return 0
            ;;
        *)
            fail "--assumeutxo-stream must be auto, 1, or 0"
            ;;
    esac
}

require_cmd() {
    command -v "$1" >/dev/null 2>&1 || fail "missing required command: $1"
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --payout-address)
                GRID_POOL_PAYOUT_ADDRESS="${2:-}"
                GRID_POOL_PAYOUT_ADDRESS_WAS_SET=1
                shift 2
                ;;
            --home)
                GRID_HOME="${2:-}"
                shift 2
                ;;
            --boot-ref)
                GRID_BOOT_REPO_REF="${2:-}"
                shift 2
                ;;
            --datum-ref)
                GRID_DATUM_REPO_REF="${2:-}"
                shift 2
                ;;
            --bitcoin-version)
                BITCOIN_CORE_VERSION="${2:-}"
                shift 2
                ;;
            --bitcoin-network)
                BITCOIN_NETWORK="${2:-}"
                shift 2
                ;;
            --prune-mb)
                BITCOIN_PRUNE_MB="${2:-}"
                shift 2
                ;;
            --dbcache-mb)
                BITCOIN_DBCACHE_MB="${2:-}"
                shift 2
                ;;
            --assumevalid)
                BITCOIN_ASSUMEVALID="${2:-}"
                shift 2
                ;;
            --assumeutxo-snapshot)
                BITCOIN_ASSUMEUTXO_SNAPSHOT="${2:-}"
                shift 2
                ;;
            --assumeutxo-stream)
                BITCOIN_ASSUMEUTXO_STREAM="${2:-}"
                shift 2
                ;;
            --bitcoin-rpc-url)
                BITCOIN_RPC_URL="${2:-}"
                shift 2
                ;;
            --bootstrap-peers)
                GRID_BOOT_BOOTSTRAP_PEERS="${2:-}"
                GRID_BOOT_BOOTSTRAP_PEERS_WAS_SET=1
                shift 2
                ;;
            --swap-mb)
                GRID_SWAP_MB="${2:-}"
                shift 2
                ;;
            --no-bitcoin)
                INSTALL_BITCOIN=0
                shift
                ;;
            --no-boot)
                INSTALL_BOOT=0
                shift
                ;;
            --no-datum)
                INSTALL_DATUM=0
                shift
                ;;
            --configure-ufw)
                CONFIGURE_UFW=1
                shift
                ;;
            --yes)
                ASSUME_YES=1
                shift
                ;;
            --noninteractive)
                NONINTERACTIVE=1
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

sudo_env_args() {
    local names=(
        GRID_HOME
        GRID_BOOT_REPO_URL
        GRID_BOOT_REPO_REF
        GRID_DATUM_REPO_URL
        GRID_DATUM_REPO_REF
        GRID_FOUNDATION_PAYOUT_ADDRESS
        GRID_POOL_PAYOUT_ADDRESS
        GRID_POOL_COINBASE_TAG
        GRID_BOOT_BOOTSTRAP_PEERS
        GRID_BOOT_NETWORK_ID
        GRID_BOOT_NODE_MODE
        BITCOIN_NETWORK
        GRID_BOOT_STATE_FILE
        GRID_SWAP_MB
        GRID_SWAP_FILE
        BITCOIN_CORE_VERSION
        BITCOIN_PRUNE_MB
        BITCOIN_DBCACHE_MB
        BITCOIN_MAX_MEMPOOL_MB
        BITCOIN_ASSUMEVALID
        BITCOIN_ASSUMEUTXO_SNAPSHOT
        BITCOIN_ASSUMEUTXO_MIN_HEADERS
        BITCOIN_ASSUMEUTXO_STREAM
        BITCOIN_DELETE_ASSUMEUTXO_SNAPSHOT
        BITCOIN_DATA_DIR
        BITCOIN_CONF_DIR
        BITCOIN_RPC_USER
        BITCOIN_RPC_PASSWORD
        BITCOIN_RPC_URL
        GRID_ALLOW_LOW_RESOURCE_BITCOIN
        BOOT_WEB_PORT
        BOOT_DATUM_PORT
        BOOT_DATUM_PUBLIC_PORT
        BOOT_PUBLIC_BASE_URL
        BOOT_DATUM_PUBLIC_HOST
        DATUM_POOL_HOST
        DATUM_STRATUM_PORT
        DATUM_API_PORT
        DATUM_VARDIFF_MIN
        DATUM_TARGET_SHARES_MIN
        DATUM_MAX_CLIENTS_PER_THREAD
        DATUM_MAX_THREADS
        DATUM_MAX_CLIENTS
        DATUM_WORK_UPDATE_SECONDS
        DATUM_POOLED_MINING_ONLY
        DATUM_ADMIN_PASSWORD
        CONFIGURE_UFW
    )

    local name
    for name in "${names[@]}"; do
        printf '%s=%s\n' "$name" "${!name-}"
    done
}

confirm_root() {
    if (( EUID == 0 )); then
        return 0
    fi

    if (( DRY_RUN )); then
        warn "not running as root; dry run will continue"
        return 0
    fi

    if command -v sudo >/dev/null 2>&1; then
        local env_args=()
        local line
        while IFS= read -r line; do
            env_args+=("$line")
        done < <(sudo_env_args)
        exec sudo env "${env_args[@]}" bash "$0" "$@"
    fi

    fail "run as root or install sudo"
}

confirm_inputs() {
    if [[ -z "$GRID_HOME" ]]; then
        fail "--home cannot be empty"
    fi

    if [[ -z "$GRID_POOL_PAYOUT_ADDRESS" ]]; then
        GRID_POOL_PAYOUT_ADDRESS="$GRID_FOUNDATION_PAYOUT_ADDRESS"
    fi

    case "${BITCOIN_NETWORK,,}" in
        main|mainnet|"")
            BITCOIN_NETWORK="mainnet"
            ;;
        test|testnet|testnet3|testnet4)
            BITCOIN_NETWORK="testnet4"
            if [[ -z "$GRID_BOOT_NETWORK_ID_WAS_SET" ]]; then
                GRID_BOOT_NETWORK_ID="testnet4-beta"
            fi
            if [[ -z "$GRID_BOOT_BOOTSTRAP_PEERS_WAS_SET" ]]; then
                GRID_BOOT_BOOTSTRAP_PEERS="https://test.gridpool.net"
            fi
            if [[ "$GRID_BOOT_STATE_FILE" == "pool_state.json" ]]; then
                GRID_BOOT_STATE_FILE="pool_state.testnet4.json"
            fi
            if [[ -z "$GRID_POOL_PAYOUT_ADDRESS_WAS_SET" || "$GRID_POOL_PAYOUT_ADDRESS" == "$GRID_FOUNDATION_PAYOUT_ADDRESS" ]]; then
                fail "testnet4 installs require --payout-address with a testnet address; refusing to use the mainnet foundation address"
            fi
            ;;
        *)
            fail "--bitcoin-network must be mainnet or testnet4"
            ;;
    esac

    if ! [[ "$BITCOIN_PRUNE_MB" =~ ^[0-9]+$ ]]; then
        fail "--prune-mb must be an integer"
    fi
    if (( BITCOIN_PRUNE_MB < 550 )); then
        warn "Bitcoin Core minimum prune target is 550 MiB; raising prune target to 550 MiB"
        BITCOIN_PRUNE_MB=550
    fi
    if [[ -n "$BITCOIN_ASSUMEUTXO_SNAPSHOT" && "$BITCOIN_PRUNE_MB" -lt 1100 ]]; then
        warn "assumeUTXO with pruning needs at least about 1100 MiB; raising prune target to 1100 MiB"
        BITCOIN_PRUNE_MB=1100
    fi
    if [[ -n "$BITCOIN_ASSUMEVALID" && ! "$BITCOIN_ASSUMEVALID" =~ ^[0-9a-fA-F]{64}$ ]]; then
        fail "assumevalid must be a 64-character block hash"
    fi

    if [[ -z "$BITCOIN_RPC_PASSWORD" ]]; then
        BITCOIN_RPC_PASSWORD="$(read_existing_install_secret BITCOIN_RPC_PASSWORD)"
        BITCOIN_RPC_PASSWORD="${BITCOIN_RPC_PASSWORD:-$(random_secret)}"
    fi

    if [[ -z "$DATUM_ADMIN_PASSWORD" ]]; then
        DATUM_ADMIN_PASSWORD="$(read_existing_install_secret DATUM_ADMIN_PASSWORD)"
        DATUM_ADMIN_PASSWORD="${DATUM_ADMIN_PASSWORD:-$(random_secret)}"
    fi

    GRID_PRIMARY_IP="${GRID_PRIMARY_IP:-$(primary_ipv4)}"
    if [[ -z "${GRID_PRIMARY_IP:-}" ]]; then
        GRID_PRIMARY_IP="127.0.0.1"
    fi

    BOOT_PUBLIC_BASE_URL="${BOOT_PUBLIC_BASE_URL:-http://${GRID_PRIMARY_IP}:${BOOT_WEB_PORT}}"
    BOOT_DATUM_PUBLIC_HOST="${BOOT_DATUM_PUBLIC_HOST:-$GRID_PRIMARY_IP}"
    BOOT_DATUM_PUBLIC_PORT="${BOOT_DATUM_PUBLIC_PORT:-$BOOT_DATUM_PORT}"
    DATUM_POOL_HOST="${DATUM_POOL_HOST:-127.0.0.1}"
    BITCOIN_DBCACHE_MB_RESOLVED="$(resolve_bitcoin_dbcache_mb)"
    GRID_SWAP_MB_RESOLVED="$(resolve_swap_mb)"

    local available_mb
    available_mb="$(disk_available_mb /)"

    log "install root: $GRID_HOME"
    log "detected primary IP: $GRID_PRIMARY_IP"
    log "GridPool UI: $BOOT_PUBLIC_BASE_URL"
    log "Public DATUM server advertised to remote gateways: ${BOOT_DATUM_PUBLIC_HOST}:${BOOT_DATUM_PUBLIC_PORT}"
    log "ASIC Stratum endpoint after install: ${GRID_PRIMARY_IP}:${DATUM_STRATUM_PORT}"
    log "payout address: $GRID_POOL_PAYOUT_ADDRESS"
    log "Bitcoin network: $BITCOIN_NETWORK"
    log "GridPool network id: $GRID_BOOT_NETWORK_ID"
    if (( INSTALL_BITCOIN )); then
        BITCOIN_RPC_URL="http://127.0.0.1:8332"
        log "Bitcoin mode: local pruned node"
        log "Bitcoin prune target: ${BITCOIN_PRUNE_MB} MiB"
        log "Bitcoin dbcache: ${BITCOIN_DBCACHE_MB_RESOLVED} MiB"
    else
        log "Bitcoin mode: external RPC at ${BITCOIN_RPC_URL}"
    fi
    if [[ -n "${available_mb:-}" ]]; then
        log "root filesystem available: ${available_mb} MiB"
        if (( INSTALL_BITCOIN && available_mb < 30000 && GRID_ALLOW_LOW_RESOURCE_BITCOIN != 1 )); then
            fail "less than 30 GiB is available; rerun with --no-bitcoin and --bitcoin-rpc-url for edge mode, or set GRID_ALLOW_LOW_RESOURCE_BITCOIN=1 to force a risky local Bitcoin install"
        elif (( INSTALL_BITCOIN && available_mb < 60000 )); then
            warn "less than 60 GiB is available; pruned Bitcoin plus Docker may be tight during sync and assumeUTXO background validation"
        fi
    fi
    if (( INSTALL_BITCOIN && $(mem_total_mb) < 1800 && GRID_ALLOW_LOW_RESOURCE_BITCOIN != 1 )); then
        fail "less than 2 GiB RAM detected; use --no-bitcoin with an external Bitcoin RPC, or set GRID_ALLOW_LOW_RESOURCE_BITCOIN=1 to force a risky local Bitcoin install"
    fi
    if (( GRID_SWAP_MB_RESOLVED > 0 )); then
        warn "low-RAM system detected; installer will create ${GRID_SWAP_MB_RESOLVED} MiB swap at $GRID_SWAP_FILE"
    fi
    if [[ "$GRID_POOL_PAYOUT_ADDRESS" == "$GRID_FOUNDATION_PAYOUT_ADDRESS" ]]; then
        warn "using the default 256 Foundation donation address; pass --payout-address to mine to your own address"
    fi

    if (( ASSUME_YES || DRY_RUN )); then
        return 0
    fi

    read -r -p "Proceed with install? [y/N] " reply
    case "$reply" in
        y|Y|yes|YES) ;;
        *) fail "aborted" ;;
    esac
}

apt_install() {
    export DEBIAN_FRONTEND=noninteractive
    run apt-get update
    run apt-get install -y --no-install-recommends "$@"
}

ensure_swap() {
    local swap_mb="${GRID_SWAP_MB_RESOLVED:-0}"
    if (( swap_mb <= 0 )); then
        return 0
    fi

    if swapon --show=NAME --noheadings 2>/dev/null | grep -Fxq "$GRID_SWAP_FILE"; then
        log "swapfile already active: $GRID_SWAP_FILE"
        return 0
    fi

    if [[ -e "$GRID_SWAP_FILE" ]]; then
        warn "$GRID_SWAP_FILE already exists; not overwriting it"
        return 0
    fi

    log "creating ${swap_mb} MiB swapfile at $GRID_SWAP_FILE"
    run fallocate -l "${swap_mb}M" "$GRID_SWAP_FILE"
    run chmod 600 "$GRID_SWAP_FILE"
    run mkswap "$GRID_SWAP_FILE"
    run swapon "$GRID_SWAP_FILE"

    if ! grep -Eq "^[^#].*[[:space:]]${GRID_SWAP_FILE//\//\\/}[[:space:]]" /etc/fstab 2>/dev/null; then
        if (( DRY_RUN )); then
            log "would add $GRID_SWAP_FILE to /etc/fstab"
        else
            printf '%s none swap sw 0 0\n' "$GRID_SWAP_FILE" >> /etc/fstab
        fi
    fi
}

install_dependencies() {
    log "installing OS dependencies"
    apt_install \
        ca-certificates \
        curl \
        gnupg \
        git \
        jq \
        openssl \
        tar \
        xz-utils \
        iproute2 \
        ufw \
        build-essential \
        cmake \
        pkgconf \
        libcurl4-openssl-dev \
        libjansson-dev \
        libsodium-dev \
        libmicrohttpd-dev \
        psmisc

    if ! command -v docker >/dev/null 2>&1; then
        log "installing Docker from Ubuntu packages"
        if ! apt_install docker.io docker-compose-v2; then
            warn "Ubuntu Docker packages failed; falling back to get.docker.com"
            run sh -c 'curl -fsSL https://get.docker.com | sh'
        fi
    fi

    if ! docker compose version >/dev/null 2>&1; then
        log "installing Docker Compose plugin"
        if ! apt_install docker-compose-v2 docker-compose-plugin; then
            warn "Docker Compose plugin package was not available; verify docker compose manually"
        fi
    fi

    run systemctl enable --now docker
}

bitcoin_platform() {
    local arch
    arch="$(uname -m)"
    case "$arch" in
        aarch64|arm64)
            printf 'aarch64-linux-gnu'
            ;;
        x86_64|amd64)
            printf 'x86_64-linux-gnu'
            ;;
        *)
            fail "unsupported Bitcoin Core binary architecture: $arch"
            ;;
    esac
}

bitcoin_cli() {
    bitcoin-cli -conf="${BITCOIN_CONF_DIR}/bitcoin.conf" -datadir="${BITCOIN_DATA_DIR}" "$@"
}

wait_for_bitcoin_rpc() {
    local timeout="${1:-180}"
    local start
    start="$(date +%s)"

    if (( DRY_RUN )); then
        log "would wait for Bitcoin RPC"
        return 0
    fi

    until bitcoin_cli getblockchaininfo >/dev/null 2>&1; do
        if (( $(date +%s) - start > timeout )); then
            fail "timed out waiting for Bitcoin RPC"
        fi
        sleep 3
    done
}

wait_for_bitcoin_headers() {
    [[ -n "$BITCOIN_ASSUMEUTXO_SNAPSHOT" ]] || return 0

    local timeout="${1:-900}"
    local min_headers="$BITCOIN_ASSUMEUTXO_MIN_HEADERS"
    local start headers blocks
    start="$(date +%s)"

    if (( DRY_RUN )); then
        log "would wait for Bitcoin headers before loading assumeUTXO"
        return 0
    fi

    log "waiting for Bitcoin headers before loading assumeUTXO snapshot"
    while true; do
        headers="$(bitcoin_cli getblockchaininfo 2>/dev/null | jq -r '.headers // 0' || printf '0')"
        blocks="$(bitcoin_cli getblockchaininfo 2>/dev/null | jq -r '.blocks // 0' || printf '0')"
        if [[ "$headers" =~ ^[0-9]+$ ]] && (( headers >= min_headers )); then
            log "Bitcoin headers available: headers=${headers}, blocks=${blocks}"
            return 0
        fi
        if (( $(date +%s) - start > timeout )); then
            fail "timed out waiting for Bitcoin headers before assumeUTXO load; last headers=${headers:-unknown}, blocks=${blocks:-unknown}"
        fi
        sleep 5
    done
}

restart_bitcoin_for_assumeutxo_load() {
    [[ -n "$BITCOIN_ASSUMEUTXO_SNAPSHOT" ]] || return 0

    if (( DRY_RUN )); then
        log "would restart Bitcoin with P2P disabled for assumeUTXO load"
        return 0
    fi

    log "restarting Bitcoin with P2P disabled for assumeUTXO load"
    systemctl stop bitcoind
    if ! grep -q '^connect=0$' "$BITCOIN_CONF_DIR/bitcoin.conf"; then
        printf '\nconnect=0\n' >> "$BITCOIN_CONF_DIR/bitcoin.conf"
    fi
    systemctl start bitcoind
    wait_for_bitcoin_rpc 240
}

restore_bitcoin_network_after_assumeutxo_load() {
    [[ -n "$BITCOIN_ASSUMEUTXO_SNAPSHOT" ]] || return 0

    if (( DRY_RUN )); then
        log "would re-enable Bitcoin P2P after assumeUTXO load"
        return 0
    fi

    log "re-enabling Bitcoin P2P after assumeUTXO load"
    sed -i '/^connect=0$/d' "$BITCOIN_CONF_DIR/bitcoin.conf"
    systemctl restart bitcoind
    wait_for_bitcoin_rpc 240
}

load_assumeutxo_snapshot() {
    [[ -n "$BITCOIN_ASSUMEUTXO_SNAPSHOT" ]] || return 0

    wait_for_bitcoin_rpc 240
    wait_for_bitcoin_headers 900
    restart_bitcoin_for_assumeutxo_load
    if (( ! DRY_RUN )); then
        log "pausing Bitcoin P2P while loading assumeUTXO snapshot"
        bitcoin_cli setnetworkactive false >/dev/null
    fi

    local source="$BITCOIN_ASSUMEUTXO_SNAPSHOT"
    local snapshot_path="$BITCOIN_DATA_DIR/assumeutxo-snapshot.dat"

    if should_stream_assumeutxo "$source"; then
        local fifo_path="$BITCOIN_DATA_DIR/assumeutxo-snapshot.fifo"
        local curl_pid=""

        log "streaming assumeUTXO snapshot directly into Bitcoin Core"
        run rm -f "$fifo_path"
        run mkfifo "$fifo_path"
        run chown bitcoin:bitcoin "$fifo_path"

        if (( DRY_RUN )); then
            log "would stream $source into $fifo_path and run bitcoin-cli loadtxoutset"
        else
            local load_pid=""
            bitcoin_cli -rpcclienttimeout=0 loadtxoutset "$fifo_path" &
            load_pid=$!
            sleep 2
            if ! kill -0 "$load_pid" 2>/dev/null; then
                wait "$load_pid"
                fail "bitcoin-cli loadtxoutset failed before snapshot streaming started"
            fi

            curl -fL --retry 5 --retry-delay 5 -o "$fifo_path" "$source" &
            curl_pid=$!
            local load_status=0
            local curl_status=0
            wait "$load_pid" || load_status=$?
            wait "$curl_pid" || curl_status=$?
            if (( load_status != 0 )); then
                fail "bitcoin-cli loadtxoutset failed while streaming assumeUTXO snapshot"
            fi
            if (( curl_status != 0 )); then
                fail "snapshot download failed while streaming assumeUTXO snapshot"
            fi
        fi

        run rm -f "$fifo_path"
        return 0
    fi

    if [[ "$source" =~ ^https?:// ]]; then
        log "downloading assumeUTXO snapshot"
        run curl -fL --retry 5 --retry-delay 5 -o "$snapshot_path" "$source"
        run chown bitcoin:bitcoin "$snapshot_path"
    else
        if [[ ! -f "$source" && "$DRY_RUN" -ne 1 ]]; then
            fail "assumeUTXO snapshot not found: $source"
        fi
        if [[ "$source" != "$snapshot_path" ]]; then
            log "copying assumeUTXO snapshot into Bitcoin datadir"
            run cp "$source" "$snapshot_path"
            run chown bitcoin:bitcoin "$snapshot_path"
        else
            run chown bitcoin:bitcoin "$snapshot_path"
        fi
    fi

    log "loading assumeUTXO snapshot; this can take several minutes"
    if (( ! DRY_RUN )); then
        bitcoin_cli -rpcclienttimeout=0 loadtxoutset "$snapshot_path" || {
            fail "bitcoin-cli loadtxoutset failed"
        }
    else
        log "would run bitcoin-cli loadtxoutset $snapshot_path"
    fi
    restore_bitcoin_network_after_assumeutxo_load

    if [[ "$BITCOIN_DELETE_ASSUMEUTXO_SNAPSHOT" == "1" ]]; then
        log "deleting loaded assumeUTXO snapshot to recover disk space"
        run rm -f "$snapshot_path"
    fi
}

install_bitcoin_core() {
    (( INSTALL_BITCOIN )) || return 0

    local platform archive base_url tmpdir
    platform="$(bitcoin_platform)"
    archive="bitcoin-${BITCOIN_CORE_VERSION}-${platform}.tar.gz"
    base_url="https://bitcoincore.org/bin/bitcoin-core-${BITCOIN_CORE_VERSION}"
    tmpdir="$(mktemp -d)"

    log "installing Bitcoin Core $BITCOIN_CORE_VERSION for $platform"
    (
        cd "$tmpdir"
        run curl -fsSLO "${base_url}/SHA256SUMS"
        run curl -fsSLO "${base_url}/${archive}"
        if (( ! DRY_RUN )); then
            grep "  ${archive}\$" SHA256SUMS > SHA256SUMS.filtered \
                || grep " ${archive}\$" SHA256SUMS > SHA256SUMS.filtered \
                || fail "could not find $archive in SHA256SUMS"
            sha256sum -c SHA256SUMS.filtered
            tar -xzf "$archive"
            install -m 0755 -o root -g root "bitcoin-${BITCOIN_CORE_VERSION}/bin/"* /usr/local/bin/
        fi
    )
    rm -rf "$tmpdir"

    if ! id bitcoin >/dev/null 2>&1; then
        run useradd --system --home "$BITCOIN_DATA_DIR" --shell /usr/sbin/nologin bitcoin
    fi

    run install -d -m 0750 -o bitcoin -g bitcoin "$BITCOIN_DATA_DIR"
    run install -d -m 0755 -o root -g root "$BITCOIN_CONF_DIR"

    backup_if_exists "$BITCOIN_CONF_DIR/bitcoin.conf"
    write_file "$BITCOIN_CONF_DIR/bitcoin.conf" 0640 root:bitcoin <<EOF
server=1
daemon=0
disablewallet=1
$(if [[ "$BITCOIN_NETWORK" != "mainnet" ]]; then printf 'chain=%s\n' "$BITCOIN_NETWORK"; fi)
prune=${BITCOIN_PRUNE_MB}
dbcache=${BITCOIN_DBCACHE_MB_RESOLVED}
maxmempool=${BITCOIN_MAX_MEMPOOL_MB}
txindex=0
blockfilterindex=0
coinstatsindex=0
peerblockfilters=0
rest=0
maxconnections=32
maxuploadtarget=500
persistmempool=1
checkblocks=6
checklevel=3
blockmaxsize=3985000
blockmaxweight=3985000
$(if [[ -n "$BITCOIN_ASSUMEVALID" ]]; then printf 'assumevalid=%s\n' "$BITCOIN_ASSUMEVALID"; fi)

rpcuser=${BITCOIN_RPC_USER}
rpcpassword=${BITCOIN_RPC_PASSWORD}
$(if [[ "$BITCOIN_NETWORK" != "mainnet" ]]; then printf '\n[%s]\n' "$BITCOIN_NETWORK"; fi)
rpcbind=127.0.0.1
rpcallowip=127.0.0.1
rpcport=8332
rpcthreads=4
rpcworkqueue=64

zmqpubhashblock=tcp://127.0.0.1:28332
zmqpubrawblock=tcp://127.0.0.1:28333

blocknotify=curl -fsS --max-time 2 http://127.0.0.1:${DATUM_API_PORT}/NOTIFY >/dev/null 2>&1 || true
EOF

    write_file /etc/systemd/system/bitcoind.service 0644 root:root <<EOF
[Unit]
Description=Bitcoin Core daemon
After=network-online.target
Wants=network-online.target

[Service]
User=bitcoin
Group=bitcoin
Type=simple
ExecStart=/usr/local/bin/bitcoind -conf=${BITCOIN_CONF_DIR}/bitcoin.conf -datadir=${BITCOIN_DATA_DIR}
ExecStop=/usr/local/bin/bitcoin-cli -conf=${BITCOIN_CONF_DIR}/bitcoin.conf -datadir=${BITCOIN_DATA_DIR} stop
Restart=on-failure
RestartSec=10
TimeoutStopSec=300
PrivateTmp=true
ProtectSystem=full
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

    run systemctl daemon-reload
    run systemctl enable bitcoind
    run systemctl restart bitcoind
    load_assumeutxo_snapshot
}

clone_or_update_repo() {
    local repo_url="$1"
    local ref="$2"
    local dest="$3"

    if [[ ! -d "$dest/.git" ]]; then
        log "cloning $repo_url into $dest"
        run mkdir -p "$(dirname "$dest")"
        run git clone "$repo_url" "$dest"
    fi

    log "checking out $dest at $ref"
    run git -c "safe.directory=$dest" -C "$dest" fetch --all --tags --prune
    run git -c "safe.directory=$dest" -C "$dest" checkout "$ref"
    run git -c "safe.directory=$dest" -C "$dest" pull --ff-only ||
        warn "pull skipped; $ref may be a detached commit or local branch"
}

bootstrap_peers_json() {
    printf '%s' "$GRID_BOOT_BOOTSTRAP_PEERS" \
        | jq -R 'split(",") | map(gsub("^\\s+|\\s+$"; "")) | map(select(length > 0))'
}

write_boot_compose() {
    local boot_dir="$1"

    if [[ "$GRID_BOOT_LOCAL_BUILD" == "1" ]]; then
        write_file "$boot_dir/docker-compose.sovereign.yml" 0644 root:root <<EOF
services:
  boot-portal:
    image: ${GRID_BOOT_IMAGE}
    build:
      context: .
      dockerfile: Dockerfile
    container_name: boot-portal
    restart: unless-stopped
    network_mode: host
    environment:
      BOOT_PORTAL_CONFIG_PATH: /data/boot_portal_config.json
      BOOT_PORTAL_STATE_PATH: /data/${GRID_BOOT_STATE_FILE}
    volumes:
      - ./data:/data
EOF
        return 0
    fi

    write_file "$boot_dir/docker-compose.sovereign.yml" 0644 root:root <<EOF
services:
  boot-portal:
    image: ${GRID_BOOT_IMAGE}
    container_name: boot-portal
    restart: unless-stopped
    network_mode: host
    environment:
      BOOT_PORTAL_CONFIG_PATH: /data/boot_portal_config.json
      BOOT_PORTAL_STATE_PATH: /data/${GRID_BOOT_STATE_FILE}
    volumes:
      - ./data:/data
EOF
}

install_boot() {
    (( INSTALL_BOOT )) || return 0

    local boot_dir="$GRID_HOME/boot-protocol"
    local boot_config_sample="$boot_dir/docker/boot_portal_config.sample.json"
    clone_or_update_repo "$GRID_BOOT_REPO_URL" "$GRID_BOOT_REPO_REF" "$boot_dir"

    if [[ "$BITCOIN_NETWORK" == "testnet4" ]]; then
        boot_config_sample="$boot_dir/docker/boot_portal_config.testnet4.sample.json"
    fi
    run mkdir -p "$boot_dir/data"
    if [[ ! -f "$boot_dir/data/boot_portal_config.json" ]]; then
        run cp "$boot_config_sample" "$boot_dir/data/boot_portal_config.json"
    fi

    local peers_json
    peers_json="$(bootstrap_peers_json)"

    if (( DRY_RUN )); then
        log "would write $boot_dir/data/boot_portal_config.local.json"
    else
        local local_config_path="$boot_dir/data/boot_portal_config.local.json"
        local existing_local_config="{}"
        if [[ -f "$local_config_path" ]]; then
            existing_local_config="$(jq -c '.' "$local_config_path" 2>/dev/null || printf '{}')"
        fi

        jq -n \
            --argjson existing "$existing_local_config" \
            --arg publicBaseUrl "$BOOT_PUBLIC_BASE_URL" \
            --arg datumPublicHost "$BOOT_DATUM_PUBLIC_HOST" \
            --arg nodeMode "$GRID_BOOT_NODE_MODE" \
            --arg bitcoinNetwork "$BITCOIN_NETWORK" \
            --arg networkId "$GRID_BOOT_NETWORK_ID" \
            --arg payout "$GRID_POOL_PAYOUT_ADDRESS" \
            --arg tag "$GRID_POOL_COINBASE_TAG" \
            --argjson webPort "$BOOT_WEB_PORT" \
            --argjson datumPort "$BOOT_DATUM_PORT" \
            --argjson datumPublicPort "$BOOT_DATUM_PUBLIC_PORT" \
            --argjson peers "$peers_json" \
            '$existing * {
                NotificationSource: "BitcoinZmq",
                WebUI_Port_http: $webPort,
                WebUI_Port_https: 0,
                Datum_Port: $datumPort,
                public_base_url: $publicBaseUrl,
                datum_public_host: $datumPublicHost,
                datum_public_port: $datumPublicPort,
                node_mode: $nodeMode,
                bitcoin_network: $bitcoinNetwork,
                boot_network_id: $networkId,
                enable_peer_sync: true,
                bootstrap_peers: $peers,
                enable_admin_api: false,
                enable_peer_persistent_sessions: true,
                enable_peer_udp_fast_relay: true,
                peer_udp_bind_port: 5001,
                peer_udp_port: 5001,
                peer_udp_public_host: "",
                peer_udp_max_datagram_bytes: 1200,
                pool_payout_script: $payout,
                coinbase_tag: $tag,
                min_diff: 300
            }' >"$local_config_path"
        chmod 0600 "$local_config_path"
    fi

    write_boot_compose "$boot_dir"
    run chown -R 1000:1000 "$boot_dir/data"
    run chmod 0750 "$boot_dir/data"

    if [[ "$GRID_BOOT_LOCAL_BUILD" == "1" ]]; then
        log "building and starting GridPool from local source"
        run docker compose -f "$boot_dir/docker-compose.sovereign.yml" --project-directory "$boot_dir" up -d --build
    else
        log "pulling and starting GridPool image ${GRID_BOOT_IMAGE}"
        run docker compose -f "$boot_dir/docker-compose.sovereign.yml" --project-directory "$boot_dir" pull boot-portal
        run docker compose -f "$boot_dir/docker-compose.sovereign.yml" --project-directory "$boot_dir" up -d
    fi
    wait_for_http "http://127.0.0.1:${BOOT_WEB_PORT}/health/live" 180
}

wait_for_http() {
    local url="$1"
    local timeout="${2:-60}"
    local start
    start="$(date +%s)"

    if (( DRY_RUN )); then
        log "would wait for $url"
        return 0
    fi

    until curl -fsS --max-time 5 "$url" >/dev/null 2>&1; do
        if (( $(date +%s) - start > timeout )); then
            fail "timed out waiting for $url"
        fi
        sleep 3
    done
}

boot_pubkey_from_logs() {
    docker logs boot-portal 2>&1 \
        | sed -n 's/.*Server Public Key (Hex): //p' \
        | grep -E '^[0-9a-f]{128}$' \
        | tail -n 1
}

wait_for_boot_pubkey() {
    local timeout="${1:-120}"
    local start key
    start="$(date +%s)"

    if (( DRY_RUN )); then
        printf 'dryrun-public-key-placeholder'
        return 0
    fi

    while true; do
        key="$(boot_pubkey_from_logs || true)"
        if [[ "$key" =~ ^[0-9a-f]{128}$ ]]; then
            printf '%s' "$key"
            return 0
        fi

        if (( $(date +%s) - start > timeout )); then
            fail "could not discover GridPool DATUM public key from Docker logs"
        fi
        sleep 2
    done
}

install_datum() {
    (( INSTALL_DATUM )) || return 0

    local datum_dir="$GRID_HOME/datum_gateway"
    local boot_pubkey
    boot_pubkey="$(wait_for_boot_pubkey 180)"

    clone_or_update_repo "$GRID_DATUM_REPO_URL" "$GRID_DATUM_REPO_REF" "$datum_dir"

    log "building DATUM Gateway"
    run env \
        GIT_CONFIG_COUNT=1 \
        GIT_CONFIG_KEY_0=safe.directory \
        GIT_CONFIG_VALUE_0="$datum_dir" \
        cmake -S "$datum_dir" -B "$datum_dir/build"
    run env \
        GIT_CONFIG_COUNT=1 \
        GIT_CONFIG_KEY_0=safe.directory \
        GIT_CONFIG_VALUE_0="$datum_dir" \
        cmake --build "$datum_dir/build" --parallel "$(nproc)"

    local datum_bin="$datum_dir/build/datum_gateway"
    if [[ ! -x "$datum_bin" && -x "$datum_dir/datum_gateway" ]]; then
        datum_bin="$datum_dir/datum_gateway"
    fi
    if [[ ! -x "$datum_bin" && ! "$DRY_RUN" -eq 1 ]]; then
        fail "DATUM binary not found after build"
    fi

    if ! id datum >/dev/null 2>&1; then
        run useradd --system --home "$datum_dir" --shell /usr/sbin/nologin datum
    fi

    run install -d -m 0750 -o datum -g datum /etc/datum_gateway
    run install -d -m 0750 -o datum -g datum /var/log/datum_gateway

    backup_if_exists /etc/datum_gateway/config.json
    write_file /etc/datum_gateway/config.json 0640 datum:datum <<EOF
{
  "bitcoind": {
    "rpcuser": "${BITCOIN_RPC_USER}",
    "rpcpassword": "${BITCOIN_RPC_PASSWORD}",
    "rpcurl": "${BITCOIN_RPC_URL}",
    "work_update_seconds": ${DATUM_WORK_UPDATE_SECONDS},
    "notify_fallback": true
  },
  "stratum": {
    "listen_addr": "0.0.0.0",
    "listen_port": ${DATUM_STRATUM_PORT},
    "max_clients_per_thread": ${DATUM_MAX_CLIENTS_PER_THREAD},
    "max_threads": ${DATUM_MAX_THREADS},
    "max_clients": ${DATUM_MAX_CLIENTS},
    "vardiff_min": ${DATUM_VARDIFF_MIN},
    "vardiff_target_shares_min": ${DATUM_TARGET_SHARES_MIN}
  },
  "mining": {
    "pool_address": "${GRID_POOL_PAYOUT_ADDRESS}",
    "coinbase_tag_primary": "${GRID_POOL_COINBASE_TAG}",
    "coinbase_tag_secondary": "DATUM User"
  },
  "api": {
    "admin_password": "${DATUM_ADMIN_PASSWORD}",
    "listen_addr": "127.0.0.1",
    "listen_port": ${DATUM_API_PORT},
    "modify_conf": false
  },
  "logger": {
    "log_to_console": true,
    "log_to_file": true,
    "log_file": "/var/log/datum_gateway/datum.log",
    "log_rotate_daily": true,
    "log_level_console": 2,
    "log_level_file": 1
  },
  "datum": {
    "pool_host": "${DATUM_POOL_HOST}",
    "pool_port": ${BOOT_DATUM_PORT},
    "pool_pubkey": "${boot_pubkey}",
    "pool_pass_workers": ${DATUM_POOL_PASS_WORKERS},
    "pool_pass_full_users": ${DATUM_POOL_PASS_FULL_USERS},
    "pooled_mining_only": ${DATUM_POOLED_MINING_ONLY},
    "protocol_global_timeout": 60
  }
}
EOF

    local datum_after="network-online.target docker.service"
    if (( INSTALL_BITCOIN )); then
        datum_after="${datum_after} bitcoind.service"
    fi

    write_file /etc/systemd/system/datum-gateway.service 0644 root:root <<EOF
[Unit]
Description=DATUM Gateway
After=${datum_after}
Wants=network-online.target

[Service]
User=datum
Group=datum
WorkingDirectory=${datum_dir}
ExecStart=${datum_bin} --config /etc/datum_gateway/config.json
Restart=always
RestartSec=5
LimitNOFILE=1048576
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

    run chown -R datum:datum "$datum_dir"
    run systemctl daemon-reload
    run systemctl enable datum-gateway
    run systemctl restart datum-gateway
}

configure_firewall() {
    (( CONFIGURE_UFW )) || return 0
    command -v ufw >/dev/null 2>&1 || return 0

    if ufw status 2>/dev/null | grep -qi '^Status: active'; then
        log "opening UFW ports"
        run ufw allow 22/tcp
        run ufw allow "${BOOT_WEB_PORT}/tcp"
        run ufw allow "${BOOT_DATUM_PORT}/tcp"
        run ufw allow "${DATUM_STRATUM_PORT}/tcp"
    else
        warn "UFW is not active; not changing firewall rules"
    fi
}

write_install_record() {
    run install -d -m 0750 -o root -g root /etc/grid-pool
    write_file /etc/grid-pool/install.env 0600 root:root <<EOF
GRID_HOME=${GRID_HOME}
GRID_BOOT_REPO_URL=${GRID_BOOT_REPO_URL}
GRID_BOOT_REPO_REF=${GRID_BOOT_REPO_REF}
GRID_DATUM_REPO_URL=${GRID_DATUM_REPO_URL}
GRID_DATUM_REPO_REF=${GRID_DATUM_REPO_REF}
GRID_PRIMARY_IP=${GRID_PRIMARY_IP}
BOOT_PUBLIC_BASE_URL=${BOOT_PUBLIC_BASE_URL}
BOOT_DATUM_PUBLIC_HOST=${BOOT_DATUM_PUBLIC_HOST}
BOOT_DATUM_PUBLIC_PORT=${BOOT_DATUM_PUBLIC_PORT}
BOOT_WEB_PORT=${BOOT_WEB_PORT}
BOOT_DATUM_PORT=${BOOT_DATUM_PORT}
DATUM_STRATUM_PORT=${DATUM_STRATUM_PORT}
DATUM_API_PORT=${DATUM_API_PORT}
DATUM_MAX_CLIENTS_PER_THREAD=${DATUM_MAX_CLIENTS_PER_THREAD}
DATUM_MAX_THREADS=${DATUM_MAX_THREADS}
DATUM_MAX_CLIENTS=${DATUM_MAX_CLIENTS}
BITCOIN_CORE_VERSION=${BITCOIN_CORE_VERSION}
BITCOIN_PRUNE_MB=${BITCOIN_PRUNE_MB}
BITCOIN_DBCACHE_MB=${BITCOIN_DBCACHE_MB_RESOLVED}
BITCOIN_MAX_MEMPOOL_MB=${BITCOIN_MAX_MEMPOOL_MB}
BITCOIN_ASSUMEVALID=${BITCOIN_ASSUMEVALID}
BITCOIN_ASSUMEUTXO_SNAPSHOT=${BITCOIN_ASSUMEUTXO_SNAPSHOT}
BITCOIN_ASSUMEUTXO_STREAM=${BITCOIN_ASSUMEUTXO_STREAM}
BITCOIN_RPC_USER=${BITCOIN_RPC_USER}
BITCOIN_RPC_PASSWORD=${BITCOIN_RPC_PASSWORD}
BITCOIN_RPC_URL=${BITCOIN_RPC_URL}
DATUM_ADMIN_PASSWORD=${DATUM_ADMIN_PASSWORD}
GRID_SWAP_MB=${GRID_SWAP_MB_RESOLVED}
EOF
}

run_self_check() {
    (( INSTALL_BOOT )) || return 0
    local boot_dir="$GRID_HOME/boot-protocol"

    if (( DRY_RUN )); then
        log "would run GridPool self-check"
        return 0
    fi

    if [[ -x "$boot_dir/scripts/boot-self-check.sh" ]]; then
        "$boot_dir/scripts/boot-self-check.sh" "http://127.0.0.1:${BOOT_WEB_PORT}" || true
    fi
}

print_summary() {
    local pubkey=""
    if (( INSTALL_BOOT )) && (( ! DRY_RUN )); then
        pubkey="$(boot_pubkey_from_logs || true)"
    fi

    cat <<EOF

GridPool sovereign stack install complete.

Endpoints:
  Web UI:             ${BOOT_PUBLIC_BASE_URL}
  Public DATUM server: ${BOOT_DATUM_PUBLIC_HOST}:${BOOT_DATUM_PUBLIC_PORT}
  DATUM pool pubkey:  ${pubkey:-see: docker logs boot-portal}
  ASIC Stratum:       ${GRID_PRIMARY_IP}:${DATUM_STRATUM_PORT}
  DATUM local API:    http://127.0.0.1:${DATUM_API_PORT}

Bitcoin mode:
  Bitcoin network:    ${BITCOIN_NETWORK}
  GridPool network:   ${GRID_BOOT_NETWORK_ID}
  Source:             $(if (( INSTALL_BITCOIN )); then printf 'local pruned Bitcoin Core'; else printf 'external RPC'; fi)
  RPC URL:            ${BITCOIN_RPC_URL}
  Wallet:             $(if (( INSTALL_BITCOIN )); then printf 'disabled'; else printf 'external node setting'; fi)
  Prune target:       $(if (( INSTALL_BITCOIN )); then printf '%s MiB' "$BITCOIN_PRUNE_MB"; else printf 'n/a'; fi)
  Dbcache:            $(if (( INSTALL_BITCOIN )); then printf '%s MiB' "$BITCOIN_DBCACHE_MB_RESOLVED"; else printf 'n/a'; fi)
  AssumeUTXO:         ${BITCOIN_ASSUMEUTXO_SNAPSHOT:-not loaded}

Useful commands:
  $(if (( INSTALL_BITCOIN )); then printf 'sudo systemctl status bitcoind --no-pager'; else printf 'curl --user "%s:%s" --data-binary '\''{"jsonrpc":"1.0","id":"curl","method":"getblockchaininfo","params":[]}'\'' -H "content-type:text/plain;" %s' "$BITCOIN_RPC_USER" "$BITCOIN_RPC_PASSWORD" "$BITCOIN_RPC_URL"; fi)
  sudo systemctl status datum-gateway --no-pager
  sudo journalctl -u datum-gateway -f
  cd ${GRID_HOME}/boot-protocol && sudo docker compose -f docker-compose.sovereign.yml logs -f boot-portal
  bitcoin-cli -conf=${BITCOIN_CONF_DIR}/bitcoin.conf -datadir=${BITCOIN_DATA_DIR} getblockchaininfo

Secrets/config record:
  /etc/grid-pool/install.env
EOF
}

main() {
    parse_args "$@"
    confirm_root "$@"
    confirm_inputs

    require_cmd awk
    require_cmd sed

    ensure_swap
    install_dependencies
    install_bitcoin_core
    install_boot
    install_datum
    configure_firewall
    write_install_record
    run_self_check
    print_summary
}

main "$@"
