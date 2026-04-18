# Boot Protocol Launch Checklist

## Purpose

This document turns the remaining launch work into concrete gates with measurable exit criteria.

The goal is to make longer implementation sessions possible without user input by making it explicit:

- what the next milestone is
- what must be true before it counts as complete
- what evidence should be captured
- what is a launch blocker versus a post-launch improvement

This checklist is written against the current architecture:

- DATUM is a supported mining client
- Hydrapool HTTP share submission is a required launch path
- Boot nodes form a decentralized peer network
- the public WebUI has `Lottery`, `Business`, and `Nerd` modes

## Core Protocol Assumption: Share Attribution

Launch work must preserve this protocol rule:

- share attribution is keyed off the coinbase **slot-0 payout address**
- not off a caller-supplied `MinerAddress`
- not off a peer-supplied `Username`
- not off a DATUM worker string

Rationale:

- For untrusted HTTP and peer-submitted shares, the only trustworthy attribution is what is actually committed into the hashed coinbase transaction.
- If an attacker changes slot `0`, the merkle root changes, the header changes, and the original proof-of-work no longer validates.
- Therefore, every untrusted share path must:
  - reconstruct and validate the merkle root
  - hash the header
  - validate the achieved difficulty
  - extract slot `0`
  - attribute the share to that slot-0 address

Non-goal:

- The Boot server does **not** attempt to protect miners against local compromise inside their own mining stack.
- If a miner or gateway builds templates with the wrong slot-0 address before the ASIC hashes them, the share belongs to that slot-0 address as submitted.

This is a hard launch requirement because Hydrapool support depends on the HTTP share path being trustless.

## Gate Overview

Launch gates:

1. `G1` Security and protocol correctness
2. `G2` Reliability and convergence
3. `G3` Load, abuse, and stress readiness
4. `G4` Product, operator, and launch operations

`G1` and `G2` are mandatory before public beta.
`G3` is mandatory before any serious public announcement.
`G4` is mandatory before calling the network "launched".

## Gate 1: Security And Protocol Correctness

### G1.1 Slot-0 attribution on all untrusted share paths

Status:
- `BLOCKING`

Required implementation:
- For HTTP share submission:
  - ignore caller-supplied payout attribution after request validation
  - derive attribution from validated slot `0`
- For peer share submission:
  - ignore sender-supplied payout attribution after request validation
  - derive attribution from validated slot `0`
- For DATUM session submission:
  - keep current session-lock behavior
  - ensure internal attribution still resolves to the same slot-0 address that was actually hashed

Must be true:
- A valid share submitted with a forged `MinerAddress` but unchanged header/coinbase is still accepted and attributed to the slot-0 address in the coinbase.
- A valid share cannot be reassigned to another payout address without invalidating the proof.

Acceptance criteria:
- Automated tests exist and pass for:
  - `HTTP valid share, forged MinerAddress -> accepted, attributed to slot-0`
  - `Peer valid share, forged MinerAddress -> accepted, attributed to slot-0`
  - `Changing slot-0 without recomputing header -> rejected`
  - `Changing slot-0 with recomputed header but insufficient PoW -> rejected`

Evidence to capture:
- test output from `boot.tests`
- one JSON example from each path showing forged request fields but correct slot-0 attribution

### G1.2 Exact duplicate and replay resistance

Status:
- `PARTIALLY DONE`

Current known behavior:
- exact duplicate share IDs are rejected via `_seenShareIds` in [BootProtocolStateService.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Services/BootProtocolStateService.cs)

Remaining work:
- verify duplicate semantics after slot-0 attribution migration
- verify replay resistance across:
  - local submit
  - peer relay
  - imported archived state
  - round rotation boundary

Must be true:
- The same accepted share cannot occupy multiple On Deck slots.
- The same accepted share cannot be reinserted after peer relay.
- Duplicate block-finding shares do not produce duplicate rotations.

Acceptance criteria:
- Automated tests exist and pass for:
  - `local submit -> duplicate local submit`
  - `local submit -> same share via peer relay`
  - `peer submit -> duplicate peer submit`
  - `duplicate winning block share around rotation`
- In all cases:
  - second response is `duplicate`
  - `onDeckCount` is unchanged
  - `candidateStateId` is unchanged

Evidence to capture:
- API responses for first/second submit
- state summary before/after duplicate replay

