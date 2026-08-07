# Node Bootstrap And Critical Configuration

This is the canonical rebuild checklist for GridPool node operators and automation agents. Never delete or replace an existing state file or node key as part of bootstrap.

## Automatic Safety

- A node with no valid `pool_payout_script` starts in setup-only mode when the
  Web UI is enabled. Only `/setup`, `/setup.css`, `/health/live`, and
  `/health/ready` remain available; Bitcoin notification, DATUM, peer relay,
  UDP, pulse, synchronization, and local-adapter polling services do not start.
  Saving the address uses an atomic local override and requires a process
  restart before operational services are enabled. Headless nodes fail config
  validation instead of entering setup mode.
- `mainnet-beta` nodes with an empty bootstrap list seed `main.gridpool.net`, `dallas.gridpool.net`, and `detroit.gridpool.net` (excluding their own endpoint).
- Pulse proofs default on. Production `mainnet-beta` rejects an explicit pulse-off configuration.
- Peer polling, share relay, and chain-tip relay run under independent supervisors. WebSocket send-lock and frame writes time out, close the stuck session, and allow HTTP share fallback.
- `/health/ready` becomes unavailable when peer polling is stale for `peer_loop_stale_seconds` (default 600).
- With active local DATUM sessions, pulse proofs, and peer sync enabled, `outbound_relay_stale_seconds` (default 300) raises a visible health warning when no outbound share/pulse delivery succeeds. It does not stop coinbaser service: doing so can force DATUM into solo fallback and prevent automatic recovery. `pause_mining_on_outbound_relay_stale` is retained as a deprecated configuration key and should be `false`.
- `/api/network/summary` exposes node identity, endpoint, release, loop timestamps/faults, queue depth, pulse/outbound health, and configuration warnings. DATUM/relay diagnosis includes the last valid local DATUM share, successful coinbaser response, session close reason, relay enqueue, and successful UDP/WebSocket/HTTP share sends.

Peer-observed acceptance acknowledgements are not yet authenticated end-to-end; the current health signal proves a bounded local transport send/HTTP acceptance, not that a remote node admitted a candidate-changing proof. An authenticated proof-ID ack/reject remains follow-up work.

## Humans And Agents Must Set

- `public_base_url`: the real externally reachable HTTPS base URL for a dialable production node. Placeholder/example/localhost values fail production startup.
- `datum_public_host` and optional public port: the real DATUM endpoint advertised to miners.
- `pool_payout_script`: the operator-controlled address on the selected Bitcoin network.
  Appliance packages must treat this as a package-wide setting and render the
  same fallback address into GridPool and native SV2 before either mining
  service becomes ready. The first-run `/setup` page cannot rotate an already
  configured address; use the authenticated package action or edit the local
  configuration offline and restart all mining services.
- Absolute, persistent paths for state, history, local-adapter token, and service working directory. Do not store state on an ephemeral container layer.
- Select `bitcoin_notification_mode` explicitly. Sovereign mining nodes use
  `attached-node` with authenticated RPC plus both ZMQ topics; intentional
  node-less relays use `external-fallback`.
- Configure RPC/ZMQ addresses for the actual native, shared-bridge,
  host-gateway, or remote-LAN topology. Container loopback is not the host.
- Run `scripts/check-bitcoin-connectivity.sh` before enabling miners. Confirm
  RPC is synchronized and `getzmqnotifications` advertises both topics.
- Ed25519 and X25519 private keys. Back them up offline with the state database; a changed key produces a changed public `nodeId`.
- `GRIDPOOL_RELEASE_VERSION` when the build informational version lacks a git suffix. Use a value such as `2.2.0+g<commit>`; bare `1.0.0`/`dev` is warned in the summary.
- Strong `admin_api_key` through an untracked local override or environment when the admin API is enabled. Never commit secrets.

V2.2 activation remains height **959500**. Rebuilds do not justify changing it.

## Post-Rebuild Validation

```bash
curl -fsS https://YOUR_NODE/health/ready | jq
curl -fsS https://YOUR_NODE/api/network/summary | jq '{nodeId,selfEndpoint,releaseVersion,serviceStartedUtc,configWarnings,bitcoinNotification,pulseProofsEnabled,peerLoopsHealthy,outboundRelayHealthy,activeDatumSessionCount,lastDatumSessionOpenedUtc,lastDatumHelloReceivedUtc,lastDatumCoinbaserRequestUtc,lastSuccessfulDatumCoinbaserResponseUtc,lastValidLocalDatumShareUtc,lastDatumSessionClosedUtc,lastDatumSessionCloseReason,lastShareRelayQueuedUtc,lastUdpShareRelayUtc,lastWebSocketShareRelayUtc,lastHttpShareRelayUtc,lastSuccessfulOutboundRelayUtc,shareRelayQueueDepth,localDatumDiagnostics}'
systemctl status bootserverapp.service --no-pager
journalctl -u bootserverapp.service -n 200 --no-pager
```

Verify the restored `nodeId` matches the pre-rebuild record, `selfEndpoint` is public and correct, `configWarnings` is empty or understood, pulses are enabled, poll timestamps advance, outbound relay advances after a local pulse/share, peer current state/tip fields advance, and the node agrees with at least two public peers on current state and active snapshot before enabling miners.

## Copy-Paste Agent Checklist

1. Preserve and back up node keys, state, history, and local overrides; do not wipe state.
2. Confirm absolute persistent paths and file ownership.
3. Set the real public/DATUM endpoints, payout address, Bitcoin/ZMQ endpoints, and release provenance.
4. Leave V2.2 activation at 959500, pulses enabled, peer-loop readiness enabled, and outbound relay health monitoring enabled. Keep the deprecated outbound-stale mining pause disabled.
5. Start the node; compare `nodeId` to the saved identity.
6. Require ready HTTP 200, advancing peer poll and outbound relay timestamps, no unexplained config warnings, and agreement with two public nodes.
7. Run the health monitor once manually, then confirm its timer and Telegram alert path.
8. Only then reconnect or enable local mining traffic.
