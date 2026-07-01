# Simulation Findings, June 2026

Status: internal review draft.

Source repository: `/home/keegreil/Documents/GitHub/gridpool-simulations`

The simulation repo is intentionally separate from the live node implementation. Generated reports are ignored by git until reviewed and promoted. This note summarizes what looks publishable now and what still needs more evidence.

## Runs Reviewed

Reviewed generated outputs under:

- `reports/generated/sweeps/pool_hopping_external_mode_299_long`
- `reports/generated/sweeps/pool_hopping_physical_scale_solo_299_long`
- `reports/generated/sweeps/pool_hopping_fee_sensitivity_299_long`
- `reports/generated/sweeps/pool_hopping_snapshot_policy_299_long`
- `reports/generated/sweeps/block_withholding_fee_sensitivity_long`
- `reports/generated/sweeps/payout_variance_fee_long`
- `reports/generated/sweeps/latency_peer_degree_long`
- `reports/generated/sweeps/majority_cartel_share_long`

The last timestamped output, `reports/generated/smoke`, is a sanity run, not a long-run result.

## Findings That Look Publishable With Caveats

### 1. Pool Hopping Does Not Forge Work

The model supports a narrow but important claim:

> Pool hopping does not let a miner create unpaid shares or steal slots. The miner can only retain claims to proof-of-work already contributed.

This is a mechanism claim, not an economic claim. It follows from proof validation and paid-once reserve mechanics.

### 2. Zero-Fee Outside Options Can Create A Small Free-Option Effect

The external-mode sweep shows that a miner who earns GridPool proofs and then leaves for a zero-fee deterministic FPPS-like outside option can show a small positive paired delta. With a 2% external fee, the same strategies generally lose absolute EV in the tested setup.

Useful numbers from `pool_hopping_external_mode_299_long`:

- zero-fee deterministic FPPS outside option: hopper EV ratios around `1.0006` to `1.0020`;
- 0.5% deterministic FPPS outside option: hopper EV ratios around `0.9960` to `0.9972`;
- 2% deterministic FPPS outside option: hopper EV ratios around `0.9814` to `0.9826`;
- zero-fee solo outside option is noisier and showed one strategy near `1.0175` in this run.

Publishable wording should be careful:

> Simulations found a small free-option effect when the outside option is idealized and zero-fee. This is not share theft; it is the consequence of already-earned GridPool proofs remaining payable while the miner temporarily earns elsewhere. Realistic fees and variance can erase or reverse the apparent edge.

### 3. Paid-Once Reserve Leaves Some Inactive Claims, But They Are Small In These Runs

In the physical-scale solo sweep, inactive snapshot slots under hopping strategies were typically small:

- `hopper_leaves_after_3_slots_for_6_blocks`: average inactive snapshot fraction about `1.0%`;
- `hopper_leaves_while_any_reserve_proof`: about `0.14%`;
- `hopper_leaves_while_in_snapshot`: about `0.43%`.

That supports the claim that stale earned claims exist but were not dominant in the tested parameter range.

### 4. Block Withholding Looks Expensive

In `block_withholding_fee_sensitivity_long`, the attacker that withheld all blocks performed far worse than honest mining.

Attacker EV ratios:

- fees `0.0 BTC`: `0.8345`;
- fees `0.05 BTC`: `0.8214`;
- fees `0.25 BTC`: `0.7727`;
- fees `1.0 BTC`: `0.6322`.

Selective withholding when underrepresented also underperformed honest mining in this model:

- fees `0.0 BTC`: `0.9515`;
- fees `1.0 BTC`: `0.9081`.

This supports a cautious public claim:

> In the current model, withholding valid GridPool blocks is costly because the finder gives up slot 0, transaction fees, and the acceleration of its own existing payout claims.

### 5. More Slots Greatly Reduce Variance Once The Team Is Large Enough

The variance sweep supports the value of a large payout list, but it also clarifies the boundary:

- For tiny miners on tiny teams, variance remains essentially solo-like because expected paid events are still rare.
- As team hashrate grows, 300 slots reduce variance much more than 10 or 100 slots.
- For a `1 PH` miner on a much larger team, 300 slots dramatically lowers the probability of no GridPool payout over the modeled period compared with small slot counts.

Selected no-support-fee examples from `payout_variance_fee_long`:

- `1 PH`, 300 slots, team multiplier `10000`: variance reduction vs solo about `271.7x`; zero payout probability about `11.9%`.
- `1 PH`, 100 slots, same team multiplier: variance reduction about `96.7x`; zero payout probability about `48.8%`.
- `1 PH`, 10 slots, same team multiplier: variance reduction about `10.0x`; zero payout probability about `93.1%`.

This is a strong argument for keeping a large 300-slot default when firmware can support it.

### 6. Compact Relay Helps Latency Split Risk In The Network Model

The latency sweep suggests compact WebSocket and UDP relay reduce split rates and payload relative to JSON HTTP.

For degree `8`:

- 16 nodes: JSON split rate `0.0050`, compact WebSocket `0.00083`, UDP `0.0`.
- 24 nodes: JSON `0.00542`, compact WebSocket `0.00208`, UDP `0.0`.
- 48 nodes: JSON `0.01333`, compact WebSocket `0.00542`, UDP `0.00083`.

Payload numbers in these reports are model-estimated total relay payload over the simulated run, not per day. Charts should be relabeled before public use.

## Findings Not Ready For Public Claim

### Majority / Censorship Model

The majority-cartel model is still too abstract for strong claims. It is useful as an argument map, but not yet a realistic network-convergence model.

Do not yet claim that GridPool is immune to 51% style cartel behavior. Safer wording:

> GridPool has no sharechain to privately reorg, but majority miners can still create team-split and censorship pressure. The current design aims to make inclusive states economically attractive, but this needs stronger network/adversary modeling.

### Consensus Scoring

Current code uses summed proof difficulty to choose heavier states. The simulations reviewed here do not yet compare alternative scoring rules. This remains launch-gating work before broad packaged release.

### Clear-Every-Bitcoin-Block Policy

The snapshot-policy sweep shows that clearing every Bitcoin block eliminates inactive claims but can hurt hopper EV sharply under some strategies and changes the game mechanics materially. It is not recommended without deeper study.

The current paid-once reserve policy remains the safer live-beta design.

## Recommended Public Claims Now

Use cautious language:

- GridPool shares are independently verifiable proof-of-work commitments to a payout snapshot.
- A miner cannot alter another miner's slot-0 address without invalidating the merkle root/header.
- Pool hopping does not manufacture unpaid claims; at most it preserves claims to work already performed.
- Block withholding appears expensive in current simulations because the finder gives up direct slot-0/fee upside.
- Large payout lists materially improve payout variance for miners once team block cadence is high enough.
- Compact relay appears useful for reducing latency-driven state splits, but real-world measurements are still needed.

Avoid stronger claims for now:

- "GridPool is immune to 51% attacks."
- "Pool hopping is never profitable."
- "Latency cannot hurt GridPool."
- "300 slots is always optimal for every miner."

## Next Simulation Work

- Compare consensus scoring rules for competing states.
- Add real peer-network convergence around the majority/censorship model.
- Add better chart labels for payload timeframe and latency assumptions.
- Re-run pool-hopping external-mode sweeps with more variants and confidence intervals per variant, not only one aggregate row.
- Add direct comparison of 10, 30, 100, and 300 slot teams under firmware-constrained team scenarios.
