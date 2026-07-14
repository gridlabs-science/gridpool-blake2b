# Fast Chain-Tip Header Telemetry

Status: measurement-only beta

GridPool can compare how quickly an authenticated peer header reaches a node
over its encrypted peer transports against the same node's local Bitcoin
`rawblock` ZMQ notification. This is an observability experiment, not a source
of mining work or consensus state.

## Safety Boundary

Peer header announcements do not:

- advance the canonical Bitcoin tip;
- rotate a GridPool payout snapshot;
- invalidate mining work;
- create an empty-block template; or
- get re-gossiped by the receiving node.

A peer observation is counted as confirmed only after the receiving node's own
Bitcoin source independently delivers the exact same 80-byte header. Invalid,
unconfirmed, and mismatched announcements cannot affect mining.

## Transport

When local Bitcoin Core or Knots publishes a `rawblock` notification, GridPool:

1. timestamps receipt in the NetMQ callback;
2. extracts the first 80 bytes as the block header;
3. computes the block hash locally;
4. records a `local-chain-tip-header` event;
5. sends the header over the encrypted persistent WebSocket session; and
6. sends a compact encrypted UDP datagram to UDP-compatible peers.

The compact UDP payload is 93 bytes. Existing authenticated UDP framing and
AES-GCM add 45 bytes, producing a 138-byte datagram before IP/UDP headers.
UDP relay version 5 identifies this capability.

Receiving nodes timestamp the completed WebSocket frame or UDP datagram before
JSON decoding, header decoding, or state processing. They record a
`peer-chain-tip` event with transport `v2-session` or `udp`.

## Bitcoin Configuration

The local Bitcoin node should expose both notifications:

```ini
zmqpubhashblock=tcp://127.0.0.1:28332
zmqpubrawblock=tcp://127.0.0.1:28333
```

GridPool configuration:

```json
{
  "NotificationSource": "BitcoinZmq",
  "bitcoin_zmq_endpoint": "tcp://127.0.0.1:28332",
  "bitcoin_zmq_rawblock_endpoint": "tcp://127.0.0.1:28333",
  "enable_peer_persistent_sessions": true,
  "enable_peer_udp_fast_relay": true
}
```

Container networking must allow GridPool to reach those endpoints. The current
one-shot installer uses host networking for this reason.

## Report

Run against each node after at least several Bitcoin blocks:

```bash
node scripts/chain-tip-latency-report.mjs \
  --url https://main.gridpool.net \
  --window 24h \
  --limit 5000 \
  --json /tmp/main-chain-tip-latency.json
```

The report pairs events by block hash on the same receiving machine:

```text
lead_ms = local_bitcoin_rawblock_arrival - peer_transport_arrival
```

A positive value means the peer transport arrived first. A negative value
means the local Bitcoin node arrived first. No cross-machine clock comparison
is used.

Report results separately by transport and source peer. Multiple peer copies of
one block are useful transport observations, but the number of unique Bitcoin
blocks is the appropriate independent-sample count for broad conclusions.

## Interpretation Limits

This experiment can establish whether GridPool header gossip often beats local
Bitcoin block notification in the deployed topology. It does not establish
that mining on an unconfirmed header is safe, that the full block is available,
or that empty-block mining would be profitable. Those decisions require a
separate design and threat-model review after field data exists.
