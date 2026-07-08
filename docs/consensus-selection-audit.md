# Consensus Selection Audit

Status: draft, updated after delayed-snapshot attack modeling.

Purpose: document how GridPool currently chooses between competing valid states, evaluate whether that rule is mathematically defensible, and define the next simulations/tests needed before Umbrel and Start9 packaging.

## Context

GridPool does not use a sharechain. Nodes maintain bounded current state:

- active payout snapshot, represented internally as `WinnersList`;
- unpaid Work Set reserve, represented internally as `OnDeckProofs`;
- top shared payout preview, represented internally as `OnDeckList`;
- retained payout snapshot contexts required to validate proofs mined against older snapshots.

When two peers disagree, the V2.1 rule is "validate proofs, preserve local snapshot boundaries, and merge forward." A state is not accepted by reputation. It must be backed by proof-of-work shares whose headers, merkle roots, payout snapshot outputs, slot-0 attribution, parent context, and duplicate status all validate.

The key open question has split into two separate questions:

1. How should nodes score competing same-boundary Work Sets under honest latency?
2. Should nodes ever allow a later-arriving stale-parent branch to retroactively replace a payout snapshot that the local node already created after observing a Bitcoin block?

The first question is an estimator problem. The second is a consensus-boundary problem. The delayed-snapshot attack model shows that the boundary problem is the more important short-term launch gate.

## Current Implementation

The current reference implementation uses summed proof difficulty as the primary scoring rule.

### Candidate Work Set Import

`TryImportCandidateStateAsync` validates the remote Work Set proofs, rebuilds expected payouts, and checks that the remote candidate state ID is internally consistent. For established local nodes, it does not replace the canonical reserve wholesale with the remote candidate. It merges admissible proofs forward.

It processes the remote candidate if:

- protocol/network version is compatible;
- proof counts are within configured limits;
- parent block context overlaps local accepted parent context;
- every imported proof validates;
- rebuilt payouts match the bundle;
- candidate state ID matches the validated proofs.

It then updates the canonical unpaid Work Set with:

- already-known local proofs;
- remote proofs that are already known locally by share ID;
- remote proofs whose parent is the local current Bitcoin tip.

Remote previous-parent proofs that were not already known locally are rejected from the canonical reserve after the local node has crossed the Bitcoin-block boundary. The node recomputes its own candidate state ID after merge.

### Active Snapshot Adoption

`TryAdoptCurrentStateAsync` validates a remote active payout snapshot and computes:

```text
remote_locked_total_difficulty = sum(validated_remote_snapshot_proof.difficulty)
local_locked_total_difficulty = sum(local_winners_list.difficulty)
```

It adopts the remote current state if:

- protocol/network version is compatible;
- round number is newer; or
- local state is empty/proofless and remote is proof-backed; or
- same round, local state is proofless, and remote is proof-backed.

Proofless fast-forward is allowed for newer states in limited bootstrap/upgrade cases.

## Why Sum Difficulty Is Attractive

For independent proof-of-work samples, difficulty is an estimate of expected work represented by that sample. If a share has difficulty `D`, then it is evidence of roughly `D` units of expected hash work. Difficulty is additive in the same intuitive way accumulated chainwork is additive.

Advantages:

- Uses all valid evidence.
- Rewards the state backed by the most observed work.
- Simple to explain.
- Simple to verify deterministically.
- Hard to game without doing actual proof-of-work.
- Similar in spirit to Bitcoin's accumulated-work rule, though over a bounded unordered proof set rather than a chain.

This makes summed difficulty a reasonable first implementation.

## Why Sum Difficulty Is Risky

Share difficulty is heavy-tailed. A single lucky share can be orders of magnitude larger than nearby shares. That creates two concerns:

1. A smaller team can sometimes look heavier because it found one monster share.
2. A strategic miner might reveal a high-difficulty private proof at a timing boundary to drag state selection toward a weaker team.

The first concern is ordinary estimator variance. The second is adversarial timing.

Summed difficulty is therefore unbiased but noisy. It may not be the best predictor of which side currently has more hashrate during a latency-driven split.

## Median Is Not A Drop-In Replacement

Median-based scoring is robust against one monster share, but it has different failure modes.

Advantages:

- Reduces sensitivity to the highest outlier.
- Better matches the browser hashrate estimator, which intentionally uses median rank-adjusted order statistics.
- May better predict hashrate when two lists have the same number of slots and one has an extreme lucky tail.

