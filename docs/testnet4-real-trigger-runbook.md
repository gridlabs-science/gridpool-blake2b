# Testnet4 Real-Trigger Runbook

Use this when moving a small Grid Pool test cluster from deterministic round resets to real GridPool block-triggered rounds on Bitcoin testnet4.

## Goal

Run at least two Boot nodes, preferably three, on an isolated testnet4 Grid Pool network:

- Bitcoin chain: `testnet4`
- Boot network ID: `testnet4-beta`
- Round trigger: real GridPool block found, not deterministic test trigger
- Boot state file: separate from mainnet, for example `pool_state.testnet4.json`
- Payout addresses: testnet addresses only (`tb1...`, testnet P2PKH, or testnet P2SH)

Do not reuse mainnet Boot state or mainnet peer seeds for this test.

## Required Code Support

Boot supports:

- `bitcoin_network: "mainnet"` or `bitcoin_network: "testnet4"`
- Network-aware address validation for local pool payout config.
- Network-aware slot-0 attribution when decoding share coinbase outputs.
- Network-aware payout-output matching.
- Installer support via `--bitcoin-network testnet4`.

`testnet`, `testnet3`, and `test` are accepted as aliases, but they normalize to `testnet4` because this project only intends to support testnet4 for testing.

## Minimal Boot Config

Each testnet4 Boot node should use a config like:

```json
{
  "bitcoin_network": "testnet4",
  "boot_network_id": "testnet4-beta",
  "testing_round_reset_mode": "none",
  "testing_round_reset_low_nibble_threshold": 0,
  "pool_payout_script": "tb1...",
  "enable_peer_sync": true,
  "bootstrap_peers": [
    "http://FIRST-TESTNET-BOOT-NODE:5000"
  ],
  "min_diff": 300,
  "coinbase_tag": "Grid Pool"
}
```

The first node can start with an empty `bootstrap_peers` list. Later nodes should point at the first node, then peer discovery should spread the rest.

## Installer Example

For a full sovereign node with local Bitcoin Core:

```bash
sudo ./scripts/install-sovereign-stack.sh \
  --bitcoin-network testnet4 \
  --payout-address tb1... \
  --bootstrap-peers "" \
  --yes
```

For a second node:

```bash
sudo ./scripts/install-sovereign-stack.sh \
  --bitcoin-network testnet4 \
  --payout-address tb1... \
  --bootstrap-peers http://FIRST-TESTNET-BOOT-NODE:5000 \
  --yes
```

The installer automatically switches defaults for testnet4:

- `GRID_BOOT_NETWORK_ID=testnet4-beta`, unless explicitly overridden.
- `GRID_BOOT_BOOTSTRAP_PEERS=""`, unless explicitly overridden.
- `GRID_BOOT_STATE_FILE=pool_state.testnet4.json`, unless explicitly overridden.
- Bitcoin Core config includes `chain=testnet4`.
- Local Bitcoin RPC is pinned to `127.0.0.1:8332` with `rpcport=8332`, avoiding chain-specific default port ambiguity.

## Manual Cutover Checklist

Use this path for existing laptop/Pi nodes if you do not want to reinstall.

1. Stop Boot and DATUM.
2. Stop or reconfigure Bitcoin Core/Knots for `chain=testnet4`.
3. Make sure Bitcoin RPC credentials and `rpcurl` in DATUM point at the testnet4 node.
4. Set DATUM `mining.pool_address` to a testnet address.
5. Set DATUM pool host/port to the local testnet4 Boot node.
6. In Boot config, set `bitcoin_network` to `testnet4`.
7. In Boot config, set `boot_network_id` to `testnet4-beta`.
8. In Boot config, set `pool_payout_script` to the same testnet address used by DATUM.
9. In Boot config, set `testing_round_reset_mode` to `none`.
10. Use a separate state file, for example `BOOT_PORTAL_STATE_PATH=/data/pool_state.testnet4.json`.
11. Remove or isolate old mainnet state before starting the testnet cluster.
12. Start the first Boot node with no bootstrap peers.
13. Start DATUM on the first node and confirm it gets fresh testnet4 templates.
14. Start second and third Boot nodes with bootstrap peer pointed at the first node.
15. Confirm `/api/network/summary` shows the same `boot_network_id`, current tip, and candidate/current state across nodes.
16. Point a small amount of hashrate at each DATUM node.
17. Confirm shares are accepted and attributed to `tb1...` addresses.
18. Wait for an actual GridPool testnet4 block to trigger round rotation.

## Safety Checks

Before starting a node:

- A mainnet payout address in `bitcoin_network=testnet4` config should fail validation.
- A testnet payout address in `bitcoin_network=mainnet` config should fail validation.
- Nodes with different `boot_network_id` values should not converge.
- Mainnet public seed `https://gridpool.net` should not be used for testnet4 unless it is intentionally serving testnet4.

## Important Limitation

Bitcoin scriptPubKeys do not encode the address network. The same witness program can be displayed as a `bc1...` address on mainnet or a `tb1...` address on testnet.

That means Boot can reliably validate configured payout addresses against `bitcoin_network`, and it can decode slot-0 attribution in the configured network. But if a direct share submitter sends only raw coinbase/header data, Boot cannot infer whether the miner originally thought of that script as `bc1...` or `tb1...`; it can only decode the script according to the local node's configured network.

For UI/API warnings, direct clients should submit their intended payout address metadata alongside the proof. Boot can compare that metadata to slot-0 attribution and warn on mismatch, while still using slot-0 as the authoritative trustless attribution.
