# DATUM Coinbase Size Compatibility For GridPool

Status: public-beta compatibility requirement.

GridPool's 300-slot beta requires every mining template to contain the full
GridPool payout output set. DATUM Gateway can serve different coinbase variants
to different Stratum V1 clients based on miner fingerprinting. That is useful
for traditional pool payouts, but it is dangerous for GridPool because a
truncated payout list creates consensus-invalid shares.

## DATUM Behavior Observed

Current DATUM `master` has one relevant public config flag:

```json
{
  "stratum": {
    "fingerprint_miners": true
  }
}
```

This does not force a large coinbase. It only enables user-agent based miner
workarounds.

In DATUM's Stratum subscribe path, every client defaults to coinbase selection
`2`, the older Antminer-compatible class. If `fingerprint_miners` is enabled,
known user agents can be moved to another class:

- `NiceHash/`: class `1`, roughly 500 bytes.
- unknown/default: class `2`, roughly 755 bytes.
- `whatsminer/v1` and `bitaxe`: class `3`, roughly 6.5 KB.
- `PowerPlay-BM/` and `xminer-1.`: class `4`, roughly 16 KB.
- `Antminer S21/` and detected Braiins tuner firmware: class `5`, roughly
  2.25 KB.

Disabling `stratum.fingerprint_miners` is therefore not GridPool-safe. It makes
all miners use the smaller default class `2`, which will truncate any full
GridPool payout list.

## Size Implication

A full 300-unique-address GridPool payout list is much larger than ordinary
single-recipient pool coinbases:

- 300 P2WPKH-style outputs are roughly 9.3 KB before slot 0, witness commitment,
  tags, and transaction overhead.
- 300 P2TR-style outputs are roughly 12.9 KB before slot 0, witness commitment,
  tags, and transaction overhead.
- DATUM's 6.5 KB class is therefore not enough for the worst-case 300-slot beta.
- DATUM's 16 KB `YUGE` class is the only existing class that is clearly sized
  for a full 300-slot GridPool team.

Condensed GridPool output mode can hide this during early beta when many slots
belong to the same address. That is not a valid compatibility signal. The
launch test must use uncondensed 300-output stress mode.

## Current GridPool-Safe Operating Guidance

For existing DATUM builds, the safest available guidance is:

1. Keep `stratum.fingerprint_miners` enabled.
2. Use firmware that DATUM fingerprints into `YUGE`, currently ePIC
   `PowerPlay-BM/` or `xminer-1.` class firmware.
3. Treat lower DATUM coinbase classes as unproven or incompatible with a full
   300-unique-address GridPool team.
4. Use GridPool's firmware truncation rejection diagnostics as a backstop, not
   as the primary compatibility mechanism.

This is not enough for a polished Umbrel/Start9 launch because operators cannot
force deterministic GridPool-safe behavior from DATUM config alone.

For the September 2026 public beta, the direct DATUM endpoint therefore accepts
standard DATUM wire-protocol connections but does not claim universal firmware
compatibility. The GridPool DATUM build with forced `yuge` mode is the supported
configuration. Unmodified gateways are experimental and require downstream
firmware that selects DATUM's 16-KB class; strict server validation rejects
truncated, fallback, and mismatched templates without adding them to GridPool
state.

## DATUM PR Status

GridPool proposed a small DATUM operating-mode extension rather than forking
DATUM long term.

PR branch behavior:

```json
{
  "stratum": {
    "coinbase_selection_mode": "force",
    "coinbase_selection": "yuge"
  }
}
```

Semantics:

- `coinbase_selection_mode = "auto"` preserves current behavior.
- `coinbase_selection_mode = "force"` uses the configured
  `coinbase_selection` class for all compatible miners.
- When forced mode is active and `stratum.fingerprint_miners` is enabled, DATUM
  still checks known user-agent fingerprints before sending work.
- If a known miner fingerprints below the configured forced class, DATUM rejects
  `mining.subscribe` and disconnects before serving oversized templates.
- For controlled lab tests, a Stratum client can include
  `UNSAFE_FULL_COINBASE` in its password to receive the forced template despite
  a known-incompatible fingerprint. This is explicitly risky because some
  firmware can hard-lock on oversized templates.
- Unknown or unfingerprinted clients are served optimistically because DATUM has
  no reliable compatibility signal for them.
- The feature is local Gateway policy. It does not change the DATUM wire
  protocol and does not require OCEAN-style servers to know about the setting.

For GridPool 300-slot beta, the operator setting should be equivalent to:

```json
{
  "stratum": {
    "fingerprint_miners": true,
    "coinbase_selection_mode": "force",
    "coinbase_selection": "yuge"
  }
}
```

The open risk is unknown or misleading user agents. Forced mode prevents silent
DATUM downgrades for known classes, but it cannot prove unknown firmware can
handle a 16 KB template. That is why GridPool still needs a community
compatibility matrix and a full 300-output stress endpoint.

## GridPool-Side Follow-Ups

- Keep strict share validation. Do not accept shortened payout lists.
- Keep firmware truncation rejection categorization.
- Add a UI/API warning when local DATUM sessions repeatedly submit truncated
  coinbases.
- Maintain the community compatibility matrix in
  [firmware-coinbase-compatibility-matrix.md](firmware-coinbase-compatibility-matrix.md).
- Do not treat "works with current condensed beta state" as evidence that a
  firmware works with a full 300-unique-address launch state.
- Run compatibility tests with `coinbase_uncondensed_outputs_enabled: true`
  before recommending any firmware or rental path publicly.
- Use the testnet full-coinbase compatibility endpoint runbook for first-pass
  public testing: [testnet-full-coinbase-compatibility-endpoint.md](testnet-full-coinbase-compatibility-endpoint.md).
