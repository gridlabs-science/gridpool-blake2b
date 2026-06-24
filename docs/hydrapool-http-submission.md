# Hydrapool HTTP Share Submission

This document records the expected HTTP integration path for Hydrapool or any other non-DATUM client that submits GridPool shares directly.

## Endpoint

`POST /api/mining/share`

The endpoint accepts a JSON `ShareSubmissionDto`:

```json
{
  "minerAddress": "bc1qexample...",
  "username": "bc1qexample.worker",
  "headerHex": "<80-byte block header hex>",
  "coinbaseHex": "<coinbase transaction hex>",
  "merklePath": ["<txid/hash hex>", "<txid/hash hex>"],
  "prevBlockHash": "<optional parent block hash>",
  "nonce": 0,
  "difficulty": 0
}
```

`minerAddress`, `username`, `nonce`, and caller-reported `difficulty` are metadata. The server must not trust them for payout attribution or share ranking.

## Validation Model

For every untrusted HTTP share, the server verifies the proof rather than trusting the submitter:

1. Parse the submitted block header.
2. Parse the submitted coinbase transaction.
3. Rebuild the merkle root from `coinbaseHex` plus `merklePath`.
4. Compare the rebuilt merkle root to the block header merkle root.
5. Hash the 80-byte block header and compute actual share difficulty.
6. If the share is high enough for the current on-deck list, attribute it to the slot-0 payout address in the coinbase transaction.

Slot-0 attribution is a core protocol invariant. If a third party intercepts a valid share and changes slot 0, the coinbase transaction changes, the merkle root changes, and the submitted header no longer validates.

## Coinbase Tag Compatibility

The DATUM-facing coinbase tag defaults to `Grid Pool`, but HTTP share validation is tag-agnostic.

Hydrapool does not need to include the default DATUM tag, and changing or clearing `coinbase_tag` must not change whether a valid HTTP share can be accepted. Consensus-relevant attribution comes from the verified coinbase output list, not from coinbase tag text.

## Response Shape

Accepted share:

```json
{
  "status": "accepted",
  "difficulty": 123456.78,
  "isBlock": false,
  "blockHash": "<share hash>",
  "stateId": "<candidate-state-id>"
}
```

Duplicate share:

```json
{
  "status": "duplicate",
  "difficulty": 123456.78,
  "isBlock": false,
  "blockHash": "<share hash>",
  "stateId": "<candidate-state-id>"
}
```

Rejected share:

```json
{
  "status": "rejected",
  "reason": "Low difficulty or invalid proof"
}
```

Missing or malformed bodies return a structured rejection instead of throwing server errors.

## Share Advice Endpoint

`GET /api/mining/share-advice`

Direct HTTP clients should use this lightweight status endpoint to avoid submitting shares that cannot enter the current on-deck list.

The response includes:

- current round and state IDs
- current Bitcoin parent tip
- shared winner slot count
- current on-deck count and open slots
- current on-deck floor difficulty
- minimum computed difficulty needed to enter the on-deck list
- whether the share must be strictly greater than the current floor

When the on-deck list is not full, `minimumDifficultyToEnterOnDeck` is `1`. When the list is full, clients should submit only shares with computed difficulty greater than the current floor. The server still recomputes difficulty and remains authoritative.

## Operational Notes

- Clients should fetch `GET /api/mining/payouts` to learn the current payout list and network status.
- Low-difficulty shares that cannot enter the on-deck list may be rejected by the HTTP endpoint; long-running clients should use `GET /api/mining/share-advice` to avoid spammy retries.
- Parent-block races are expected near Bitcoin tip changes. The server may accept fresh-parent shares when the proof is otherwise valid and the parent appears newer than the local view.
- A valid Bitcoin block found through this path triggers normal round rotation.

## Launch Checklist

- Unit tests cover tag-agnostic HTTP validation with both `Grid Pool` and empty configured coinbase tags.
- Unit tests cover clean rejection of missing HTTP bodies.
- Before launch, run malformed request smoke tests against a staging node and confirm no 5xx responses for bad JSON, null bodies, missing merkle paths, or oversized request bodies.
