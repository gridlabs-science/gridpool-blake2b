# Fast Chain-Tip Header Telemetry

Status: measurement plus opt-in stale-work protection beta

GridPool can compare how quickly an authenticated peer header reaches a node
over its encrypted peer transports against the same node's local Bitcoin
`rawblock` ZMQ notification. By default this remains an observability
experiment. An opt-in operational mode can freeze a provisional payout
boundary and stop issuing stale work while still requiring independent local
full-node validation before any active GridPool snapshot changes.

## Safety Boundary

With `enable_peer_tip_stale_protection` disabled, peer header announcements do
not:

- advance the canonical Bitcoin tip;
- rotate a GridPool payout snapshot;
- invalidate mining work;
- create an empty-block template; or
- get re-gossiped by the receiving node.

A peer observation is counted as confirmed only after the receiving node's own
Bitcoin source independently delivers the exact same 80-byte header. Invalid,
unconfirmed, and mismatched announcements cannot affect mining.

With stale-work protection enabled, a peer header must have valid Bitcoin PoW,
directly extend the locally active tip, pass configured timestamp bounds, and
match the expected mainnet target outside retarget boundaries. Receipt freezes
a provisional copy of the unpaid Work Set. It does not activate Winners,
advance the canonical tip, pay/remove proofs, or authorize a peer state bundle.

If the local Bitcoin node has not confirmed the header after the configured
grace period, GridPool pauses fresh DATUM/SV2 work and quarantines newly arriving
proofs on the old parent. Matching local validation activates the frozen
snapshot. A different locally validated block discards it and snapshots the
then-current Work Set normally.

The first local raw header after startup establishes the expected compact
target. Operational action is fail-closed until then and is also disabled at
mainnet retarget boundaries. Testnet4 currently remains measurement-only until
its contextual minimum-difficulty/target rules are implemented.

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
  "enable_peer_udp_fast_relay": true,
  "enable_peer_tip_stale_protection": true,
  "peer_tip_grace_seconds": 3,
  "peer_tip_max_header_age_seconds": 86400,
  "peer_tip_max_future_seconds": 7200
}
```

The operational option defaults to `false` because changing the boundary
cutoff on only part of a live network can create a short-lived snapshot split.
Enable it as a coordinated node rollout.

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

## Deferred Decisions

Peer-header snapshot activation is a future coordinated consensus change,
tentatively V2.2. It requires deterministic rules and test vectors for
competing headers, reorgs, withheld block bodies, rollback, missed headers,
retarget boundaries, and the exact old-parent eligibility cutoff.

Header-only empty-block mining is a separate, much later experiment. If ever
implemented it must be disabled by default and tightly time-bounded. A valid
header does not prove the parent block body is valid, and empty templates also
forfeit fees. Full/compact block systems such as FIBRE remain the appropriate
solution for production fast block propagation.
