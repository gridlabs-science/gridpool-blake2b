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

Related execution docs:

- [launch-infra-plan.md](/home/keegreil/Documents/GitHub/boot-protocol/docs/launch-infra-plan.md)
- [stress-test-plan.md](/home/keegreil/Documents/GitHub/boot-protocol/docs/stress-test-plan.md)

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

## Execution Environment Tags

Remaining launch work should be planned around three execution environments:

- `DEV/LAPTOP NOW`: reasonable to implement and validate using the current dev machine plus laptop test setup
- `START NOW, FINISH HOSTED`: implementation can start locally, but final acceptance requires the hosted seed cluster
- `HOSTED ONLY`: defer until the `3` public hosted nodes exist, because the current home/laptop setup gives misleading evidence

### DEV/LAPTOP NOW

These are good work items while the current test setup is still in use:

- Fix untrusted peer fresh-parent handling.
  - Local DATUM can still learn fresh parents from otherwise-valid local shares.
  - Peer and HTTP paths should not advance/learn an unknown parent from a bare `prevhash` unless Boot can verify the parent.
  - Add regression tests for fake-parent peer shares.
- Change the default primary coinbase tag to `Grid Pool`.
  - Keep it configurable.
  - Allow an empty tag.
  - Validate length at startup instead of silently truncating.
  - Confirm tag is not consensus-critical.
- Clean up dependency vulnerabilities.
  - Run `dotnet list package --vulnerable --include-transitive`.
  - Upgrade or override vulnerable transitive packages.
  - Keep `dotnet test` green.
- Add config validation and safer startup failures.
  - invalid payout address
  - invalid/too-long coinbase tag
  - missing required public URL fields in production mode
  - invalid rate-limit values
  - testing reset mode accidentally enabled in production mode
- Move hardcoded ZMQ endpoint into config.
  - Keep `MempoolSpace` as the easy no-bitcoind default.
  - Make local `bitcoind`/ZMQ setup documented and testable.
- Improve Docker packaging locally.
  - non-root runtime user
  - container healthcheck
  - clearer `/data` layout
  - sample config copied/generated on first run
  - local compose smoke test
- Build the miner/operator install path.
  - one-command or near-one-command Docker quickstart
  - config wizard or setup script
  - DATUM connection snippet generator
  - first-run self-check command
  - backup/restore and upgrade docs
- Start appliance packaging research.
  - Umbrel app package plan
  - Start9 / StartOS service package plan
  - document platform constraints before implementation
- Write launch-facing docs.
  - install guide
  - upgrade guide
  - backup/restore guide
  - DATUM setup guide
  - Hydrapool HTTP submission guide
  - "what normal rejects look like" guide
  - public developer-preview warning language
- Start the G3 harness locally.
  - malformed request flood
  - duplicate/replay flood
  - HTTP share fixtures
  - peer simulator skeleton
  - DATUM-like session simulator if feasible
  - small local runs only
- Continue UI polish.
  - Lottery/Business/Nerd mode cleanup
  - connect instructions
  - production warning state
  - local miner address default selection
  - mobile layout polish
- Targeted code cleanup.
  - fix nullable warnings
  - establish formatting baseline
  - remove empty/dead files
  - split only the riskiest launch-critical pieces out of large files
  - avoid broad rewrites without tests

### START NOW, FINISH HOSTED

These can be mostly built locally, but should not be marked complete until the hosted cluster validates them:

- Hydrapool compatibility.
  - Local work:
    - keep HTTP share validation tag-agnostic
    - build synthetic Hydrapool-style fixtures
    - document required fields and slot-0 attribution
  - Hosted completion:
    - run real Hydrapool-compatible submission against a public node
    - confirm no Cloudflare/proxy/TLS behavior breaks the path
- G2 soak tooling.
  - Local work:
    - keep improving monitors and disk-flush behavior
    - add clearer pass/fail summaries
    - preserve partial data if a run stops early
  - Hosted completion:
    - run `48h` multi-node soak on stable public hosts
    - verify no process restarts, bounded rejects, and convergence limits
- G3 load harness.
  - Local work:
    - build simulators and reports
    - run small sanity tests
  - Hosted completion:
    - run the minimum target scenarios against realistic hardware and network paths
- Peer abuse and rate-limit tuning.
  - Local work:
    - unit/integration tests for spoofed IPs, malformed requests, duplicates, and low-diff spam
    - baseline limiter behavior
  - Hosted completion:
    - tune limits under public ingress and real reverse-proxy topology
