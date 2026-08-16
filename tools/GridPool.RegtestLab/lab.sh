#!/usr/bin/env bash
set -euo pipefail

tool_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
lab_root="${GRIDPOOL_LAB_ROOT:-/home/gridlabs/gridpool-regtest-lab}"
gridpool_source="${GRIDPOOL_SOURCE:-$(cd "$tool_root/../.." && pwd)}"
sv2_source="${GRIDPOOL_SV2_SOURCE:-/home/gridlabs/gridpool-sv2-pool}"
compose_file="$tool_root/compose.yaml"
env_file="$lab_root/lab.env"
sv2_image="gridpool-regtest-sv2:baseline"

export GRIDPOOL_LAB_ROOT="$lab_root"
export GRIDPOOL_SOURCE="$gridpool_source"
export GRIDPOOL_SV2_SOURCE="$sv2_source"
export GRIDPOOL_TOOL_ROOT="$tool_root"

compose() {
  docker compose --env-file "$env_file" -f "$compose_file" --project-directory "$lab_root" "$@"
}

require_tools() {
  command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }
  command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }
  test -x /usr/local/bin/bitcoind || { echo "Bitcoin Core bitcoind is missing" >&2; exit 1; }
  test -x /usr/local/bin/bitcoin-cli || { echo "Bitcoin Core bitcoin-cli is missing" >&2; exit 1; }
}

prepare() {
  require_tools
  mkdir -p "$lab_root"/{bitcoin,core-dist/bin,node-a,node-b,node-c,sv2,artifacts}
  chmod 700 "$lab_root" "$lab_root"/{bitcoin,node-a,node-b,node-c,sv2,artifacts}

  install -m 0755 /usr/local/bin/bitcoind "$lab_root/core-dist/bin/bitcoind"
  install -m 0755 /usr/local/bin/bitcoin-cli "$lab_root/core-dist/bin/bitcoin-cli"

  if [[ ! -f "$env_file" ]]; then
    umask 077
    cat > "$env_file" <<EOF
GRIDPOOL_LAB_ROOT=$lab_root
GRIDPOOL_SOURCE=$gridpool_source
GRIDPOOL_SV2_SOURCE=$sv2_source
GRIDPOOL_TOOL_ROOT=$tool_root
GRIDPOOL_TAG=baseline
RPC_USER=gridpool_regtest
RPC_PASSWORD=$(openssl rand -hex 32)
LAB_NETWORK_ID=gridpool-regtest-v22-$(openssl rand -hex 6)
LAB_PAYOUT_ADDRESS=
AUTHORITY_PUBLIC_KEY=
AUTHORITY_SECRET_KEY=
EOF
  fi

  set -a
  source "$env_file"
  set +a
  sed -i "s|^GRIDPOOL_SOURCE=.*|GRIDPOOL_SOURCE=$gridpool_source|" "$env_file"
  sed -i "s|^GRIDPOOL_SV2_SOURCE=.*|GRIDPOOL_SV2_SOURCE=$sv2_source|" "$env_file"
  sed -i "s|^GRIDPOOL_TOOL_ROOT=.*|GRIDPOOL_TOOL_ROOT=$tool_root|" "$env_file"
  chmod 600 "$env_file"
}

ensure_sv2_keys() {
  set -a
  source "$env_file"
  set +a
  test -d "$sv2_source" || { echo "GRIDPOOL_SV2_SOURCE not found: $sv2_source" >&2; exit 1; }
  if [[ -n "${AUTHORITY_PUBLIC_KEY:-}" && -n "${AUTHORITY_SECRET_KEY:-}" ]]; then
    return
  fi
  echo "Building the disposable SV2 lab image to generate authority keys..."
  docker build --build-arg APP=pool_sv2 -f "$sv2_source/docker/Dockerfile" -t "$sv2_image" "$sv2_source" >/dev/null
  local key_output
  key_output="$(docker run --rm "$sv2_image" --generate-authority-keypair)"
  local public_key secret_key
  public_key="$(awk -F= '$1 == "authority_public_key" {print $2}' <<<"$key_output")"
  secret_key="$(awk -F= '$1 == "authority_secret_key" {print $2}' <<<"$key_output")"
  [[ -n "$public_key" && -n "$secret_key" ]] || {
    echo "SV2 authority key generation returned an unexpected result" >&2
    exit 1
  }
  sed -i "s|^AUTHORITY_PUBLIC_KEY=.*|AUTHORITY_PUBLIC_KEY=$public_key|" "$env_file"
  sed -i "s|^AUTHORITY_SECRET_KEY=.*|AUTHORITY_SECRET_KEY=$secret_key|" "$env_file"
  chmod 600 "$env_file"
}

