# Blake2b DATUM interoperability evidence — 2026-09-04

## Scope

The VPS test used unmodified upstream `innerhat-dev/datum_gateway` commit
`2fea7e51286d3821c19dc1c240b8caa92bd92532` against the Blake2b GridPool
listener on loopback. A Blake2b CPU Stratum miner connected through that stock
gateway with a payout address distinct from the GridPool support address.

## Results

- The stock encrypted DATUM handshake, client configuration, coinbaser fetch,
  Blake2b jobs, full PoW submissions, and nonce-only submissions interoperate.
- GridPool commit `e087b2c` restores a nonce-only submission's scheduler
  decision from the server's exact cached DATUM job context.
- GridPool commit `3cc89af` verifies that reconstructed coinbase output zero
  exactly matches the job-bound scheduler decision before accepting a share.
- GridPool commit `106cfef` sends the authenticated session payout in the
  standard DATUM client-configuration payout-script field. This keeps stock
  gateway fallback coinbases scoped to that session after payout lock and on
  authenticated reconnect.
- The full suite passes at `265/265` on `106cfef`.
- Live tests confirmed exact-policy support-template shares can be accepted and
  ordinary stale-parent work remains rejected.

## Remaining public-ingress gate

Stock DATUM can create an uncoordinated fallback job with `coinbaser_id = 0`
when it does not obtain a usable coinbaser response. Such a job cannot be
matched to a deterministic 5% support-template decision. GridPool rejects it
fail-closed; accepting it would let a client bypass the scheduler.

Therefore TCP 3008 remains bound to `127.0.0.1` and its UFW allowance is
removed. Before public ingress, choose and test one of these policies:

1. Keep the 5% direct-DATUM scheduler and tolerate/recover from rejected stock
   fallback jobs, after a longer stock-client soak proves coordinated work is
   stable enough for public use.
2. Launch the stock-compatible direct DATUM listener fee-free. A zero-fee
   listener does not need to associate fallback work with a support-template
   decision, while canonical coinbase and chain validation still apply.
3. Require a gateway implementation that never mines uncoordinated fallback
   templates. This gives strict fee enforcement but does not meet the goal of
   compatibility with arbitrary existing DATUM clients.

The hosted Stratum gateway remains a separate gate: one gateway process has a
single upstream DATUM session and cannot safely provide independent slot-zero
payouts to many downstream Stratum addresses merely by forwarding usernames.

