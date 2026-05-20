# 2500 DATUM Client Stress Architecture

## Purpose

This document sketches a practical path to stress-test one GridPool Boot node with `2500` concurrent DATUM-facing clients.

The primary goal is to prove that a single production-style VPS Boot node can keep `2500` DATUM sessions alive, serve coinbase/payout work quickly, and survive reconnect churn.

The secondary goal is to make the load increasingly realistic by attaching real ASIC hashrate to some or all of those DATUM clients. That second goal is useful, but it is much harder than the connection-count goal because Stratum V1 shares are tied to the exact job/session that issued them.

## Inputs And Constraints

- The target system is one GridPool Boot node, likely on a VPS.
- The target DATUM-side connection count is `2500`.
- The available LuxOS miner can split hashrate to at most `100` upstream pools/DATUM nodes.
- It is acceptable if most of the `2500` DATUM sessions do not have real hashrate behind them during the first capacity tests.
- HashScope is available and may be useful for Stratum V1 capture, traffic modeling, and replay tooling.
- GridPool already has an HTTP share-submission API that can validate untrusted shares by recomputing the header hash, coinbase merkle root, payout list, and slot-0 attribution.

## Research Findings

### HashScope

HashScope is a transparent Stratum V1 MITM proxy with a distributed agent fleet for capture, analysis, and testing. The upstream README describes it as a platform for transparent proxying, Stratum V1 parsing, message capture, and distributed agents that receive share events and submit to pools.

Relevant local source paths:

- `../HashScope/backend/hashscope/proxy/session.py`
- `../HashScope/backend/hashscope/stratum/parser.py`
- `../HashScope/agents/hashscope_agent/pool_client.py`

Important implementation details:

- HashScope relays miner-to-pool and pool-to-miner traffic byte-for-byte.
- Stratum V1 messages are parsed as newline-delimited JSON-RPC.
- A `ProxySession` represents one downstream miner connection and one upstream pool connection.
- HashScope can publish `mining.submit` share events and has distributed agents that can connect to pools and submit shares.
- HashScope is not, as currently written, a general-purpose hashrate splitter from one ASIC connection into thousands of independently valid upstream DATUM sessions.

HashScope is still valuable here. It can capture real Stratum timing, share cadence, reconnect behavior, pool response shapes, and valid share examples. It can also help build a replay model for load tests. But a submitted Stratum V1 share cannot usually be replayed against a different DATUM session because the share belongs to one specific job/extranonce/template context.

### Stratum V1 Share Binding

Stratum V1 uses newline-delimited JSON-RPC over TCP. The usual flow is:

- `mining.subscribe`
- `mining.authorize`
- `mining.set_difficulty`
- `mining.notify`
- `mining.submit`

A `mining.submit` message is not just "a nonce". It is tied to the upstream job:

- worker username
- job ID
- extranonce allocation
- ntime
- nonce
- version rolling bits, when enabled
- coinbase prefix/suffix
- merkle branch
- parent block
- payout list

That means one ASIC share from one upstream job is not automatically valid for another DATUM client's independently issued job. If we want one physical miner to feed many DATUM sessions with valid work, a fanout component must own the job construction and ensure the downstream ASIC work maps back to the correct upstream DATUM session.

### GridPool HTTP Share Path

GridPool's HTTP share API is documented in `docs/hydrapool-http-submission.md`.

Relevant local source paths:

- `boot_portal/Controllers/MiningApiController.cs`
- `boot_portal/Models/MiningModels.cs`
- `boot_portal/Services/BootShareVerifier.cs`

The HTTP share endpoint accepts:

- `headerHex`
- `coinbaseHex`
- `merklePath`
- `prevBlockHash`
- metadata such as `minerAddress`, `username`, `nonce`, and caller-reported `difficulty`

The server does not trust caller attribution. It hashes the header, rebuilds the merkle root from the coinbase and merkle path, verifies payout outputs, computes difficulty, and attributes the share to the address in coinbase output slot 0.

This is important for the stress plan because synthetic or replayed low-value traffic can exercise HTTP validation independently from DATUM connection capacity.

## Recommended Test Architecture

### Phase 1: DATUM-Like Session Load Simulator

Build a lightweight `datum-sim` harness that speaks just enough of the DATUM-facing protocol to stress the Boot node's DATUM server path.

Recommended implementation:

- Use .NET, Go, or Rust.
- Run from the dev machine or a separate load box.
- Open `2500` TCP sessions to one Boot node's DATUM-facing port.
- Complete the Boot/DATUM handshake enough to be treated like a DATUM client.
- Request coinbase/payout work at realistic intervals.
- Keep sessions alive with the same cadence as real DATUM.
- Randomly churn a small percentage of clients.
- Record per-client latency, disconnects, errors, and response parse failures.

This phase tests the primary launch concern: can one Boot process carry thousands of DATUM clients without CPU, memory, socket, GC, or lock-contention collapse?

This phase does not require real ASIC hashrate.

Acceptance criteria:

- `2500` concurrent DATUM-like sessions for `2h`.
- Zero Boot process crashes.
- Zero systemd restarts.
- p95 coinbase/work response latency `< 50 ms`.
- p99 coinbase/work response latency `< 150 ms`.
- No unbounded memory growth after the first `15m`.
- DATUM session churn is explained by the harness, not by Boot-side failures.
- Reconnect storms do not cause persistent candidate/current divergence.

### Phase 2: Synthetic Share Load

Extend the simulator to submit share traffic.

Two modes are useful:

- Low-difficulty spam mode, to prove rate limits and "below on-deck threshold" behavior.
- Valid high-difficulty fixture mode, to exercise the real share validation and state mutation path.

For low-difficulty spam, the harness can submit malformed or insufficient shares through HTTP or DATUM-like paths and verify clean rejection.

For valid fixtures, we need either:

- a deterministic regtest/signet environment with an artificially low target and generated valid shares, or
- a checked-in share fixture generator that can build real `headerHex`, `coinbaseHex`, and `merklePath` for the current test state.

Acceptance criteria:

- `2500` sessions remain connected while synthetic shares are submitted.
- Duplicate shares never create multiple on-deck slots.
- Low-difficulty shares are rejected or ignored without high CPU cost.
- Valid fixture shares mutate on-deck state exactly once.
- Bad input produces structured `4xx` responses, not `5xx`.

### Phase 3: Real Hashrate Baseline With LuxOS Direct Split

Use LuxOS to split real miner hashrate across up to `100` upstream DATUM clients.

This is the simplest real-hash test because it stays inside the miner's supported "multiple pool" model.

Recommended topology:

```text
LuxOS miner
  -> DATUM client 001 -> GridPool VPS Boot
  -> DATUM client 002 -> GridPool VPS Boot
  -> ...
  -> DATUM client 100 -> GridPool VPS Boot
```

Run this alongside `2400` idle or synthetic DATUM-like sessions from `datum-sim`.

This gives a realistic Boot profile:

- `2500` total DATUM-side sessions
- `100` real hashrate-producing sessions
- `2400` idle/synthetic sessions

Acceptance criteria:

- Same capacity criteria as Phase 1.
- Real DATUM clients do not churn under synthetic background load.
- Real share accept/reject rates match the low-load baseline.
- Coinbaser latency for real DATUM clients remains within p99 threshold.

### Phase 4: HashScope-Assisted Traffic Model

Use HashScope as a measuring instrument, not yet as the splitter.

Recommended uses:

- Put HashScope between a real miner and one DATUM client.
- Capture Stratum V1 timing and share cadence.
- Capture `mining.set_difficulty`, `mining.notify`, and `mining.submit` behavior around round rotations and new Bitcoin tips.
- Use the captured distributions to tune the synthetic simulator.
- Optionally feed share-event cadence to a fleet of agents that submit synthetic traffic to test endpoints.

Avoid assuming a captured `mining.submit` can be replayed as a valid share against another DATUM client. It usually cannot.

Acceptance criteria:

- Captured trace can reproduce realistic message timing in simulator mode.
- Simulator's request cadence resembles the real miner/DATUM trace.
- Replay tooling is clearly labeled as timing/load replay, not proof-valid share replay.

### Phase 5: Custom Stratum Fanout Gateway, Only If Needed

If it becomes important to put real ASIC work behind far more than `100` DATUM sessions, build a custom Stratum fanout gateway.

This is significantly harder than the previous phases.

Possible design:

```text
LuxOS miner
  -> Stratum fanout gateway
       -> upstream DATUM session 0001
       -> upstream DATUM session 0002
       -> ...
       -> upstream DATUM session 2500
```

