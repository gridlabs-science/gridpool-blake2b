# Stratum V2 / GridPool Evaluation

Status: research note. The selected MVP path is documented in
`docs/stratum-v2-gridpool-integration-plan.md`.

Purpose: evaluate whether Stratum V2 can remove or reduce GridPool's miner-firmware coinbase-size constraint, especially for the 300-slot payout list.

## Why This Matters

GridPool consensus currently requires miners to hash work committing to the active payout snapshot. In the 300-slot beta, the logical payout list can contain:

- slot 0 finder payout;
- optional Grid Labs support slot;
- up to 298 shared proof payout slots;
- witness commitment and any tag/metadata outputs.

Some Stratum V1 ASIC firmware cannot reliably handle large coinbase transactions. DATUM can fingerprint downstream clients and select smaller coinbase layouts for firmware compatibility, but a shortened GridPool payout list is consensus-invalid for the 300-slot team. The minimal short-term behavior remains:

- detect likely coinbase truncation;
- reject invalid shares;
- warn users clearly;
- test and document compatible firmware/rental paths.

## Relevant Stratum V2 Facts

Primary source: Stratum V2 specification.

- The Mining Protocol distributes work and receives shares. It can be used alone with pool-provided work, or with Job Declaration and Template Distribution when miners coordinate custom work with a pool.
  Source: <https://stratumprotocol.org/specification/05-mining-protocol/>

- A Standard Job is restricted to a fixed Merkle root. The device only mutates header fields such as `version`, `nonce`, and `nTime`. The spec calls this header-only mining.
  Source: <https://stratumprotocol.org/specification/05-mining-protocol/>

- `NewMiningJob` for a standard channel includes `version` and `merkle_root`, not the full coinbase transaction. This is the key property that could avoid exposing 300-output coinbases to end ASIC firmware.
  Source: <https://stratumprotocol.org/specification/05-mining-protocol/>

- Extended Jobs allow rolling Merkle roots and are intended for proxies, translation, difficulty aggregation, and search-space splitting. A proxy can derive standard-channel Merkle roots for downstream devices from extended job coinbase prefix/suffix data.
  Source: <https://stratumprotocol.org/specification/05-mining-protocol/>

- SV2 explicitly models roles that map well onto GridPool architecture: Mining Device, Pool Service, Mining Proxy, Job Declarator, and Template Provider.
  Source: <https://stratumprotocol.org/specification/03-protocol-overview/>

- The Job Declaration Protocol lets miner-side software declare custom work. In coinbase-only mode, the pool does not learn the transaction set, preserving mempool privacy. In full-template mode, the job declaration server may validate transaction data.
  Source: <https://stratumprotocol.org/specification/06-job-declaration-protocol/>

- Template Distribution replaces `getblocktemplate` with push-style template updates, and includes `CoinbaseOutputConstraints` so a template provider can reserve block/coinbase space for pool or job-declarator outputs.
  Source: <https://stratumprotocol.org/specification/07-template-distribution-protocol/>

## Initial Assessment

SV2 standard-channel/header-only mining is likely the cleanest long-term answer to the ASIC coinbase-size problem.

The end ASIC receives a fixed Merkle root and header fields, so it does not need to parse, store, or mutate a 300-output coinbase. The 300-output coinbase still exists and must still be constructed, validated, and submitted by upstream software, but that work moves to an SV2 proxy / Job Declarator / Template Provider layer where larger memory and more flexible code are reasonable.

This does not make the problem disappear entirely:

- GridPool-compatible upstream software must still construct the full payout coinbase.
- The upstream software must preserve GridPool's slot-0 attribution rule.
- Share submission to GridPool still needs enough proof data to verify header PoW, Merkle root, coinbase commitment, payout outputs, and parent context.
- If an SV2 proxy translates to Stratum V1 downstream, the V1 firmware may still see a coinbase and remain constrained.
- If the ASIC supports native SV2 standard channels, the coinbase-size constraint should be much less relevant.

