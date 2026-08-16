#!/usr/bin/env bash
set -euo pipefail

output_path="${1:?output path required}"
: "${LAB_PAYOUT_ADDRESS:?set LAB_PAYOUT_ADDRESS}"
: "${AUTHORITY_PUBLIC_KEY:?set AUTHORITY_PUBLIC_KEY}"
: "${AUTHORITY_SECRET_KEY:?set AUTHORITY_SECRET_KEY}"
: "${RPC_USER:?set RPC_USER}"
: "${RPC_PASSWORD:?set RPC_PASSWORD}"

umask 077
cat > "$output_path" <<EOF
authority_public_key = "$AUTHORITY_PUBLIC_KEY"
authority_secret_key = "$AUTHORITY_SECRET_KEY"
cert_validity_sec = 3600
listen_address = "0.0.0.0:34265"
coinbase_reward_script = "addr($LAB_PAYOUT_ADDRESS)"
server_id = 1
pool_signature = "GridPool regtest lab"
shares_per_minute = 6.0
share_batch_size = 1
monitoring_address = "0.0.0.0:34290"
monitoring_cache_refresh_secs = 5

[gridpool]
node_url = "http://node-a:5000"
fallback_payout_address = "$LAB_PAYOUT_ADDRESS"
operator_fee_percent = 0.0
adapter_token_file = "/data/gridpool-adapter.token"
proof_spool_dir = "/data/proof-spool"
refresh_seconds = 2
telemetry_flush_seconds = 2
fee_cycle_seconds = 60

[template_provider_type.BitcoinJsonRpc]
url = "http://bitcoin:18443"
username = "$RPC_USER"
password = "$RPC_PASSWORD"
timeout_seconds = 30
retry_seconds = 1
min_interval = 1
EOF

printf 'generated %s\n' "$output_path"
