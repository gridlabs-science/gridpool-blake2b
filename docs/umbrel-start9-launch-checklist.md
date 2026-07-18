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
- [ ] Firmware and rental compatibility with 300-slot coinbases is tested and documented. The community matrix shell exists, but enough real test rows have not been collected yet.
- [x] Monitoring catches the failure classes already seen in public beta.
- [ ] Repo is clean enough that outside contributors can orient quickly. G6 now has a target architecture map; remaining work is README/docs pruning and more archive passes.

## G1: Consensus Selection And State Convergence

Goal: keep V2.1 snapshot/reserve consensus predictable and safe enough for a short-term packaged beta. This gate is not trying to solve the full V3 branch-market / adversarial fork-choice research problem.

Current V2.1 posture:

- Work Set and active snapshot proofs must remain fully validated before import.
- Incompatible consensus or state-bundle schema versions must fail visibly and safely.
- Established nodes merge different subsets of valid current-parent proofs only when they share a compatible active payout state; they do not replace active snapshots wholesale.
- A node must not rewrite an already-observed Bitcoin-block payout snapshot using proofs that first arrive after that node's local snapshot boundary. Late previous-parent proofs should be rejected or quarantined from the canonical future reserve. Proofs committing to genuinely different active payout states cannot be cross-credited.
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
- [x] Add a same-active-state merge regression fixture: two nodes on the same current Bitcoin parent and compatible active payout state should merge fully valid current-parent proofs into the unpaid reserve.
- [ ] Specify and test recovery from a genuine active-snapshot split. Do not describe same-parent merge-forward as automatic cross-snapshot reconciliation; candidate IDs bind work to the active payout state, and the current tested import path rejects cross-state candidates.
- [ ] Run a multi-node public beta soak and confirm current/candidate state IDs converge without manual state wipes.
- [x] Decide whether V2.1 state selection plus non-retroactive snapshot boundaries is "good enough for beta" or whether package launch waits for a coordinated V3 rule.
- [x] Coordinate V2.1 rollout: heal current public-node state first, then deploy code and config with `boot_protocol_version: 21` together on all participating nodes.
- [ ] Revisit the Grid Labs support-fee construction before declaring the packaged payout format stable. Compare the current optional canonical `1/300` support slot with cleaner alternatives, document their incentive and coinbase implications, and decide whether any change belongs in V2.1 or requires a coordinated consensus-version bump. Do not make custom fee addresses consensus-valid.
- [ ] Model and evaluate delayed snapshot activation: build the payout snapshot at a Bitcoin boundary but make it applicable one or more Bitcoin blocks later. Determine whether the added propagation window materially reduces latency-driven snapshot disagreement, and quantify the costs in payout freshness, state retention, work attribution, reorg handling, and implementation complexity before considering a consensus change.
- [ ] Specify and regression-test GridPool behavior across ordinary one- and two-block Bitcoin reorgs. Define how active snapshots, retained contexts, unpaid Work Set proofs, paid-proof lineage, candidate/current state IDs, and any observed GridPool payment transition roll back or reconcile without double-paying or losing valid unpaid work.
- [ ] Analyze a sustained Bitcoin ruleset split, including a BIP110-driven chain split scenario. Define how GridPool network IDs, parent validation, peer discovery, state bundles, payout snapshots, and operator-visible warnings prevent proofs from incompatible Bitcoin branches from being merged silently; decide whether branch choice is strictly inherited from each node's attached Bitcoin node or needs explicit GridPool configuration/version separation.

Completion criteria:

- A packaged node rejects incompatible consensus/schema versions instead of silently importing them.
- Two live V2.1 nodes converge after temporary divergence without manual state wipes.
- A late stale-parent branch cannot pull an established node back to a different active snapshot for a Bitcoin block it already observed.
- One- and two-block Bitcoin reorg behavior is deterministic, tested, and preserves paid-once lineage and valid unpaid work.
- Nodes attached to incompatible Bitcoin consensus branches fail visibly and do not merge proofs or state bundles across the split.
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
- At least one full 300-output coinbase stress run is completed against representative firmware and rental providers before recommending them publicly.