- Deployment automation.
  - Local work:
    - make scripts generic
    - remove machine-specific paths
    - support dry-run/config-check mode
  - Hosted completion:
    - prove deploy, restart, rollback, and log collection on all hosted nodes

### HOSTED ONLY

These should wait until the `3` hosted nodes exist:

- Final `48h` G2 launch soak.
  - The current home/laptop setup is too sensitive to sleep, Tailscale, Wi-Fi/LAN quirks, and accidental laptop shutdown.
- Seed-node readiness.
  - DNS records
  - public firewall rules
  - TLS/reverse proxy
  - service restart policy
  - monitoring
  - backups
  - log retention
- Real multi-region convergence testing.
  - Candidate/current/tip divergence on real latency paths
  - peer reconnect behavior across regions
  - region-specific failure/recovery tests
- Public DATUM connection testing.
  - miner-to-node reachability
  - no Cloudflare proxy on DATUM TCP
  - correct advertised `datum_public_host`
  - public firewall and provider networking behavior
- Launch runbook rehearsal.
  - fresh deploy
  - config update
  - emergency admin disable
  - rollback
  - node replacement
  - restoring state from backup
- Public seed topology decisions.
  - official seed list
  - bootstrap peer list
  - node naming
  - production versus staging separation
- Production monitoring thresholds.
  - alert thresholds should be tuned from hosted-node behavior, not laptop behavior.

### Optional Backlog Items

The newest backlog ideas remain optional and should not block launch unless they become strategically important:

- coinbase explorer submission after name/public URL are final
- censorship/fairness detection
- transaction-policy attestation
- proof-of-work-gated DoS protection
- public network map

These can be researched locally, but implementation should wait until the launch-critical gates above are stable.

## Gate 1: Security And Protocol Correctness

Latest verification snapshot:
- `2026-04-21`
- targeted suite: `dotnet test boot.tests/boot.tests.csproj --no-restore`
- result: `Passed 32 / 32`

### G1.1 Slot-0 attribution on all untrusted share paths

Status:
- `DONE`

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

Current evidence:
- automated coverage in:
  - [ShareAttributionTests.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot.tests/ShareAttributionTests.cs)
  - `ValidateShareAttributesToSlotZeroInsteadOfClaimedMinerAddress`
  - `HttpShareWithForgedMinerAddressIsAcceptedAndAttributedToSlotZeroAsync`
  - `PeerShareWithForgedMinerAddressIsAcceptedAndAttributedToSlotZeroAsync`
  - `ValidateShareRejectsSlotZeroMutationWithoutHeaderRecompute`
  - `SlotZeroMutationWithRecomputedHeaderButLowPowIsRejectedAsync`
- implementation lives in:
  - [BootShareVerifier.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Services/BootShareVerifier.cs)
  - [MiningApiController.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Controllers/MiningApiController.cs)
  - [BootPeerController.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Controllers/BootPeerController.cs)

### G1.2 Exact duplicate and replay resistance

Status:
- `DONE`

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

Current evidence:
- automated coverage in [ShareAttributionTests.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot.tests/ShareAttributionTests.cs):
  - `LocalSubmitFollowedByPeerReplayReturnsDuplicateWithoutChangingCandidateStateAsync`
  - `LocalSubmitFollowedByDuplicateLocalSubmitReturnsDuplicateWithoutChangingCandidateStateAsync`
  - `PeerSubmitFollowedByDuplicatePeerSubmitReturnsDuplicateWithoutChangingCandidateStateAsync`
  - `DuplicateBlockRotationIsIgnoredAfterFirstApplyAsync`
- implementation continues to use `_seenShareIds` in [BootProtocolStateService.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Services/BootProtocolStateService.cs)
- duplicate winning-block protection is currently covered at the rotation boundary by block-hash reapplication suppression
- suite currently green in the `2026-04-18` snapshot above

### G1.3 Forwarded-IP trust and rate-limit hardening

Status:
- `DONE`

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

Current evidence:
- automated coverage in [BootRequestIdentityTests.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot.tests/BootRequestIdentityTests.cs):
  - `GetClientKeyIgnoresForwardedHeadersFromUntrustedDirectClient`
  - `GetClientKeyUsesForwardedHeadersFromTrustedProxy`
  - `TrustedProxyRangesHonorCidrMatching`
- implementation lives in:
  - [BootRequestIdentity.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Utils/BootRequestIdentity.cs)
  - [Program.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Program.cs)

### G1.4 Request validation hardening

Status:
- `DONE`

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

