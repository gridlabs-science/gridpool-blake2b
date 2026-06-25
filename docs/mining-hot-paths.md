# GridPool protocol Mining Hot Paths

## Purpose

This document identifies the code paths that directly affect mining freshness, share acceptance, and round convergence.

The practical goal is not premature micro-optimization. It is to protect the few paths where latency or lock contention can turn into:

- stale work
- delayed coinbase refresh
- false share rejects
- DATUM fallback bursts
- candidate-state drift

## Critical Paths

### 1. DATUM coinbaser fetch response path

Files:
- [Program.cs](boot_portal/Program.cs)
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs)

Path:
1. DATUM requests a coinbaser response from GridPool.
2. GridPool parses the request.
3. GridPool reads current payout state.
4. GridPool builds coinbase outputs.
5. GridPool serializes the response.
6. GridPool writes the encrypted response back to DATUM.

Why it matters:
- If this stalls, DATUM times out waiting for GridPool and mines fallback templates.
- That produces `Solo fallback template` rejects and can trigger reconnect churn.

Current instrumentation:
- `parseDurationMs`
- `stateReadDurationMs`
- `buildDurationMs`
- `serializeDurationMs`
- `sendDurationMs`

Current expectation after the persistence split:
- normal fetches should usually be sub-`1 ms` to low single-digit `ms`
- occasional low double-digit `ms` fetches are tolerable
- repeated `>= 1000 ms` fetches are a bug

Recent finding:
- the dominant stall was `stateReadDurationMs`
- root cause was large `pool_state.json` snapshot work interacting with the `_sync` lock

### 2. Share validation and Work Set mutation

Files:
- [Program.cs](boot_portal/Program.cs)
- [BootShareVerifier.cs](boot_portal/Services/BootShareVerifier.cs)
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs)

Path:
1. DATUM share arrives at GridPool.
2. GridPool verifies:
   - parent block validity
   - GridPool payout validity
   - duplicate share rules
3. If accepted, GridPool inserts the share into the bounded unpaid Work Set.
4. GridPool recomputes candidate Work Set state ID.
5. GridPool emits SignalR/UI updates and relays the accepted share to peers.

Why it matters:
- This is the main steady-state hot path while mining.
- It should stay CPU-cheap and avoid blocking on file I/O or large-history work.

Current expectations:
- validation should be in-memory and deterministic
- Work Set insertion cost is bounded because the reserve is capped, default `897`
- no disk write or large JSON serialization should happen inline on this path

### 3. Chain-tip observation and DATUM refresh

Files:
- [MempoolSpaceSocketSubscriber.cs](boot_portal/HostedServices/MempoolSpaceSocketSubscriber.cs)
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs)
- [Program.cs](boot_portal/Program.cs)

Path:
1. GridPool observes a new Bitcoin tip.
2. GridPool updates current accepted parent set.
3. GridPool invalidates work templates.
4. GridPool nudges DATUM to refresh.

Why it matters:
- This path decides how quickly miners stop working on obsolete parents.
- Over-aggressive refresh loops can also be harmful.

Current expectations:
- one real chain-tip change should produce one refresh action
- repeated synthetic refresh storms are a bug
- some `Wrong parent block` rejects immediately after a real tip change are normal

### 4. Snapshot and payment transitions

Files:
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs)

Path:
1. A new Bitcoin tip creates an active payout snapshot from the current unpaid Work Set.
2. DATUM templates must refresh to the new payout snapshot quickly.
3. A valid GridPool block pays the active snapshot.
4. GridPool removes only the proof IDs that were actually paid.
5. Unpaid reserve proofs remain eligible for later snapshots.

Why it matters:
- This is the most synchronization-sensitive path in the protocol.
- Most stale/fallback bursts cluster around Bitcoin-block snapshots or GridPool payment transitions if anything is slow.

Current expectations:
- both nodes should converge on the active payout snapshot and unpaid Work Set quickly
- a short reject burst after snapshot/payment transition is tolerable
- a multi-second to multi-minute fallback window is a bug

### 5. Peer sync and share relay

Files:
- [BootPeerSyncService.cs](boot_portal/HostedServices/BootPeerSyncService.cs)

Path:
1. Nodes poll peer summaries and state bundles.
2. Nodes import stronger active snapshot and candidate Work Set states when appropriate.
3. Nodes relay accepted Work Set shares.

Why it matters:
- It affects convergence, not raw miner latency.
- It must not be allowed to crash the host or block mining-critical paths.

Current expectations:
- peer timeouts should degrade peer health, not stop the process
- candidate Work Set fetch races should be handled via cached recent bundles
- peer share relay limits must scale with expected per-node share rate, especially while test min difficulty is low
- relay failures should be visible as protocol telemetry, not only as HTTP client logs

Recent finding:
- the old `peer_write_rate_limit_per_minute = 90` throttled the high-hashrate laptop node and produced many HTTP `429` responses
- this caused candidate divergence risk because the main node was rejecting valid peer relays before share validation
- the test/default value is now `3000/min`, and non-success peer relay responses record `peer-relay-failed` events

## Locking Principles

The `_sync` lock in [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs) protects protocol state.

That is correct, but only if the lock stays narrow.

Rules:
- Never do file I/O while holding `_sync`.
- Never serialize large history/debug payloads while holding `_sync`.
- Avoid cloning large historical structures on every accepted share.
- Keep share validation snapshots small and local.

Recent fix:
- core live state now persists separately from history/debug state
- the large history/debug sidecar is saved on a slower cadence
- this removed multi-second coinbaser stalls caused by state snapshotting

## Current Persistence Split

Core file:
- [pool_state.json](boot_portal/pool_state.json)

History/debug sidecar:
- [pool_state.history.json](boot_portal/pool_state.history.json)

Intent:
- core file contains only what the miner-facing protocol needs immediately
- sidecar contains archived rounds, diagnostics, telemetry, and charts

This is a performance boundary, not just a storage cleanup.

## Current Timing Heuristics

These are practical thresholds, not protocol rules.

Coinbaser fetch:
- good: `< 5 ms`
- watch: `5-50 ms`
- bad: repeated `>= 1000 ms`

State lock wait:
- good: effectively unnoticeable
- watch: `>= 50 ms`
- bad: repeated `>= 250 ms`

Pool-state save:
- acceptable if asynchronous
- dangerous if it contaminates the coinbaser or share hot path

Reject bursts:
- a handful right after a real Bitcoin block snapshot or GridPool payment transition is normal
- sustained `Solo fallback template` rejects indicate delayed template/coinbaser refresh

## Next Optimization Targets

1. Keep history-side saves coarse and bounded.
2. Consider capping persisted archived bundles separately from UI-visible in-memory history.
3. Add a cheap lock-hold profiler around the largest write-side state mutations.
4. If needed later, move archived round history to an append-only file or lightweight database.
5. Only consider a Rust/C rewrite if measured hot paths remain problematic after these architectural fixes.

## What Not To Optimize First

- UI rendering
- historical chart formatting
- non-critical peer polling
- broad code rewrites without timing evidence

The main lesson from recent debugging is that architectural mistakes in persistence and lock scope cost far more than instruction-level inefficiency.