Current evidence:

- `scripts/gridpool-health-monitor.mjs` compares `mainnet-beta` nodes separately from `testnet4-beta`.
- Live config currently monitors `main.gridpool.net`, `test.gridpool.net`, `evomining.farted.net`, and `dallas.gridpool.net`.
- DATUM TCP endpoint checks cover `datum.main.gridpool.net:3008`, `datum.test.gridpool.net:3009`, and `datum.dallas.gridpool.net:3008`.
- Monitor logs are written to `~/.local/state/gridpool-monitor/latest-summary.json`, `latest-consensus.json`, and dated `snapshots/`, `consensus/`, and `alerts/` JSONL files.
- As of the first dry run after this change, `main` and `evomining` were aligned on mainnet current/candidate/active snapshot IDs.

## G4: Networking And NAT Readiness

Goal: home miners should not need to understand router internals to participate safely.

Short-term beta posture:

- Public nodes with manually configured DNS and ports are acceptable for early beta.
- Private/Umbrel/Start9 nodes may participate as outbound-only peers as long as this is visible, safe, and well documented.
- The launch-critical goal is not perfect peer-to-peer censorship resistance on day one; it is a low-friction setup where users can mine, verify state, and relay shares without router knowledge.
- This is sufficient for the near-term value proposition of lower fees, sovereign template construction, and open participation, provided there are several independent public nodes.

Long-term censorship-resistance posture:

- Public seed nodes should become bootstrap/rendezvous helpers, not central relay dependencies.
- Outbound-only home nodes should eventually attempt automatic reachability and direct encrypted peer paths.
- UDP hole punching, Tor/I2P transports, and more diverse peer discovery should be evaluated as censorship-resistance upgrades even if short-term latency data looks acceptable.
- Route-dependency metrics should make centralization visible: direct public peers, outbound-only session peers, UDP-direct peers, and relay-fallback paths should be counted separately.

- [x] Make outbound-only peers visible in UI/API as live sessions instead of fake dialable endpoints.
- [x] Relay accepted shares to live outbound-only WebSocket sessions directly connected to a public node.
- [x] Add encrypted V2 session state-bundle sync so outbound-only peers can serve stronger current/candidate state without public HTTP reachability.
- [x] Add optional peer-only listener port so publicly mapped home nodes do not expose the UI/admin surface by default.
- [ ] Validate outbound-only state-bundle sync across at least two real nodes after deployment.
- [x] Add initial seed-assisted reachability self-test for peer HTTP routes and peer-session route visibility.
- [x] Add UDP reachability challenge/ack for router/NAT diagnostics.
- [x] Add admin-triggered PCP/NAT-PMP port mapping for the peer-only TCP port and UDP relay port.
- [ ] Validate PCP/NAT-PMP mapping against at least one real home router and one expected-failure/CGNAT case.
- [ ] Research optional UPnP IGD port mapping after PCP/NAT-PMP testing.
- [x] Add measurement-only full-header relay telemetry over encrypted V2 sessions and compact encrypted UDP, paired against receiver-local Bitcoin rawblock ZMQ arrival.
- [ ] Run chain-tip latency reports during the 7-day soak and compare against local Bitcoin tip detection.
- [ ] Track Bitcoin ZMQ topic sequence numbers instead of discarding the third message frame. Expose per-topic last sequence, gaps, duplicates, reconnects, and reset/wrap handling in API/monitor telemetry so delayed or missing notifications can be distinguished from a lagging Bitcoin node.
- [ ] Investigate and choose a redundant attached-node notification strategy for packaged installs. Evaluate ZMQ `rawblock`/`hashblock`/`sequence`, Bitcoin Core mining IPC `waitTipChanged`, GBT long polling, `blocknotify`, and periodic RPC best-tip reconciliation; document the preferred and fallback matrix for Core, Knots, Docker, Umbrel, and Start9.
- [ ] Decide whether peer-relayed chain-tip headers remain measurement/early-warning signals or may eventually trigger provisional or consensus snapshot behavior. Any activation must specify header PoW and parent validation, freshness/replay protection, reorg handling, local-node confirmation, failure behavior, and whether it requires a coordinated consensus-version bump.
- [ ] Add advanced, disabled-by-default optimistic peer-header mining for GridPool-controlled SV2/direct-template adapters.
- [ ] Decide whether UDP hole punching is necessary for beta performance after 7-day latency data review.
- [ ] Separately decide the censorship-resistance roadmap priority for UDP hole punching even if beta performance is acceptable.
- [ ] Add NAT traversal status fields: none/manual/pcp/nat-pmp/upnp/failed, observed external UDP endpoint, and mapping stability.
- [ ] Add clear docs for direct public peer, outbound-only-safe peer, and relay-fallback peer modes.
- [x] Ensure hidden peers are never advertised as dialable endpoints.
- [ ] Add metrics for relay dependency: number of peers reached directly, by outbound-only session sync, and through any relay fallback.
- [ ] Keep seed-mediated relay as fallback only; do not treat it as the preferred launch topology.
- [ ] Document Tor/I2P as future optional privacy/reachability transports for censorship-resistant mode.

