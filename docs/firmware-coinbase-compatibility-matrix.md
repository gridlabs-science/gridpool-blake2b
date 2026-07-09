# Firmware Coinbase Compatibility Matrix

Status: community-maintained beta compatibility matrix.

GridPool's 300-slot beta can require a large coinbase transaction when the
payout snapshot contains many unique addresses. Some ASIC firmware, Stratum V1
gateways, and hashrate-rental intermediaries cannot handle large coinbase
templates. This document tracks practical compatibility results.

This matrix is intentionally community-driven. Grid Labs cannot test every ASIC
model, control board, firmware version, rental service, and Stratum proxy. If
you test a setup, please open a pull request updating this file.

## Result Labels

- `works`: tested with a full uncondensed 300-output GridPool payout template
  and accepted work without lockups, disconnect loops, or persistent rejected
  shares.
- `fails`: tested with a full uncondensed 300-output template and failed.
  Include whether the miner rejected work, disconnected, locked up, or produced
  GridPool-invalid shares.
- `suspected works`: expected to work based on DATUM's current fingerprinting
  research or known coinbase-size class, but not yet independently tested with
  a full GridPool 300-output stress template.
- `suspected fails`: expected to fail or be unsafe based on known small
  coinbase limits, but not yet independently tested with GridPool.
- `untested`: no useful GridPool-specific data yet.
- `requires alternate firmware`: stock firmware is suspected or known to be
  unsafe, but alternate firmware may work.

## How To Test

Use a non-production GridPool node configured to serve a full uncondensed
300-output payout set:

```json
{
  "node_mode": "staging",
  "coinbase_uncondensed_outputs_enabled": true
}
```

Do not enable `coinbase_uncondensed_outputs_enabled` on production nodes. It is
a firmware/rental stress-test mode. Its purpose is to expose the worst-case
300-unique-address coinbase shape even when the live beta state is currently
condensed because many slots belong to the same address.

Recommended test path:

1. Connect the miner or rental endpoint through DATUM or a GridPool-compatible
   Stratum gateway.
2. Confirm the gateway receives full 300-output GridPool templates.
3. Let the miner run for at least `15` minutes, or longer if the firmware has a
   history of delayed template switching.
4. Record whether the miner accepts work, keeps hashing, and avoids reconnect
   loops.
5. Record GridPool diagnostics: acceptance rate, rejection reasons, and any
   `Firmware coinbase truncation` rejects.
6. If a miner locks up, immediately remove power or revert to a known-safe pool
   according to the firmware vendor's recovery instructions.

Minimum evidence for a PR:

- ASIC model or rental provider.
- Firmware name and exact version if available.
- Gateway used: DATUM, Hydrapool, direct GridPool HTTP, Stratum V2 proxy, etc.
- GridPool node version or commit.
- Whether uncondensed stress mode was enabled.
- DATUM user agent or Stratum user agent if available.
- Result label and short notes.

## Public Test Endpoint Plan

The preferred first beta setup is to use the testnet node as the compatibility
endpoint. That keeps the endpoint clearly test-only and avoids standing up a
separate GridPool node before the testing process is proven.

Recommended layout:

- `main.gridpool.net`: normal public beta node.
- `datum.main.gridpool.net:3008`: normal public beta DATUM endpoint.
- `test.gridpool.net/compat`: lab UI for firmware/rental compatibility testing.
- `datum.test.gridpool.net:3009`: testnet DATUM-upstream endpoint for DATUM
  gateways.
- `stratum.test.gridpool.net:3334`: testnet Stratum V1 endpoint for ASICs,
  backed by DATUM forced `yuge` coinbase mode.

The testnet compatibility node should use uncondensed output mode and should be
kept separate from normal mainnet beta state. See
[testnet-full-coinbase-compatibility-endpoint.md](testnet-full-coinbase-compatibility-endpoint.md).

## Seed Matrix

These rows are seeded from DATUM's current Stratum V1 coinbase-size
fingerprinting categories. Treat them as suspected status until someone tests
the exact firmware version with full uncondensed GridPool templates.

