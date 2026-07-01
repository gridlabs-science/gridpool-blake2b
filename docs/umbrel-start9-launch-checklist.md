# Umbrel And Start9 Launch Checklist

Status: primary working checklist before packaging GridPool for broad one-click installs.

This checklist is intentionally stricter than "public beta works on my machine." Umbrel and Start9 users will expect upgrade stability, clear failure modes, and a node that can run unattended without needing frequent manual git pulls.

## Launch Gate Summary

- [ ] Consensus selection rule audited and frozen for the first packaged beta.
- [ ] Protocol and API versioning policy documented and enforced.
- [ ] External multi-node beta runs stably for at least 7 consecutive days.
- [ ] Hidden/outbound-only node behavior is clear, observable, and safe.
- [ ] Install and upgrade paths are repeatable on clean machines.
- [ ] Public docs and UI match V2 snapshot/reserve consensus.
- [ ] Monitoring catches the failures we have already seen in the wild.
- [ ] Repo is clean enough that outside contributors can orient quickly.

## G1: Consensus Selection And State Convergence

Goal: prove or revise the "heaviest valid state" rule before nontechnical users install nodes.

- [ ] Document the current state-selection rule in code-level detail.
- [ ] Decide whether current total-difficulty scoring remains the packaged beta rule.
- [ ] Coordinate and implement the proposed snapshot-boundary consensus revision:
  fixed-size Work Set scoring by 897th-proof difficulty, local snapshot-boundary finality, and merge-only compatible Work Sets.
- [ ] Compare candidate scoring methods in simulation:
  `sum difficulty`, `trimmed sum`, `winsorized sum`, `median rank-adjusted hashrate`, and a likelihood-style score.
- [ ] Model score behavior under honest latency splits.
- [ ] Model score behavior under one huge lucky share.
- [ ] Model score behavior under selective relay / censorship attempts.
- [ ] Define deterministic tie-breakers for equal-score states.
- [ ] Add consensus tests for every adopted scoring rule.
- [ ] Add a state-bundle fixture with two competing valid states where the expected winner is explicit.

Completion criteria:

- Current consensus scoring has a written rationale.
- At least 3 scoring alternatives have reproducible simulation output.
- A packaged node rejects incompatible consensus versions instead of silently importing them.
- Two live nodes converge after temporary divergence without manual state wipes.

### Proposed Snapshot-Boundary Consensus Revision

Status: design note only. Do not implement until coordinated with active beta
operators, because this is a consensus-breaking change.

Core idea:

- Treat the unpaid Work Set as a fixed-size top-`897` reserve.
- If a node has fewer than `897` proofs, missing slots score as difficulty `0`.
- Score reserve strength by the 897th-best proof difficulty, not summed
  difficulty. This turns the Work Set floor itself into the state-selection
  score and avoids monster-share and proof-count artifacts.
- Require that a claimed Work Set score be backed by the full ordered reserve:
  one 897th proof plus 896 proofs with equal or greater difficulty.

Snapshot and Work Set objects must be treated differently:

- Active payout snapshot: the frozen coinbase payout list miners are currently
  hashing against. It is created when a node observes the Bitcoin trigger block.
- Unpaid Work Set: the bounded reserve of valid unpaid proofs. It continues to
  evolve, but it must not retroactively change which active snapshot was valid
  at an earlier Bitcoin trigger boundary.

Consensus invariants to preserve:

- Each node's Bitcoin-block snapshot boundary is local and final.
- Peer timestamps are not trusted for deciding whether a share arrived before a
  Bitcoin trigger block.
- Shares first learned after a node has crossed its local trigger boundary must
  not strengthen that node's already-created snapshot for that trigger.
- A proposed stronger snapshot may only be evaluated using frozen
  snapshot-boundary evidence, not work accumulated later on that branch.
- Post-split work must never be allowed to decide canonicality of the
  pre-split snapshot. Otherwise a large miner could keep mining a favorable
  branch and turn snapshot convergence into a 51%-style branch-coercion attack.
- Work Sets are mergeable only when their active payout snapshot lineage is
  compatible.
- If two peers share the same active snapshot, Work Set sync should be union and
  trim: validate all proofs, deduplicate, sort by difficulty descending with a
  deterministic share-ID tie-break, keep top `897`, and recompute candidate
  preview.
- If two peers have different active snapshots, do not merge live Work Sets as
  one list. Resolve the active snapshot conflict using frozen snapshot-boundary
  reserve evidence only.