The gateway would need to:

- connect to many upstream DATUM sessions
- track each upstream job, extranonce, target, coinbase, and merkle context
- issue downstream Stratum jobs to the ASIC in a way that maps solved shares back to one upstream DATUM session
- partition extranonce/ntime/version/nonce space so work is not duplicated
- decide how to distribute hashrate among thousands of upstream sessions
- handle `clean_jobs` immediately on new parent blocks
- handle upstream difficulty changes without producing stale garbage

This is closer to writing a mini mining proxy/pool than a simple load harness. It is not recommended as the first implementation.

Acceptance criteria if built:

- One downstream miner can produce valid accepted shares through at least `250` upstream DATUM sessions.
- No duplicate work assignment across upstream sessions.
- A share submitted through one upstream session is never replayed to an incompatible session.
- Clean-job propagation from every upstream session is honored quickly.

## VPS And OS Tuning Checklist

Before running a `2500`-session test against a VPS:

- Set `LimitNOFILE` for the Boot service to at least `65535`.
- Confirm `ulimit -n` inside the service is at least `65535`.
- Increase TCP backlog and connection tracking limits if needed.
- Ensure Docker/container limits do not cap open files or memory unexpectedly.
- Record CPU, RSS, socket count, GC counters, and restart count during the test.
- Keep persistent debug/history writes out of the hot path.
- Use an isolated staging domain or firewall allowlist during destructive load tests.

Suggested node metrics to expose or collect:

- active DATUM sessions
- DATUM session opens/closes per minute
- coinbase/work request count
- coinbase/work p50/p95/p99 latency
- share validation p50/p95/p99 latency
- accepted shares by source
- rejected shares by reason
- duplicate shares
- state write duration
- peer relay queue depth
- current open sockets
- RSS and GC heap size

## Pass Criteria For The Primary Goal

The primary goal is complete when all of this passes on the target VPS:

- `2500` concurrent DATUM-like sessions for `2h`.
- `2500` sessions plus controlled reconnect churn for `30m`.
- p95 coinbase/work response latency `< 50 ms`.
- p99 coinbase/work response latency `< 150 ms`.
- zero unhandled exceptions.
- zero process restarts.
- no memory growth trend after warmup.
- no persistent peer divergence caused by mining-side load.
- malformed and low-difficulty traffic is rejected cheaply.

## Pass Criteria For The Secondary Goal

The real-hash secondary goal is "good enough" when:

- at least `100` real DATUM clients receive split hashrate from LuxOS for `2h`;
- those `100` run while `2400` other DATUM-like sessions are connected;
- real accept/reject rates are comparable to low-load baseline;
- real share propagation and round rotation remain healthy;
- the simulator can reproduce realistic non-hashing DATUM traffic based on HashScope traces.

A stronger future goal is valid real ASIC work behind all `2500` sessions, but that likely requires the custom Stratum fanout gateway described above.

## Recommended Implementation Order

1. Implement `datum-sim` for idle DATUM-like sessions.
2. Add machine-readable per-client latency and disconnect reporting.
3. Run `100`, `500`, `1000`, and `2500` idle-session tests locally or on a staging VPS.
4. Add synthetic low-difficulty and malformed share traffic.
5. Add deterministic valid-share fixture generation.
6. Run LuxOS split across up to `100` real DATUM clients.
7. Use HashScope to capture real Stratum timing and tune simulator distributions.
8. Decide whether the custom Stratum fanout gateway is worth building.

## Open Questions

- What exact subset of the DATUM upstream protocol must `datum-sim` speak to exercise the Boot hot path faithfully?
- Can the existing Boot test trigger/regtest tooling generate valid share fixtures cheaply enough for load tests?
- What is the target VPS size for the first real `2500`-session run?
- Should the simulator run from one load generator or from multiple regional load boxes to avoid client-side port/CPU limits?
- Is `100` real DATUM clients plus `2400` simulated DATUM clients sufficient evidence for launch, or do we need a real-hash fanout prototype before public release?

## Sources

- HashScope repository: `https://github.com/256foundation/HashScope`
- Stratum V1 reference: `https://reference.cash/mining/stratum-protocol`
- GridPool HTTP share plan: `docs/hydrapool-http-submission.md`
- GridPool stress-test plan: `docs/stress-test-plan.md`
