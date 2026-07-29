# GridPool V2.2 Dashboard

The V2.2 dashboard is a replaceable React/TypeScript observer of the GridPool
reference node. It has no consensus authority and does not mutate protocol
state.

## Routes

- `/`: current adaptive dashboard.
- `/legacy`: temporary Razor dashboard retained during the testnet evaluation.
- `/api/dashboard/v1/summary`: redacted public dashboard projection.
- `/api/dashboard/v1/history`: `6h`, `24h`, or `7d` local telemetry history.
- `/api/dashboard/v1/address/{address}`: active-snapshot and provisional-reserve
  positions for one payout address.
- `/api/dashboard/v1/operator`: authenticated local clients, peers, Bitcoin
  notification health, and detailed diagnostics.
- `/api/dashboard/v1/schema`: machine-readable capability and route manifest.
- `/dashboardHub`: revision and changed-topic notifications only.

The hub never broadcasts internal state objects. Clients refetch the explicit
HTTP projections after receiving an invalidation.

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
synthetic state, with loopback controls and optional read-only LAN observers:

```bash
scripts/run-dashboard-lab.sh
scripts/run-dashboard-lab.sh --lan
```

It is excluded from the solution, release workflow, production Docker image,
and node deployments. See `boot_portal/ui/AGENTS.md` for scenarios, actions,
timeline YAML, API contracts, and extension guidance.

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