| Miner / Service | Firmware / Client UA | Gateway | DATUM Class | Status | Evidence | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| ePIC control boards | `PowerPlay-BM/` | DATUM | `YUGE` ~16 KB | suspected works | DATUM fingerprint category | Needs full 300-output GridPool stress confirmation. |
| VNish / xminer-class firmware | `xminer-1.` | DATUM | `YUGE` ~16 KB | suspected works | DATUM fingerprint category | Needs model/version-specific testing. |
| Whatsminer-class firmware | `whatsminer/v1` | DATUM | `RESPECTABLE` ~6.5 KB | suspected fails | DATUM fingerprint category | 6.5 KB is likely too small for 300 unique P2WPKH/P2TR outputs. Test to confirm exact behavior. |
| Bitaxe / AxeOS | `bitaxe` | DATUM or direct HTTP | `RESPECTABLE` ~6.5 KB in DATUM | untested | DATUM fingerprint category | Direct GridPool HTTP firmware may avoid DATUM's coinbase class behavior; test both paths separately. |
| Antminer S21 stock-like firmware | `Antminer S21/` | DATUM | `ANTMAIN2` ~2.25 KB | suspected fails | DATUM fingerprint category | Likely too small for full 300-output templates. Alternate firmware may work. |
| Braiins tuner path | contains `bosminer-plus-tuner` | DATUM | `ANTMAIN2` ~2.25 KB | suspected fails | DATUM fingerprint category | DATUM currently gives this a moderate class, not full `YUGE`. Needs firmware-specific testing. |
| Older stock Antminer-class firmware | unknown/default | DATUM | default ~750 bytes | suspected fails | DATUM default behavior | Use alternate firmware or a future smaller-team compatibility tier. |
| NiceHash-style clients | `NiceHash/` | DATUM / rental | `SMALL` ~500 bytes | suspected fails | DATUM fingerprint category | Very unlikely to support full GridPool 300-output templates through DATUM. |
| Antminer S19 XP | `Antminer S19 XP\|LUXminer 2025.7.10.152155-6e13fb74\|BHB56801\|Unknown\|Unknown\|blockware` | DATUM forced full-coinbase test endpoint | fingerprinted `ANTMAIN2` class 2; forced `YUGE` class 4 with unsafe override | works | GridPool testnet full-coinbase endpoint, 2026-07-08/09 | Safe mode was intentionally disconnected by DATUM because class 2 is smaller than forced class 4. With password override `UNSAFE_FULL_COINBASE`, the miner stayed subscribed/authorized, DATUM generated 300-output jobs (`Coinbaser v2 size 10201`), LuxOS accepted full-coinbase work at diff 512, and GridPool/DATUM recorded 6,500+ accepted shares with no full-coinbase rejects. Initial observation showed a delayed/idle period before shares started; allow a longer soak before classifying. |
| LuxOS | other versions/models | DATUM / Hydrapool | unknown | untested | community testing needed | One S19 XP/LUXminer version passed full 300-output DATUM testing only with the unsafe override. Do not generalize to all LuxOS versions without testing. |
| Hashrate rental provider: MiningRigRentals | provider-specific | Hydrapool / Stratum V1 | n/a | untested | community testing needed | Test against the lab Stratum endpoint before recommending publicly. |
| Hashrate rental provider: NiceHash | provider-specific | Hydrapool / Stratum V1 | n/a | suspected fails | DATUM `NiceHash/` limit is small | Needs direct Hydrapool/Stratum test; DATUM path is expected to fail for full 300-output templates. |

## Pull Request Template

Copy this into your PR description:

```text
Firmware compatibility test result

- ASIC or provider:
- Firmware/client version:
- Stratum/DATUM user agent:
- Gateway path:
- GridPool node version/commit:
- Uncondensed 300-output stress mode enabled: yes/no
- Test duration:
- Result label:
- Miner behavior:
- GridPool acceptance rate:
- GridPool rejection reasons observed:
- Logs/screenshots attached:
- Notes:
```

## Safety Notes

- If a firmware is known to lock up on oversized templates, test on a small
  controlled miner first, not a production fleet.
- A result against today's condensed beta templates is not enough. The test must
  use uncondensed 300-output mode to be launch-relevant.
- Compatibility is version-specific. A miner model marked `works` with one
  firmware version does not prove that another firmware version works.