Completion criteria:

- A fresh home node behind NAT appears in the peer list as outbound-only within 30 seconds.
- That node receives shares from public peers without manual port forwarding.
- A public node can fetch and validate current/candidate state bundles from an outbound-only peer over the encrypted V2 session.
- If `peer_listener_port` is configured and mapped, only peer protocol endpoints are exposed on that listener.
- If automatic port mapping succeeds, the node verifies its own public reachability.
- If automatic port mapping fails, the UI says the node is still participating outbound-only.
- `node scripts/peer-reachability-test.mjs <seed> <target> [udp-port]` returns peer HTTP/session reachability and UDP challenge/ack status.
- `GRIDPOOL_ADMIN_KEY=... node scripts/peer-port-map.mjs --url <private-node-url> --tcp-port <peer-port> --udp-port <udp-port>` returns per-gateway PCP/NAT-PMP mapping results without exposing the UI/admin surface publicly.
- `node scripts/chain-tip-latency-report.mjs --url <node> --window 7d` summarizes peer chain-tip relay latency.
- A 7-day soak report includes share propagation latency, chain-tip observation latency, direct-vs-session reachability, and UDP first-arrival rates.

Current evidence:

- Implementation status is tracked in [robust-networking-architecture-plan.md](robust-networking-architecture-plan.md).
- Hidden session accounting and direct live WebSocket share relay are implemented.
- Encrypted V2 session bundle sync and optional peer-only listener support are implemented but need real-node validation.
- Reachability self-test, UDP diagnostics, chain-tip latency instrumentation, and admin-triggered PCP/NAT-PMP mapping are implemented.
- Current ZMQ telemetry records arrival timestamps but discards Bitcoin Core's per-topic sequence frame; this prevents direct diagnosis of message gaps and duplicates. Evomining has also exhibited intermittent multi-block catch-up batches and duplicate rawblock observations that should be resolved during the soak.
- Peer-relayed headers remain measurement-only: they do not currently advance the chain tip, rotate snapshots, invalidate work, or build mining templates.
- Real-router PCP/NAT-PMP validation, UPnP decision, direct-vs-session relay dependency metrics, and the 7-day latency report remain open.
- Current production-like topology is acceptable for public beta if enough independent public nodes stay reachable, but it is not the final censorship-resistance topology.

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

## G5.5: Miner Firmware, Rental, And Stratum V2 Compatibility

Goal: avoid launching a 300-slot team that silently breaks common ASIC firmware or rental intermediaries.

