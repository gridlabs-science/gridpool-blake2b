# GridPool Development Roadmap: 22-31 July 2026

Status: active execution plan. The Umbrel/Start9 checklist remains the package
launch gate; this document orders the next work without expanding that gate.

## Operating Constraint

V2.2 activated at Bitcoin height `959500`; reachable public nodes now report
consensus version 22 and schema version 3. The meaningful seven-day protocol
soak begins after Main and Oregon run one pinned, provenanced commit, pass a
24-hour no-intervention canary, and reconverge. Evomining and Detroit add useful
independent evidence when available but do not control the clock. During the
soak, avoid consensus, state, peer, payout, and mining hot-path changes except
fixes for a demonstrated safety or availability bug.

An operator experiment, planned restart, or endpoint outage is not a protocol
failure, but it must be recorded so the soak report can distinguish controlled
intervention from unexplained behavior. A consensus defect, lost state,
unexplained same-tip divergence, or required state wipe resets the soak clock.

## Priority Order

### P0: Pin The Soak Pair And Start The Clock

1. Freeze one reference-node consensus/networking revision for Main and Oregon.
2. Rebuild Oregon with commit provenance and confirm both nodes report the same
   release, consensus/schema 22/3, Bitcoin tip, current state, and candidate
   state. Align consensus-adjacent safety flags, especially
   `enable_peer_tip_stale_protection`, before the canary.
3. Back up state and identity files on both nodes.
4. Pass a 24-hour no-intervention canary with healthy attached-node RPC/ZMQ,
   peer sessions, UDP relay, and no unexplained same-tip divergence.
5. Start a new seven-day soak ledger. Record code/config changes, restarts,
   miner changes, node lag, and operator experiments. Include Dallas,
   Evomining, and Detroit whenever reachable without making them required.

### P0: Remove Prototype-Era Security Exposures

1. Deploy the removal of startup private-key logging before the canonical soak
   begins. Do not print long-term identity keys or derived session secrets at
   any log level.
2. Inventory logs and unauthenticated APIs for keys, tokens, RPC URLs, remote
   IPs, LAN addresses, miner identities, and raw peer endpoints.
3. Define a private-node disclosure contract: outbound-only nodes expose node
   identity/capabilities as required by the peer protocol, but no dialable,
   observed, LAN, or socket address in public UI/API/gossip.
4. Add a safe package privacy mode and separate public peer identity from local
   operator diagnostics.
5. Document key-file permissions, backup/restore, log retention, and deliberate
   identity rotation. Treat private keys already captured in retained VPS logs
   as potentially exposed, but do not rotate live identities without a tested
   state migration procedure.

The detailed gate is [security-privacy-review.md](security-privacy-review.md).
The review may proceed during the consensus soak on staging fixtures. Any
security fix that changes production runtime behavior must be recorded and may
restart the affected stability window.

### P1: Run Two Concurrent Evidence Tracks

**Network soak**

- Keep the health monitor and incident capture running.
- Review same-tip state divergence, reconciliation events, paid-once behavior,
  DATUM/SV2/CKPool acceptance, session churn, queue depth, and service restarts.
- Generate daily compact summaries and a final seven-day report.
- Treat different-height snapshot IDs as propagation lag, not automatically as
  a consensus split; escalate same-height/same-tip state disagreement.

**StratumRace multi-vantage collection**

- Keep the existing Main collector running.
- Keep Oregon as the controlled remote collector with an attached Bitcoin node.
- Add Evomining and Detroit after their local Bitcoin notification paths and
  clock health are verified. Dallas can measure public endpoint arrivals but is
  not a useful local-node latency vantage until it has an attached Bitcoin node.
- Verify NTP/clock offset, stable vantage IDs, authenticated ingestion, local
  endpoint labels, GridPool peer-header correlation, and data retention.
- Do not compare absolute cross-site milliseconds until clock quality is
  recorded. Publish medians, p95, sample counts, missing observations, and
  topology caveats.

### P1: Stabilize Existing Mining Adapters

- Complete a controlled CKPool/AtlasPool canary using the generic work-plan,
  SSE, local share, and telemetry APIs.
- Verify plan freshness fail-closed behavior, exact issued-job retention,
  slot-0 attribution, fee buckets, durable proof retry, accepted network proofs,
  and local Bitcoin block submission.
- Keep the adapter labeled early public beta. Do not turn the soak into an
  adapter development environment; use a staging node or bounded canary.
- Continue the native SV2 soak and verify per-channel slot-0 attribution and
  restart recovery.

