# Public Bitcoin Notification Rollout

Status: Main canary active; network-wide canary not started.

All state directories, identity keys, DATUM data, and adapter data must be
backed up and preserved. Do not wipe state to perform this rollout.

## Common Deployment

1. Pull the same reviewed GridPool commit on every node.
2. Rebuild without cache or deploy the image tagged for that exact commit.
3. Preserve the existing `/data` mount and node identity.
4. Apply the node-specific local override below.
5. Run the attached-node preflight where applicable.
6. Require `/health/ready` HTTP 200 and inspect:

```bash
curl -fsS http://127.0.0.1:5000/api/network/summary |
  jq '{nodeId,selfEndpoint,releaseVersion,currentTipBlockHeight,currentStateId,candidateStateId,bitcoinNotification,configWarnings}'
```

## Main

Main uses native systemd GridPool plus Core 31.1:

- RPC: `http://127.0.0.1:8334`, cookie authentication.
- ZMQ hashblock: `tcp://127.0.0.1:28342`.
- ZMQ rawblock: `tcp://127.0.0.1:28343`.

The canary started after the attached-node coordinator reported synchronized
RPC, one publisher for each topic, and no warning.

## Evomining

Restore valid public HTTPS first. Place `boot-portal` and `zima-bitcoind` on one
stable user-defined Docker network. Use the Bitcoin service name, not
`127.0.0.1`, a transient container IP, or the bridge gateway:

```json
{
  "bitcoin_notification_mode": "attached-node",
  "bitcoin_rpc_url": "http://zima-bitcoind:8332",
  "bitcoin_rpc_username": "LOCAL_RPC_USER",
  "bitcoin_rpc_password": "LOCAL_RPC_PASSWORD",
  "bitcoin_rpc_cookie_file": "",
  "bitcoin_zmq_endpoint": "tcp://zima-bitcoind:28332",
  "bitcoin_zmq_rawblock_endpoint": "tcp://zima-bitcoind:28333"
}
```

Bitcoin must bind RPC/ZMQ to that private bridge and restrict RPC access to the
bridge subnet. `getzmqnotifications` must show exactly one `pubhashblock` and
one `pubrawblock`. Preserve Evomining's state, identity, and DATUM volumes.

## Detroit

Set:

```json
{
  "public_base_url": "https://detroit.gridpool.net",
  "bitcoin_notification_mode": "attached-node"
}
```

Use either the Bitcoin Compose service DNS name on a shared network or
`host.docker.internal` with `host-gateway`; configure RPC and both ZMQ endpoints
consistently. The public summary must not advertise a LAN address. Verify UDP
5001 and ensure outbound relay timestamps advance.

## Dallas

Dallas is deliberately node-less:

```json
{
  "bitcoin_notification_mode": "external-fallback"
}
```

It should expose `authorityClass=external-observer`, remain ready without local
RPC/ZMQ, and emit no missing-ZMQ warning. Deploy a build with commit provenance.

## Canary And Soak Gate

Do not call the soak started until:

- Main, Evomining, and Detroit each report synchronized attached RPC.
- Each attached node shows exactly one publisher for both required topics.
- Dallas reports explicit external fallback.
- All four nodes run one commit and advertise correct public identities.
- Current Bitcoin tip and current GridPool state agree.
- Temporary candidate differences converge normally.
- UDP/WebSocket sessions and outbound relay timestamps are healthy.

After those conditions hold, run 24 hours without restart, manual state repair,
or topology edits. Reset the clock after any intervention. Begin the seven-day
soak only after the canary passes.
