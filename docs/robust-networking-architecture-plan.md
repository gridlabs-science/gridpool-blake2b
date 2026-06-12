# GridPool Robust Networking Architecture Plan

## Summary

GridPool needs peer networking that is easy for home miners to join, but robust enough for a mining network where low-latency share propagation reduces payout-list splits at round rotation. The network should borrow Bitcoin's peer discovery pattern: seed nodes are bootstrap hints, not authorities; nodes maintain a persistent address book, gossip reachable peers, score peers by usefulness, and connect to a bounded but diverse active set.

V1 keeps the existing HTTP control/data path and hardens peer discovery. Later phases add encrypted persistent sessions and a FIBRE-inspired UDP fast-relay overlay for single-packet share propagation.

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

## V2: Encrypted Persistent Sessions

- Add a long-lived peer channel for low-latency gossip while preserving HTTP APIs as fallback.
- Recommended transport: Noise-based encrypted sessions over TCP/WebSocket.
- Prefer Noise because it does not require home miners to own domains or TLS certificates, supports node identity keys, and gives a route to encrypted UDP packet keys.
- Keep HTTPS as the recommended public relay transport until native encrypted sessions exist.

## V3: FIBRE-Inspired UDP Share Fast Relay

- Add a compact binary single-packet message for new on-deck share proofs.
- Current observed proof sizes:
  - JSON share proof: roughly 1.3 KB to 2.1 KB;
  - compact binary share proof: roughly 0.5 KB to 0.9 KB.
- Target a safe UDP payload under 1200 bytes to avoid IP fragmentation.
- Use UDP only as an optimistic fast data plane:
  - HTTP/persistent peer channel remains canonical fallback;
  - duplicate suppression remains keyed by share ID;
  - receiver validates every share before using it;
  - missing context triggers normal HTTP state/share fetch.
- Do not ship raw unauthenticated UDP. Require per-peer authentication/encryption, replay windows, rate limits, and duplicate suppression.

## V4: Bitcoin Header / Compact Block Relay

- Explore relaying fresh Bitcoin headers and compact block sketches between GridPool nodes to reduce chain-tip asymmetry.
- Treat this as separate from V1-V3. FIBRE-style block relay is harder than GridPool share relay because it depends on mempool overlap, compact block reconstruction, missing transaction recovery, and larger payloads that may require FEC.

## Acceptance Criteria For V1

- A node with no `public_base_url` can run outbound-only, sync state, and relay shares to reachable peers.
- `boot.example.com` and other placeholder endpoints cannot be persisted or reintroduced through peer gossip.
- Configured private/LAN peers still work in local development and lab deployments.
- Public nodes do not advertise private/LAN peers unless explicitly configured to allow them.
- Peer polling and share relay are bounded by configured active-peer targets, not by total known peer count.
- Repeated failures back off instead of being retried every sync tick.
- Address gossip survives restart through the existing pool state file.
