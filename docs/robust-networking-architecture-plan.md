# GridPool Robust Networking Architecture Plan

## Summary

GridPool needs peer networking that is easy for home miners to join, but robust enough for a mining network where low-latency share propagation reduces payout-list splits at round rotation. The network should borrow Bitcoin's peer discovery pattern where possible: seed nodes are bootstrap hints, not authorities; nodes maintain a persistent address book, gossip reachable peers, score peers by usefulness, and connect to a bounded but diverse active set.

The original V1-V3 plan focused on reachable public peer endpoints. Public beta testing showed the missing pleb-miner requirement: most home nodes will not have public IPs or router ports configured. The next phase is therefore **V2.1 Hidden Peer Mode**, which treats outbound-only WebSocket sessions as first-class peers and uses public seeds as rendezvous/relay fallback without changing trust assumptions.

## Implementation Status At A Glance

| Phase | Status | Notes |
| --- | --- | --- |
| V1 HTTP address manager | Complete | Bounded peer selection, persistent address book, endpoint validation, backoff, and address gossip are implemented. |
| V2 encrypted persistent sessions | Initial implementation complete | WebSocket sessions, signed hello, AES-GCM encrypted frames, share relay, address gossip, and ping/pong are implemented. |
| V2.1 hidden peer sessions and seed relay | Planned next | Needed so outbound-only home nodes are visible, bidirectional, and relay-capable without a public endpoint. |
| V3 UDP fast relay | Initial implementation complete | Authenticated UDP relay exists after a V2 session, with V2/HTTP fallback when packets do not fit. |
| V3.1 compact slot-0 reconstruction | Planned | Needed for production-scale 300-output coinbases to fit fast UDP reliably. |
| V4 Bitcoin header / compact block relay | Research only | Not started. |

## V1: HTTP Address Manager

- Keep `bootstrap_peers` as cold-start seeds only. Nodes should use them to refill a weak or empty address book, not as permanent central infrastructure.
- Replace "poll every known peer" behavior with bounded peer selection from a persistent address manager.
- Store peer metadata: endpoint, source, configured-seed flag, discovered time, last attempt, last success, last failure, failure count, relay success/failure counts, state/tip seen, suppression/tombstone expiry, and score.
- Validate peer endpoints before accepting them from headers or gossip:
  - reject empty, self, malformed, non-HTTP(S), and placeholder endpoints such as `boot.example.com`;
  - reject private/LAN/localhost advertisements unless private advertisements are explicitly allowed or the node is in development mode;
  - allow configured bootstrap peers even if they are private, so local test setups still work.
- Add address gossip through `GET /api/network/peer-addresses?limit=128`.
- Keep `/api/network/summary` backward compatible and continue merging peer endpoints from older nodes that only expose summary data.
- Select active peers by score and backoff:
  - normal outbound target: 16 peers;
  - share relay target: 32 peers;
  - address book max: 2048 entries;
  - failure backoff: 30 seconds minimum, 30 minutes maximum.
- Make `public_base_url` optional. Nodes without a public endpoint can validate and sync outbound, but do not advertise themselves.
- Preserve all existing share/state validation. Peer networking remains transport, not trust.

### V1 Implementation Status

V1 is implemented and should be treated as the current baseline:

- `bootstrap_peers` seeds the persistent peer address book.
- `/api/network/peer-addresses` exposes bounded address gossip.
- `/api/network/summary` still works as the backward-compatible state and discovery endpoint.
- Peer selection is bounded by outbound, relay, and session targets instead of polling every known peer.
- Endpoint validation rejects malformed, placeholder, and non-public gossip unless private advertisements are explicitly allowed.
- Peer failures back off and stale failed peers can be pruned.
- Nodes without `public_base_url` run in outbound-only mode and do not advertise themselves as reachable endpoints.

## V2: Encrypted Persistent Sessions

- Add a long-lived peer channel for low-latency gossip while preserving HTTP APIs as fallback.
- Recommended transport: Noise-based encrypted sessions over TCP/WebSocket.
- Prefer Noise because it does not require home miners to own domains or TLS certificates, supports node identity keys, and gives a route to encrypted UDP packet keys.
- Keep HTTPS as the recommended public relay transport until native encrypted sessions exist.

### V2 Implementation Status

The first V2 implementation is now an additive WebSocket transport:

- `GET /api/peer/session` accepts WebSocket peer sessions.
- Nodes dial a bounded set of high-scoring peers from the V1 address book when `enable_peer_persistent_sessions` is true.
- Each session starts with a signed `hello` message using the node's long-term Ed25519 identity key.
- The signed hello binds protocol version, network id, advertised endpoint, Ed25519 node id, X25519 public key, nonce, and timestamp.
- After both hellos are exchanged, peers derive per-direction AES-GCM keys from their X25519 shared secret and both nonces.
- All post-handshake session frames are encrypted and sequence-checked.
- Accepted shares are relayed over open V2 sessions first, then fall back to the existing HTTP `/api/peer/share` relay path for peers not reached by session.
- Inbound V2 share frames are validated through the same share payload and proof verification path as HTTP peer shares.
- V2 session health contributes to peer scoring, but V1 HTTP polling and relay remain canonical fallback.

