# Miner / Node Swap Test Notes

## Goal

Compare stale-share and invalid-share behavior across two mining setups by swapping miners between the two DATUM + Boot node stacks.

Questions:

- Does the bad behavior follow a specific miner / firmware?
- Does it stay with a specific Boot + DATUM node stack?
- Do reject patterns differ between the StartOS DATUM path and the source-built laptop DATUM path?

## Test A

Timestamp of snapshot: 2026-04-06 around 22:17 EDT

Assignment:

- Main / StartOS DATUM / `https://boot.gridlabs.science`
  - Miner: Bitaxe
  - Expected hashrate: about `1 TH/s`
  - Payout address: `...9s8y`
- Laptop / source-built DATUM / `http://100.96.249.123:5000`
  - Miner: space heater
  - Expected hashrate: about `9.5 TH/s`
  - Payout address: `...q4p`

### Snapshot Summary

Shared locked state at snapshot:

- `currentRoundNumber = 129`
- `currentStateId = 152b992b2cffb05b36def0f7dd191151e7d931306123821765ca0232cb9b0543`
- `currentTipBlockHeight = 943965`

Live candidate state at snapshot:

- Main candidate: `b54b7aab8e61d006b5544fbc2ab1a9263560e48dcc8931efbd34de57d912dd74`
- Laptop candidate: `ef903bec8c6e49e3e27ad575a3c5a62301bb2441135a056de2407fdf420ae681`

This means the nodes were still aligned on the current locked round, but not fully converged on the active On Deck candidate at the instant of capture.

### Main Node Diagnostics

Source:

- `GET https://boot.gridlabs.science/api/network/summary`

Local DATUM diagnostics:

- submissions: `2827`
- accepted: `2764`
- accepted onto On Deck: `2265`
- rejected: `63`
- acceptance rate: about `97.8%`
- local DATUM estimate: `946.91 GH/s`

Top rejection reasons:

- `Payout mismatch = 45`
- `Slot 0 mismatch = 11`
- `Wrong parent block = 7`

Log pattern:

- repeated `Coinbase winners payouts do not match...`
- repeated stale-client disconnect fallback after `4` consecutive stale payout shares
- occasional `Coinbase slot 0 does not pay the submitting miner`

Interpretation:

- The main / StartOS path is still not clean.
- Even with the lower-hashrate Bitaxe, it is still producing stale-template behavior.
- However, the overall reject rate is comparatively low.

### Laptop Node Diagnostics

Source:

- `GET http://127.0.0.1:5000/api/network/summary` via laptop SSH

Local DATUM diagnostics:

- submissions: `6426`
- accepted: `5451`
- accepted onto On Deck: `5216`
- rejected: `975`
- acceptance rate: about `84.8%`
- local DATUM estimate: `5.35 TH/s`

Top rejection reasons:

- `Slot 0 mismatch = 435`
- `Payout mismatch = 394`
- `Low difficulty = 123`
- `Wrong parent block = 23`

Log pattern:

- long runs of `Coinbase slot 0 does not pay the submitting miner`
- then repeated `Coinbase winners payouts do not match...`
- then stale-client disconnect fallback

Interpretation:

- The laptop / source-built DATUM path is much noisier during this run.
- The dominant distinctive failure here is `Slot 0 mismatch`.
- This is the clearest signal to compare after swapping miners.

### Recent Round Outcomes

Recent completed rounds visible from laptop history:

- Round `128`
  - paid recipients:
    - `...q4p`: `80` slots, `177304960 sats`
    - `...9s8y`: `60` slots, `132978720 sats`
  - next recipients:
    - `...q4p`: `115` slots, `208938900 sats`
    - `...9s8y`: `56` slots, `101744160 sats`

- Round `127`
  - paid recipients:
    - `...q4p`: `287` slots, `298958142 sats`
    - `...9s8y`: `12` slots, `12499992 sats`
  - next recipients:
    - `...q4p`: `80` slots, `177304960 sats`
    - `...9s8y`: `60` slots, `132978720 sats`

