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
- GridPool commit `1a12b2b` rejects scheduler/coinbase mismatches without
  disconnecting the DATUM session and publishes the guarded beta deployment.
- The full suite passes at `267/267` on `1a12b2b`.
- Live tests confirmed exact-policy support-template shares can be accepted and
  ordinary stale-parent work remains rejected.
- A forced-YUGE GridPool DATUM gateway submitted a post-lock share that was
  persisted under the distinct client payout address
  `1FhDPLPpw18X4srecguG3MxJYe4a1JsZnd`, proving client-payout attribution on
  coordinated full-coinbase work. Pre-lock scheduled support work was persisted
  under the configured support address, as required by the 5% policy.

## Public-beta decision

Stock DATUM can create an uncoordinated fallback job with `coinbaser_id = 0`
when it does not obtain a usable coinbaser response. Such a job cannot be
matched to a deterministic 5% support-template decision. GridPool rejects it
fail-closed; accepting it would let a client bypass the scheduler.

The rejection is explicitly controlled by `require_coordinated_coinbaser` and
is independent of the listener fee. This distinction matters because output
validation alone is not sufficient in every state: an empty bootstrap snapshot
has no winner outputs to distinguish, and winner outputs paying the same script
as slot zero can be aggregated into a single output. A decisionless fallback
must therefore never become GridPool-valid merely because a listener is
fee-free or all expected outputs happen to share one script.

Stock DATUM's default coinbase class is too small for a general 300-unique-output
GridPool winner list. Its encrypted protocol interoperates, but an unmodified
gateway produces GridPool-valid work only when downstream firmware fingerprints
into DATUM's 16-KB YUGE class. A stock-gateway CPU canary confirmed that smaller
single-recipient jobs are rejected; accepted pre-lock samples were scheduled
support jobs and were not evidence of client-payout compatibility.

The supported beta path is the GridPool DATUM build with forced YUGE mode. TCP
3008 was published at `datum.blake.gridpool.net:3008` on release
`blake2b-mainnet-beta-3+1a12b2b`, with immutable image digest
`sha256:65257cc727da05c3ce213777f69fb50ebc2a3e927dfa25fae143f9836d285149`.
The public behavior is:

1. accept stock DATUM protocol sessions and coordinated jobs;
2. enforce the deterministic 5% slot-zero scheduler decision exactly;
3. reject every `coinbaser_id = 0` or otherwise uncorrelated fallback share;
4. preserve the mainnet non-empty bootstrap invariant as defense in depth.

The server difficulty floor is 4096. The direct DATUM listener retains a
deterministic 5% support-template schedule. HTTP, RPC, ZMQ, DATUM dashboards,
the hosted-policy listener, and compatibility-probe Stratum remain private.

The hosted Stratum gateway is intentionally not payout-multiplexed. Its
upstream listener selects the operator address for 100% of templates, the
gateway does not pass downstream usernames upstream, and the endpoint is
advertised solely as a short-lived firmware compatibility test with zero
expected user rewards.