This is **Noise-inspired**, not a complete Noise protocol implementation. It gives encrypted long-lived sessions and identity continuity using primitives already present in the codebase, while leaving a cleaner Noise XX/IK transport as a later hardening step.

Current V2 limits:

- No forward secrecy yet because the first implementation uses the node's long-term X25519 key for session key agreement.
- No explicit peer allowlist or reputation identity policy yet; self-signed node identities are useful for continuity, not Sybil prevention.
- Session transport currently carries share relay, address gossip, and ping/pong only. State-bundle sync still uses HTTP.
- WebSocket sessions are still TCP-based. The UDP fast-relay path remains V3.
- Endpoint-less sessions are not yet first-class UI/address-book peers. This is the main V2.1 gap.

## V2.1: Hidden Peer Sessions And Seed Relay

Public beta testing showed that the "optional `public_base_url`" model is not enough by itself. A home node behind NAT can open an outbound WebSocket to a public seed, and that connection is bidirectional while it remains open, but the current peer table is still centered on dialable HTTP(S) endpoints. That makes outbound-only nodes hard to see, hard to reason about, and dependent on public seeds as implicit hubs.

V2.1 makes outbound-only nodes explicit first-class participants without requiring port forwarding.

### Desired Behavior

- A node with no `public_base_url` connects outbound to one or more public seeds and appears as a live `outbound-only` peer keyed by node ID.
- Public nodes relay accepted shares to live hidden sessions over the encrypted WebSocket path.
- Hidden nodes are not advertised as dialable HTTP peers unless they prove a reachable endpoint.
- Nerd Mode and health tooling distinguish:
  - reachable public peers;
  - live outbound-only sessions;
  - relay/rendezvous seed dependencies.
- Public seeds can relay between two hidden nodes connected to the same seed.
- Direct NAT traversal is attempted later, but relay fallback is sufficient for correctness.

### V2.1 Implementation Plan

1. Hidden session peer accounting
   - Track live sessions by stable `nodeId` even when `RemoteEndpoint` is empty.
   - Extend network status DTOs with `nodeId`, `connectionMode`, `sessionConnected`, `lastSessionUtc`, and `capabilities`.
   - Show endpoint-less sessions in Nerd Mode and monitor output as `outbound-only`.

2. Hidden session share relay
   - Relay accepted shares over every live encrypted session first, including sessions without endpoints.
   - Keep HTTP relay only for endpoint peers not already reached by session relay.
   - Prevent loops with share IDs, source node IDs, and existing duplicate suppression.

3. Seed relay fallback
   - Let public seeds relay encrypted share payloads between hidden sessions that cannot dial each other.
   - Rate-limit relay by node ID and keep all proof validation unchanged.
   - Treat seed relay as transport only: seeds never become trusted state authorities.

4. Public reachability assistance
   - Add optional UPnP/NAT-PMP/PCP port mapping to automatically set `public_base_url` when routers support it.
   - Leave this disabled by default until common-router testing is complete.

5. NAT traversal
   - Add UDP rendezvous through public seeds: hidden nodes exchange observed UDP addresses, punch, then use authenticated V3 UDP directly if it succeeds.
   - Fall back to WebSocket seed relay when direct UDP fails.

6. Optional Tor mode
   - Add onion-service documentation and config later for users who prefer privacy/reachability over lowest latency.

### V2.1 Acceptance Criteria

- A node with no `public_base_url` appears in `/api/network/summary` and Nerd Mode as `outbound-only` within 30 seconds of opening a persistent session.
- Accepted shares relay to live outbound-only sessions over WebSocket.
- Hidden peers are not returned by `/api/network/peer-addresses` as dialable endpoints.
- Two hidden nodes connected to the same public seed can receive each other's accepted shares through seed relay.
- Public endpoint peers continue to use V1/V2/V3 behavior unchanged.
- Relay failures are visible as peer/session health, not as mining failures.

## V3: FIBRE-Inspired UDP Share Fast Relay

- Add a compact binary single-packet message for new on-deck share proofs.
- Current observed proof sizes in the small lab topology:
  - JSON share proof: roughly 1.3 KB to 2.1 KB;
  - compact binary share proof: roughly 0.5 KB to 0.9 KB.
