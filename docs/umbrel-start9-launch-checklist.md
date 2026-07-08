# Umbrel And Start9 Launch Checklist

Status: primary working checklist before packaging GridPool for broad one-click installs.

This checklist is intentionally stricter than "public beta works on my machine." Umbrel and Start9 users will expect upgrade stability, clear failure modes, and a node that can run unattended without needing frequent manual git pulls.

## Launch Gate Summary

- [x] Consensus selection rule audited and frozen for the first packaged beta. V2.1 boundary/merge behavior is documented and regression-tested; rollout still requires coordination.
- [x] Protocol/API/state/peer version fields and compatibility checks are implemented.
- [ ] External multi-node beta runs stably for at least 7 consecutive days. Monitoring is implemented; the clock still needs to run.
- [ ] Hidden/outbound-only node behavior is clear, observable, and safe.
- [ ] Install and upgrade paths are repeatable on clean machines.
- [ ] Public docs and UI match V2.1 snapshot/reserve consensus.
- [x] Monitoring catches the failure classes already seen in public beta.
- [ ] Repo is clean enough that outside contributors can orient quickly.

## G1: Consensus Selection And State Convergence

Goal: keep V2.1 snapshot/reserve consensus predictable and safe enough for a short-term packaged beta. This gate is not trying to solve the full V3 branch-market / adversarial fork-choice research problem.

Current V2.1 posture:

- Work Set and active snapshot proofs must remain fully validated before import.
- Incompatible consensus or state-bundle schema versions must fail visibly and safely.
- Established nodes merge valid current-parent proofs from retained divergent snapshot contexts instead of replacing active snapshots wholesale.
- A node must not rewrite an already-observed Bitcoin-block payout snapshot using proofs that first arrive after that node's local snapshot boundary. Late previous-parent proofs should be rejected or quarantined from the canonical future reserve. Valid current-parent proofs from divergent retained snapshot contexts should merge forward into the unpaid reserve.
- "Heaviest state" adoption is limited to bootstrap/proofless recovery and future explicit consensus-version changes, not ordinary same-round active snapshot replacement.
- Any deeper change to fork choice, boundary finality, or multi-branch markets must be a coordinated future consensus-version bump.

Deferred research is already tracked in:

- [consensus-selection-audit.md](consensus-selection-audit.md)
- [v3-branch-market-examples.md](v3-branch-market-examples.md)
- [simulation-findings-2026-06.md](simulation-findings-2026-06.md)

Short-term V2.1 checklist:

- [x] Document the current boundary/merge state-convergence rule in code-level detail.
- [x] Implement explicit consensus, state-bundle schema, HTTP API, peer transport, UDP relay, and release version fields.
- [x] Reject incompatible consensus/state-bundle schema versions before import.
- [x] Preserve HTTP fallback when only peer transport version differs.
- [x] Add visible UI/API/monitor visibility for version mismatch.
- [x] Add compact V2.1 state-convergence regression coverage for the two important cases: reject late previous-parent proofs and merge valid current-parent divergent proofs.
- [x] Add a delayed-snapshot regression fixture: after a local node observes a Bitcoin block and activates a snapshot, a peer bundle with extra stale-parent proofs from the previous parent must not replace the active snapshot or enter the canonical future reserve.
- [x] Add a split-recovery regression fixture: two nodes on the same current Bitcoin parent but different payout snapshot contexts should merge fully valid current-parent proofs into the unpaid reserve and converge at the next snapshot.
- [ ] Run a multi-node public beta soak and confirm current/candidate state IDs converge without manual state wipes.
- [x] Decide whether V2.1 state selection plus non-retroactive snapshot boundaries is "good enough for beta" or whether package launch waits for a coordinated V3 rule.
- [ ] Coordinate V2.1 rollout: heal current public-node state first, then deploy code and config with `boot_protocol_version: 21` together on all participating nodes.

Completion criteria:

- A packaged node rejects incompatible consensus/schema versions instead of silently importing them.
- Two live V2.1 nodes converge after temporary divergence without manual state wipes.
- A late stale-parent branch cannot pull an established node back to a different active snapshot for a Bitcoin block it already observed.
- Public nodes advertise consensus/protocol version `21` after the coordinated V2.1 cutover.
- No V3 or experimental fork-choice rule is required for the short-term development-mode beta.
- Any later fork-choice redesign is documented as a future consensus-version bump, not as an implicit beta behavior change.

## G2: Protocol And Release Versioning

Goal: stop relying on "everyone pull latest" before packaged installs exist.

- [x] Define separate versions for consensus rules, state bundle schema, peer transport, HTTP API, UDP relay, and node release.
- [x] Add those version fields to `/api/network/summary`.
- [x] Add those version fields to state bundles, peer share announcements, and peer session hellos.
- [x] Define compatibility behavior for each version class.
- [x] Define hard-fork style behavior for consensus/schema version changes.
- [x] Define transport fallback behavior when WebSocket/UDP versions differ but HTTP/state consensus remains compatible.
- [x] Add a visible UI warning when peers are unreachable because of version mismatch.
- [x] Add health-monitor alerts for version mismatch or missing version visibility.
- [x] Add a release-note template that explicitly labels coordinated-upgrade releases.

