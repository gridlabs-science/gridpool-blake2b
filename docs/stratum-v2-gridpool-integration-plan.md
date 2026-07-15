# Stratum V2 GridPool Integration Plan

> **Status update (2026-07-15):** The JDC/JDS adapter architecture below has been superseded by
> the smaller `gridpool-sv2-pool` SRI Pool fork. The fork retains SRI's Bitcoin Core IPC, SV2
> channel, vardiff, validation, and block-submission paths while adding a modular GridPool payout
> and share-observer layer. This document remains as design history.

Status: implementation plan, MVP path selected.

## Goal

Add Stratum V2 support without changing GridPool consensus by making GridPool
look like the pool side of an SV2 Job Declaration setup.

In SV2 terminology:

- GridPool provides the pool-side pieces: Pool Service plus Job Declarator Server
  behavior.
- The miner runs the miner-side pieces: Job Declarator Client, Template Provider,
  Mining Proxy, and ASICs.

This replaces the role DATUM currently fills for some users: the miner-side
stack talks to the miner's Bitcoin node, builds templates, distributes work to
ASICs, and sends shares upstream for GridPool accounting.

## MVP Architecture

Build a separate `gridpool-sv2-adapter` process first, preferably in Rust using
the Stratum Reference Implementation crates/apps where practical.

The .NET GridPool node remains the consensus and share-verification authority.
The adapter talks SV2 to miner-side software and talks GridPool HTTP/local API
to the node.

The first target mode is SV2 coinbase-only Job Declaration:

- The miner keeps its transaction set private.
- The adapter validates the proposed coinbase payout outputs and merkle proof.
- The adapter does not need transaction data to account GridPool shares.
- Invalid hidden templates are treated like block withholding: costly to the
  miner and not rewarded unless a valid Bitcoin block is actually found.

Full-template Job Declaration can be added later if operators want server-side
transaction validation or secondary block propagation.

## GridPool API Contract

GridPool exposes:

```text
GET /api/mining/sv2-work-selection
```

This returns the active GridPool payout commitment for an SV2 Job Declarator:

- `networkId`
- `bitcoinNetwork`
- `protocolVersion`
- `activeSnapshotId`
- `currentStateId`
- `candidateStateId`
- `currentTipBlockHash`
- `currentTipBlockHeight`
- `totalPayoutSlotCount`
- `sharedWinnerSlotCount`
- `supportFeeEnabled`
- `coinbaseTxOutputsHex`
- `coinbaseOutputs`
- `minimumDifficultyToEnterReserve`

`coinbaseTxOutputsHex` is the exact CompactSize-prefixed serialized transaction
output vector that the adapter/JDC should include in the custom job after the
slot-0 miner payout output, subject to the standard SV2 custom-job rules.

Share submission remains unchanged:

```text
POST /api/mining/share
```

The adapter must submit the existing full GridPool proof shape:

- `HeaderHex`
- `CoinbaseHex`
- `MerklePath`
- `PayoutSnapshotId`
- `MinerAddress` / `Username` metadata

GridPool still attributes the share from slot 0 in the coinbase, not from
metadata.

## SV2 Data Flow

1. Miner-side JDC connects to the GridPool SV2 adapter.
2. JDC requests a mining job token with `user_identifier = payoutAddress` or
   `payoutAddress.worker`.
3. Adapter fetches `/api/mining/sv2-work-selection` from the local GridPool node.
4. Adapter returns an SV2 `AllocateMiningJobToken.Success` bound to the active
   snapshot.
5. JDC builds a custom coinbase with:
   - slot 0: miner payout address;
   - GridPool payout outputs from the active snapshot;
   - normal witness commitment/tag outputs.
6. JDC sends `SetCustomMiningJob`.
7. Adapter validates the custom job's coinbase outputs and merkle path against
   the active snapshot, then returns `SetCustomMiningJob.Success`.
8. ASICs receive SV2 standard/header-only jobs.
9. Adapter receives `SubmitSharesStandard` or `SubmitSharesExtended`,
   reconstructs the full header/coinbase/merkle proof, and submits it to
   GridPool.
10. If a real block is found, JDC/Template Provider broadcasts it through the
    miner's Bitcoin node. GridPool records the proof and advances state only
    after validating the GridPool block proof.

## Key Constraints

- Header-only mining solves the ASIC coinbase-size problem only for native SV2
  standard-channel downstream clients.
- SV1 translation may still expose large coinbases to old firmware and should
  not be described as a complete fix.
- Adapter tokens must bind to `activeSnapshotId`; stale-snapshot custom jobs
  should be rejected clearly.
- The adapter may use `user_identifier` for UX/session labeling, but share
  attribution remains slot-0 based.
- GridPool consensus does not require knowing transaction selection in
  coinbase-only mode.

## Acceptance Tests

- `sv2-work-selection` returns exact serialized payout output bytes.
- Serialized output vectors use CompactSize correctly for 300-output cases.
- A valid custom job reconstructs into a GridPool full proof accepted by the
  existing verifier.
- Missing/truncated/reordered required payout outputs are rejected.
- Shares for stale snapshot IDs are rejected or marked stale with a clear reason.
- A mainnet-beta smoke run can connect SRI miner-side software to the GridPool
  SV2 adapter, submit shares, and independently record a found block through the
  miner-side Bitcoin node.

## References

- SV2 Mining Protocol:
  <https://stratumprotocol.org/specification/05-mining-protocol/>
- SV2 Job Declaration Protocol:
  <https://stratumprotocol.org/specification/06-job-declaration-protocol/>
- SV2 Template Distribution Protocol:
  <https://stratumprotocol.org/specification/07-template-distribution-protocol/>
- SRI application repo:
  <https://github.com/stratum-mining/sv2-apps>

## Mainnet Beta Rollout Notes

Do not require a testnet-first rollout for this integration. The first practical
target is a controlled mainnet-beta smoke test using low-risk hashrate after the
GridPool node exposes `/api/mining/sv2-work-selection` and the SV2 adapter is
available.

The implemented path no longer uses stock JDC/JDS. `gridpool-sv2-pool` connects
directly to Bitcoin Core through SRI IPC and to boot-portal through
`GET /api/mining/sv2-work-selection`, `POST /api/mining/local/share`, and
`POST /api/mining/local/share-telemetry`.

The current SRI application repo requires Rust 1.88 and `capnp` from
Cap'n Proto. On Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y capnproto libcapnp-dev
git clone https://github.com/stratum-mining/sv2-apps ~/Documents/GitHub/sv2-apps
cd ~/Documents/GitHub/sv2-apps
cargo build --manifest-path miner-apps/Cargo.toml -p jd_client_sv2
cargo build --manifest-path pool-apps/Cargo.toml -p pool_sv2
```
