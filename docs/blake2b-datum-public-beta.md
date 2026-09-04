# Blake2b DATUM Public Beta

Status: live, experimental, September 2026.

The public Blake2b GridPool DATUM endpoint is:

```text
datum.blake.gridpool.net:3008
```

Use this 64-byte DATUM server public key:

```text
a14988a25e9e5ad92ff7692a97ec96e711474292d778ab6ceebd00a954841f8e7663764f14eaeb1ad04e5885b14d3aed65a4330bbae82183a82b44556fbb4727
```

The listener has a minimum share difficulty of 4096 and a deterministic 5%
support-template schedule. On the other 95% of templates, slot zero uses the
payout address authenticated for the DATUM session. A session locks to its
first valid payout address; do not multiplex payout addresses through one
gateway session.

## Supported gateway configuration

The supported beta gateway is
[`gridlabs-science/datum-gateway-blake2b-gridpool`](https://github.com/gridlabs-science/datum-gateway-blake2b-gridpool)
at commit `6936c17` or a later compatible release. In addition to the operator's
normal Bitcoin RPC and local Stratum settings, use:

```json
{
  "stratum": {
    "fingerprint_miners": true,
    "allow_unsafe_coinbase_override": false,
    "coinbase_selection_mode": "force",
    "coinbase_selection": "yuge",
    "vardiff_min": 4096
  },
  "mining": {
    "pool_address": "YOUR_BLAKE_MAINNET_PAYOUT_ADDRESS",
    "pow_algorithm": "blake2b"
  },
  "datum": {
    "pool_host": "datum.blake.gridpool.net",
    "pool_port": 3008,
    "pool_pubkey": "a14988a25e9e5ad92ff7692a97ec96e711474292d778ab6ceebd00a954841f8e7663764f14eaeb1ad04e5885b14d3aed65a4330bbae82183a82b44556fbb4727",
    "pool_pass_workers": true,
    "pool_pass_full_users": true,
    "pooled_mining_only": true
  }
}
```

Set every downstream miner username to the same Blake mainnet payout address,
optionally followed by a worker suffix. Confirm the address locally before
starting hashrate; GridPool cannot recover rewards sent to an incorrect address.

## Compatibility boundary

The endpoint speaks the standard encrypted DATUM upstream protocol. An
unmodified DATUM gateway can connect, but its default and several
firmware-fingerprinted coinbase classes are too small for GridPool's 300-slot
payout transaction. It is usable only when the downstream firmware selects the
16-KB YUGE class. Smaller, single-recipient fallback, stale, and scheduler-
mismatched templates are rejected and never enter the Work Set.

This is why a successful connection is not yet proof of reward compatibility.
Verify accepted shares and payout attribution with a small amount of hashrate
before scaling up, and report the exact gateway commit and firmware version.
The public network summary at
`https://blake.gridpool.net/api/network/summary` exposes aggregate DATUM
accept/reject diagnostics and the recently observed local DATUM payout
addresses; it does not provide accounts or custodial balances.

The separate endpoint `stratum.blake.gridpool.net:3333` is only a no-rewards
firmware behavior test. It is locked to the operator payout address and should
not be used for sustained mining.