### P1: Build Appliance Packages During The Soak

- Create thin, separate Umbrel and StartOS wrapper repositories that pin the
  exact reference-node image digest.
- Sideload the Umbrel package on Detroit and the StartOS package on the local DC
  appliance. Exercise install, state sync, private UI defaults, restart,
  upgrade, backup, restore, and outbound-only networking.
- Track package stability separately from protocol stability. A manifest/UI
  correction resets the package canary; a shared runtime or state change resets
  the protocol soak.
- Resolve native SV2 template acquisition across appliance service boundaries.
  The current SRI-derived pool's local Bitcoin Core IPC dependency cannot be
  assumed to cross Umbrel/StartOS app boundaries. Do not bundle a second Bitcoin
  node; add or reuse a supported remote template-provider/RPC path.
- Initial package support is native SV2 only. DATUM and raw SV1 remain
  experimental and disabled by default.

### P2: PublicPool Integration Spike

PublicPool is a worthwhile next adapter because it is an open-source NestJS /
TypeScript Stratum server with a substantial self-hosted installation base. It
should not be deployed onto the reference network during the V2.2 soak.

Deliverables for this week:

1. Map PublicPool job construction, per-user payout attribution, vardiff share
   handling, block submission, and template refresh boundaries.
2. Compare those seams with GridPool's generic work-plan, SSE, full-proof, and
   low-difficulty telemetry APIs.
3. Decide whether the smallest maintainable design is an in-process optional
   module, a sidecar, or a narrow upstreamable interface.
4. Write an end-to-end regtest plan covering ordinary solo mode, opt-in
   GridPool mode, exact coinbase retention, wrong-network failure, snapshot
   changes, and local block submission.
5. Produce an issue/design note before writing a large fork. Reuse the generic
   gateway contract rather than adding PublicPool-specific consensus behavior
   to the reference node.

### P2: UI Refresh Design, Not Production Rewrite

- Audit every UI card and calculation against V2.2 terminology and current API
  semantics.
- Define the information architecture for simple, operator, and Nerd views.
- Prototype against captured/synthetic API fixtures, including split detected,
  reconciling, reconciled, local-node lag, adapter failure, and outbound-only
  states.
- Prioritize correctness and responsiveness: active snapshot positions are
  locked for the current template; unpaid Work Set rankings remain provisional.
- Make privacy a first-class UI mode. Public views show only intentionally
  advertised hostnames and redacted node IDs; raw IPs, observed addresses, LAN
  addresses, miner/session identities, and NAT diagnostics are operator-only.
- Verify that an outbound-only node cannot become publicly identifiable merely
  because a seed, monitor, or public UI observed its connection.
- Defer the production replacement and main-node deployment until the soak is
  complete. A UI-only staging branch may proceed concurrently.

## Seven-Day Soak Exit Criteria

- No unexplained same-tip state/snapshot divergence lasting over 10 minutes.
- No state wipe or manual branch selection required.
- V2.2 reconciliation events, if any, converge deterministically and remain
  within configured member/context bounds.
- No unexplained payout mismatch burst or paid-lineage inconsistency.
- Native SV2 job delivery, accepted-share flow, durable retry, and restart
  recovery have no unexplained failure. Experimental DATUM/SV1/CKPool
  rejection rates are categorized but do not define launch support.
- Restarts preserve identity, state, peers, and adapter recovery.
- Outbound-only peer behavior and state-bundle sync have at least one real-node
  validation.
- The final report lists every intervention and separates protocol defects from
  operator experiments, Bitcoin-node lag, miner failover, and expected
  propagation delay.

Daily/final evidence commands:

```bash
node scripts/boot-soak-report.mjs \
  --main-url https://main.gridpool.net \
  --peer-url https://oregon.gridpool.net \
  --window 24h \
  --limit 5000 \
  --out ~/.local/state/gridpool-monitor/soak-$(date -u +%F).json

node scripts/chain-tip-latency-report.mjs \
  --url https://main.gridpool.net \
  --window 7d \
  --limit 5000 \
  --json ~/.local/state/gridpool-monitor/chain-tip-7d.json
```

## After The Soak

1. Fix any soak findings and repeat only the affected stability window.
2. Merge/deploy the UI refresh after API semantics are frozen.
3. Implement the PublicPool integration from the reviewed spike.
4. Promote the successful Umbrel/StartOS sideload wrappers into reproducible
   release candidates and finish ARM64 validation.
5. Continue community firmware/rental compatibility testing without making
   broad SV1 coverage a gate for the native-SV2 package.