Risks:

- Throws away real accumulated-work evidence in the tails.
- Can underweight a genuinely stronger state that produced many high-difficulty proofs.
- May be easier to manipulate near the median rank if an attacker can selectively reveal proofs.
- Harder to explain as a consensus rule than accumulated work.

Median is excellent for display and estimation. It is not obviously better for consensus.

## Candidate Scoring Rules To Compare

The next simulation pass should compare at least these deterministic rules:

| Rule | Description | Expected Strength | Expected Weakness |
| --- | --- | --- | --- |
| `sum` | Sum all proof difficulties. | Uses all work; Bitcoin-like. | Sensitive to monster outliers. |
| `trimmed_sum` | Drop top and bottom `k%`, sum the middle. | Reduces tail dominance. | Throws away real work; needs parameter. |
| `winsorized_sum` | Cap top proofs at a percentile, then sum. | Keeps rank contribution while limiting outliers. | Parameterized; cap choice may be contentious. |
| `median_rank_hashrate` | Convert ranks to hashrate estimates and take median. | Robust estimator for current hashrate. | Less direct as an accumulated-work rule. |
| `log_sum` | Sum `log(1 + difficulty)`. | Strong outlier dampening. | No obvious work-conservation interpretation. |
| `likelihood_score` | Score proof set under a Poisson/order-statistic model for hashrate. | Statistically principled. | More complex; needs careful implementation and tests. |

## Simulation Questions

### Honest Latency Split

Two groups see shares in different order around a Bitcoin block boundary.

Measure:

- probability each scoring rule selects the side with more actual hashrate;
- expected work wasted before convergence;
- probability of flip-flopping between states;
- sensitivity to peer latency and share propagation delay.

### Monster Share Outlier

A smaller team has one extremely high-difficulty proof.

Measure:

- how often each scoring rule picks the smaller team;
- how large the outlier must be to dominate;
- whether reserve depth changes the result.

### Strategic Withholding / Reveal

A miner withholds valid high-difficulty proofs and reveals them near a snapshot boundary.

Measure:

- expected value impact;
- convergence disruption;
- whether the strategy is profitable or only griefing.

### Selective Relay / Censorship

A cartel excludes one miner's proofs and produces a competing state.

Measure:

- whether inclusive states are selected when at least one honest relay path exists;
- minimum honest relay connectivity needed;
- whether sum difficulty or robust scores better resist private-list games.

## Delayed-Snapshot Attack Economics

A delayed-snapshot attack is different from ordinary pool hopping or ordinary latency.

The attacker intentionally keeps mining templates on the old Bitcoin parent after honest nodes have already observed a new Bitcoin block and activated a payout snapshot. That stale work cannot win slot 0 or transaction fees on the real Bitcoin chain. The attacker's goal is to reveal extra stale-parent proofs later and convince peers to replace the active snapshot with a branch that contains more attacker-favorable payout slots.

This is economically similar to block withholding because the attacker burns valid mining opportunity during the stale window. The important caveat is that the cost scales with GridPool's share of total Bitcoin hashrate. The possible extra shared-slot reward must be discounted by survival probability: easy low-difficulty shares on a weak early list are unlikely to remain in the top shared slots by the time the team actually finds a GridPool block.

Focused simulations in `/home/keegreil/Documents/GitHub/gridpool-simulations/run_delayed_snapshot_attack.py` now use survival-discounted reward by default. With a mature `897`-proof reserve and a one-Bitcoin-block stale window:

| Pool Share Of Bitcoin Network | Attacker Share Of GridPool | Success | Reward/Cost | Expected Net BTC |
| ---: | ---: | ---: | ---: | ---: |
| `0.01%` | `51%` | `48.0%` | `4.25x` | `+0.00053` |
| `0.1%` | `51%` | `48.0%` | `2.14x` | `+0.00185` |
| `1%` | `51%` | `48.0%` | `0.94x` | `-0.00095` |
| `3%` | `51%` | `48.0%` | `0.50x` | `-0.02449` |
| `10%` | `51%` | `48.0%` | `0.18x` | `-0.13306` |

Interpretation:

- Survival discounting sharply reduces the apparent value of easy early slots. A stale branch with several extra low-difficulty active slots may have far less than one expected surviving payout slot.
- Once GridPool is a meaningful share of Bitcoin hashrate, intentionally mining stale templates is economically irrational for a profit-seeking miner in this model.
- During a tiny early beta, the same strategy can still look mildly profitable if nodes allow late stale-parent proofs to retroactively replace an established snapshot, but the edge is much smaller than the obsolete immediate-slot model suggested.
- The model is intentionally favorable to the attacker. It ignores detection, operator response, coordination failure, and explicit local receive-order finality.
- Therefore the correct V2 safety rule is not "the attack is always too expensive." The correct V2 safety rule is "late stale-parent proofs cannot rewrite a locally observed snapshot boundary."

## Snapshot Boundary And Merge Rule

For V2 packaged beta, the active payout snapshot should be local-boundary final:

- When a node observes a new Bitcoin block, it snapshots the valid unpaid Work Set it has accepted before that local observation.
- Proofs for the previous parent that arrive after this boundary may be logged or quarantined as alternate-branch evidence, but they must not alter the active snapshot for that Bitcoin height.
- Late previous-parent proofs should not enter the canonical future reserve by default. A peer can claim it found the proof before seeing the new block, but local nodes cannot verify that timing without trusting the peer's clock.
- Valid current-parent proofs mined against a retained divergent payout snapshot can be merged into the canonical future reserve, provided the full proof validates against its snapshot context.
- Candidate Work Set import should merge valid current-parent proofs for future snapshots, not use late stale-parent proofs to replace the current active payout snapshot.
- A newly bootstrapping node that lacks local receipt history may adopt a peer's current state, but established nodes must not be pulled backward through that bootstrap path.

This is a deliberate tradeoff. A valid share discovered milliseconds before a Bitcoin block may be missed by some peers and may not count everywhere. That is preferable to creating a deterministic majority-hashrate path for retroactive snapshot takeover.

This rule also changes the role of "heaviest list" selection. For established V2 nodes, ordinary split recovery should mostly be merge-based:

- same current parent and valid retained snapshot context: merge the proof into the unpaid reserve;
- old parent after local boundary: reject or quarantine from canonical reserve;
- valid GridPool block on chain: Bitcoin consensus decides the paid snapshot;
- newly bootstrapping or locally empty node: adopt a compatible proof-backed state from peers.

Under this model, a stale-work attacker gives up slot 0 and transaction fees for work that cannot enter the canonical future reserve. Mining honestly on the current parent gives the same chance to earn future shared slots while preserving the chance to win slot 0. That makes intentional stale mining economically dominated for profit-seeking miners.

## Current Recommendation

Do not package V2 until the snapshot-boundary rule is encoded in tests and verified against the implementation.

For honest same-boundary Work Set selection, simulation now favors the fixed reserve floor / lowest retained proof difficulty as the cleanest estimator of relative hashrate when the reserve is full. That is separate from the boundary rule above. Changing the candidate scoring rule from summed difficulty to reserve-floor comparison may be reasonable, but it does not by itself stop delayed stale-parent rewrites.

Short-term V2 launch should freeze only after:

- late stale-parent candidate/current-state replacement is rejected;
- same-snapshot Work Set merging remains valid and deterministic;
- the chosen candidate scoring rule is explicit and covered by regression tests;
- any later branch-market or multi-team redesign is treated as a future consensus-version bump.

## Tests To Add Before Packaging

- Competing candidate states where higher total difficulty wins.
- Competing candidate states where one monster share dominates, documenting current expected behavior.
- Equal difficulty tie-break test.
- Newer round beats older round test.
- Proofless local state loses to proof-backed same-round state.
- Incompatible consensus version refuses import.
- State bundle imported from an outbound-only peer converges with a public node.
- Late stale-parent proofs cannot rewrite an already-created active snapshot.
- Same-snapshot Work Sets merge or select deterministically without changing the active snapshot for the current Bitcoin height.

## Open Decisions

- Should candidate Work Set scoring compare full reserve floor, full reserve summed difficulty, or only the active snapshot slice?
- Should active snapshot scoring use only paid snapshot proof IDs or all retained proof context?
- Should support-fee output presence be part of state ID / payout variant tie-breaks?
- Should state tie-break use lexicographic ID, first-seen, lowest hash, or another deterministic rule?
- Should nodes expose both `totalDifficulty` and a robust `estimatedHashrateScore` for operator visibility?