Completion criteria:

- A node on incompatible consensus version refuses sync and says why.
- A node on older transport version can still use canonical HTTP fallback when consensus-compatible.
- Release notes have an explicit "requires coordinated upgrade" marker when needed.

Current evidence:

- Version DTOs and compatibility evaluation live in `boot_portal/Models/BootProtocolVersions.cs` and `BootProtocolStateService`.
- `/api/network/summary`, state bundles, peer share announcements, and peer session hellos carry version fields.
- Node UI surfaces consensus/schema/API/transport/release metadata and peer compatibility.
- Health monitor flags version mismatch or missing version visibility.
- Branch/tag policy lives in [release-process.md](release-process.md), and the release-note template lives in [release-notes-template.md](release-notes-template.md).

## G3: External Beta Stability

Goal: make the current beta boring before inviting one-click installers.

- [x] Monitor at least 2 independently operated mainnet beta nodes.
- [ ] Complete 7 consecutive days of stable external multi-node beta runtime.
- [x] Monitor at least 1 testnet4 node for real GridPool-block trigger testing.
- [x] Track DATUM acceptance rate by node and by rejection reason.
- [x] Track peer relay success/failure by transport where exposed by node summaries.
- [x] Track state ID convergence across consensus groups.
- [x] Track Work Set count, active snapshot ID, and candidate state ID drift.
- [x] Track DATUM session churn through existing diagnostics and rejection-rate alerts.
- [x] Track real quickdiff submissions after the quickdiff reconstruction fix through local DATUM diagnostics.
- [x] Write compact monitor logs for later Codex/human review.
- [x] Add `dallas.gridpool.net` to the production monitor config once its deployed version is compatible.

Completion criteria:

- No unexplained payout mismatch bursts for 7 days.
- No unexplained state divergence lasting more than 10 minutes.
- DATUM share acceptance is above 95% after excluding clearly invalid solo fallback or firmware-truncated templates.
- Any lower acceptance rate has a documented root cause and mitigation.
- External tester can upgrade from a previous beta release without wiping state.

Current evidence:

- `scripts/gridpool-health-monitor.mjs` compares `mainnet-beta` nodes separately from `testnet4-beta`.
- Live config currently monitors `main.gridpool.net`, `test.gridpool.net`, `evomining.farted.net`, and `dallas.gridpool.net`.
- DATUM TCP endpoint checks cover `datum.main.gridpool.net:3008`, `datum.test.gridpool.net:3009`, and `datum.dallas.gridpool.net:3008`.
- Monitor logs are written to `~/.local/state/gridpool-monitor/latest-summary.json`, `latest-consensus.json`, and dated `snapshots/`, `consensus/`, and `alerts/` JSONL files.
- As of the first dry run after this change, `main` and `evomining` were aligned on mainnet current/candidate/active snapshot IDs.

## G4: Networking And NAT Readiness

Goal: home miners should not need to understand router internals to participate safely.

- [x] Make outbound-only peers visible in UI/API as live sessions instead of fake dialable endpoints.
- [x] Relay accepted shares to live outbound-only WebSocket sessions directly connected to a public node.
- [ ] Finish seed-mediated relay between two hidden peers connected through the same public seed.
- [ ] Decide whether seed relay is acceptable for the first packaged beta.
- [ ] Add reachability self-test for public endpoint, peer WebSocket, and UDP relay.
- [ ] Research and prototype UPnP, NAT-PMP, and PCP port mapping.
- [ ] Research UDP hole punching with public seeds as rendezvous.
- [ ] Add clear docs for direct public peer, outbound-only peer, and relay-fallback peer modes.
- [x] Ensure hidden peers are never advertised as dialable endpoints.
- [ ] Add metrics for relay dependency: number of peers reached directly vs through seed relay.

Completion criteria:

- A fresh home node behind NAT appears in the peer list as outbound-only within 30 seconds.
- That node receives shares from public peers without manual port forwarding.
- If automatic port mapping succeeds, the node verifies its own public reachability.
- If automatic port mapping fails, the UI says the node is still participating outbound-only.

Current evidence:

- Implementation status is tracked in [robust-networking-architecture-plan.md](robust-networking-architecture-plan.md).
- Hidden session accounting and direct live WebSocket share relay are implemented.
- Seed-mediated hidden-to-hidden relay, NAT traversal, and automated port mapping remain open.

## G5: Packaging And Installer Readiness

Goal: make installation boring and reversible.

