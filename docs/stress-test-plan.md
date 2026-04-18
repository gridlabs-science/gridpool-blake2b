# Boot Protocol Stress-Test Plan

## Purpose

This document defines how to stress-test Boot Protocol before launch.

The goals are:

- prove the hot mining path remains fast under load
- prove the protocol does not collapse under reconnect churn or abuse
- prove the peer network converges under realistic multi-node conditions
- capture comparable reports across runs

This plan assumes stress testing will be driven by a checked-in load harness rather than manual miners.

## What Must Be Simulated

Two independent dimensions matter:

1. `Mining-side load`
   - many DATUM clients or DATUM-like sessions hitting one Boot node
   - many accepted and rejected shares
   - reconnect churn

2. `Network-side load`
   - many Boot peers
   - share relay fanout
   - candidate/current state polling and fetches
   - node join/rejoin churn

Both must be tested separately and together.

## Harness Requirements

The harness should support:

- `datum-sim`
  - opens many DATUM sessions
  - requests coinbasers
  - submits valid shares
  - can also submit stale/fallback/malformed patterns on purpose

- `peer-sim`
  - simulates Boot peers polling summaries
  - relays peer shares
  - fetches candidate/current state bundles

- `abuse-sim`
  - replays duplicates
  - spoofs forwarded-IP headers
  - sends malformed requests
  - creates reconnect churn

- machine-readable report output
  - JSON or CSV
  - one report per run

## Metrics To Record

### Node health

- process alive / restart count
- CPU %
- RSS / working set
- open sockets
- GC pause indicators if available

### Boot protocol

- current and candidate convergence time
- peer poll success/failure counts
- share relay success/failure counts
- round rotation timestamps

### Mining hot path

- coinbaser fetch count
- average/p95/p99 coinbaser fetch duration
- average/p95/p99 state-read duration
- accepted share rate
- rejected share rate by category
- accepted On Deck share rate

### Abuse-path metrics

- rate-limit rejections
- duplicate-share rejections
- malformed-request rejections
- `BackgroundService failed` count
- unhandled exception count

## Pass/Fail Conventions

Unless otherwise specified:

- `pass`: every threshold met
- `soft fail`: service survives but misses one latency threshold
- `hard fail`: crash, restart loop, unbounded memory growth, or persistent convergence failure

## Test Suite

### Suite A: Correctness Under Light Load

Purpose:
- establish a clean baseline before scaling up

Scenario:
- `10` DATUM sessions
- `2` Boot peers
- run for `15m`

Pass criteria:
- zero process restarts
- zero slow coinbaser fetches `>= 250 ms`
- p95 coinbaser fetch `< 25 ms`
- exact duplicate share replay returns `duplicate`
- no candidate divergence lasting more than `5s`

### Suite B: Single-Node Mining Load

Purpose:
- test one Boot node serving many miners

Scenarios:

1. `100` DATUM sessions for `30m`
2. `500` DATUM sessions for `30m`
3. `1000` DATUM sessions for `30m`

Pass criteria:
- zero crashes
- zero `systemd` restarts
- p95 coinbaser fetch `< 50 ms`
- p99 coinbaser fetch `< 100 ms`
- p95 accepted-share handling `< 100 ms`
- no slow fetches `>= 250 ms`
- no sustained memory growth after the first `10m`

### Suite C: Rotation-Storm Behavior

Purpose:
- stress the most fragile window: round change

Scenario:
- `250` DATUM sessions
- trigger synthetic rapid round resets or repeated qualifying test-trigger rounds
- run `30` consecutive rotations

Pass criteria:
- `Payout mismatch` rejects after `60s` from rotation: `0`
- `Wrong parent block` rejects after `60s` from tip change: `0`
- no candidate divergence lasting more than `15s`
- no solo-fallback loops longer than `30s`

### Suite D: Multi-Peer Network Load

Purpose:
- test peer polling, relay fanout, and convergence

Scenarios:

1. `10` peers for `30m`
2. `25` peers for `30m`
3. `100` peers in bounded-degree overlay simulation for `60m`

Pass criteria:
- zero crashes
- peer failures degrade cleanly to peer-status changes
- no host exit from peer timeouts
- convergence restored within `30s` after perturbations

### Suite E: Mixed Realistic Load

Purpose:
- combine mining load and peer load

Scenario:
- `500` DATUM sessions
- `25` Boot peers
- `2h` run

Pass criteria:
- all of:
  - no crashes
  - p95 coinbaser fetch `< 50 ms`
  - p99 coinbaser fetch `< 100 ms`
  - no slow fetches `>= 250 ms`
  - candidate divergence windows `<= 15s`
  - no persistent payout-mismatch bursts outside the first `60s` after a rotation

### Suite F: Abuse And Adversarial Load

Purpose:
- verify the node stays safe under deliberate misuse

Scenarios:

1. exact duplicate flood
2. same share replay from multiple peer identities
3. malformed coinbase/header flood
4. spoofed `X-Forwarded-For` flood from one real IP
5. reconnect churn storm

Pass criteria:
- zero crashes
- zero unhandled host exits
- rate limiting actually engages
- no attacker-controlled unbounded state growth
- duplicate shares never change On Deck state

## Required Reports Per Run

Each run should capture:

- test name and parameters
- software revision / commit hash
- config overrides
- start/end time
- pass/fail verdict
- summary metrics:
  - p50/p95/p99 coinbaser fetch
  - accepted/rejected counts by category
  - restart count
  - max memory
  - max CPU
  - longest candidate divergence interval

## Recommended Implementation Order

1. Build `datum-sim` for steady-state valid sessions
2. Add duplicate/replay scenarios
3. Add reconnect churn and fallback scenarios
4. Build `peer-sim`
5. Add combined mixed-load suite

## Launch Stress Targets

Minimum before public beta:

- `500` concurrent DATUM sessions
- `25` Boot peers
- `2h` mixed run
- all pass criteria met

Preferred before broad public launch:

- `1000` concurrent DATUM sessions
- `100` simulated peers with bounded degree
- `12h` mixed run
- all pass criteria met
