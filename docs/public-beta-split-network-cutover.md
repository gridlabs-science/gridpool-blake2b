# Public Beta Split-Network Cutover

This is the launch-prep shape for running GridPool as two separate public beta networks.

## Network Roles

- `https://main.gridpool.net`
  - Bitcoin network: `mainnet`
  - GridPool network ID: `mainnet-beta`
  - Round trigger: real GridPool block found (`testing_round_reset_mode = "none"`)
  - Purpose: mainnet soft launch and broad miner onboarding

- `https://test.gridpool.net`
  - Bitcoin network: `testnet4`
  - GridPool network ID: `testnet4-beta`
  - Round trigger: real GridPool block found (`testing_round_reset_mode = "none"`)
  - Purpose: round-rotation, pool-split, and block-found behavior testing

- `https://gridpool.net`
  - Human landing page only.
  - Do not use as a GridPool peer bootstrap endpoint after this cutover.

## Hard Break From Old Public Beta

The old `public-beta` network ID is intentionally abandoned. Official nodes should not bridge or peer with it.

For official seed nodes:

1. Stop GridPool.
2. Back up existing `pool_state*.json` and history files.
3. Move the old state files out of the active state path.
4. Configure the node for either `mainnet-beta` or `testnet4-beta`.
5. Start GridPool and allow it to initialize a fresh genesis/Foundation-only state.

This creates a clean genesis-era display for the public beta and avoids carrying stale deterministic-test rounds forward.

## Mainnet Seed Config

Use this for the first public mainnet seed:

```json
{
  "public_base_url": "https://main.gridpool.net",
  "datum_public_host": "datum.main.gridpool.net",
  "datum_public_port": 3008,
  "bitcoin_network": "mainnet",
  "boot_network_id": "mainnet-beta",
  "testing_round_reset_mode": "none",
  "bootstrap_peers": [],
  "pool_payout_script": "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y",
  "coinbase_tag": "Grid Pool"
}
```

When a second mainnet seed is online, add each public seed to the other's `bootstrap_peers`.

## Testnet4 Seed Config

Use this for the first public testnet4 seed:

```json
{
  "public_base_url": "https://test.gridpool.net",
  "datum_public_host": "datum.test.gridpool.net",
  "datum_public_port": 3009,
  "bitcoin_network": "testnet4",
  "boot_network_id": "testnet4-beta",
  "testing_round_reset_mode": "none",
  "bootstrap_peers": [],
  "pool_payout_script": "mxt9bYPtfBdzoTeZcHr23QgL4Un45PVvF5",
  "coinbase_tag": "Grid Pool"
}
```

Use any valid testnet4 payout address for `pool_payout_script`; the value above is one of the current test addresses.

## Cloudflare Setup

If using Cloudflare Tunnel on the dev machine:

1. Add a public hostname for `main.gridpool.net`.
2. Route it to the dev machine GridPool WebUI, usually `http://localhost:5000`.
3. Publish `datum.main.gridpool.net` as a DNS-only direct TCP endpoint, or route it through a true TCP proxy. For the current home-router setup, forward public TCP `3008` to the main node DATUM listener.
4. Add a public hostname for `test.gridpool.net`.
5. Route it to the Pi 5 GridPool WebUI, usually `http://192.168.1.198:5000` or the Pi's Tailscale IP if that is the stable path from the tunnel host.
6. Publish `datum.test.gridpool.net` as a DNS-only direct TCP endpoint, or route it through a true TCP proxy. For the current home-router setup, forward public TCP `3009` to the test node DATUM listener.
7. Keep `gridpool.net` free for the separate marketing/landing site.

Cloudflare Tunnel works well for the WebUI/API hostnames. Do not assume a `tcp://` Tunnel published application is reachable by arbitrary DATUM clients; normal DATUM clients need a plain TCP path unless they are explicitly using a Cloudflare client-side TCP proxy.

Recommended DNS records if not using tunnel hostnames:

- `main.gridpool.net`: proxied CNAME to the Cloudflare Tunnel target for the dev machine.
- `test.gridpool.net`: proxied CNAME to the Cloudflare Tunnel target for the Pi 5 route.
- `datum.main.gridpool.net`: DNS-only `A`/`AAAA` record to the direct TCP endpoint for mainnet DATUM, public port `3008`.
- `datum.test.gridpool.net`: DNS-only `A`/`AAAA` record to the direct TCP endpoint for testnet4 DATUM, public port `3009`.
- `gridpool.net`: landing-page host.

## Verification

For each seed:

```bash
curl -fsS https://main.gridpool.net/api/mining/share-advice
curl -fsS https://main.gridpool.net/api/network/summary
curl -fsS https://test.gridpool.net/api/mining/share-advice
curl -fsS https://test.gridpool.net/api/network/summary
```

Expected:

- Mainnet reports `mainnet-beta` and `mainnet`.
- Testnet4 reports `testnet4-beta` and `testnet4`.
- No node reports `public-beta`.
- No official node uses `https://gridpool.net` as a peer seed.
- State starts fresh after the backed-up old state files are removed from the active path.
