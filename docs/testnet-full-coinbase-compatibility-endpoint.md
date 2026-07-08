# Testnet Full-Coinbase Compatibility Endpoint

Status: beta lab runbook.

This endpoint is for testing whether miner firmware, Stratum gateways, and
hashrate-rental services can handle a mature GridPool 300-slot coinbase. It is
testnet-only and does not pay tester-supplied payout addresses.

## Endpoint Roles

- GridPool UI/API: `https://test.gridpool.net`
- Compatibility page: `https://test.gridpool.net/compat`
- ASIC Stratum V1 test endpoint: `stratum.test.gridpool.net:3334`
- GridPool DATUM-upstream endpoint for DATUM gateways:
  `datum.test.gridpool.net:3009`

Do not point ASICs directly at `datum.test.gridpool.net:3009`. That port is
for DATUM Gateway clients. ASICs should use the Stratum V1 endpoint.

## GridPool Testnet Config

Use a separate lab/testnet config and state directory if possible:

```json
{
  "bitcoin_network": "testnet4",
  "node_mode": "staging",
  "boot_network_id": "testnet4-compat",
  "coinbase_uncondensed_outputs_enabled": true,
  "compatibility_page_enabled": true,
  "compatibility_stratum_public_host": "stratum.test.gridpool.net",
  "compatibility_stratum_public_port": 3334,
  "compatibility_unsafe_override_phrase": "UNSAFE_FULL_COINBASE"
}
```

`coinbase_uncondensed_outputs_enabled` is intentionally rejected in production
mode. It exists to force worst-case 300-output templates during compatibility
testing.

## DATUM Gateway Config

Use the DATUM branch that includes forced coinbase selection and unsafe override
support.

Recommended DATUM settings:

```json
{
  "mining": {
    "pool_address": "YOUR_FIXED_TESTNET_ADDRESS"
  },
  "stratum": {
    "listen_port": 23334,
    "fingerprint_miners": true,
    "coinbase_selection_mode": "force",
    "coinbase_selection": "yuge"
  },
  "datum": {
    "pool_host": "127.0.0.1",
    "pool_port": 3008,
    "pooled_mining_only": true,
    "pool_pass_workers": true,
    "pool_pass_full_users": false
  },
  "api": {
    "listen_port": 7152,
    "admin_password": "STRONG_LOCAL_ONLY_PASSWORD",
    "modify_conf": false
  }
}
```

Keep the DATUM API/UI private. Bind or firewall it to localhost/tailnet. The
public compatibility page reads a sanitized telemetry file, not DATUM directly.

## Router / DNS

Forward public TCP `3334` to the testnet host's DATUM Stratum V1 listener
`23334`.

`stratum.test.gridpool.net` should resolve to the public IP handling that port.
Use DNS-only records or another plain TCP path. Do not use an HTTP-only
Cloudflare tunnel for Stratum.

## Telemetry Collector

Run the collector on the same host as DATUM:

```bash
export DATUM_API_PASSWORD='STRONG_LOCAL_ONLY_PASSWORD'
export GRIDPOOL_COMPAT_SALT='random-private-salt'

node scripts/collect-datum-compatibility.mjs \
  --datum-url http://127.0.0.1:7152/clients.json \
  --user gridpool \
  --password-env DATUM_API_PASSWORD \
  --out /path/to/gridpool/data/compatibility_status.json \
  --raw-log ~/.local/state/gridpool-compatibility/raw-datum-clients.jsonl
```

Run it every minute with `cron` or a systemd timer. The `--out` path must match
GridPool's `compatibility_telemetry_path`, or the default
`compatibility_status.json` next to `pool_state.json`.

The public telemetry file is sanitized:

- remote IPs are hashed with `GRIDPOOL_COMPAT_SALT`;
- raw passwords are never stored;
- raw DATUM usernames are reduced to `testerTag` and `workerName`;
- the unsafe override is recorded as a boolean.

The optional `--raw-log` is local-only evidence for debugging. Do not publish
it.

## Tester Instructions

Use:

```text
URL: stratum+tcp://stratum.test.gridpool.net:3334
Username: testerTag.workerName
Password: anything
```

DATUM may reject known incompatible firmware before sending large work. To
intentionally test a firmware version anyway, set the password to include:

```text
UNSAFE_FULL_COINBASE
```

This is risky. Some firmware can hard-lock when sent oversized coinbase
templates. Only use unsafe mode when you can recover the miner.

## Success Criteria

- Miner stays connected for at least `15` minutes.
- `/compat` shows the tester tag, worker name, firmware/user-agent, selected
  coinbase class, and whether unsafe mode was used.
- DATUM does not reconnect-loop.
- GridPool does not show persistent firmware-truncation rejects.
- If unsafe mode is not used and DATUM fingerprints the miner as too small, the
  miner is refused before oversized work is sent.