- Those measurements are not representative of the mature 300-recipient pool case. A P2WPKH output is roughly 31 bytes (8-byte value, 1-byte script length, 22-byte script). A full 300-address Winners List therefore adds roughly 9,300 bytes of output data before the coinbase input, extranonce/script data, tx framing, merkle path, and auth overhead.
- Expected mature full-proof UDP payload: roughly 9.8 KB to 10.5 KB for 300 unique P2WPKH payout addresses, well above the 1200-byte no-fragmentation target.
- Target a safe UDP payload under 1200 bytes to avoid IP fragmentation.
- Use UDP only as an optimistic fast data plane:
  - HTTP/persistent peer channel remains canonical fallback;
  - duplicate suppression remains keyed by share ID;
  - receiver validates every share before using it;
  - missing context triggers normal HTTP state/share fetch.
- Do not ship raw unauthenticated UDP. Require per-peer authentication/encryption, replay windows, rate limits, and duplicate suppression.

### V3 Implementation Status

The first V3 implementation is complete as an authenticated UDP fast-relay overlay:

- UDP is enabled by `enable_peer_udp_fast_relay`.
- Default bind/advertised UDP port is `5001`.
- UDP relay only operates after a V2 persistent peer session exists.
- V3 UDP keys are derived from the V2 X25519 session secret and both session nonces.
- Datagram header:
  - 4-byte magic: `GP3S`;
  - 1-byte V3 datagram version;
  - 16-byte truncated sender node key;
  - 8-byte per-session sequence.
- Datagram body is AES-GCM encrypted/authenticated.
- Receiver drops datagrams unless it has a live V2 session for the sender node key.
- Receiver keeps a replay window per session.
- Payload is a compact binary full share proof:
  - 80-byte block header;
  - exact coinbase transaction;
  - merkle path;
  - optional truncated username.
- If a proof cannot fit within `peer_udp_max_datagram_bytes` (default 1200), UDP relay is skipped and V2/HTTP carry the share normally.
- UDP is an optimistic fast path only. V2 session relay and HTTP share relay remain correctness fallbacks.
- Relay latency and packet-fit telemetry is available at `/api/network/peer-relay-latency`.
- `peer_relay_latency_probe_all_transports` can be enabled in lab testing to intentionally send redundant HTTP/WebSocket/UDP copies and measure which transport arrives first.

### Slot-0-Only Compression Decision

Slot-0-only UDP packets are deferred for the first V3 implementation, but should now be treated as the likely V3.1/V4 path for production-scale fast relay.

In theory, every converged node has the same Winners List, which defines most of the coinbase payout structure. A packet containing only the slot-0 address plus the header and compact work fields could be much smaller.

For the first V3 implementation, the complexity is deferred because the fallback path is correct and lower-risk:

- Receivers must verify the header merkle root, which requires the exact coinbase transaction hash.
- DATUM/direct clients can vary extranonce material, tags, coinbase script data, and transaction selection.
- During latency-driven team splits, peers may have different current Winners Lists; reconstruction failure would be ambiguous.
- Current measured compact full proofs fit under the safe 1200-byte UDP budget only in small test conditions.

Recommended V3.1 design direction:

- Negotiate a compact-share capability during the V2 encrypted session.
- Include a current-state identifier or Winners List commitment in each compact UDP packet.
- Send slot-0 address/script, header, merkle path, and the minimal coinbase variable fields required to reconstruct the exact coinbase transaction.
- Receiver reconstructs the full coinbase from its local Winners List and validates the merkle root and share difficulty.
- If reconstruction fails because state differs, receiver requests or waits for the full V2/HTTP proof and records the event as a possible team-split/convergence signal.

### V3.1 Implementation Status

V3.1 is not implemented. It should wait until V2.1 hidden session relay is stable, because hidden peers need reliable encrypted session fallback before compact UDP reconstruction becomes operationally important.

## V4: Bitcoin Header / Compact Block Relay

- Explore relaying fresh Bitcoin headers and compact block sketches between GridPool nodes to reduce chain-tip asymmetry.
- Treat this as separate from V1-V3. FIBRE-style block relay is harder than GridPool share relay because it depends on mempool overlap, compact block reconstruction, missing transaction recovery, and larger payloads that may require FEC.

### V4 Implementation Status

V4 is research only. No implementation has started.

## Completed V1 Acceptance Criteria

- A node with no `public_base_url` can run outbound-only, sync state, and relay shares to reachable peers.
- `boot.example.com` and other placeholder endpoints cannot be persisted or reintroduced through peer gossip.
- Configured private/LAN peers still work in local development and lab deployments.
- Public nodes do not advertise private/LAN peers unless explicitly configured to allow them.
- Peer polling and share relay are bounded by configured active-peer targets, not by total known peer count.
- Repeated failures back off instead of being retried every sync tick.
- Address gossip survives restart through the existing pool state file.

## Current Priority

Implement V2.1 before adding more UDP sophistication. The current network can move shares between public nodes well enough, but the beta usability bottleneck is that normal home miners are likely to be outbound-only. Making those nodes visible, bidirectional, and relay-capable is the fastest route toward a less centralized practical topology.
