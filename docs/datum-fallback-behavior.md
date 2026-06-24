# DATUM Fallback and Reconnect Behavior

This note documents the current DATUM Gateway behavior that matters for GridPool integration, based on reading the local DATUM source in `../datum_gateway/src`.

## Executive Summary

In DATUM `reward_sharing = prefer` mode, a short burst of non-GridPool shares immediately after connect/reconnect is normal.

The reason is not just "the miner is slow to switch jobs". DATUM itself is designed to:

- keep miners hashing when pooled mining is unavailable
- generate an immediate "empty" fallback coinbase
- wait only up to 5 seconds for the remote coinbaser
- continue serving work even if the full remote payout split is not ready

That means GridPool should expect some single-recipient fallback templates right after:

- initial pool connect
- upstream reconnect
- some block changes
- some coinbaser delays/timeouts

The more interesting bug signal is not the existence of a few fallback shares. It is how long that state persists, and how often DATUM reconnects back into it.

## Relevant DATUM Components

### 1. Global upstream DATUM session

DATUM uses one global upstream protocol session for the whole gateway, not one per downstream miner.

Key code:

- `datum_protocol_client_active` in `src/datum_protocol.c`
- `datum_protocol_is_active()` returns true only when the state reaches `3`

Implication:

- reconnects are gateway-wide events
- all miners behind that DATUM instance are affected together

### 2. `prefer` vs `require` pooled mining

The user-facing `reward_sharing` setting maps to:

- `require` -> `datum_pooled_mining_only = true`
- `prefer` -> `datum_pooled_mining_only = false`
- `never` -> no pooled mining host

Key code:

- `src/datum_api.c`
- `src/datum_conf.c`

When `datum_pooled_mining_only = false`, miners are not disconnected if the pool is unavailable.
They continue mining on fallback work that pays `mining.pool_address`.

When `datum_pooled_mining_only = true`, DATUM shuts down miner serving if the pool is unavailable.

Key code:

- `src/datum_gateway.c`
- `src/datum_sockets.c`

### 3. Immediate "empty" coinbase generation

When a new stratum job is created, DATUM always builds a base coinbase first.

Key code:

- `generate_base_coinbase_txns_for_stratum_job(...)` in `src/datum_coinbaser.c`

If DATUM is active upstream:

- it uses `override_mining_pool_scriptsig`
- it marks the job as a DATUM job
- but if no remote coinbase outputs are available yet, it forces `empty_only = true`

If DATUM is not active upstream:

- it uses `mining.pool_address`
- it is not a DATUM job
- it also forces `empty_only = true`

The `empty_only` path creates a simple coinbase with one payout output plus the witness commitment.

Implication:

- "connected to pool" does not mean "already has full pooled outputs"
- there is an intermediate state where miners can work on a single-recipient template

### 4. Coinbaser fetch is asynchronous and times out

The remote pooled payout split is fetched asynchronously by the coinbaser thread.

Key code:

- `datum_protocol_coinbaser_fetch(...)` in `src/datum_protocol.c`
- `datum_coinbaser_thread(...)` in `src/datum_coinbaser.c`

Important detail:

- DATUM waits up to 5 seconds for the remote coinbaser response
- if that times out, it logs and continues

So the full pooled payout list is best-effort on a short timer, not guaranteed before miners resume work.

### 5. Stratum job broadcast intentionally does not wait forever

DATUM’s stratum layer uses a readiness check:

- `stratum_job_coinbaser_ready(...)` in `src/datum_stratum.c`

That function has an explicit backup timeout:

- if more than 5 seconds have passed since job creation, it returns ready
- but sets `full_coinbase_ready = false`

This is a deliberate policy choice:

- miners should keep hashing rather than sit idle
- a full pooled coinbase is preferred, but not required to continue serving work

### 6. New block handling prefers getting work out quickly

On a new block, DATUM pushes miners through a staged update flow:

- empty work first
- then full job variants
- then coinbaser-backed work when ready

Key code:

- `datum_blocktemplates.c`
- `datum_stratum.c`

Implication:

- a new block or pool reconnect naturally creates a short window where some miners can still be on fallback-style work

### 7. Share acceptance timeout is based on accepted shares only

This is the most important integration detail.

Key code:

- `datum_protocol_share_response(...)` in `src/datum_protocol.c`
- protocol main loop in `src/datum_protocol.c`

DATUM updates `datum_last_accepted_share_tsms` only on:

- `DATUM_POW_SHARE_RESPONSE_ACCEPTED`
- `DATUM_POW_SHARE_RESPONSE_ACCEPTED_TENTATIVELY`

Rejected shares do **not** refresh that timer.

The main protocol loop exits and reconnects if:

- DATUM has been sending shares
- but it has not seen an accepted share response for 30 seconds

Implication:

- if GridPool rejects all fallback/stale shares for 30 seconds, DATUM may reconnect even though the connection itself is healthy
- that reconnect can throw DATUM right back into the same fallback window
- this can create a positive feedback loop

## Username Handling

DATUM has three upstream username modes:

- pass full usernames
- append usernames as worker names to `mining.pool_address`
- ignore miner usernames and use only `mining.pool_address`

Key doc:

- `doc/usernames.md`

Important limitation from DATUM’s own docs:

- Stratum usernames only matter in pooled mode
- in non-pooled mode, only `mining.pool_address` is used

For GridPool, the safest supported configuration is:

- one DATUM session represents one GridPool payout identity
- DATUM should not pass multiple payout addresses upstream on one session
- worker names are fine