### G1.3 Forwarded-IP trust and rate-limit hardening

Status:
- `BLOCKING`

Current known issue:
- request identity currently trusts `CF-Connecting-IP`, `X-Forwarded-For`, and `X-Real-IP` unconditionally in [BootRequestIdentity.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Utils/BootRequestIdentity.cs)
- a direct client may be able to spoof rate-limit identity

Required implementation:
- only trust forwarded-IP headers from explicitly configured reverse proxies
- otherwise use `RemoteIpAddress`
- document supported proxy topology

Must be true:
- direct clients cannot mint new rate-limit buckets by spoofing headers

Acceptance criteria:
- automated or scripted test:
  - same source IP sends repeated requests with changing `X-Forwarded-For`
  - limiter still treats them as one client unless the request came from a trusted proxy

Evidence to capture:
- test log showing same partition key for spoofed direct requests
- rate-limit rejection after expected threshold

### G1.4 Request validation hardening

Status:
- `IN PROGRESS`

Required implementation:
- keep request size limits
- keep merkle-path and coinbase length limits
- audit all request paths for:
  - unbounded parsing
  - attacker-controlled list growth
  - expensive work before cheap validation

Must be true:
- malformed share requests fail cheap
- malformed requests do not trigger expensive work repeatedly

Acceptance criteria:
- malformed corpus test passes:
  - oversized coinbase
  - oversized merkle path
  - malformed hex
  - bad header length
  - unsupported network/protocol peer payload
- no crash, no host restart, no multi-second lock stalls

Evidence to capture:
- corpus test output
- no `BackgroundService failed` or unhandled exceptions in logs

### G1.5 Admin surface hardening

Status:
- `BLOCKING`

Required implementation:
- ensure no default production admin key is shipped
- document production guidance for admin API
- optionally disable admin reset endpoints by config in production builds/configs

Must be true:
- production config cannot accidentally expose easy admin access

Acceptance criteria:
- sample/docker configs do not contain usable admin keys
- production checklist explicitly sets:
  - strong admin key
  - or admin API disabled

Evidence to capture:
- config diff
- operator docs section

## Gate 2: Reliability And Convergence

### G2.1 Post-rotation reject burst bounded

Status:
- `OPEN`

Goal:
- rejects immediately after a round rotation or parent-block change may happen
- they must be short-lived and self-recovering

Acceptance criteria:
- over a `48h` two-node soak:
  - `Payout mismatch` rejects occurring more than `60s` after a round rotation are `0`
  - `Wrong parent block` rejects occurring more than `60s` after a normal tip change are `0`
  - `Solo fallback template` bursts are absent in steady state and only tolerated briefly during reconnect/failover tests

Evidence to capture:
- `share-diagnostics` correlated with `events`
- one summary report per soak

### G2.2 Candidate convergence is self-healing

Status:
- `OPEN`

Acceptance criteria:
- during a `48h` soak:
  - no candidate divergence lasting more than `15s`
- during explicit disruption tests:
  - laptop sleep/wake
  - peer offline `1h`
  - one node power loss
  - DATUM reconnect storm
  - candidate convergence returns within `30s`

Evidence to capture:
- timestamped summaries from both nodes
- event log around each disruption

### G2.3 Coinbaser hot-path performance stays inside budget

Status:
- `MOSTLY DONE`

Acceptance criteria:
- over `24h` soak on both nodes:
  - `coinbaserDiagnostics.slowFetchCount == 0`
  - average coinbaser fetch `< 10 ms`
  - p95 coinbaser fetch `< 50 ms`
  - p95 `stateReadDurationMs < 25 ms`

Evidence to capture:
- `/api/network/coinbaser-diagnostics`
- summary snapshots every hour

### G2.4 No host crash on peer/network turbulence

Status:
- `OPEN`

Acceptance criteria:
- over `48h` soak:
  - zero unhandled process exits
  - zero `systemd` restart events not initiated by deployment
- peer timeout / relay timeout must degrade to peer failure state, not process death

Evidence to capture:
- `journalctl -u bootserverapp.service`
- explicit restart count = `0`

### G2.5 Round-history invariants hold

Status:
- `OPEN`

Acceptance criteria:
- automated tests and live validation both confirm:
  - `Round N nextRecipients == Round N+1 paidRecipients`
  - orphaned rounds never appear as canonical later
  - genesis reset returns both nodes to round `0` with empty history

