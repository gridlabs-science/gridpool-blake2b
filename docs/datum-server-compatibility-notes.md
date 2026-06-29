# DATUM Server Compatibility Notes For GridPool-Style Integrations

Status: implementation note, based on GridPool public beta debugging.

These notes are for projects that reused early GridPool DATUM server code or are implementing a DATUM upstream server for a GridPool-like payout-template protocol.

## Summary

Two DATUM-facing issues caused high reject rates and frequent session churn on GridPool mainnet beta:

- Coinbaser responses were not keyed tightly enough to the payout snapshot they represented.
- Share validation performed expensive state normalization on every DATUM share, delaying responses long enough that DATUM clients repeatedly reconnected and mined short bursts of fallback work.

Both issues can look like "DATUM is unstable," but in this case the root cause was server behavior.

## DATUM Behavior To Expect

DATUM Gateway can emit shares from several kinds of work:

- Full pooled work using the upstream server's coinbase payout list.
- Empty or single-recipient fallback work during startup, reconnect, or while waiting for full coinbaser data.
- Stale work after a Bitcoin block update or payout-template update, depending on miner firmware and DATUM refresh timing.

GridPool consensus only accepts shares built against a valid GridPool payout snapshot. Single-recipient fallback templates are valid Bitcoin mining work, but they are not valid GridPool shares and must be rejected.

The important operational target is not "never reject fallback shares." The target is "do not trigger repeated reconnects or long fallback windows."

## Issue 1: Coinbaser ID And Snapshot Attribution

### Symptom

Shares built from DATUM jobs were rejected with payout mismatch or were validated against the wrong payout snapshot after frequent GridPool snapshot updates.

In V2 GridPool, every Bitcoin block can create a new payout snapshot. DATUM jobs and coinbaser responses therefore need to be tied to the exact snapshot used to construct the coinbase transaction.

### Root Cause

Early server code treated DATUM coinbaser responses as if the current server state would still be sufficient when a later share arrived. That assumption breaks when payout templates rotate faster than DATUM jobs expire.

There was also a response-field bug risk: the server must return a stable coinbaser response ID in the DATUM coinbaser response. Do not overload that field with payout count, list index, or any other value. The share submit path needs to be able to recover which coinbaser template the ASIC actually hashed.

### Required Server Behavior

For every DATUM coinbaser fetch response:

1. Allocate a nonzero `coinbaser_id` for that response.
2. Store `coinbaser_id -> active_payout_snapshot_id`.
3. Return that `coinbaser_id` in the coinbaser response message.
4. When a later DATUM POW submit references that coinbaser ID, attach the matching payout snapshot ID to the share before validation.
5. For nonce-only DATUM submits that reuse cached job data, preserve the cached job's payout snapshot ID.

Pseudo-flow:

```text
on_coinbaser_fetch:
    snapshot = current_active_snapshot()
    coinbaser_id = next_nonzero_byte()
    coinbaser_snapshot_ids[coinbaser_id] = snapshot.id
    response.coinbaser_id = coinbaser_id
    response.coinbase_outputs = build_outputs(snapshot)

on_pow_submit:
    snapshot_id = coinbaser_snapshot_ids[pow.coinbaser_id]
              or cached_job_snapshot_ids[pow.job_id]
    validate_share_against_snapshot(pow, snapshot_id)
```

### Validation Rule

If a share includes a known payout snapshot ID, validate against that snapshot first. Only fall back to scanning retained snapshot contexts when no snapshot ID is available and the failure is not already terminal, such as a clear single-recipient fallback template.

This keeps validation correct and prevents old unpaid proofs from being rejected simply because the active snapshot has moved.

## Issue 2: Slow Share Handling Caused DATUM Session Churn

### Symptom

The DATUM client reconnected every few minutes. Each new session produced a burst of rejected single-recipient fallback shares. Mainnet acceptance dropped near 36-45%, while the ASIC itself showed few or no rejects.

Observed session close reasons included:

- broken pipe while writing to DATUM
- connection reset by peer
- client closed before full header
- encrypted channel decryption failed for short failed handshakes

### Root Cause

The server hot path normalized accepted parent block hashes on every share while holding the main state lock.

The old implementation deduplicated parent hashes using repeated linear scans:

```text
for each candidate_hash:
    if normalized.any(existing == candidate_hash):
        skip
```

