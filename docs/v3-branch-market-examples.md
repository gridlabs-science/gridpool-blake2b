# V3 Branch Market Examples

Status: exploratory V3 design note. This is not current GridPool consensus.

Purpose: make the "many branches as feature" idea concrete enough to critique,
model, and eventually explain. The core question is whether GridPool should stop
treating every payout-list split as a failure and instead treat competing payout
branches as a market of mining teams.

## Core Reframe

Current intuition:

```text
All honest miners should converge onto the strongest valid payout snapshot.
```

Possible V3 intuition:

```text
Miners can choose among many valid payout teams. A split is not automatically a
failure. It is a branch market.
```

This matters because an unconditional "strongest team wins" rule can become a
majority-hashpower coercion rule. A large miner can create a favorable branch,
keep adding work to it, and make it look increasingly attractive to new miners.

Branch Market Mode would instead make branch choice a local mining policy:

- Is this branch valid?
- Does it include my proofs?
- What is its estimated block cadence?
- What is my expected payout variance on it?
- Does it censor me or other miners?
- Is it cheap enough to track and relay?

There may be no single canonical answer.

## Example 1: Same Snapshot, Different Work Sets

Two nodes are mining the same active payout snapshot `S`.

- Node A has Work Set proofs `{a, b, c, d}`.
- Node B has Work Set proofs `{a, b, c, e}`.
- All proofs were mined against the same active snapshot lineage.

This is not a fork-choice problem.

Expected behavior:

1. Validate all proofs.
2. Deduplicate by share ID.
3. Merge into `{a, b, c, d, e}`.
4. Sort by difficulty.
5. Keep the top reserve slice.

There is no reason for A or B to "win." They are compatible views of the same
team.

## Example 2: Last-Millisecond Snapshot Split

The team is synced on snapshot `S0`. A Bitcoin block arrives.

- Most nodes see the Bitcoin block first and snapshot `S1`.
- One node sees a strong share first, then the Bitcoin block, and snapshots
  `S1-alt`.

In current V2 thinking this is a dangerous split because later branch work
must not retroactively decide which snapshot was "really" correct.

In Branch Market Mode, both branches may be valid local histories:

- `S1` is valid for nodes that did not see the late share before their local
  boundary.
- `S1-alt` is valid for the node that did see it.

The late share may not be globally creditable. There is no trusted global clock
and no sharechain proving pre-block ordering.

Expected behavior:

- Existing nodes keep their local snapshot-boundary finality.
- Branches are tracked as distinct teams if worth tracking.
- Later work on `S1-alt` does not force `S1` nodes to rewrite history.
- A miner may choose to mine either branch based on local policy.

This gives up perfect last-millisecond fairness in exchange for avoiding
majority-hashpower branch coercion.

## Example 3: Large Miner Creates A Favorable Branch

An attacker has 60% of GridPool-visible hashrate.

They create a branch `A` whose payout snapshot favors them, while the rest of
the network mines branch `H`.

Old convergence intuition:

```text
If A accumulates more work, everyone should eventually follow A.
```

Branch Market intuition:

```text
A is a valid team if its proofs validate. H is also a valid team. Miners are not
forced to accept A merely because A has more current branch work.
```

Possible outcomes:

- Miners excluded by `A` keep mining `H`.
- New miners who only care about near-term block cadence may choose `A`.
- New miners who care about inclusion, censorship resistance, or lower branch
  dominance may choose `H`.
- The attacker has not captured GridPool consensus. They have created a
  competing team.

This turns a 51%-style coercion attack into an expensive act of team formation.
It may still be disruptive, but it is not automatically protocol death.

## Example 4: Bitaxe Miner Chooses A Small Branch

A Bitaxe miner has tiny hashrate relative to the largest GridPool branch.

Observed branches:

| Branch | Estimated Team Hashrate | Miner Position | Expected Feeling |
| --- | ---: | ---: | --- |
| Big | 300 PH/s | Almost never visible | Low variance as a team, but little personal feedback |
| Mid | 30 PH/s | Occasionally visible | Better chance to see own shares |
| Micro | 300 TH/s | Can sometimes hold one slot | Rare team blocks, but tangible participation |

Expected BTC is not created by choosing a smaller branch. But psychology and
variance cadence may differ.

If the miner can hold about one payout slot on a smaller branch, that may be a
more compelling experience than being statistically invisible on a giant branch.
This is not obviously irrational. It may be the natural GridPool equivalent of
choosing a table size in poker.

Research question:

```text
For a given miner size, what branch hashrate and payout-list depth minimize the
miner user's perceived variance while preserving acceptable block cadence?
```

## Example 5: One PH Miner Chooses Between Branches

A 1 PH/s miner sees two honest branches:

- Branch Big: 600 PH/s, roughly twice as many blocks as a 300 PH/s branch.
- Branch Medium: 300 PH/s, half as many blocks as Big.

If the payout list has fixed shared slots, the miner's per-block share count is
roughly proportional to its fraction of branch hashrate. Big pays more often but
with fewer expected slots per block. Medium pays less often but with more
expected slots per block.

For expected BTC, those effects largely cancel if all else is equal.

That means the "always mine the biggest branch" rule is not obviously
economically mandatory once a branch is large enough to have tolerable payout
cadence.

Research questions:

- Where is the minimum practical branch size for a business miner?
- Where is the minimum practical branch size for a lotto miner?
- Does fee variance or transaction-fee policy change the preferred branch?
- Does a miner prefer one high-cadence branch or several lower-cadence branches
  where it has more visible representation?

## Example 6: Censorship Branch

A large miner creates branch `C` and refuses to include proofs from a disfavored
address.

Under single-canonical convergence, this is scary if `C` can become the
"strongest" branch.

Under Branch Market Mode:

- `C` can exist.
- The censored miner's node can advertise branch `U`, which includes its proof.
- Other miners can inspect both branches.
- Miners who dislike censorship can mine `U`.
- Miners who only chase short-term cadence may mine `C`.

This does not magically eliminate censorship pressure. It makes censorship
visible as branch policy and avoids making the censor's branch automatically
canonical.

Research question:

```text
When does an inclusive branch survive against a higher-hashrate censoring
branch, and what UI/relay policy helps miners find it?
```

## Example 7: Multi-Branch Optionality

A miner submits strong proofs that appear in several compatible or near-compatible
branches. Later, the miner chooses to hash only on one of them.

This creates optionality:

- The miner has payout claims on multiple possible teams.
- The miner spends current hashrate on only one team.

This may be fine if all miners have the same opportunity. It may be harmful if
only highly connected miners can get broad branch inclusion.

Research questions:

- Does multi-branch optionality create persistent expected-value advantage?
- Is the advantage mostly bandwidth/latency-driven?
- Does it favor large miners or well-connected public nodes?
- Can compact relay and dense peering make the opportunity broadly available?

## Example 8: UI View For Normal Users

Branch Market Mode should not dump raw branch chaos on casual users.

Lottery view could say:

```text
Your address is currently represented on 3 GridPool teams.
Best active chance: Team Marble, 1 slot, estimated block cadence 11 days.
Other teams: Team Slate, Team Ash.
Recommended: mine Team Marble.
```

Business view could say:

```text
You are represented on 5 teams.
Recommended branch: Team Slate.
Reason: strongest branch that includes your address and has stable peers.
Expected payout cadence: 17 days median, 46 days at 95%.
```

Nerd view can expose:

- branch IDs
- parent lineage
- active snapshot IDs
- reserve floors
- estimated hashrate
- peer support
- censorship/inclusion differences
- memory/bandwidth cost per branch

The UI challenge is severe, but not fatal if ordinary users see a synthesized
recommendation and advanced users can inspect the branch market.

## Hard Constraints

Branch Market Mode is not viable if it requires server-grade hardware for
ordinary miners.

Consumer-node targets to model:

- Raspberry Pi 5 / Umbrel / Start9 class should remain viable.
- GridPool RAM target should ideally stay below roughly `512 MB` in normal mode.
- Disk writes should stay low enough for SD-card or consumer SSD deployments.
- Normal bandwidth should remain modest enough for home internet connections.
- Public seed nodes may track more branches than home nodes, but home nodes
  should not depend on trusting seed branch choice.

Potential shortcuts:

- store full proof blobs once by share ID
- represent branches by proof-ID sets or compact deltas
- validate/fetch full branch proofs lazily
- keep full data only for active and near-recommended branches
- gossip summaries for low-ranked branches
- prune branches below viability thresholds

## Open Modeling Questions

- How many branches naturally survive under simple miner branch-choice policies?
- Does branch fragmentation improve censorship resistance without destroying
  payout cadence?
- Is a miner's economically ideal branch one where it expects to hold about one
  payout slot?
- Does multi-branch optionality create a real expected-value exploit?
- What are the memory, disk, and bandwidth costs at `10`, `50`, `100`, and
  `250` branches?
- How much proof overlap exists between real branches after ordinary latency
  splits?
- Can a censoring branch with majority hashrate pull in rational miners, or does
  inclusion preference keep alternatives alive?

## Current Takeaway

Branch Market Mode is a serious V3 research path. It is very aligned with
GridPool's core philosophy: take the thing traditional mining systems avoid and
turn it into an advantage.

Do not implement yet. First, model resource scaling and multi-branch incentives.