Current evidence:
- cheap-fail request corpus currently covered in [BootRequestGuardsTests.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot.tests/BootRequestGuardsTests.cs):
  - well-formed payload
  - missing caller miner address
  - oversized payload
  - oversized coinbase
  - oversized merkle path
  - malformed hex header
  - malformed header length
  - malformed hex coinbase
  - malformed merkle entry
- peer compatibility rejection coverage in [ShareAttributionTests.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot.tests/ShareAttributionTests.cs):
  - `PeerSubmitWithWrongNetworkIsRejectedBeforeInsertionAsync`
  - `PeerSubmitWithWrongProtocolVersionIsRejectedBeforeInsertionAsync`
- live malformed-request smoke run:
  - [malformed-request-smoke.sh](/home/keegreil/Documents/GitHub/boot-protocol/scripts/malformed-request-smoke.sh)
  - executed successfully on `2026-04-18`
  - validated:
    - `400` for missing header
    - `400` for malformed coinbase hex
    - `400` for malformed merkle entry
    - `413` for oversized share payload
    - `400` for peer network mismatch
  - server remained alive and post-run `/api/network/summary` stayed under the smoke-test threshold
- suite currently green in the `2026-04-18` snapshot above

### G1.5 Admin surface hardening

Status:
- `DONE`

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

Current progress:
- config support now exists for `enable_admin_api`
- admin reset endpoints can be disabled completely by config
- tracked Docker sample [boot_portal_config.sample.json](/home/keegreil/Documents/GitHub/boot-protocol/docker/boot_portal_config.sample.json) now defaults `enable_admin_api` to `false`
- tracked runtime config [boot_portal_config.json](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/boot_portal_config.json) now contains placeholder values only
- adjacent untracked local override config is supported via `boot_portal_config.local.json`
- operator guidance is documented in [README.md](/home/keegreil/Documents/GitHub/boot-protocol/README.md)

Evidence:
- code/config:
  - [Program.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Program.cs)
  - [BootPortalPaths.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Utils/BootPortalPaths.cs)
  - [boot_portal_config.json](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/boot_portal_config.json)
  - [boot_portal_config.sample.json](/home/keegreil/Documents/GitHub/boot-protocol/docker/boot_portal_config.sample.json)
- operator docs:
  - [README.md](/home/keegreil/Documents/GitHub/boot-protocol/README.md)

## Gate 2: Reliability And Convergence

Latest `G2` soak snapshot:
- `2026-04-21` active soak to `17:00 America/New_York`
- tooling artifacts:
  - [g2-monitor-2026-04-21-1700.json](/home/keegreil/Documents/GitHub/boot-protocol/g2-monitor-2026-04-21-1700.json)
  - [g2-laptop-issue-prelim-2026-04-21.json](/home/keegreil/Documents/GitHub/boot-protocol/g2-laptop-issue-prelim-2026-04-21.json)
  - [g2-laptop-issue-post-ratefix-2026-04-21.json](/home/keegreil/Documents/GitHub/boot-protocol/g2-laptop-issue-post-ratefix-2026-04-21.json)
- [g2-laptop-issue-2026-04-21-1700.json](/home/keegreil/Documents/GitHub/boot-protocol/g2-laptop-issue-2026-04-21-1700.json)
- [g2-laptop-issue-post-soak-2026-04-21.json](/home/keegreil/Documents/GitHub/boot-protocol/g2-laptop-issue-post-soak-2026-04-21.json)
- key findings:
  - main node was throttling laptop peer share relay with HTTP `429` at the old `peer_write_rate_limit_per_minute = 90`
  - test/default peer write limit raised to `3000/min`
  - no `peer-relay-failed` events were observed after the rate-limit change
  - current state stayed well behaved:
    - current-state divergence after the deploy window: `2` intervals, longest about `5.1s`
    - tip divergence after the deploy window: `12` intervals, longest about `10.1s`
  - candidate divergence after the deploy window improved but still needs watching:
    - `203` intervals
    - longest about `25.2s`
    - `3` intervals above `15s`
    - `0` intervals above `30s`
  - coinbaser hot path passed:
    - main avg/p95 about `0.57 ms / 0.76 ms`
    - laptop avg/p95 about `0.93 ms / 1.48 ms`
    - slow fetch count `0` on both nodes
  - remaining issue:
    - wrong-parent rejects can begin a few seconds before Boot records the new chain tip, meaning DATUM/miner can see fresh Bitcoin work before Boot's block-notification path
    - payout-mismatch bursts then continue after deterministic test rotations while DATUM/miner stale templates drain
    - laptop still shows frequent DATUM session locks/reconnects
  - example post-soak burst:
    - laptop wrong-parent rejects at `2026-04-21T21:27:11Z` to `2026-04-21T21:27:15Z`
    - laptop chain-tip/round-rotation recorded at `2026-04-21T21:27:15Z`
    - laptop payout-mismatch burst continued until about `2026-04-21T21:27:54Z`
    - one Boot-side `datum-session-reset` fired after about `20.1s` of stale payout shares

