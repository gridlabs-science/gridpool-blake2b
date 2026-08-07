# GridPool V2.2 Dashboard

The V2.2 dashboard is a replaceable React/TypeScript observer of the GridPool
reference node. It has no consensus authority and does not mutate protocol
state.

When a payout address has not been configured, the server exposes a local,
self-contained `/setup` page styled to match this dashboard. Operational APIs
and background services remain disabled until the address is persisted and the
node restarts. The setup page has no runtime CDN, font, script, or analytics
dependency.

## Routes

- `/`: live GridPool system map.
- `/details`: adaptive diagnostic module dashboard.
- `/legacy`: temporary Razor dashboard retained during the testnet evaluation.
- `/setup`: first-run payout configuration; redirects to `/` after an
  operational restart.
- `/api/dashboard/v1/summary`: redacted public dashboard projection.
- `/api/dashboard/v1/history`: `6h`, `24h`, or `7d` local telemetry history.
- `/api/dashboard/v1/address/{address}`: active-snapshot and provisional-reserve
  positions for one payout address.
- `/api/dashboard/v1/operator`: authenticated local clients, peers, Bitcoin
  notification health, and detailed diagnostics.
- `/api/dashboard/v1/schema`: machine-readable capability and route manifest.
- `/api/dashboard/v1/diagram`: redacted exact-rank diagram snapshot.
- `/api/dashboard/v1/diagram/events`: redacted bounded visualization journal.
- `/api/dashboard/v1/diagram/operator`: authenticated peer, miner, proof, and Slot-0 detail.
- `/api/dashboard/v1/diagram/operator/events`: authenticated visualization journal.
- `/api/dashboard/v1/diagram/history`: public validated proof history for the
  verified Slot-0 address (`24h` or `7d`, bounded to 256 results).
- `/api/dashboard/v1/diagram/operator/history`: the same history with local
  source and worker detail, authenticated by the operator key.
- `/dashboardHub`: revision and changed-topic notifications only.

The hub never broadcasts internal state objects. Clients refetch the explicit
HTTP projections after receiving an invalidation.

The visualization journal is presentation-only, in memory, and bounded to
2,048 events or 10 minutes. Cursor gaps force clients to discard queued motion
and reconcile from a fresh diagram snapshot. The public diagram exposes exact
Work Set proof IDs, payout addresses, difficulty, arrival time, and verified
Slot 0 as shared consensus evidence. It exposes peer node IDs and allowlisted
names for Dallas, Detroit, Oregon, and `evomining.farted.net`, while peer endpoints/IPs/locations and
miner identities remain operator-only. Public peer latency controls anonymous
link length. Anonymous miner rates, aggregate local
rate, the estimated remote-pool rate, and Bitcoin network difficulty are public.
The schema-3 diagram also exposes anonymous Bitcoin peer rays, aggregate peer
counts, network hashrate, telemetry freshness, mining-safety state, peer state
relations, miner quality counters, and snapshot lineage. It never exposes
Bitcoin peer addresses, binds, user agents, or inferred geography.

Journal proof events expose a public `blockQuality` classification alongside
their salted proof and source visual IDs. Ordinary work uses a short
perpendicular tick on the real topology; only Bitcoin block-quality work and
new chain tips use squares. Retained work travels to its rank and relay peers,
block-quality work also travels to the attached Bitcoin node, and full-rail
admission ejects the displaced rank-897 tick. Accepted local-share counts
produce a bounded three-tick vardiff burst; connection state changes travel
along the affected peer link. These are presentation effects derived from
validated journal facts, not new protocol state.

The Work Set is drawn as one exact-rank, log-difficulty skyline rather than 897
individual ticks. Rank 300 marks the prospective payout cutoff, network
difficulty is geometrically connected to Bitcoin, and every verified Slot-0
match is highlighted. A focused logarithmic chase compares recent local best,
rank 897, rank 300, pool best, and Bitcoin network difficulty. The observer-only
`gp>` line can change the history window and focus, inspect a rank, select a rail
mode, or export a privacy-safe Work Set plus public local-proof history.

## Correct Terminology

- The active payout snapshot is locked for the block templates currently being
  mined.
- The unpaid Work Set is provisional and difficulty-ranked. Stronger proofs can
  displace weaker proofs before the next snapshot.
- V2.2 merges compatible fully validated sibling reserves within one snapshot
  family. It does not elect a branch using peer count or later hashrate.
- Pulse proofs measure current liveness and relay health. They are not included
  in the displayed team work-rate estimate.

## Work-Rate Estimate

The dashboard uses a local, non-consensus order-statistic measurement. For each
selected window it records unique validated Work proofs using the node's local
arrival clock and records every Work Set admission-floor change.

The estimator selects the complete observations above the maximum admission
floor in the window, keeps at most the strongest 897, and computes:

```text
H = m * D_m * 2^32 / elapsed
relative standard error ~= 1 / sqrt(m)
```

The API reports the sample count, boundary difficulty, uncertainty, admission
floor, and whether the local window is complete. New or restarted nodes report
`collecting` rather than rebuilding confidence from peer-supplied timestamps.
Telemetry is stored separately from consensus state at
`pool_state.dashboard-telemetry.json` by default and is never included in state
bundles.

Persistent telemetry schema 2 retains at most seven days and 100,000 Work plus
100,000 pulse observations. Schema-1 files migrate in place. Legacy unattributed
observations remain valid for global work-rate estimation but never acquire
invented address attribution.

## Building

Normal .NET tests do not require Node.js. Build the dashboard explicitly before
a source deployment:

```bash
scripts/build-dashboard.sh
dotnet publish boot_portal/boot_portal.csproj -c Release
```

Docker release builds use a Node 24 build stage automatically. Generated
dashboard assets under `boot_portal/wwwroot/dashboard/` are not tracked.

For development:

```bash
cd boot_portal/ui
npm install
npm run dev
```

Vite proxies API and SignalR requests to `http://127.0.0.1:5000`.

### Interactive Simulator

The development-only simulator serves the real dashboard against deterministic
synthetic state, with loopback controls and a read-only LAN observer enabled by
default:

```bash
scripts/run-dashboard-lab.sh
scripts/run-dashboard-lab.sh --local-only
```

The second command disables LAN access. The simulator is excluded from the
solution, release workflow, production Docker image, and node deployments. See
`boot_portal/ui/AGENTS.md` for scenarios, actions, timeline YAML, API contracts,
and extension guidance.

## Headless And Custom Dashboards

Set:

```json
{
  "enable_web_ui": false
}
```

The node continues serving health, mining, peer, dashboard API, and SignalR
routes. `/` returns a small headless-service document. A separate dashboard can
consume the same versioned API contract.

`enable_legacy_ui` controls the temporary `/legacy` route independently. Both
settings default to `true` during the testnet evaluation.

Operator keys entered into the dashboard remain in memory for the current tab.
They are not written to URLs, local storage, logs, or dashboard exports.