- [x] Build a repeatable community firmware compatibility matrix shell for the 300-slot beta team.
- [ ] Test uncondensed 300-output coinbases against known-good sovereign firmware.
- [ ] Test uncondensed 300-output coinbases against stock/older Bitmain-class firmware if available, and document failure behavior rather than supporting it silently.
- [ ] Test at least one Whatsminer-class setup, one Bitaxe/AxeOS-class setup, and one alternate firmware path such as ePIC/PowerPlay-BM or VNish/xminer-class firmware.
- [ ] Test hashrate rental paths before recommending any provider publicly.
- [x] Publish a public compatibility table with `works`, `fails`, `untested`, `suspected works`, `suspected fails`, and `requires alternate firmware` states.
- [ ] Add a UI/API warning when firmware truncation rejects are observed repeatedly from a local DATUM session.
- [x] Investigate whether DATUM coinbase-size selection can be made GridPool-safe with existing config. See [datum-gridpool-coinbase-compatibility.md](datum-gridpool-coinbase-compatibility.md).
- [x] Propose or track a DATUM operating mode that can force or require a large coinbase class for GridPool-compatible templates.
- [x] Stand up the testnet full-coinbase compatibility endpoint with `coinbase_uncondensed_outputs_enabled: true`, separate state or network ID, and public `/compat` telemetry.
- [x] Expose DATUM Stratum V1 on `stratum.test.gridpool.net:3334` for first-pass firmware and rental-provider testing.
- [x] Complete the Stratum V2/GridPool integration review in [stratum-v2-gridpool-evaluation.md](stratum-v2-gridpool-evaluation.md).
- [x] Decide whether Stratum V2 standard-channel/header-only mining is the preferred long-term path for avoiding ASIC coinbase-size constraints.
- [x] Add GridPool node-side SV2 work-selection API and smoke test. See [stratum-v2-gridpool-integration-plan.md](stratum-v2-gridpool-integration-plan.md) and `GET /api/mining/sv2-work-selection`.
- [x] Prove a native SV2/JDC path can submit accepted shares into GridPool on mainnet beta with a Bitaxe-class miner.
- [x] Replace the overbuilt JDC/JDS experiment with a maintained SRI Pool fork that talks directly to Bitcoin Core and the local GridPool node.
- [x] Support per-channel slot-0 attribution, a global fallback payout address, batched vardiff telemetry, pulse/reserve proofs, and durable proof retry in the fork.
- [ ] Run a sustained native-SV2 miner soak against the new `gridpool-sv2-pool` fork and verify slot-0 attribution plus block submission end to end.
- [ ] Replace temporary SV2 beta keys/config with production-managed keys before broad public advertising.
- [ ] Document public SV2 endpoint operation, monitoring, restart behavior, and upgrade process.

Completion criteria:

- A miner can check the docs before renting or redirecting hashrate and know whether their firmware path is expected to work with a full 300-slot payout list.
- The beta website clearly says that firmware/rental support is compatibility-tested, not assumed.
- The project has a written decision on whether SV2 support is a launch-adjacent priority or a post-Umbrel/Start9 roadmap item.

Current evidence:

- Strict firmware truncation detection is implemented and tested.
- `coinbase_uncondensed_outputs_enabled` exists for non-production firmware/rental stress testing.
- Current support docs warn that some firmware cannot handle large coinbase templates, and the community matrix shell lives in [firmware-coinbase-compatibility-matrix.md](firmware-coinbase-compatibility-matrix.md).
- Existing DATUM `stratum.fingerprint_miners` does not solve this. Unknown miners default to a smaller Antminer-compatible coinbase class, and disabling fingerprinting makes that worse. A full 300-unique-address GridPool list likely requires DATUM's 16 KB `YUGE` class or an equivalent Stratum V2/header-only path.
- A DATUM PR now exists for `coinbase_selection_mode = "force"` plus known-incompatible client disconnects before oversized work is served.
- The first recommended lab endpoint shape is `test.gridpool.net/compat`, `datum.test.gridpool.net:3009` for DATUM gateways, and `stratum.test.gridpool.net:3334` for raw Stratum V1 ASICs, backed by testnet uncondensed output mode.
- Native SV2 is now the preferred long-term path for firmware that cannot safely parse large SV1/DATUM coinbases. The maintained implementation is the `gridpool-sv2-pool` fork: SV2 miners connect directly to the fork, which uses Bitcoin Core IPC and authenticated local GridPool APIs without JDC/JDS.

