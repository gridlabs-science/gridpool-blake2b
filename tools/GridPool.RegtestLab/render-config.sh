#!/usr/bin/env bash
set -euo pipefail

node_name="$1"
peer_urls="$2"
output_path="$3"
web_port="$4"

: "${LAB_NETWORK_ID:?set LAB_NETWORK_ID}"
: "${LAB_PAYOUT_ADDRESS:?set LAB_PAYOUT_ADDRESS}"
: "${RPC_USER:?set RPC_USER}"
: "${RPC_PASSWORD:?set RPC_PASSWORD}"

jq -n \
  --arg node_name "$node_name" \
  --arg network_id "$LAB_NETWORK_ID" \
  --arg payout "$LAB_PAYOUT_ADDRESS" \
  --arg rpc_user "$RPC_USER" \
  --arg rpc_password "$RPC_PASSWORD" \
  --argjson peers "$peer_urls" \
  --argjson web_port "$web_port" \
  '{
    NotificationSource: "BitcoinZmq",
    bitcoin_notification_mode: "attached-node",
    bitcoin_network: "regtest",
    bitcoin_rpc_url: "http://bitcoin:18443",
    bitcoin_rpc_username: $rpc_user,
    bitcoin_rpc_password: $rpc_password,
    bitcoin_zmq_endpoint: "tcp://bitcoin:28332",
    bitcoin_zmq_rawblock_endpoint: "tcp://bitcoin:28333",
    bitcoin_rpc_poll_interval_seconds: 1,
    bitcoin_rpc_timeout_seconds: 3,
    bitcoin_rpc_lag_grace_seconds: 1,
    boot_network_id: $network_id,
    boot_protocol_version: 22,
    v22_activation_block_height: 0,
    node_mode: "development",
    pool_payout_script: $payout,
    grid_labs_support_fee_enabled: false,
    coinbase_tag: "GridPool private regtest lab",
    WebUI_Port_http: $web_port,
    WebUI_Port_https: 0,
    Datum_Port: 3008,
    enable_web_ui: true,
    enable_legacy_ui: false,
    public_base_url: ("http://" + $node_name + ":5000"),
    datum_public_host: "",
    datum_public_port: 0,
    bootstrap_peers: $peers,
    enable_peer_sync: true,
    peer_allow_private_advertisements: true,
    peer_sync_interval_seconds: 1,
    peer_request_timeout_seconds: 3,
    peer_session_target: 2,
    max_peers: 8,
    peer_outbound_target: 2,
    peer_share_relay_target: 2,
    enable_peer_udp_fast_relay: false,
    peer_udp_bind_port: 0,
    peer_udp_port: 1,
    enable_pulse_proofs: false,
    enable_peer_tip_stale_protection: false,
    pause_mining_on_outbound_relay_stale: false,
    local_adapter_token_file: "data/local-adapter.token",
    min_diff: 1,
    winners_list_size: 299,
    work_set_reserve_multiplier: 3,
    admin_api_key: "",
    enable_admin_api: false,
    testing_round_reset_mode: "none"
  }' > "$output_path"

chmod 600 "$output_path"