## What Is Normal vs Suspicious

### Normal

These should be expected occasionally:

- a few single-recipient fallback templates right after initial connect
- a few fallback shares right after upstream reconnect
- a few stale/wrong-parent shares right after block changes
- a local DATUM share built on a fresh Bitcoin parent before GridPool's own block notifier has observed that parent
- some single-output work during coinbaser delay windows

### Suspicious

These point to a real issue:

- fallback-style rejects continuing for tens of seconds or minutes
- frequent DATUM reconnects
- repeated reconnect -> fallback -> reconnect loops
- large reject bursts closely tied to `datum-session-lock` / reconnect events

## GridPool Integration Implications

These findings support the following GridPool-side policy:

1. A small burst of `Solo fallback template` rejects immediately after DATUM connect/reconnect is normal.
2. GridPool should not overreact to the first few rejects right after session lock.
3. Forced GridPool-side DATUM disconnects can make things worse by pushing DATUM back into the same fallback window.
4. GridPool may learn a fresh parent from its directly connected DATUM client after validating the submitted share without the parent allow-list.
5. The remaining health signal to watch is:
   - how often DATUM reconnects
   - how long fallback rejects persist after each reconnect

## Fresh Parent Handling

DATUM can observe a new Bitcoin tip and begin generating work before GridPool's own notifier path sees that same tip.

GridPool now treats that case as normal only on the trusted local DATUM path:

- validate the share header, merkle root, slot-0 attribution, payout list, and difficulty
- if the only failure was that the parent is unknown, add the share's `prevhash` to the accepted parent set
- accept the share without advancing GridPool's displayed `currentTipBlockHash`
- let the normal Bitcoin notifier or peer state update advance the displayed tip and trigger any deterministic test rotation

This deliberately does not apply to the public HTTP path. Hydrapool/HTTP shares must remain trustless: GridPool should not accept an unknown parent from an untrusted caller unless it also has a way to verify the parent header.

## DATUM Flush/Refresh Controls

The local DATUM source scan did not show a stronger upstream command that forces all downstream miners to discard old work beyond the existing block-notify mechanism.

Relevant code:

- `src/datum_protocol.c` handles mining command `0xF9` as `DATUM server blocknotify` and calls `datum_blocktemplates_notifynew(NULL, 0)`.
- `src/datum_stratum.c` sends `mining.notify` with `clean_jobs = true` for some new-block empty-work blasts.
- `src/datum_stratum.c` later sends full/paced job updates that can use `clean_jobs = false`.

Implication:

- GridPool can ask DATUM to check for new work quickly.
- GridPool cannot currently guarantee that every ASIC immediately drops already-buffered old templates.
- Squashing late payout-mismatch rejects to zero may require a DATUM fork, miner firmware changes, or both.

## 2026-04-21 Laptop Soak Finding

During the laptop/main two-node soak, the laptop showed two separate problems:

- High-rate laptop share relays were being throttled by the main node's `peer-write` limiter at the old `90/min` default.
- The laptop DATUM process repeatedly opened new upstream DATUM sessions, visible as frequent `datum-session-lock` events from changing source ports.

The relay-throttle issue is a GridPool configuration/design issue, not a DATUM issue. At roughly `8-9 TH/s` and low min difficulty, the laptop can produce hundreds of accepted shares per minute, so `90/min` is too low for peer relay. The test default was raised to `3000/min`, and `peer-relay-failed` events were added so future throttling can be seen through `/api/network/events` instead of only by scraping logs.

The reconnect churn appears DATUM-side from GridPool's perspective:

- GridPool logs show `Client <endpoint> disconnected (no data)`.
- Laptop DATUM logs show repeated `Starting DATUM v0.2-beta client...`.
- DATUM only refreshes its internal share-acceptance watchdog on accepted share responses, not rejected ones.

Interpretation:

- If GridPool rejects every share from a stale/fallback window for long enough, DATUM can reconnect even when the TCP path is otherwise healthy.
- Reconnects then create another short temporary/fallback-template window.
- After the peer-write limit was raised, the immediate post-fix window showed `100%` DATUM acceptance on both nodes and converged candidate state, while session-lock churn still appeared. That means reconnect churn should be treated as a warning signal, but it is only a launch blocker if it correlates with sustained reject bursts or convergence failure.

Tooling:

- `scripts/boot-laptop-issue-report.mjs` compares main/peer summaries and buckets rejects against chain tips, round rotations, DATUM refresh requests, and session events.
- Current 5pm soak monitor: `g2-monitor-2026-04-21-1700.json`.

## Likely Next Steps

These are the most promising follow-ups:

1. Correlate GridPool `Solo fallback template` rejects against DATUM reconnect/session-lock events.
2. Inspect DATUM logs on the laptop for the exact reason its upstream session is reconnecting so often.
3. Consider whether GridPool should use a longer grace period before forcing any session reset.
4. Consider whether GridPool should distinguish "normal initial fallback window" from "persistent unhealthy fallback loop" in the UI.

## Files Reviewed

- `../datum_gateway/src/datum_protocol.c`
- `../datum_gateway/src/datum_coinbaser.c`
- `../datum_gateway/src/datum_stratum.c`
- `../datum_gateway/src/datum_blocktemplates.c`
- `../datum_gateway/src/datum_gateway.c`
- `../datum_gateway/src/datum_sockets.c`
- `../datum_gateway/src/datum_api.c`
- `../datum_gateway/doc/usernames.md`
