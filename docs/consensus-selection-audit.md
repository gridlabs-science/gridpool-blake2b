# Consensus Selection Audit

Status: draft.

Purpose: document how GridPool currently chooses between competing valid states, evaluate whether that rule is mathematically defensible, and define the next simulations/tests needed before Umbrel and Start9 packaging.

## Context

GridPool does not use a sharechain. Nodes maintain bounded current state:

- active payout snapshot, represented internally as `WinnersList`;
- unpaid Work Set reserve, represented internally as `OnDeckProofs`;
- top shared payout preview, represented internally as `OnDeckList`;
- retained payout snapshot contexts required to validate proofs mined against older snapshots.

When two peers disagree, the intended rule is "adopt the heaviest valid payout state." A state is not accepted by reputation. It must be backed by proof-of-work shares whose headers, merkle roots, payout snapshot outputs, slot-0 attribution, parent context, and duplicate status all validate.

The key open question is how to score "heaviest."

## Current Implementation

The current reference implementation uses summed proof difficulty as the primary scoring rule.

### Candidate Work Set Import

`TryImportCandidateStateAsync` validates the remote Work Set proofs, rebuilds expected payouts, checks the candidate state ID, then computes:

```text
remote_total_difficulty = sum(validated_remote_work_set_proof.difficulty)
local_total_difficulty = sum(local_unpaid_work_set_proof.difficulty)
```

It imports the remote candidate if:

- protocol/network version is compatible;
- proof counts are within configured limits;
- parent block context overlaps local accepted parent context;
- every imported proof validates;
- rebuilt payouts match the bundle;
- candidate state ID matches the validated proofs;
- `remote_total_difficulty > local_total_difficulty`, unless the candidate ID already matches local state.

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
- same round and remote total difficulty is greater; or
- same round, total difficulty ties within epsilon, and the remote state ID wins lexicographic tie-break.

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

## Current Recommendation

Do not change the consensus rule yet.

Keep summed difficulty as the live beta rule while we model alternatives. It is simple, deterministic, and has the clearest proof-of-work interpretation. Switching to median without simulation could fix one aesthetic concern while creating a subtler consensus problem.

However, do not package GridPool for Umbrel/Start9 until the state-selection simulation pass is complete and the chosen rule is frozen behind an explicit consensus version.

## Tests To Add Before Packaging

- Competing candidate states where higher total difficulty wins.
- Competing candidate states where one monster share dominates, documenting current expected behavior.
- Equal difficulty tie-break test.
- Newer round beats older round test.
- Proofless local state loses to proof-backed same-round state.
- Incompatible consensus version refuses import.
- State bundle imported from an outbound-only peer converges with a public node.

## Open Decisions

- Should candidate Work Set scoring compare full reserve depth or only the active snapshot slice?
- Should active snapshot scoring use only paid snapshot proof IDs or all retained proof context?
- Should support-fee output presence be part of state ID / payout variant tie-breaks?
- Should state tie-break use lexicographic ID, first-seen, lowest hash, or another deterministic rule?
- Should nodes expose both `totalDifficulty` and a robust `estimatedHashrateScore` for operator visibility?