- Round `122`
  - paid recipients:
    - `...9s8y`: `64` slots, `227272704 sats`
    - `...q4p`: `23` slots, `81676128 sats`
  - next recipients:
    - `...q4p`: `287` slots, `298958142 sats`
    - `...9s8y`: `12` slots, `12499992 sats`

Interpretation:

- The payout split is still extremely noisy.
- Some rounds align with the expected heavier laptop miner dominance.
- Other rounds show the low-hashrate Bitaxe winning a surprisingly large share.
- This is consistent with ongoing invalid-share churn and short-round variance, not with a settled fair-distribution picture.

## Provisional Read After Test A

- The problem is **not** isolated to the main / StartOS node.
- The laptop stack is clearly worse in this run by raw rejection rate and especially by `Slot 0 mismatch`.
- The main / StartOS path still shows repeated stale payout mismatches and reconnect churn.
- Because the higher-hashrate miner is currently on the laptop, this run alone cannot distinguish:
  - miner / firmware behavior
  - laptop DATUM behavior
  - laptop Boot-node behavior

## What To Compare In Test B

After swapping the miners, compare:

- local DATUM rejection rate on each node
- `Slot 0 mismatch` count on each node
- `Payout mismatch` count on each node
- whether the worse behavior follows:
  - the space heater miner
  - the Bitaxe
  - the laptop DATUM stack
  - the main / StartOS DATUM stack

Most important discriminator:

- If `Slot 0 mismatch` follows the space heater miner, the problem is likely miner / firmware specific.
- If it stays on the laptop stack after the swap, the problem is likely in the laptop DATUM / Boot integration path.

## Interim Single-Node Observation

Timestamp of snapshot: 2026-04-07 around 12:05 EDT

Context:

- The Bitaxe dropped off the laptop path and fell back to the main / StartOS DATUM node.
- That put both miners onto the main Boot + DATUM stack temporarily.

### Reject Breakdown On Main Node

Window sampled:

- main journal since `2026-04-07 00:40:00`

Rejected shares by payout address:

- `...9s8y`: `484`
- `...kq4p`: `60`

Reason breakdown:

- `...9s8y`
  - `Slot 0 mismatch = 466`
  - `Payout mismatch = 11`
  - `Wrong parent block = 7`

- `...kq4p`
  - `Slot 0 mismatch = 59`
  - `Wrong parent block = 1`

So in this temporary same-node setup, `...9s8y` is producing about `8x` as many rejected shares as `...kq4p`.

### Important Architecture Clue

The more important finding is in the connection logs:

- the main node repeatedly logs `Restored DATUM client payout address ... for client 5b1c7ec3...`
- both `...9s8y` and `...kq4p` appear under that same client fingerprint

Interpretation:

- the upstream DATUM gateway appears to be multiplexing multiple downstream miners through one Boot-facing client identity
- Boot currently remembers payout / slot-0 state keyed too coarsely at the DATUM client level
- when two miners with different payout addresses share one DATUM client identity, they can overwrite each other's remembered slot-0 expectation

This is a much stronger explanation for the `Slot 0 mismatch` storm than a pure miner-firmware issue.

### Likely Next Engineering Direction

- stop treating one DATUM client identity as equal to one payout address
- key slot-0 / payout expectations per submitted job or per submitted address, not just per Boot-facing DATUM session
- re-check whether the same issue also exists on the laptop stack when multiple downstream miners share one DATUM connection

## Test A Rerun After DATUM Session Lock Fix

Timestamp of snapshot: 2026-04-08 around 00:20 EDT

Context:

- The Boot nodes were updated with the "one DATUM session = one payout address" rule.
- Sessions now start with the 256 Foundation address in slot `0` until the first valid DATUM share locks the session payout address.
- Slot `0` differences no longer invalidate an otherwise valid Boot share as long as the Winners List payouts are correct.
- The miner assignment is back to the original Test A shape:
  - main / StartOS node: Bitaxe / `...9s8y`
  - laptop / source-built DATUM node: space heater / `...q4p`

### Shared State Snapshot

Both nodes are converged at the snapshot:

- `currentRoundNumber = 187`
- `currentStateId = 6fecbe158d1f683e59bffd886aca3e1f72cd3402426c21cf13dcd50be2b1c9ae`
- `candidateStateId = e752d8a67d200f375396722824238b12c82ab4184dde29452b927a4f60aa18dd`
- `currentTipBlockHeight = 944124`
- `onDeckCount = 299`

So this run is materially healthier than the earlier test snapshots that drifted on candidate state.

### Main Node Diagnostics

Source:

- `GET https://boot.gridlabs.science/api/network/summary`

Local DATUM diagnostics:

- submissions: `2213`
- accepted: `2191`
- accepted onto On Deck: `1200`
- rejected: `22`
- acceptance rate: about `99.0%`

Top rejection reasons:

- `Payout mismatch = 20`
- `Wrong parent block = 2`

Notably absent:

- `Slot 0 mismatch = 0`

Interpretation:

- The session-lock fix appears to have removed the old slot-0 rejection storm on the main node.
- The main / StartOS path is now very clean in this configuration.

### Laptop Node Diagnostics

Source:

- `GET http://127.0.0.1:5000/api/network/summary` via laptop SSH

Local DATUM diagnostics:

- submissions: `1470`
- accepted: `1351`
- accepted onto On Deck: `1277`
- rejected: `119`
- acceptance rate: about `91.9%`

Top rejection reasons:

- `Payout mismatch = 97`
- `Low difficulty = 20`
- `Wrong parent block = 2`

Notably absent:

- `Slot 0 mismatch = 0`

Interpretation:

- The session-lock fix also removed the old slot-0 rejection storm on the laptop node.
- The laptop stack is still materially noisier than the main / StartOS stack, but the remaining problem is now mostly `Payout mismatch`, not slot-0 confusion.
- That is a much narrower and more actionable failure surface than before.

### Recent Canonical Round Outcomes

Recent canonical rounds visible from laptop history:

- Round `184`
  - paid recipients:
    - `...q4p`: `269` slots
    - `...9s8y`: `30` slots

- Round `185`
  - paid recipients:
    - `...q4p`: `230` slots
    - `...9s8y`: `69` slots

- Round `186`
  - paid recipients:
    - `...q4p`: `268` slots
    - `...9s8y`: `31` slots

Interpretation:

- The recent completed rounds now generally look directionally correct for the current hashrate split, with the higher-hashrate laptop miner dominating.
- There is still ordinary round-to-round variance, especially on shorter rounds.
- The large, obviously pathological slot-0-driven misbehavior from earlier snapshots is no longer the defining pattern.

### Provisional Read After The Fix

- The "one DATUM session = one payout address" change appears to have done its intended job.
- `Slot 0 mismatch` is no longer the dominant rejection category on either node.
- The main / StartOS path now looks close to healthy in this test shape.
- The laptop / source-built DATUM path is still worse, but now mainly because of `Payout mismatch`, not because Boot is confusing multiple payout identities on one session.
- The next debugging target should stay focused on why the laptop DATUM path still produces a much higher payout-mismatch rate even after session locking.

## Overnight Timing Analysis After Persisted Reject/Event Telemetry

Timestamp of analysis: 2026-04-08 around 19:00 EDT

Context:

- Both nodes were updated to persist recent rejected DATUM-share diagnostics and recent network events.
- The overnight analysis below uses:
  - `GET /api/network/share-diagnostics?window=12h&source=datum&accepted=false&limit=5000`
  - `GET /api/network/events?window=12h&limit=5000`