Evidence to capture:
- test output
- API sample from `/api/network/history`

## Gate 3: Load, Abuse, And Stress Readiness

### G3.1 Load generator exists

Status:
- `BLOCKING`

Required implementation:
- create a repeatable load harness for:
  - many simulated DATUM clients
  - many peer Boot nodes
  - malformed/abusive clients

Acceptance criteria:
- one command can start each scenario and produce a machine-readable report

Evidence to capture:
- checked-in harness docs and scripts

### G3.2 Single-node DATUM load target

Status:
- `OPEN`

Minimum beta target:
- `500` concurrent DATUM sessions

Preferred target:
- `1000` concurrent DATUM sessions

Acceptance criteria at target:
- no crashes
- no slow coinbaser fetches `>= 250 ms`
- p95 coinbaser fetch `< 50 ms`
- p95 accepted-share handling `< 100 ms`
- CPU and memory remain stable for `30m`
- local acceptance rate under normal templates `>= 99%` outside the first `60s` after rotations

### G3.3 Multi-peer network target

Status:
- `OPEN`

Minimum beta target:
- `25` Boot peers

Preferred target:
- `100` Boot peers in bounded-degree topology simulation

Acceptance criteria:
- no crash loops
- peer poll/relay failures do not cascade
- state convergence still completes after rotations
- candidate divergence windows remain within the `G2.2` limits

### G3.4 Abuse-path target

Status:
- `OPEN`

Scenarios:
- duplicate share flood
- malformed share flood
- peer replay flood
- spoofed forwarded-IP rate-limit bypass attempts
- reconnect churn

Acceptance criteria:
- service stays up
- no runaway memory growth
- rate limiter engages where expected
- no attacker-controlled growth in `_seenShareIds`, diagnostics, or archived state beyond configured caps

## Gate 4: Product, Operator, And Launch Operations

### G4.1 UI launch polish

Status:
- `OPEN`

Lottery mode must:
- prioritize odds, queue progress, and celebration state
- hide technical disclosure entirely
- keep full winner lists collapsed by default

Business mode must:
- prioritize network health, local-vs-team contribution, and payout cadence
- express reject reasons as likely causes
- keep the raw lists out of the main flow

Nerd mode must:
- remain the complete truth surface for debugging

Acceptance criteria:
- manual review against [ui-modes-plan.md](/home/keegreil/Documents/GitHub/boot-protocol/docs/ui-modes-plan.md)
- no mode-specific JS errors
- both nodes serve the same page behavior

### G4.2 Operator docs complete

Status:
- `OPEN`

Required docs:
- install guide
- upgrade guide
- backup/restore guide
- troubleshooting guide
- "what normal rejects look like" guide
- launch-day runbook

Acceptance criteria:
- a new operator can install from docs without private chat help

### G4.3 Seed-node readiness

Status:
- `OPEN`

Acceptance criteria:
- at least `3` public seed nodes
- geographically separated
- monitored
- restart policy enabled
- config/state backup documented

### G4.4 Supported topology documented

Status:
- `BLOCKING`

The launch docs must say clearly:
- one payout identity per DATUM session
- Hydrapool HTTP submission is supported
- raw HTTP share submission must follow slot-0 attribution rules
- what is unsupported at launch

Acceptance criteria:
- no ambiguous launch claims remain in docs or UI help

## Recommended Execution Order

If implementation begins immediately, the next work should be:

1. `G1.1` slot-0 attribution hardening on HTTP and peer share paths
2. `G1.3` forwarded-header / rate-limit hardening
3. `G1.2` duplicate/replay test suite
4. `G2.1` bounded post-rotation reject-burst work
5. `G2.2` extended convergence soak and disruption testing
6. `G3.1` load generator
7. `G3.2` / `G3.3` stress runs
8. `G4.1` final public-mode UI polish
9. `G4.2` / `G4.3` / `G4.4` operator and launch docs

## Launch Definition

The network is ready for launch when:

- all `BLOCKING` items in `G1` and `G4` are complete
- `G2` acceptance criteria are met in a `48h` soak
- the minimum `G3` load targets are met
- the launch runbook exists and has been exercised once on staging/test infrastructure

Until then, the project should still be treated as developer preview / beta.