Previous `G2` soak snapshot:
- `2026-04-19` to `2026-04-20`
- tooling artifacts:
  - [g2-monitor-2026-04-19-live.json](/home/keegreil/Documents/GitHub/boot-protocol/g2-monitor-2026-04-19-live.json)
- key findings:
  - coinbaser hot path remained healthy on both nodes
  - live current/candidate/tip state converged by the end of the run
  - candidate divergence was still too noisy:
    - longest observed interval about `90.6s`
    - `77` intervals in a `4h` run
  - main node history reconstruction remained incomplete after the earlier Tailscale outage:
    - main `canonical=11`, `orphaned=21`
    - peer `canonical=32`, `orphaned=0`
  - laptop DATUM path still showed too many late `Payout mismatch` rejects

Working definition of `good enough` for G2:
- `Payout mismatch`
  - indicates the share coinbase is using the wrong Boot payout layout for the current round
  - acceptable only in a short post-rotation transition window
- `Wrong parent block`
  - indicates the share header builds on a Bitcoin `prevhash` that is outside the node's currently accepted parent-block set
  - this is a Bitcoin-parent mismatch, not a direct "wrong Boot round" signal
  - some amount is expected around real tip changes and round resets
  - local DATUM shares may now teach Boot about a fresh parent before Boot's own block notifier observes it, but only after the share validates cryptographically and against the current payout list
- `Solo fallback template`
  - indicates the mining client is still hashing a non-Boot single-recipient template
  - acceptable only during brief reconnect/failover windows

Current working exit target:
- `Payout mismatch > 60s after round rotation = 0`
- `Solo fallback template > 60s after rotation/reconnect = 0`
- `Wrong parent block > 60s after parent-set changes` is tolerated only at low rate:
  - target `<= 0.5%` of local DATUM submissions per node over a soak
  - no single burst should continue past `120s`
- candidate divergence:
  - preferred `<= 15s`
  - provisional launch ceiling `<= 30s` longest interval during a stable two-node soak
- coinbaser:
  - no slow fetches
  - average and p95 remain inside the existing thresholds below

### G2.1 Post-rotation reject burst bounded

Status:
- `OPEN`

Goal:
- rejects immediately after a round rotation or parent-block change may happen
- they must be short-lived and self-recovering

Acceptance criteria:
- over a `48h` two-node soak:
  - `Payout mismatch` rejects occurring more than `60s` after a round rotation are `0`
  - `Wrong parent block` rejects occurring more than `60s` after a parent-set change are rare and bounded:
    - `<= 0.5%` of local DATUM submissions per node over the soak
    - no burst lasts longer than `120s`
  - `Solo fallback template` bursts are absent in steady state and only tolerated briefly during reconnect/failover tests

Evidence to capture:
- `share-diagnostics` correlated with `events`
- one summary report per soak

Current tooling:
- [boot-soak-report.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-soak-report.mjs)
  - summarizes local DATUM acceptance, reject categories, late reject counts, and coinbaser timings from the current APIs
- [boot-g2-monitor.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-g2-monitor.mjs)
  - polls one or two nodes over time
  - measures candidate/current/tip divergence intervals
  - emits G2-oriented verdict hints from live API data
- [boot-g2-soak.sh](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-g2-soak.sh)
  - starts a repeatable soak with the default main/laptop URLs
  - writes both a monitor JSON and a post-run summary JSON under `logs/`
  - example: `scripts/boot-g2-soak.sh 2h`
- [boot-laptop-issue-report.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-laptop-issue-report.mjs)
  - filters diagnostics by exact timestamps client-side
  - buckets rejects against chain tips, round rotations, DATUM refreshes, and DATUM session events

Current mitigation:
- [BootProtocolStateService.cs](/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/Services/BootProtocolStateService.cs) accepts a local DATUM share on an unknown fresh parent if:
  - the only initial failure is `Wrong parent block`
  - revalidation without the parent allow-list succeeds
  - the coinbase payout list matches the current Boot state
  - computed difficulty is at least `1`
  - source is the trusted local DATUM path
- Lower-height chain-tip or round-rotation observations are ignored unless they match the already-known tip hash.
  - stale observations are recorded as `chain-tip-stale`
  - the G2 monitor flags any sampled tip-height regression as a `G2.2` attention condition