- If a node receives a last-millisecond share that most peers did not see before
  the trigger boundary, that node may temporarily fork onto a minority snapshot.
  Its economically rational behavior is to abandon the minority branch and
  rejoin the common active snapshot, but it must not bring fork-only work back as
  evidence for the older boundary.

Implementation checklist for the revision:

- [ ] Add explicit consensus version bump before deploying.
- [ ] Add snapshot context fields for the frozen reserve evidence required to
  score active snapshot conflicts, not just paid proof IDs.
- [ ] Replace active snapshot conflict scoring from summed difficulty to
  frozen 897th-proof reserve floor.
- [ ] Replace same-snapshot candidate Work Set import from either-or adoption to
  deterministic union/merge/trim.
- [ ] Reject or quarantine incompatible-lineage Work Set proofs rather than
  merging them into the canonical reserve.
- [ ] Keep late old-parent proofs out of snapshot fork-choice scoring after the
  local Bitcoin trigger boundary.
- [ ] Add diagnostics for ignored late-boundary proofs and minority-branch
  abandonment so operators can see boundary losses.
- [ ] Add tests for local-boundary finality, same-snapshot Work Set merge,
  incompatible-lineage non-merge, delayed old-parent share rejection for fork
  choice, and fixed-size `897` scoring with missing slots as zero.

## G2: Protocol And Release Versioning

Goal: stop relying on "everyone pull latest" before packaged installs exist.

- [ ] Define separate versions for consensus rules, state bundle schema, peer transport, HTTP API, and node release.
- [ ] Add those version fields to `/api/network/summary`.
- [ ] Add those version fields to state bundles and peer session hellos.
- [ ] Define compatibility behavior for each version class.
- [ ] Define hard-fork style behavior for consensus version changes.
- [ ] Define capability negotiation for transport features such as WebSocket and UDP relay.
- [ ] Add a visible UI warning when peers are unreachable because of version mismatch.
- [ ] Add health-monitor alerts for version mismatch.

Completion criteria:

- A node on incompatible consensus version refuses sync and says why.
- A node on older transport version can still use canonical HTTP fallback when consensus-compatible.
- Release notes have an explicit "requires coordinated upgrade" marker when needed.

## G3: External Beta Stability

Goal: make the current beta boring before inviting one-click installers.

- [ ] Maintain at least 2 independently operated mainnet beta nodes for 7 consecutive days.
- [ ] Maintain at least 1 testnet4 node for real GridPool-block trigger testing.
- [ ] Track DATUM acceptance rate by node and by rejection reason.
- [ ] Track peer relay success/failure by transport.
- [ ] Track state ID convergence across nodes.
- [ ] Track Work Set count, active snapshot ID, and candidate state ID drift.
- [ ] Track DATUM session churn.
- [ ] Track real quickdiff submissions after the quickdiff reconstruction fix.

Completion criteria:

- No unexplained payout mismatch bursts for 7 days.
- No unexplained state divergence lasting more than 10 minutes.
- DATUM share acceptance is above 95% after excluding clearly invalid solo fallback or firmware-truncated templates.
- Any lower acceptance rate has a documented root cause and mitigation.
- External tester can upgrade from a previous beta release without wiping state.

## G4: Networking And NAT Readiness

Goal: home miners should not need to understand router internals to participate safely.

- [ ] Make outbound-only peers first-class in UI and API.
- [ ] Finish seed-mediated relay between hidden peers.
- [ ] Decide whether seed relay is acceptable for the first packaged beta.
- [ ] Add reachability self-test for public endpoint, peer WebSocket, and UDP relay.
- [ ] Research and prototype UPnP, NAT-PMP, and PCP port mapping.
- [ ] Research UDP hole punching with public seeds as rendezvous.
- [ ] Add clear docs for direct public peer, outbound-only peer, and relay-fallback peer modes.
- [ ] Ensure hidden peers are never advertised as dialable endpoints.
- [ ] Add metrics for relay dependency: number of peers reached directly vs through seed relay.

Completion criteria:

- A fresh home node behind NAT appears in the peer list as outbound-only within 30 seconds.
- That node receives shares from public peers without manual port forwarding.
- If automatic port mapping succeeds, the node verifies its own public reachability.
- If automatic port mapping fails, the UI says the node is still participating outbound-only.

## G5: Packaging And Installer Readiness

Goal: make installation boring and reversible.