## G6: Repo And Project Architecture

Goal: avoid turning the reference node into a junk drawer for every future adapter.

- [x] Write a target repo map for the GridPool ecosystem.
- [x] Keep `gridpool-web` separate from node code.
- [x] Keep `gridpool-simulations` separate from node code.
- [x] Decide whether to create `gridpool-spec` for protocol docs and test vectors.
- [x] Decide whether adapters belong in this repo or separate repos.
- [x] Move obvious old soak logs and historical test artifacts into `docs/archive/` or out of the repo.
- [x] Archive stale V1-era planning docs rather than deleting useful history.
- [x] Update README to point to the current docs and hide obsolete rabbit holes.
- [x] Ensure public docs use "GridPool" language consistently.

Completion criteria:

- A new contributor can read README and know which docs are current.
- Old V1 plans are clearly marked archived.
- Generated logs/state files are not tracked.
- Spec, node, web, and simulation responsibilities are clear.

Current evidence:

- Local sibling repos exist at `../gridpool-web`, `../gridpool-simulations`, and `../gridpool-spec`.
- Target repo responsibilities are documented in [project-architecture-map.md](project-architecture-map.md).
- Adapter decision: modules that reuse this node's consensus/networking core can live in this repo for now; independently useful gateways, firmware forks, alternate pool backends, and cross-implementation fixtures should move to separate repos when their release lifecycle diverges.
- First archive pass moved completed session/debug investigations and the superseded broad launch checklist into `docs/archive/`.
- Active docs now have an index at [README.md](README.md), and the only remaining public-facing `Boot Protocol` references are explicit old-name notes.

## G7: Public Docs, Website, And UI

Goal: public-facing language must match V2.1 consensus and current operational reality.

- [x] Replace or remove the V1-era intro video.
- [x] Update website FAQ for pool hopping, payout snapshots, block withholding, firmware coinbase limits, and outbound-only nodes.
- [x] Update node UI terminology:
  `active payout snapshot`, `unpaid Work Set`, `current shared payout slots`, and `local DATUM hashrate`.
- [x] Remove or rename V1 leftovers in Nerd Mode.
- [x] Add a clear testnet/mainnet visual banner.
- [x] Add current consensus version and network ID to Nerd Mode.
- [x] Add a concise "What happens if we find a block?" explanation in README/operator-facing docs.
- [x] Keep "The pool for cypherpunks" but make the technical explanation precise.

Completion criteria:

- A user can tell whether they are looking at mainnet or testnet in under 3 seconds.
- The UI does not imply that every Bitcoin block is a paid GridPool round.
- Public docs explain why raw Stratum V1 is not native unless using a gateway.
- Website and README describe V2.1 snapshot/reserve consensus.

Current evidence:

- README includes the DATUM block-submission safety note and V2.1 snapshot/reserve explanation.
- Node UI now exposes protocol/network version fields in Nerd Mode.
- Public website now embeds the V2.1-oriented explainer video and includes FAQ entries for pool hopping, payout snapshots, block withholding/attack resistance, firmware coinbase limits, and outbound-only nodes.
- Node UI labels now prefer `active payout snapshot`, `unpaid Work Set`, `snapshot`, and `payment transition` in user-facing text while preserving internal IDs/events for compatibility.

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
- [ ] Do we know which miner firmware and rental paths can handle a full 300-slot GridPool payout list?
- [ ] Have at least two external operators run nodes successfully?

If any answer is no, keep the project in public beta and avoid one-click platform launch.
