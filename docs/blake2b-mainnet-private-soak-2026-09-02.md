# Blake2b Mainnet Private Soak — 2026-09-02

## Scope

The first mainnet private soak ran on the Blake VPS from
`2026-09-02T11:41:35Z` through `2026-09-02T23:41:37Z`. GridPool used the
immutable image at application commit `8b3e45a`, the RC4 Knots mainnet node,
the reviewed Blake2b DATUM gateway, and a CPU miner connected through a private
SSH forward. HTTP, DATUM, Stratum, RPC, and ZMQ remained closed to the public.

The soak deliberately overlapped AssumeUTXO background validation. It therefore
tests fail-closed behavior under resource contention, but it is not the final
availability measurement for the steady-state production node.

## Results

- 8,605 GridPool API samples completed with zero polling failures.
- The monitor observed 314 round changes, from round 6 to round 391, and no tip
  height regression.
- GridPool reported 177 accepted DATUM submissions and zero rejected
  submissions at the end of the window.
- The end-of-window coinbaser average was 6.07 ms, p95 was 19.68 ms, and no
  slow fetch was recorded.
- The GridPool node identity remained unchanged across controlled restarts.
- The accepted Work Set survived a controlled restart; the post-restart proof
  set and node identity matched the pre-restart values.
- Both ZMQ topics retained exactly one advertised publisher with zero sequence
  gaps and zero duplicates.
- No kernel OOM event occurred. Knots, GridPool, DATUM, the SSH tunnel, and the
  CPU miner remained recoverable and active after the soak.

## Availability finding

DATUM logged 3,183 template-fetch failures during the window. The failures
correlated with Knots falling briefly behind its advertised header height or
holding RPC while flushing a large UTXO cache. GridPool rejected unsafe work
generation and closed affected DATUM sessions rather than admitting candidate
shares against an unconfirmed or stale attached-node state.

The VPS has six vCPUs and was not CPU-saturated. Knots was still validating the
historical chainstate while serving the active AssumeUTXO chainstate. Its log
repeatedly reported multi-gigabyte UTXO flushes, and a sampled
`getblockchaininfo` call took 19.18 seconds during one such interval. At
`2026-09-03T00:44:13Z`, background validation had reached height 879,791 of the
height-910,000 snapshot base.

This is a successful security/fail-closed result but not an acceptable public
mining availability result. Public mining remains gated until full background
validation finishes and the post-validation soak demonstrates that the
template stalls disappear.

## Monitoring correction

The 12-hour sampling loop itself completed, but its final analysis originally
exited nonzero because `boot-g2-monitor.mjs` treated two disabled private
diagnostic APIs as mandatory. Commit `b2e4782` aligns it with
`boot-soak-report.mjs`: unavailable private diagnostics no longer abort the
report, and verdicts that depend on them are explicitly marked not evaluated
instead of receiving a false pass. A live six-second VPS smoke run verified the
fix.

## Follow-up

`gridpool-blake2b-mainnet-post-validation-soak.service` is enabled on the VPS.
It waits for `getchainstates` to report one fully validated chainstate and then
runs another 12-hour private soak. Its output is written to:

```text
/opt/gridpool-blake2b/mainnet-private-soak/soak-logs/post-validation-soak.json
```

Public listener policy, payout-session isolation, a production difficulty
floor, an off-host seed-identity backup, and the post-validation soak remain
separate launch gates.