- The goal was to determine whether the remaining rejections cluster:
  - immediately after Boot round rotations
  - immediately after ordinary Bitcoin chain-tip updates
  - or remain spread throughout the round

### Shared Snapshot At Analysis Time

Both nodes were converged when analyzed:

- `currentRoundNumber = 228`
- `currentStateId = b0442737f86a516e3ca020ec8fedad65f7d42dbc9e370037de096870488105c8`
- `candidateStateId = bcc403f32dcf0aad2b5354fcd7f7632e578cdd3f8936465d2f4b6025b1eaae63`
- `currentTipBlockHeight = 944250`

So there was no evidence of an active state divergence at the time of capture.

### Main Node Reject Timeline

Source:

- `https://boot.gridlabs.science/api/network/share-diagnostics?...`

12-hour rejected DATUM shares:

- total rejects: `212`
- by reason:
  - `Payout mismatch = 165`
  - `Wrong parent block = 47`

Miner addresses observed in local DATUM rejects:

- `...9s8y`: `196`
- `...q4p`: `16`

Timing pattern:

- `Payout mismatch` on the main node is strongly front-loaded near state changes.
- Of the `165` payout mismatches:
  - `134` were within `60s` of the most recent round rotation
  - `141` were within `60s` of the most recent chain-tip event
- The recent per-rotation windows looked like this:
  - round `944219`: `6` rejects, `5` within `60s`
  - round `944221`: `10` rejects, `9` within `60s`
  - round `944230`: `17` rejects, `7` within `60s`
  - round `944250`: `10` rejects, all `10` were `Payout mismatch`

Interpretation:

- The main / StartOS path is still not clean.
- But its remaining reject pattern now looks like a comparatively short-lived stale-template / template-refresh problem immediately after round changes, not a persistent all-round failure.

### Laptop Node Reject Timeline

Source:

- `http://127.0.0.1:5000/api/network/share-diagnostics?...` via laptop SSH

12-hour rejected DATUM shares:

- total rejects: `1218`
- by reason:
  - `Payout mismatch = 929`
  - `Low difficulty = 152`
  - `Wrong parent block = 135`
  - `Round changed = 2`

Miner addresses observed in local DATUM rejects:

- `...q4p`: `1218`

Timing pattern:

- The laptop’s `Wrong parent block` rejects look like a normal near-tip-change problem:
  - `118 / 135` happened within `60s` of the most recent chain-tip event
- But the laptop’s `Payout mismatch` rejects are more persistent:
  - only `148 / 929` were within `60s` of the most recent round rotation
  - the largest bucket was `3-10m` after the most recent round rotation: `316`
  - the next largest was `>10m` after the most recent round rotation: `279`
  - relative to chain-tip events, the same payout mismatches were still concentrated in `1-10m`, not just `0-60s`

Representative recent per-rotation windows:

- round `944215`: `54` rejects, all `54` were `Payout mismatch`
- round `944221`: `67` rejects, `60` `Payout mismatch`
- round `944224`: `97` rejects, `60` `Payout mismatch`, `30` `Wrong parent block`
- round `944230`: `34` rejects, `23` `Payout mismatch`

Interpretation:

- The laptop path is materially worse than the main path.
- The remaining laptop problem is not just the first few stale shares after a reset.
- It looks more like the laptop DATUM stack can continue hashing on the wrong coinbase layout for several minutes after some transitions.

### Important Caveat: The Laptop Test Path Appears To Have Gone Quiet Mid-Run

The analysis-time summary showed:

- laptop last accepted local DATUM share: `2026-04-08T19:30:55Z`
- laptop last rejected local DATUM share: `2026-04-08T19:27:50Z`

After that point:

- the laptop Boot node still kept receiving chain-tip and round-rotation events
- but the local DATUM path produced no new accepted or rejected local shares in the later windows

This means the back half of the overnight run was **not** a clean two-miner comparison anymore. The most likely interpretations are:

- the laptop DATUM client disconnected or stopped receiving useful work
- the laptop miner failed over or otherwise stopped contributing through that path

So:

- the early and middle portions of the run are useful for diagnosing reject timing
- the late-night portion is **not** reliable for fairness conclusions between the two test rigs

### Branch / Convergence Behavior

The event feed also showed frequent `state-adopted` events:

- main node: `14`
- laptop node: `4`

On the main node, many of those adoptions happened roughly `5-20s` after a local round rotation.

Interpretation:

- the network still appears to produce occasional near-simultaneous competing candidate branches at rotation time
- the main node often rotates locally, then adopts the stronger peer branch a few seconds later
- this is consistent with the earlier orphaned-round observations, even though the canonical-only UI no longer shows those branches directly

### Bottom Line

- The session-lock fix clearly removed the old `Slot 0 mismatch` storm.
- `Wrong parent block` now mostly looks like an expected near-block-change issue.
- The main / StartOS DATUM path still has some `Payout mismatch`, but mostly in the first minute after changes.
- The laptop / source-built DATUM path is still the much worse one, and its `Payout mismatch` behavior can persist for minutes, not just seconds.
- The overnight test does **not** show a clean long-run fairness skew yet, because the laptop local DATUM path appears to have stopped contributing around `19:30 UTC`.

### Next Comparison Target

The next controlled run should answer one narrower question first:

- does the laptop DATUM path continue to accumulate multi-minute `Payout mismatch` bursts even when the miner connection itself is confirmed stable for the entire run?

That should be measured before using another long overnight run as a fairness benchmark.

## 2026-04-11 Follow-Up: Session-Lock + Disconnect-Threshold Analysis

### Current Test Setup

- Main / StartOS DATUM:
  - miner address `...9s8y`
  - small Bitaxe
- Laptop / source-built DATUM:
  - miner address `...trvzs3`
  - larger heater miner
- DATUM username forwarding was configured to use only each node's configured pool address, not downstream miner usernames.

### What The 12h Data Showed

Main node summary:

- `1447` total local DATUM submissions
- `1316` accepted
- `131` rejected
- top reject reasons:
  - `Payout mismatch = 110`
  - `Solo fallback template = 14`
  - `Wrong parent block = 7`

Laptop node summary:

- `7173` total local DATUM submissions
- `6025` accepted
- `1148` rejected
- top reject reasons:
  - `Solo fallback template = 630`
  - `Payout mismatch = 257`
  - `Low difficulty = 171`
  - `Wrong parent block = 89`

### Important Correlation Result

Using the new persisted event feed:

- Main node:
  - `25` `datum-session-lock` events in `12h`
  - median interval between locks: about `1162s` (`19.4m`)
- Laptop node:
  - `221` `datum-session-lock` events in `12h`
  - median interval between locks: about `38s`

Reject timing correlation:

- Main node rejects were mostly clustered within `60s` of real round/tip changes
- Laptop node rejects were overwhelmingly clustered within `60s` of `datum-session-lock` events

That points to a self-reinforcing reconnect loop on the laptop path, not just normal stale work after Bitcoin blocks.

### Root Cause Identified

Boot was disconnecting DATUM sessions after only:

- `4` consecutive stale payout/template rejects

That threshold was share-count based, not time-based. This biases the system against high-hashrate clients:

- a fast miner can hit 4 stale shares almost immediately
- a slower miner may never hit that threshold before the refresh succeeds

So the larger laptop miner was more likely to be forcibly disconnected even if both miners experienced the same real stale-work window.

### Code Change Applied

The stale-template reset logic in `Program.cs` was changed so that Boot now:

- still requests template refresh immediately on payout/solo-fallback rejects
- only forces a disconnect if stale rejects persist for at least `20s`
- enforces a `60s` cooldown between forced stale-template disconnects

New config knobs:

- `stale_datum_disconnect_min_seconds`
- `stale_datum_disconnect_cooldown_seconds`

The old `stale_datum_payout_mismatch_threshold` remains in place as a minimum reject-count gate, but it is no longer the only trigger.

### Immediate Post-Deploy Observation

The new behavior is visible in the fresh laptop log:

- session reconnects
- locks to `...trvzs3`
- receives several `Solo fallback template` rejects
- Boot now waits about `21s` before disconnecting, instead of severing the session after a few fast shares

Representative log pattern:

- `Rejected datum share ... Solo fallback template`
- repeated several times
- then:
  - `submitted 4 consecutive stale payout shares ... over 21.1s. Disconnecting to force a clean reconnect.`

This confirms the new logic is active.

### Interpretation After The Fix

The fix removes the share-rate bias in Boot's forced-reconnect behavior, but it does **not** solve the deeper upstream issue yet.

The laptop DATUM path is still, at least sometimes, serving a non-Boot single-recipient template for around `20s` after reconnect/transition. So there are two layers:

1. Boot was amplifying the problem by disconnecting too aggressively.
2. The laptop DATUM stack still appears to spend real time on solo-fallback or stale non-Boot work.

### Next Question

After this change, the next run should answer:

- does the laptop still show a large `Solo fallback template` / `Payout mismatch` burden even without the old rapid reconnect loop?

If yes, the next bug target is the laptop DATUM gateway's template refresh / fallback behavior rather than Boot's disconnect policy.

## 2026-04-11 Later Check: Improved, But Laptop Still Not Healthy

After a few more hours on the time-based disconnect logic:

Main node summary:

- `1293` submissions
- `1264` accepted
- `29` rejected
- acceptance about `97.8%`
- reject reasons:
  - `Payout mismatch = 24`
  - `Wrong parent block = 4`
  - `Solo fallback template = 1`

Laptop node summary:

- `1827` submissions
- `1564` accepted
- `263` rejected
- acceptance about `85.6%`
- reject reasons:
  - `Solo fallback template = 167`
  - `Low difficulty = 44`
  - `Payout mismatch = 44`
  - `Wrong parent block = 8`

### What Improved

- The laptop no longer shows the earlier ultra-fast reconnect storm.
- In the last `6h`, laptop `datum-session-lock` median interval was about `64.5s`, versus the earlier `38s` behavior.
- So the Boot-side time-based reset materially reduced the self-inflicted churn.

### What Is Still Wrong

The laptop path is still clearly worse than main, and the remaining issue is dominated by:

- `Solo fallback template`

Timing correlation over the last `6h`:

- laptop had `81` `datum-session-lock` events
- laptop had `33` `datum-session-reset` events
- `518 / 524` laptop rejects were within `60s` of a `datum-session-lock`

That means the remaining bad-share burden is still tightly clustered around DATUM reconnect/relock cycles.

Representative fresh laptop log pattern:

- several `Solo fallback template` rejects in a row
- then:
  - `submitted 6 consecutive stale payout shares ... over 32.3s. Disconnecting to force a clean reconnect.`

So the new Boot logic is better, but the upstream laptop DATUM path still appears to spend on the order of `30s` serving stale/solo-fallback work after some reconnects or transitions.

### Current Best Interpretation

- Boot is no longer the dominant cause of the laptop bias.
- The remaining issue looks upstream of Boot:
  - DATUM is still sometimes mining on a single-recipient fallback template after reconnect/transition
  - Boot correctly rejects those shares as non-Boot work
- `Low difficulty` is present but secondary

### Likely Next Step

The next diagnostic or mitigation pass should focus on the laptop DATUM behavior:

- either inspect the DATUM client logs directly
- or further soften Boot's reset behavior specifically for `Solo fallback template`

At this point, the best candidate bug is no longer Boot's payout-identity handling. It is the laptop DATUM template-refresh / fallback path.