With hundreds or thousands of retained parent hashes, this became accidentally quadratic. In production beta measurements, each DATUM share spent hundreds of milliseconds in state snapshot reads:

- Mainnet before fix: roughly 260 ms per rejected fallback share and 800-900 ms per accepted share end-to-end.
- Pi/testnet before fix: roughly 550 ms in the same state-read path.

DATUM expects the upstream server to respond quickly. Slow responses caused backlog and session churn; churn caused DATUM to briefly serve fallback/empty work; fallback work generated consensus-invalid shares.

### Fix

Use canonical hash-set dedupe and avoid rebuilding large parent lists when only membership is needed.

Required changes:

- Build accepted-parent snapshots with `HashSet<string>` keyed by canonical block hash.
- Stop scanning the full list with nested `Any`/equivalence checks.
- For `IsAcceptedParentBlockHash`, check current tip, retained parent hashes, and Work Set proof parents directly without allocating a full deduped list.
- Keep expensive history/debug persistence out of the per-share critical path.

After this fix, the live mainnet DATUM session moved to 100% accepted shares in the short current window, and measured share response time dropped to about 1-3 ms.

## Issue 3: Do Not Force DATUM Urgent Refresh On Every GridPool Snapshot

GridPool V2 creates payout snapshots on every Bitcoin block. Treating every snapshot as a reason to urgently refresh DATUM work can force DATUM into empty-work behavior too often.

Recommended behavior:

- A true GridPool block/payment transition should invalidate templates.
- A Bitcoin chain-tip snapshot should not necessarily force an urgent DATUM refresh from the GridPool server layer.
- Let DATUM refresh from its normal Bitcoin work-update path unless the old template is definitely consensus-invalid.

In GridPool, server-side DATUM urgent refresh is skipped for reasons beginning with `chain-tip:`. This reduced avoidable empty-work churn.

## Diagnostics To Add

DATUM integrations should expose or log:

- session start/end time and close reason
- accepted/rejected share counts per session
- coinbaser fetch count per session
- DATUM job ID, coinbase ID, coinbaser ID, and payout snapshot ID on share submit
- coinbase transaction byte length
- rejection category, especially single-recipient fallback vs payout mismatch
- parse/build/validation/send durations
- lock wait vs lock body timing in the share-validation state path

Useful red flags:

- `coinbaser_id = 0` on normal pooled shares
- accepted shares only after reconnect, then fallback rejects before the next reconnect
- repeated single-recipient fallback templates while the upstream server is reachable
- share response times above 50-100 ms under ordinary load
- server state reads taking hundreds of milliseconds

## Compatibility Checklist

If your project reused early GridPool DATUM server code, verify the following:

- Coinbaser responses assign and return a real nonzero coinbaser ID.
- The server stores `coinbaser_id -> payout snapshot/template context`.
- POW submit validation uses the snapshot/template context the job was built from, not just "current state."
- Nonce-only cached jobs preserve payout context.
- Single-recipient fallback work is rejected as fallback, not misclassified as arbitrary payout mismatch.
- Share validation does not clone or normalize large state structures on every share.
- Parent-hash dedupe is `O(n)`, not `O(n^2)`.
- Routine chain-tip snapshots do not cause unnecessary urgent DATUM work refreshes.
- Slow-share telemetry is available in production builds, even if sampled.

## Agent Prompt For Auditing Another Codebase

Use this prompt with an AI coding agent in another repository:

```text
Audit this DATUM upstream server implementation for GridPool-style payout-template compatibility.

Check that every DATUM coinbaser fetch response allocates a stable nonzero coinbaser ID, returns that ID in the response, and stores coinbaser_id -> payout snapshot/template context. Then check that every POW submit validates against the snapshot/template context tied to the coinbaser ID or cached job ID, not merely the current server state.

Next inspect the share hot path. Find any code that clones, sorts, deduplicates, serializes, saves, or scans large state under a global lock for every share. In particular, look for accepted-parent-block-hash dedupe using nested loops or .Any() inside a loop. Replace with canonical HashSet-based O(n) dedupe and direct membership checks.

Finally inspect DATUM work refresh behavior. Make sure routine chain-tip payout snapshots do not force repeated urgent empty-work refreshes unless the old template is definitely invalid. Add diagnostics for session churn, coinbaser IDs, payout snapshot IDs, fallback-template rejects, and lock body timing.
```