render_configs() {
  set -a
  source "$env_file"
  set +a
  test -n "${LAB_PAYOUT_ADDRESS:-}" || {
    echo "LAB_PAYOUT_ADDRESS is empty; run init first" >&2
    exit 1
  }
  "$tool_root/render-config.sh" node-a '["http://node-b:5000","http://node-c:5000"]' "$lab_root/node-a/config.json" 5000
  "$tool_root/render-config.sh" node-b '["http://node-a:5000","http://node-c:5000"]' "$lab_root/node-b/config.json" 5000
  "$tool_root/render-config.sh" node-c '["http://node-a:5000","http://node-b:5000"]' "$lab_root/node-c/config.json" 5000
}

wait_for_rpc() {
  set -a
  source "$env_file"
  set +a
  until compose exec -T bitcoin bitcoin-cli -regtest \
    -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" getblockchaininfo >/dev/null 2>&1; do
    sleep 1
  done
}

init_chain() {
  prepare
  compose build bitcoin
  compose up -d bitcoin
  wait_for_rpc
  set -a
  source "$env_file"
  set +a
  if ! compose exec -T bitcoin bitcoin-cli -regtest \
    -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" \
    listwallets | jq -e 'index("lab") != null' >/dev/null; then
    compose exec -T bitcoin bitcoin-cli -regtest \
      -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" \
      -named createwallet wallet_name=lab load_on_startup=true >/dev/null
  fi
  if [[ -z "${LAB_PAYOUT_ADDRESS:-}" ]]; then
    export LAB_PAYOUT_ADDRESS="$(compose exec -T bitcoin bitcoin-cli -regtest \
      -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" -rpcwallet=lab \
      getnewaddress "" bech32 | tr -d '\r')"
    sed -i "s|^LAB_PAYOUT_ADDRESS=.*|LAB_PAYOUT_ADDRESS=$LAB_PAYOUT_ADDRESS|" "$env_file"
  fi
  local height
  height="$(compose exec -T bitcoin bitcoin-cli -regtest \
    -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" getblockcount | tr -d '\r')"
  if (( height < 101 )); then
    compose exec -T bitcoin bitcoin-cli -regtest \
      -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" -rpcwallet=lab \
      generatetoaddress $((101 - height)) "$LAB_PAYOUT_ADDRESS" >/dev/null
  fi
  render_configs
}

start() {
  init_chain
  compose build node-a
  compose up -d node-a node-b node-c
  echo "GridPool regtest lab started; observer ports are 15001, 15002, and 15003."
}

start_sv2() {
  start
  set -a
  source "$env_file"
  set +a
  ensure_sv2_keys
  set -a
  source "$env_file"
  set +a
  mkdir -p "$lab_root/sv2/proof-spool"
  "$tool_root/render-sv2-config.sh" "$lab_root/sv2/pool-config.toml"
  printf '%s\n' 'gridpool-regtest-adapter' > "$lab_root/sv2/gridpool-adapter.token"
  chmod 600 "$lab_root/sv2/pool-config.toml" "$lab_root/sv2/gridpool-adapter.token"
  compose --profile sv2 build sv2 miner
  compose --profile sv2 up -d sv2 miner
  echo "SV2 regtest lab started; client port is 134265 and synthetic miner is running."
}

status() {
  prepare
  compose ps
  for port in 15001 15002 15003; do
    curl -fsS "http://127.0.0.1:$port/api/network/summary" 2>/dev/null |
      jq -c '{networkId,bitcoinNetwork,currentTipBlockHeight,currentStateId,candidateStateId,peerCount,miningWorkSafe}' || true
  done
}

logs() { prepare; compose logs --tail "${1:-120}"; }

stop() { prepare; compose down; }

reset() {
  [[ "${1:-}" == "--confirm" ]] || { echo "refusing reset without --confirm" >&2; exit 2; }
  prepare
  local stamp
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "$lab_root/artifacts/$stamp"
  compose logs --no-color > "$lab_root/artifacts/$stamp/compose.log" 2>&1 || true
  compose down
  find "$lab_root/node-a" "$lab_root/node-b" "$lab_root/node-c" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
  rm -rf "$lab_root/bitcoin" "$lab_root/sv2"
  mkdir -p "$lab_root/bitcoin" "$lab_root/sv2"
  chmod 700 "$lab_root/bitcoin" "$lab_root/sv2"
  sed -i 's/^LAB_PAYOUT_ADDRESS=.*/LAB_PAYOUT_ADDRESS=/' "$env_file"
}

case "${1:-}" in
  prepare) prepare ;;
  init) init_chain ;;
  start) start ;;
  start-sv2) start_sv2 ;;
  status) status ;;
  logs) shift; logs "$@" ;;
  stop) stop ;;
  reset) shift; reset "$@" ;;
  compose) shift; prepare; compose "$@" ;;
  *) echo "usage: $0 {prepare|init|start|start-sv2|status|logs|stop|reset --confirm|compose ...}" >&2; exit 2 ;;
esac
