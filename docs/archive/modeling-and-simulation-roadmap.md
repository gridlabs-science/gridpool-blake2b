# GridPool Modeling And Simulation Roadmap

Status: draft.

Purpose: define the rigorous, open-source modeling work needed to evaluate
GridPool's fairness, incentive compatibility, network dynamics, bandwidth, and
attack resistance.

This is not a marketing checklist. The goal is to make GridPool legible to
technically competent critics by publishing reproducible models, assumptions,
source code, and results.

## Guiding Principles

1. Model adversaries explicitly.
2. Separate protocol claims from current implementation performance.
3. Use deterministic seeds so every graph and table can be reproduced.
4. Compare against relevant baselines: solo mining, centralized PPLNS/FPPS-style
   pools, and sharechain-style decentralized pools.
5. Publish failures. A model that finds an edge-case weakness is useful.
6. Keep simulations parameterized so assumptions can be challenged.

## Core Mathematical Model

Mining can be modeled as a Poisson process.

For a miner with hashrate `h`, the expected rate of shares above difficulty `d`
is approximately:

```text
lambda(d) = h / (d * 2^32)
```

This gives a natural simulation basis:

- block arrivals are Poisson events at network difficulty
- share arrivals above an admission floor are Poisson events
- share difficulty has a heavy-tailed distribution
- top-k Work Set selection can be modeled through order statistics
- payout snapshots are deterministic functions of the ranked unpaid Work Set

The heavy tail matters. A miner can occasionally produce a share far above its
typical contribution. The modeling suite must account for that directly rather
than smoothing everything into normal distributions.

## Model A: Honest Baseline Fairness

Question:

Does GridPool pay miners in proportion to contributed hashrate over time under
honest continuous mining?

Inputs:

- number of miners
- miner hashrate distribution
- network difficulty
- transaction fee assumptions
- payout slot count
- support slot on/off
- reserve depth
- Work Set admission floor
- simulation duration

Outputs:

- expected payout by miner
- variance by miner
- time-to-first-payout distribution
- slot occupancy distribution
- realized payout / expected payout ratio
- comparison to solo variance
- comparison to idealized PPLNS

Initial pass criteria:

- long-run mean payout converges near hashrate share after accounting for slot 0,
  transaction fees, and support slot settings
- variance reduction approaches the expected slot-count behavior for miners large
  enough to appear in snapshots regularly
- small miners show lottery-like behavior without systematic bias

## Model B: Pool-Hopping Strategies

Question:

Can a miner improve expected value by mining GridPool only until it earns a high
difficulty proof or payout slots, then leaving?

Strategies to simulate:

- always mine GridPool
- leave after first snapshot slot
- leave after `N` snapshot slots
- leave after one outlier proof above threshold `X`
- join only after fee spikes
- split hashrate between GridPool and another pool
- switch to FPPS/PPLNS after a lucky GridPool streak
- intermittent solar/off-grid miner with random uptime

Metrics:

- expected value
- variance
- downside tail risk
- harm or benefit to remaining miners
- average time old proofs stay payable after miner exits
- reserve displacement time after exit

Expected hypothesis:

Leaving after a lucky proof should not create positive expected value relative
to continuous mining, because the miner gives up future proof production and
slot-0 block-finder upside. But this must be demonstrated, not asserted.

Important distinction:

A miner may rationally prefer another pool because of lower variance, different
fees, operational simplicity, or liquidity needs. That is not the same as a
pool-hopping exploit.

## Model C: Majority Miner And Intentional Forks

Question:

What happens when a miner or cartel controls a large fraction of GridPool
hashrate and refuses to relay or accept other miners' proofs?

Adversary configurations:

- 51 percent
- 67 percent
- 75 percent
- 90 percent
- 99 percent

Strategies:

- reject one target address
- reject all non-cartel shares
- build a private Work Set
- reveal a heavier private state only near block events
- relay selectively to partition peers
- mine honestly but delay proof relay

Metrics:

- probability of durable team split
- attacker expected value
- victim expected value
- honest miner convergence time
- percentage of work wasted on minority snapshots
- conditions under which honest miners should switch teams

Questions to answer:

- Is the attack profitable, or just griefing?
- Can a majority miner steal, or only split?
- Does the "heaviest valid payout state" rule converge under selective relay?
- How much honest relay connectivity is required to defeat targeted censorship?

## Model D: Block Withholding

Question:

Does GridPool materially reduce or remove the classic pool-layer block
withholding incentive?

Strategies:

- honest mining
- withhold valid Bitcoin blocks while still submitting non-block shares
- withhold only when attacker has low snapshot share
- withhold only when attacker has high snapshot share
- withhold to grief the team rather than maximize profit

Cost model:

- forfeited slot-0 reward
- forfeited transaction fees
- forfeited attacker's own snapshot payouts
- delayed or reduced team block rate
- effect on attacker's future snapshot share

Metrics:

- attacker expected value
- honest miner expected value
- break-even conditions
- griefing cost ratio
- detectability signals, such as high share contribution with suspiciously low
  block-finder rate over long windows

Expected hypothesis:

Because the finder receives slot 0 plus fees directly, withholding a real block
is expensive. GridPool may be more resistant than centralized pools where the
attacker can earn pool shares while hiding blocks. This should be modeled with
actual fee and slot assumptions.

## Model E: Network Latency And Snapshot Splits

Question:

How often do nodes disagree on the active payout snapshot, and how much work is
lost before convergence?

Network parameters:

- node count
- peer degree
- graph topology
- propagation delay distribution
- packet loss
- peer churn
- node outage/rejoin behavior
- compact relay enabled/disabled
- UDP relay enabled/disabled

Event parameters:

- Bitcoin block rate
- share arrival rate
- shares arriving near Bitcoin block boundaries
- simultaneous or near-simultaneous snapshots

Metrics:

- snapshot divergence frequency
- divergence duration p50/p95/p99
- percent of hashrate mining minority snapshot
- stale or rejected share burst size
- convergence time after peer outage
- benefit of compact relay versus JSON relay
- benefit of UDP relay versus WebSocket/HTTP relay

Comparison target:

This model should explicitly compare GridPool's latency sensitivity with
P2Pool-style sharechains. P2Pool v1 suffered because 30-second share blocks made
propagation latency economically meaningful. GridPool snapshots occur on Bitcoin
block cadence, but late high-difficulty shares around snapshot boundaries can
still create team splits. The model should quantify that difference.

## Model F: Bandwidth And Storage Scaling

Question:

Can GridPool scale to many miners and many nodes without turning public seed
nodes into central infrastructure?

Parameters:

- peer count
- bounded peer degree
- share proof size
- reserve depth
- snapshot context retention
- accepted Work Set share rate
- rejected share rate
- state-bundle fetch frequency
- peer polling interval

Metrics:

- bytes/sec per node
- bytes/sec at seed nodes
- CPU time per proof validation
- memory footprint
- disk writes per hour
- state bundle size
- rejoin sync time after outage

Scenarios:

- home miner with outbound-only connectivity
- public seed node
- regional VPS node
- hostile peer sending low-difficulty spam
- 2500 DATUM clients connected to one GridPool node
- 100 independent GridPool peers with bounded degree

Pass criteria should be tied to concrete launch targets from
`docs/stress-test-plan.md`.

## Model G: DoS And Low-Difficulty Spam

Question:

Can proof-of-work itself be used as an anti-DoS primitive without rejecting
honest miners during low-hashrate bootstrap conditions?

Strategies:

- spam invalid shares
- spam valid but too-low-difficulty shares
- spam duplicate valid shares
- repeatedly fetch state bundles
- peer identity churn
- reconnect storm

Mitigations to test:

- admission floor hints
- disconnect peers that ignore floor hints repeatedly
- cheap pre-validation before expensive validation
- per-peer and per-IP rate limits
- proof-of-work attached to non-share messages
- bounded state bundle fetches

Outputs:

- attack cost per accepted byte
- node CPU per rejected request
- false positive rate against honest weak miners
- recovery time after attack stops

## Model H: Coinbase Size Compatibility Variants

Question:

Can firmware with smaller coinbase limits participate without splitting into
many separate subpools or breaking incentives?

Candidate mechanisms:

- deterministic coverage variants
- coverage-weighted effective difficulty
- minimum coverage threshold for GridPool block validity
- deterministic subset selection from active snapshot
- smaller-team compatibility tiers

Adversarial tests:

- miner chooses small payout set to favor itself
- miner omits low-ranked recipients
- miner alternates between full and partial coverage based on private advantage
- miner griefs by finding blocks that underpay the team

This is not a launch blocker for the strict 300-slot beta, but it is a major
future adoption problem and deserves its own model.

## Suggested Repository Structure

Future checked-in simulation work should live in a dedicated directory:

```text
simulations/
  README.md
  requirements.txt or pyproject.toml
  gridpool_sim/
    mining.py
    workset.py
    snapshots.py
    strategies.py
    network.py
    adversaries.py
  scenarios/
    honest_baseline.yaml
    pool_hopping.yaml
    majority_censor.yaml
    latency_splits.yaml
  reports/
    .gitkeep
```

Python is probably the fastest path for research because of NumPy, pandas, and
plotting libraries. Critical invariants can later be ported into .NET property
tests for the reference implementation.

## First Three Deliverables

### Deliverable 1: Honest Baseline Notebook/Script

Goal:

Show that honest miners converge to expected payout share under V2 snapshot and
reserve rules.

Output:

- CSV
- plots
- markdown report
- deterministic seed

### Deliverable 2: Pool-Hopping Report

Goal:

Directly answer the strongest current critique: "Can I earn slots, leave, and
make other miners pay me?"

Output:

- strategy comparison table
- expected value table
- variance table
- sensitivity analysis over reserve depth and team hashrate
- plain-English conclusion with caveats

### Deliverable 3: Latency And Team-Split Report

Goal:

Quantify snapshot disagreement windows and the value of compact/UDP relay.

Output:

- divergence frequency
- divergence duration
- lost-work percentage
- bandwidth comparison by relay mode
- recommended peer degree and relay settings

## Definition Of Done For A Claim

A GridPool claim is ready for serious public use only when it has:

1. a precise statement
2. explicit assumptions
3. model source code
4. reproducible seed/config
5. generated report
6. caveats and known failure modes

Example:

Bad:

> GridPool is immune to pool hopping.

Better:

> Under the published Poisson mining model, with V2 snapshot rules, fixed reserve
> depth `3x`, no external fee variation, and honest relay, the tested
> pool-hopping strategies did not outperform continuous mining in expected value.
> They changed variance and liquidity exposure. See report X.

