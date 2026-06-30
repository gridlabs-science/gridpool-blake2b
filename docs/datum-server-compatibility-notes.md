# DATUM Upstream Server Compatibility Notes

Status: implementation note, based on debugging a DATUM-compatible upstream
server during GridPool public beta testing.

These notes are for projects implementing a DATUM upstream server. They are not
specific to GridPool's reward-sharing rules. The same failure modes can affect
standard PPLNS, FPPS-style accounting, solo-coordinating servers, or any other
backend that speaks DATUM to `datum_gateway`.

## Summary

The bugs we found were not exotic consensus problems. They were ordinary DATUM
server integration mistakes:

- Coinbaser responses were not keyed tightly enough to the exact template or
  accounting context they represented.
- Share validation did too much work before responding to DATUM, which made
  the client reconnect and briefly mine fallback work.
- Server-side work refreshes were too aggressive for routine accounting-only
  state changes.

Those issues can look like "DATUM is unstable" from the outside. In our case,
the root cause was upstream-server behavior.

## DATUM Behavior To Expect

DATUM Gateway builds block templates locally from the miner operator's Bitcoin
node. The upstream server does not send a full block template. Instead, the
server supplies pool coordination data, including coinbase/payout information
and share-accounting targets, and the gateway combines that with its local
Bitcoin template.

A DATUM client may submit shares from several work states:

- normal pooled/coordinated work using the upstream server's coinbase data;
- empty or single-recipient fallback work during startup, reconnect, or while
  waiting for upstream coinbaser data;
- stale work after a Bitcoin block update or an upstream coinbase/accounting
  update, depending on miner firmware and DATUM refresh timing;
- duplicate or late nonce submissions from ASIC firmware queues.

Your server should classify these cases clearly and respond quickly. It should
not assume every rejected share is malicious, and it should not let ordinary
fallback/stale behavior cascade into repeated reconnects.

## Issue 1: Coinbaser ID And Template Context Attribution

### Symptom

Shares built from DATUM jobs were later validated against the wrong server-side
coinbase/accounting context. Depending on the backend, this can show up as:

- payout mismatch;
- unknown or zero coinbaser ID;
- valid-looking shares attributed to the wrong payout/accounting period;
- shares accepted only immediately after reconnect, then rejected after a
  server-side state update.

### Root Cause

Early server code treated a later share submit as if "current server state" was
enough to validate the share. That assumption is unsafe.

A DATUM job can outlive the server-side context that created it. The upstream
server may rotate payout lists, accounting windows, share targets, coinbase
tags, fee outputs, or other pool-specific metadata while ASICs are still hashing
older jobs. When the share arrives, the server must know which context the ASIC
actually hashed.

There was also a response-field bug risk: the server must return a stable
coinbaser response ID in the DATUM coinbaser response. Do not overload that
field with payout count, list index, accounting-window number, or any other
meaning. The later POW-submit path needs this ID to recover the exact
coinbaser/template context.

### Required Server Behavior

For every DATUM coinbaser fetch response:

1. Allocate a stable nonzero `coinbaser_id` for that response.
2. Store `coinbaser_id -> template_context`.
3. Return that same `coinbaser_id` in the coinbaser response message.
4. When a later DATUM POW submit references that coinbaser ID, validate and
   account the share against the matching template context.
5. For nonce-only submits that reuse cached job data, preserve the cached job's
   template context.

`template_context` should contain whatever your backend needs to validate and
account the share. For a conventional PPLNS-like pool this may include pool
payout script, job target, coinbase tag, fee policy, and accounting window. For
GridPool it also includes payout-snapshot identity, but that is just one
example.

Pseudo-flow:

```text
on_coinbaser_fetch:
    context = current_template_context()
    coinbaser_id = next_nonzero_id()
    coinbaser_contexts[coinbaser_id] = context
    response.coinbaser_id = coinbaser_id
    response.coinbase_data = build_coinbase_data(context)

on_pow_submit:
    context = coinbaser_contexts[pow.coinbaser_id]
              or cached_job_contexts[pow.job_id]
    validate_and_account_share(pow, context)
```

### Validation Rule

Validate against the context the job was built from, not merely the newest
server state.

If the submit does not carry enough context to validate deterministically, do
one of these deliberately:

- recover the context from cached job state;
- fall back to a bounded set of recently retained contexts;
- reject with a specific "unknown/expired job context" reason.

Avoid generic payout-mismatch or malformed-share errors when the real problem
is missing historical context.

## Issue 2: Slow Share Handling Caused DATUM Session Churn

### Symptom

The DATUM client repeatedly disconnected and reconnected. Each reconnect could
produce a short burst of fallback or empty-work shares. From the pool side this
looked like a high reject rate, while the ASIC itself showed few or no rejects.

Observed close or failure symptoms included:

- broken pipe while writing to DATUM;
- connection reset by peer;
- client closed before full header;
- encrypted channel decryption failed for short failed handshakes;
- repeated reconnects after apparently successful work delivery.

### Root Cause

The server took too long to respond to shares.

In our implementation, the hot path normalized and deduplicated large retained
state on every share while holding a main state lock. One specific pattern was
accidentally quadratic:

```text
for each candidate_hash:
    if normalized.any(existing == candidate_hash):
        skip
```

With hundreds or thousands of retained hashes, each DATUM share spent hundreds
of milliseconds in state reads before the server responded. DATUM expects share
responses quickly. Slow responses created backlog, which led to reconnects,
which led to fallback work, which led to more rejected shares. A tiny bug became
a noisy failure loop.

### Required Server Behavior

Keep the DATUM share response path boring and fast:

- Do not clone, sort, serialize, persist, or deeply normalize large state for
  every share.
- Do not hold global locks while doing expensive validation or debug/history
  persistence.
- Use `HashSet`/dictionary membership checks for dedupe and lookup instead of
  nested scans.
- Cache immutable template/job context by ID.
- Separate "respond to DATUM" from "persist debug history" wherever possible.
- Sample expensive diagnostics instead of running them for every low-value
  heartbeat share.

Target response times should be single-digit milliseconds under normal load.
Treat sustained response times above roughly 50-100 ms as a warning sign.

## Issue 3: Do Not Force Urgent Work Refreshes For Accounting-Only Changes

### Symptom

The upstream server caused DATUM to refresh work too often. DATUM then briefly
served fallback or empty work, and miners produced shares that were not valid
for the coordinated pool job.

### Root Cause

The server treated internal accounting-state changes like mining-template
invalidations.

Not every pool-side state change means the ASICs must immediately abandon all
work. Examples that usually should not force urgent work refresh by themselves:

- PPLNS accounting-window bookkeeping;
- peer/network telemetry updates;
- local diagnostics/history updates;
- reward-estimation changes;
- GridPool-style chain-tip payout snapshots when the previous template can
  still be classified and handled correctly.

Events that normally do require fast work refresh:

- Bitcoin previous-block/hash change;
- target/difficulty change that affects share acceptance;
- coinbase output/script change that makes old jobs invalid for your pool;
- server policy change where old jobs must no longer be accepted.

### Recommended Behavior

- Distinguish "Bitcoin work changed" from "pool accounting changed."
- Only force urgent DATUM refresh when old work is definitely invalid or
  dangerous to keep accepting.
- If old work is merely stale-but-classifiable, reject or account it with a
  precise reason rather than causing a reconnect loop.
- Log the refresh reason so operators can tell whether churn came from Bitcoin
  blocks, pool policy, or accidental server behavior.

## Diagnostics To Add

DATUM upstream servers should expose or log:

- session start/end time and close reason;
- accepted/rejected share counts per session;
- coinbaser fetch count per session;
- DATUM job ID, coinbase ID, coinbaser ID, and template-context ID on share
  submit;
- coinbase transaction byte length;
- rejection category, especially fallback work vs stale work vs payout/coinbase
  mismatch vs unknown context;
- parse/build/validation/send durations;
- lock wait vs lock body timing in the share-validation path;
- work-refresh reason and whether it was urgent or routine.

Useful red flags:

- `coinbaser_id = 0` on normal coordinated shares;
- accepted shares only right after reconnect, then fallback rejects before the
  next reconnect;
- repeated fallback templates while the upstream server is reachable;
- share response times above 50-100 ms under ordinary load;
- server state reads taking hundreds of milliseconds;
- generic "payout mismatch" errors when the real issue is unknown historical
  job context.

## Compatibility Checklist

If your project reused early GridPool DATUM server code, or wrote a new DATUM
upstream server based on those examples, verify the following:

- Coinbaser responses assign and return a stable nonzero coinbaser ID.
- The server stores `coinbaser_id -> template/accounting context`.
- POW submit validation uses the context tied to the coinbaser ID or cached job
  ID, not just current server state.
- Nonce-only cached jobs preserve template/accounting context.
- Fallback or single-recipient work is classified specifically.
- Share validation does not clone or normalize large state structures on every
  share.
- Dedupe and membership checks are `O(n)` or better, not `O(n^2)`.
- Routine accounting-state changes do not trigger unnecessary urgent DATUM work
  refreshes.
- Slow-share telemetry is available in production builds, even if sampled.

## Agent Prompt For Auditing Another Codebase

Use this prompt with an AI coding agent in another repository:

```text
Audit this DATUM upstream server implementation for protocol compatibility and
session stability.

First inspect coinbaser fetch responses. Confirm that every response allocates
a stable nonzero coinbaser ID, returns that ID in the DATUM response, and stores
coinbaser_id -> template/accounting context. Then inspect every POW-submit path
and verify that shares are validated and accounted against the context tied to
the coinbaser ID or cached job ID, not merely the current server state.

Next inspect the DATUM share hot path. Find code that clones, sorts,
deduplicates, serializes, persists, or scans large state under a global lock for
every share. Replace nested scans or Any-inside-loop dedupe with HashSet or
dictionary lookups. Move debug/history persistence out of the synchronous share
response path where possible.

Finally inspect work-refresh behavior. Make sure routine accounting or telemetry
updates do not force DATUM into repeated urgent work refreshes unless old work
is genuinely invalid. Add diagnostics for session churn, coinbaser IDs,
template-context IDs, fallback-template rejects, response timing, lock timing,
and refresh reasons.
```