- [ ] Decide package architecture for Umbrel.
- [ ] Decide package architecture for Start9.
- [ ] Confirm whether Start9 package should live in this repo or a separate wrapper repo.
- [x] Provide Docker image tags for stable beta releases.
- [x] Provide sample config for mainnet beta Docker/manual installs.
- [x] Provide separate sample config for testnet4 beta installs.
- [x] Provide safe default ports for UI, DATUM, peer HTTP/WebSocket, and UDP relay.
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

Current evidence:

- Docker sample config exists at `docker/boot_portal_config.sample.json`.
- Testnet4 sample config exists at `docker/boot_portal_config.testnet4.sample.json`.
- GitHub Actions publishes branch, tag, SHA, and `latest` images to GHCR; `develop` is available for staging once that branch exists.
- Main documented defaults are `5000` WebUI/API, `3008` DATUM, and `5001/udp` peer fast relay.
- Raspberry Pi/full-stack installer docs exist, but appliance packaging is not yet complete enough for Umbrel/Start9 users.

## G6: Repo And Project Architecture

Goal: avoid turning the reference node into a junk drawer for every future adapter.

- [ ] Write a target repo map for the GridPool ecosystem.
- [x] Keep `gridpool-web` separate from node code.
- [x] Keep `gridpool-simulations` separate from node code.
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

Current evidence:

- Local sibling repos exist at `../gridpool-web` and `../gridpool-simulations`.
- This repo still needs an explicit architecture map and archive pass before broad outside contributors are invited.

## G7: Public Docs, Website, And UI

Goal: public-facing language must match V2.1 consensus and current operational reality.

- [ ] Replace or remove the V1-era intro video.
- [ ] Update website FAQ for pool hopping, payout snapshots, block withholding, firmware coinbase limits, and outbound-only nodes.
- [ ] Update node UI terminology:
  `active payout snapshot`, `unpaid Work Set`, `current shared payout slots`, and `local DATUM hashrate`.
- [ ] Remove or rename V1 leftovers in Nerd Mode.
- [x] Add a clear testnet/mainnet visual banner.
- [x] Add current consensus version and network ID to Nerd Mode.
- [x] Add a concise "What happens if we find a block?" explanation in README/operator-facing docs.
- [ ] Keep "The pool for cypherpunks" but make the technical explanation precise.

Completion criteria:

- A user can tell whether they are looking at mainnet or testnet in under 3 seconds.
- The UI does not imply that every Bitcoin block is a paid GridPool round.
- Public docs explain why raw Stratum V1 is not native unless using a gateway.
- Website and README describe V2.1 snapshot/reserve consensus.

Current evidence:

- README includes the DATUM block-submission safety note and V2.1 snapshot/reserve explanation.
- Node UI now exposes protocol/network version fields in Nerd Mode.
- Public website still needs the intro video refreshed and FAQ tightened for the latest consensus language.

## G8: Security, Abuse, And Operational Monitoring

Goal: keep failures visible and bounded.

- [x] Keep duplicate share suppression tested.
- [x] Keep firmware coinbase truncation detection tested.
- [x] Keep DATUM quickdiff reconstruction observable through diagnostics.
- [ ] Add malformed peer bundle tests.
- [ ] Add rate limits for low-difficulty peer spam.
- [ ] Add rate limits for state bundle fetches.
- [x] Add health-monitor checks for GridPool block found, service down, peer divergence, version mismatch, coinbase stress mode, and DATUM acceptance drops.
- [x] Add alert suppression so ordinary V2.1 snapshots do not page the operator.
- [x] Add monitor logs and summaries for top rejection categories and node acceptance rates.
- [x] Add lab-only uncondensed coinbase output mode for firmware/rental compatibility testing.

Completion criteria:

- Health monitor pages only on actual action-worthy events.
- Known invalid input classes are categorized, not generic "payout mismatch."
- A peer cannot force unbounded CPU or storage by sending low-value proofs.

Current evidence:

- Duplicate and firmware-truncation regression tests live in `boot.tests/ShareAttributionTests.cs`.
- Multi-node monitoring lives in `scripts/gridpool-health-monitor.mjs` and writes compact JSONL logs for review.
- `coinbase_uncondensed_outputs_enabled` is available for non-production firmware stress testing and rejected in production configs.
- Low-difficulty peer-spam policy and state-bundle fetch abuse tests still need dedicated implementation.

## G9: Release Decision

Before Umbrel/Start9 launch, answer yes to all:

- [x] Do we know the current consensus rule is good enough for packaged beta?
- [x] Can incompatible nodes fail safely?
- [ ] Can an outbound-only home node participate usefully?
- [ ] Can a fresh install sync without handholding?
- [ ] Can a nontechnical user recover from restart/power loss?
- [ ] Are public docs accurate enough that users will not connect unsupported firmware and blame the protocol?
- [ ] Have at least two external operators run nodes successfully?

If any answer is no, keep the project in public beta and avoid one-click platform launch.