- HTTP shares remain strict until Boot already knows the parent, preserving the trustless Hydrapool launch path.
- Automated coverage:
  - `DatumShareOnFreshParentIsAcceptedAndLearnsParentWithoutTipAdvanceAsync`
  - `HttpShareOnFreshParentIsRejectedUntilTipIsKnownAsync`
  - `LowerHeightChainTipObservationIsIgnoredWithoutRegressingTipAsync`
  - `LowerHeightRoundRotationIsIgnoredWithoutAdvancingRoundAsync`

Fresh-parent soak signals:
- `fresh-parent-learned` event counts are included in `boot-g2-monitor`, `boot-soak-report`, and `boot-laptop-issue-report`.
- `Wrong parent block` rejects are split into:
  - `within 10s before chain-tip`
  - `within 10s after chain-tip`
  - `outside the +/-10s chain-tip window`
- G2 verdicts use `wrongParentAfter60sOutside10sChainTipWindow` for the noisy wrong-parent alarm, so a DATUM-sees-tip-first reject is reported without being treated the same as an unexplained late stale-parent condition.
- Routine `datum-coinbaser-fetch` events are kept in `/api/network/coinbaser-diagnostics` instead of `/api/network/events`, so consensus events like `chain-tip`, `round-rotation`, and `fresh-parent-learned` are not pushed out of the network-event retention window during high-churn DATUM sessions.
- Desired post-patch shape:
  - some `fresh-parent-learned` events are acceptable and useful
  - `Wrong parent block` before-chain-tip rejects should drop materially
  - `Wrong parent block` outside the +/-10s chain-tip window should trend toward zero

### G2.2 Candidate convergence is self-healing

Status:
- `OPEN`

Acceptance criteria:
- during a `48h` soak:
  - preferred target:
    - no candidate divergence lasting more than `15s`
  - provisional launch ceiling:
    - no candidate divergence lasting more than `30s`
- during explicit disruption tests:
  - laptop sleep/wake
  - peer offline `1h`
  - one node power loss
  - DATUM reconnect storm
  - candidate convergence returns within `30s`

Evidence to capture:
- timestamped summaries from both nodes
- event log around each disruption

Current tooling:
- [boot-soak-report.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-soak-report.mjs)
  - captures current-state and candidate-state convergence at the time of sampling for one or two nodes
- [boot-g2-monitor.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-g2-monitor.mjs)
  - captures longest candidate/current/tip divergence windows during an actual polling run

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

Current tooling:
- [boot-soak-report.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-soak-report.mjs)
- [boot-g2-monitor.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-g2-monitor.mjs)

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

Current tooling:
- [boot-g2-monitor.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-g2-monitor.mjs)
  - captures API availability interruptions and peer-side instability during a soak
- [boot-main.sh](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-main.sh)
  - existing wrapper for service status and journal access on the main node

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

Current tooling:
- [boot-history-check.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-history-check.mjs)
  - validates canonical round-history consistency from the live API
  - checks `Round N nextRecipients == Round N+1 paidRecipients`
  - compares current/candidate/tip alignment across two nodes when both URLs are provided

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
- low-difficulty peer share spam below the current on-deck admission floor
- peer replay flood
- spoofed forwarded-IP rate-limit bypass attempts
- reconnect churn

Acceptance criteria:
- service stays up
- no runaway memory growth
- rate limiter engages where expected
- peers repeatedly submitting shares below the advertised on-deck admission floor are disconnected or deprioritized
- no attacker-controlled growth in `_seenShareIds`, diagnostics, or archived state beyond configured caps

Design note:
- Proof-of-work should be the primary peer-share admission control.
- A peer that submits a valid high-difficulty share has already paid a real work cost.
- A peer that keeps sending low-difficulty shares after being told the current minimum useful difficulty is producing obvious spam and should be disconnected before it consumes hot-path validation time.

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
- appliance packaging notes for Umbrel and Start9 / StartOS

Acceptance criteria:
- a new operator can install from docs without private chat help
- Docker install path works without source checkout
- Umbrel and Start9 packaging requirements are documented, even if full packages remain beta/post-launch

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
9. `G4.2` Docker/operator docs and appliance packaging plans
10. `G4.3` / `G4.4` seed-node readiness and supported topology docs

## Launch Definition

The network is ready for launch when:

- all `BLOCKING` items in `G1` and `G4` are complete
- `G2` acceptance criteria are met in a `48h` soak
- the minimum `G3` load targets are met
- the launch runbook exists and has been exercised once on staging/test infrastructure

Until then, the project should still be treated as developer preview / beta.