- [ ] Decide package architecture for Umbrel.
- [ ] Decide package architecture for Start9.
- [ ] Confirm whether Start9 package should live in this repo or a separate wrapper repo.
- [ ] Provide Docker image tags for stable beta releases.
- [ ] Provide sample configs for mainnet and testnet4.
- [ ] Provide safe default ports for UI, DATUM, peer HTTP, peer WebSocket, and UDP relay.
- [ ] Provide migration scripts for state files.
- [ ] Provide backup and restore docs for node identity keys and pool state.
- [ ] Test fresh install on a clean Linux VM.
- [ ] Test fresh install on Raspberry Pi 5 or equivalent ARM64 host.
- [ ] Test upgrade from previous release without wiping state.
- [ ] Test uninstall leaves keys/state backed up or clearly prompts before deletion.

Completion criteria:

- Fresh install reaches the mainnet beta seed and syncs state.
- Fresh install shows correct DATUM connection info.
- Upgrade preserves node identity and state.
- Package logs are visible in the platform UI or documented shell path.

## G6: Repo And Project Architecture

Goal: avoid turning the reference node into a junk drawer for every future adapter.

- [ ] Write a target repo map for the GridPool ecosystem.
- [ ] Keep `gridpool-web` separate from node code.
- [ ] Keep `gridpool-simulations` separate from node code.
- [ ] Decide whether to create `gridpool-spec` for protocol docs and test vectors.
- [ ] Decide whether adapters belong in this repo or separate repos.
- [ ] Move old soak logs and historical test artifacts into `docs/archive/` or out of the repo.
- [ ] Archive stale V1-era planning docs rather than deleting useful history.
- [ ] Update README to point to the current docs and hide obsolete rabbit holes.
- [ ] Ensure public docs use "GridPool" language consistently.

Completion criteria:

- A new contributor can read README and know which docs are current.
- Old V1 plans are clearly marked archived.
- Generated logs/state files are not tracked.
- Spec, node, web, and simulation responsibilities are clear.

## G7: Public Docs, Website, And UI

Goal: public-facing language must match V2 consensus and current operational reality.

- [ ] Replace or remove the V1-era intro video.
- [ ] Update website FAQ for pool hopping, payout snapshots, block withholding, firmware coinbase limits, and outbound-only nodes.
- [ ] Update node UI terminology:
  `active payout snapshot`, `unpaid Work Set`, `current shared payout slots`, and `local DATUM hashrate`.
- [ ] Remove or rename V1 leftovers in Nerd Mode.
- [ ] Add a clear testnet/mainnet visual banner.
- [ ] Add current consensus version and network ID to Nerd Mode.
- [ ] Add a concise "What happens if we find a block?" explanation.
- [ ] Keep "The pool for cypherpunks" but make the technical explanation precise.

Completion criteria:

- A user can tell whether they are looking at mainnet or testnet in under 3 seconds.
- The UI does not imply that every Bitcoin block is a paid GridPool round.
- Public docs explain why raw Stratum V1 is not native unless using a gateway.
- Website and README describe V2 snapshot/reserve consensus.

## G8: Security, Abuse, And Operational Monitoring

Goal: keep failures visible and bounded.

- [ ] Keep duplicate share suppression tested.
- [ ] Keep firmware coinbase truncation detection tested.
- [ ] Keep DATUM quickdiff reconstruction tested or covered by integration fixture.
- [ ] Add malformed peer bundle tests.
- [ ] Add rate limits for low-difficulty peer spam.
- [ ] Add rate limits for state bundle fetches.
- [ ] Add health-monitor checks for GridPool block found, service down, peer divergence, relay failure spikes, and DATUM acceptance drops.
- [ ] Add alert suppression so ordinary snapshots do not page the operator.
- [ ] Add a dashboard or report for top rejection categories.

Completion criteria:

- Health monitor pages only on actual action-worthy events.
- Known invalid input classes are categorized, not generic "payout mismatch."
- A peer cannot force unbounded CPU or storage by sending low-value proofs.

## G9: Release Decision

Before Umbrel/Start9 launch, answer yes to all:

- [ ] Do we know the current consensus scoring rule is good enough for packaged beta?
- [ ] Can incompatible nodes fail safely?
- [ ] Can an outbound-only home node participate usefully?
- [ ] Can a fresh install sync without handholding?
- [ ] Can a nontechnical user recover from restart/power loss?
- [ ] Are public docs accurate enough that users will not connect unsupported firmware and blame the protocol?
- [ ] Have at least two external operators run nodes successfully?

If any answer is no, keep the project in public beta and avoid one-click platform launch.