## Possible Integration Paths

### Path A: Native SV2 Pool Service Adapter

GridPool implements enough SV2 Mining Protocol to serve standard-channel jobs directly.

Pros:

- Directly solves the end-device coinbase-size problem for native SV2 firmware.
- Gives GridPool a modern encrypted binary mining endpoint.
- Avoids relying on DATUM-specific behavior for SV2 devices.

Cons:

- Significant implementation surface.
- Needs careful channel/session accounting and target management.
- Still needs block-template and coinbase construction integration.

### Path B: GridPool-Aware SV2 Proxy

A separate proxy talks GridPool/DATUM/Bitcoin upstream and serves SV2 standard jobs downstream to ASICs.

Pros:

- Keeps GridPool reference node smaller.
- Lets the proxy own translation, firmware quirks, and device-specific job cadence.
- Could reuse existing SV2 libraries or projects.

Cons:

- Another moving part for users.
- The proxy becomes critical infrastructure for miners using it.
- Still needs robust share proof export back to GridPool.

### Path C: SV2 Job Declaration / Template Provider Research First

Do not implement a mining endpoint yet. First map exactly how GridPool payout snapshots fit into:

- `AllocateMiningJobToken.Success.coinbase_tx_outputs`;
- `CoinbaseOutputConstraints`;
- coinbase-only custom jobs;
- full-template custom jobs;
- `SetCustomMiningJob`;
- `SubmitSolution` / block propagation.

Pros:

- Best next step before coding.
- Clarifies whether GridPool can use existing SV2 semantics cleanly.
- Useful for future third-party adapters.

Cons:

- Does not solve current firmware compatibility immediately.

## Open Engineering Questions

- Which currently available ASIC firmwares support native SV2 standard channels well enough to test?
- Can the GridPool payout snapshot be represented cleanly as the pool-designated output set in SV2 job declaration, or does it need a GridPool-specific extension?
- In coinbase-only Job Declaration mode, can GridPool validate enough to preserve its trust model while remaining transaction-set blind?
- Does full-template mode materially improve GridPool's censorship/audit story, or does it leak too much transaction-selection information for the project's goals?
- What exact proof should an SV2 proxy submit to GridPool for an accepted share?
- Can existing SV2 libraries be used without pulling a large unstable dependency into the reference node?
- How should SV2 support interact with the current DATUM support and Hydrapool/HTTP share API?

## Recommended Near-Term Plan

1. Keep DATUM as the current beta path.
2. Use `coinbase_uncondensed_outputs_enabled` on non-production nodes to build a firmware/rental compatibility matrix.
3. Warn users that 300-slot GridPool requires firmware or proxy software that can handle large payout coinbases unless using a header-only/native SV2 path in the future.
4. Treat SV2 as a serious post-V2.1 integration track, not a quick patch.
5. Start with a sidecar adapter that implements the pool-side SV2 Pool Service / Job Declarator Server behavior and converts SV2 custom-job shares into GridPool's existing full-proof HTTP submission format.
6. Target coinbase-only Job Declaration first, preserving miner transaction-set privacy while keeping GridPool's slot-0 attribution and payout-output validation.

GridPool now exposes `GET /api/mining/sv2-work-selection` as the stable node-side
contract for that adapter. The endpoint returns active snapshot metadata,
serialized GridPool coinbase payout outputs, and the current reserve admission
difficulty. Share submission remains `POST /api/mining/share`.

## Launch Checklist Impact

Before Umbrel/Start9 packaging, GridPool should either:

- publish a tested list of compatible DATUM/firmware/rental paths for the 300-slot beta; or
- clearly label 300-slot support as experimental and unsuitable for untested hashrate rental providers.

SV2 support itself is not required for the first package launch, but the SV2 evaluation should be completed enough to explain whether it is the likely long-term compatibility path.
