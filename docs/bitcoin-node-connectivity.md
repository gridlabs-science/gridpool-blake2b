# Bitcoin Node Connectivity

GridPool supports two explicit Bitcoin notification modes:

- `attached-node`: an authenticated Bitcoin RPC connection is the local
  correctness authority. ZMQ supplies low-latency `hashblock` and `rawblock`
  notifications, while RPC polls every five seconds and reconciles missed
  notifications.
- `external-fallback`: Mempool.Space supplies best-effort notifications to a
  deliberately node-less relay. This mode is not advertised as sovereign local
  template validation.

Peer headers, compact UDP, FIBRE, and external mining-pool hints are optional
mining or operations accelerators. They cannot activate payout snapshots or
replace local full-node validation.

## Attached-Node Configuration

Use either RPC username/password or a cookie file, never both. Keep credentials
in `boot_portal_config.local.json` with mode `0600`, or inject them through a
package secret mechanism.

```json
{
  "NotificationSource": "BitcoinZmq",
  "bitcoin_notification_mode": "attached-node",
  "bitcoin_rpc_url": "http://127.0.0.1:8332",
  "bitcoin_rpc_username": "gridpool",
  "bitcoin_rpc_password": "LOCAL_SECRET",
  "bitcoin_rpc_cookie_file": "",
  "bitcoin_rpc_poll_interval_seconds": 5,
  "bitcoin_rpc_timeout_seconds": 3,
  "bitcoin_rpc_lag_grace_seconds": 5,
  "bitcoin_zmq_endpoint": "tcp://127.0.0.1:28332",
  "bitcoin_zmq_rawblock_endpoint": "tcp://127.0.0.1:28333"
}
```

Bitcoin Core/Knots:

```ini
server=1
zmqpubhashblock=tcp://127.0.0.1:28332
zmqpubrawblock=tcp://127.0.0.1:28333
```

Bind RPC and ZMQ only to interfaces required by the selected topology. Do not
expose either service to the public Internet.

## Topology Contract

| Topology | RPC/ZMQ host visible to GridPool |
| --- | --- |
| Native systemd | `127.0.0.1` |
| Docker host network | `127.0.0.1` |
| Shared Docker bridge | Bitcoin Compose service name, such as `bitcoin` |
| Docker container to host | `host.docker.internal` plus `host-gateway` |
| Remote LAN node | Stable private hostname or IP |
| Umbrel | Bitcoin app service/network values injected by the package |
| StartOS | Dependency-provided service address and credentials |
| Node-less relay | Explicit `external-fallback`; no RPC/ZMQ warning |

Container loopback always means the GridPool container itself. It never means
the Docker host or another container.

### Shared Docker Bridge

Configure Bitcoin to listen on the shared private network, attach GridPool to
that external network, and use its service DNS name:

```yaml
services:
  boot-portal:
    networks: [bitcoin]

networks:
  bitcoin:
    external: true
    name: bitcoin
```

```json
{
  "bitcoin_rpc_url": "http://bitcoin:8332",
  "bitcoin_zmq_endpoint": "tcp://bitcoin:28332",
  "bitcoin_zmq_rawblock_endpoint": "tcp://bitcoin:28333"
}
```

### Docker Container To Host

```yaml
services:
  boot-portal:
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

Use `host.docker.internal` in all three configured endpoints. Bitcoin must bind
to the Docker bridge gateway and restrict RPC access to that private subnet.

## Preflight

The checker reads secrets from files and does not print them:

```bash
scripts/check-bitcoin-connectivity.sh \
  --mode attached-node \
  --rpc-url http://127.0.0.1:8332 \
  --rpc-username gridpool \
  --rpc-password-file /run/secrets/bitcoin-rpc-password \
  --zmq-hashblock tcp://127.0.0.1:28332 \
  --zmq-rawblock tcp://127.0.0.1:28333
```

It verifies RPC authentication, chain synchronization, both advertised ZMQ
topics, and TCP reachability. `/api/network/summary` exposes the redacted
runtime result under `bitcoinNotification`.

## Readiness Semantics

In attached mode, synchronized RPC is required for readiness and mining safety.
ZMQ failure is a latency degradation, not a correctness failure, because RPC
reconciliation remains authoritative. GridPool does not call ZMQ stale merely
because no Bitcoin block has arrived recently; sequence gaps and disagreement
with RPC are the relevant evidence.

If a verified peer header indicates that the local node may be behind, GridPool
requests an immediate RPC reconciliation. It does not activate a snapshot from
the peer header.
