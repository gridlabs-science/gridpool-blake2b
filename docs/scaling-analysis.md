# GridPool Scaling Analysis

## Purpose

This document analyzes whether the GridPool protocol can scale far beyond the current test setup, including the extreme stretch goal of eventually supporting a very large fraction of global Bitcoin hashrate.

The goal is not to prove that outcome today. The goal is to identify:

- which parts of the protocol scale well
- which parts of the current implementation are just engineering bottlenecks
- which design choices would fundamentally limit scale if left unchanged

## Executive Summary

Protocol-level view:

- The protocol has promising scaling properties because it only keeps and gossips a bounded reserve of top unpaid share proofs.
- Active payout snapshots are bounded by `299` post-slot-0 slots, or `1` support slot plus `298` shared proof slots when the support fee is enabled.
- The default unpaid Work Set reserve is `3x` the shared slot count, currently `897` proofs, so state bundle size grows with a fixed protocol cap rather than total team size.

Implementation-level view:

- The current implementation can scale **if** peer degree stays bounded.
- It will not scale if every node tries to connect to every other node.
- The most important current scaling risk is peer fanout and polling topology, not the size of the bounded Work Set itself.

Conclusion:

- "Can this eventually support enormous network share?" -> technically plausible
- "Can the current naive topology support every node talking to every node?" -> no

So the stretch goal is only realistic with a bounded-degree peer graph and disciplined hot-path engineering.

## What Scales Well By Design

### 1. Bounded shared state per round

The protocol tracks only a bounded reserve of top unpaid work:

- unpaid Work Set proofs: capped
- active payout snapshot: capped
- active/paid snapshot state bundles: capped

That means:

- per-round reserve state does not grow with miner count
- per-block payout snapshot size does not grow with miner count
- verification of active and paid snapshots remains bounded

This is a strong scaling property.

### 2. Share selection cost is effectively constant

Today, insertion into the unpaid Work Set in [BootProtocolStateService.cs](../boot_portal/Services/BootProtocolStateService.cs) works on a list capped by `workSetReserveLimit`, default `897`.

That means the cost of:

- ranking
- trimming
- rebuilding the active payout snapshot

is effectively `O(897)`, which is constant in practice under current parameters.

### 3. Snapshot bundle transfer size is bounded

A node joining or rejoining does not need the entire network history to participate in the current round.

It needs:

- active payout snapshot state
- current candidate Work Set state
- accepted parent context
- retained snapshot contexts needed by unpaid proofs

That bounded-state property is favorable for global scale.

## What Does Not Automatically Scale

### 1. Full-mesh peer topology

Current peer behavior is effectively:

- poll every configured/discovered peer
- relay accepted Work Set shares to every peer

If each node had peer degree `P`, then per-node peer work scales roughly like:

- summary polling: `O(P)`
- relay fanout per accepted Work Set share: `O(P)`

This is fine if `P` is bounded, for example:

- `8`
- `16`
- `32`

It is not fine if `P` grows with total network size `N`.

So:

- bounded-degree gossip overlay: plausible
- global full mesh: not plausible

### 2. Low-threshold share storms

When the Work Set admission floor is low, many shares may briefly be good enough
to enter the reserve.

This creates:

- more share relay traffic
- more UI churn
- more candidate Work Set churn

This is not a fatal flaw, but it is a real scaling pressure.

Potential mitigations:

- keep DATUM min-diff/vardiff tuned
- tune reserve depth and admission-floor behavior with measured data
- keep telemetry and persistence off the hot path

### 3. Seed-node centrality

Seed nodes are necessary for discovery, but they must not become mandatory transit hubs for all traffic.

At scale:

- seeds should only help nodes discover peers
- normal share/state gossip must happen across the wider graph

If seeds become operational bottlenecks, the system may remain decentralized in theory but centralized in practice.

## Current Implementation Hot Paths

These are the main places where scale pressure shows up:

### DATUM coinbaser fetch

Path:
- [Program.cs](../boot_portal/Program.cs)

Risk:
- if coinbaser fetch latency rises, DATUM can fall back to solo templates and create reject churn

Current status:
- much improved after moving large history/debug persistence out of the core hot path

### Share validation and Work Set mutation

Paths:
- [BootShareVerifier.cs](../boot_portal/Services/BootShareVerifier.cs)
- [BootProtocolStateService.cs](../boot_portal/Services/BootProtocolStateService.cs)

Risk:
- expensive validation before cheap rejection
- attacker-created floods

### Peer polling and relay

Path:
- [BootPeerSyncService.cs](../boot_portal/HostedServices/BootPeerSyncService.cs)

Risk:
- per-share relay fanout to too many peers
- too-frequent summary polling

This is the strongest scaling concern in the current design.

## Scaling Conditions For Very Large Networks

GridPool could plausibly scale to very large hashrate only if these conditions hold:

### 1. Peer degree stays bounded

Each node must maintain a limited number of peers.

Requirement:
- peer degree `d` remains approximately constant as network size grows

If that is true:
- per-node network overhead grows roughly with `d`, not with total `N`

### 2. State propagation latency stays below snapshot/payment tolerance

The network must propagate stronger candidate Work Set states and active
snapshots quickly enough that:

- orphaned local branches remain rare
- short divergence windows remain acceptable

What matters is not perfect instant convergence, but bounded convergence time.

### 3. Mining hot path stays independent of history/debug features

Historical telemetry, UI data, and diagnostics must remain off the critical mining path.

This is an engineering discipline issue, not a protocol limitation.

### 4. Client/gateway behavior remains sane

The protocol can only scale if mining clients:

- switch templates promptly
- do not spend long windows on fallback work
- do not multiply reconnect storms under load

This is partly a client issue, partly an integration issue.

## Quantities To Measure

To evaluate large-scale plausibility, measure:

### Per-node steady-state

- peer degree
- summary polls per minute
- relayed Work Set shares per minute
- average and p95 coinbaser fetch latency
- average CPU and memory at fixed miner load

### Per snapshot/payment transition

- relay burst size
- time to Work Set/snapshot convergence
- reject burst duration
- peak simultaneous peer fetches

### Per accepted Work Set share

- bytes sent to peers
- CPU time spent validating / inserting / relaying

## Practical Scaling Targets

Reasonable staged targets:

1. `500` DATUM sessions, `25` peers
2. `1000` DATUM sessions, `25` peers
3. `1000` DATUM sessions, `100` simulated peers in bounded-degree overlay

If the system handles those with good convergence and hot-path latency, the protocol is still in the game.

## Likely Inherent Limits

These would be genuine protocol-level problems if they prove unavoidable:

1. If Work Set convergence requires near-full-mesh relay
2. If short-round storms produce unbounded global chatter
3. If state adoption requires large historical backfill to remain correct
4. If fairness depends on all miners seeing almost exactly the same candidate immediately

Right now, the codebase does **not** prove those are inherent limits. They remain open questions to test.

## Likely Engineering Limits

These are implementation problems, not protocol death sentences:

1. coarse lock contention
2. synchronous persistence on hot paths
3. over-trusting forwarded headers
4. weak load harnesses
5. excessive peer degree defaults

These can be fixed.

## Recommendation

The most important scaling work before launch is not a rewrite in Rust or C.

It is:

1. enforce and document bounded peer degree
2. build the stress harness
3. measure mixed mining + peer load
4. keep history/telemetry/UI off the hot path

If those are done and the stress targets are met, then a later systems-language rewrite can be treated as a performance optimization, not as a rescue mission.
