# DATUM Fallback and Reconnect Behavior

This note documents the current DATUM Gateway behavior that matters for Boot integration, based on reading the local DATUM source in `/home/keegreil/Documents/GitHub/datum_gateway/src`.

## Executive Summary

In DATUM `reward_sharing = prefer` mode, a short burst of non-Boot shares immediately after connect/reconnect is normal.

The reason is not just "the miner is slow to switch jobs". DATUM itself is designed to:

- keep miners hashing when pooled mining is unavailable
- generate an immediate "empty" fallback coinbase
- wait only up to 5 seconds for the remote coinbaser
- continue serving work even if the full remote payout split is not ready

That means Boot should expect some single-recipient fallback templates right after:

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

- if Boot rejects all fallback/stale shares for 30 seconds, DATUM may reconnect even though the connection itself is healthy
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

For Boot, the safest supported configuration is:

- one DATUM session represents one Boot payout identity
- DATUM should not pass multiple payout addresses upstream on one session
- worker names are fine

## What Is Normal vs Suspicious

### Normal

These should be expected occasionally:

- a few single-recipient fallback templates right after initial connect
- a few fallback shares right after upstream reconnect
- a few stale/wrong-parent shares right after block changes
- some single-output work during coinbaser delay windows

### Suspicious

These point to a real issue:

- fallback-style rejects continuing for tens of seconds or minutes
- frequent DATUM reconnects
- repeated reconnect -> fallback -> reconnect loops
- large reject bursts closely tied to `datum-session-lock` / reconnect events

## Boot Integration Implications

These findings support the following Boot-side policy:

1. A small burst of `Solo fallback template` rejects immediately after DATUM connect/reconnect is normal.
2. Boot should not overreact to the first few rejects right after session lock.
3. Forced Boot-side DATUM disconnects can make things worse by pushing DATUM back into the same fallback window.
4. The remaining health signal to watch is:
   - how often DATUM reconnects
   - how long fallback rejects persist after each reconnect

## Likely Next Steps

These are the most promising follow-ups:

1. Correlate Boot `Solo fallback template` rejects against DATUM reconnect/session-lock events.
2. Inspect DATUM logs on the laptop for the exact reason its upstream session is reconnecting so often.
3. Consider whether Boot should use a longer grace period before forcing any session reset.
4. Consider whether Boot should distinguish "normal initial fallback window" from "persistent unhealthy fallback loop" in the UI.

## Files Reviewed

- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_protocol.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_coinbaser.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_stratum.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_blocktemplates.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_gateway.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_sockets.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/src/datum_api.c`
- `/home/keegreil/Documents/GitHub/datum_gateway/doc/usernames.md`
