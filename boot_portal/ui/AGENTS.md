# GridPool V2.2 Dashboard And Simulator Guide

This directory contains the optional GridPool web dashboard and its
development-only synthetic-node laboratory. Read this file before changing
dashboard projections, terminology, modules, or simulator behavior.

## Boundaries

- The dashboard observes GridPool state. It never changes consensus state.
- Production HTTP projections are implemented by
  `boot_portal/Controllers/DashboardController.cs` and
  `boot_portal/Services/DashboardReadModelService.cs`.
- Production DTOs live in `boot_portal/Models/DashboardModels.cs`; TypeScript
  contracts live in `src/types.ts`.
- The simulator is a presentation tool. It models coherent UI state, not
  cryptographic validation or complete consensus correctness.
- `npm run build` builds only production dashboard assets.
- `npm run simulator:build` builds the lab into an ignored development output.
- The simulator project is deliberately absent from `boot_portal.slnx`, the
  Dockerfile, release CI, and deployment scripts.
- Never import runtime node configuration, identities, credentials, state
  files, or private diagnostics into the simulator.

## V2.2 Presentation Rules

- Only the active payout snapshot is **locked**.
- The 897-proof Work Set is **provisional**, **currently ranked**, or the
  **unpaid reserve**. A reserve position can be displaced by stronger work.
- The top 300 reserve proofs form the prospective payout suffix. They are not
  locked until a validated Bitcoin boundary activates the snapshot.
- Compatible sibling snapshot-family work merges forward by proof ID and
  achieved difficulty. Do not describe this as heaviest-chain election.
- Distinguish current state, candidate state, active snapshot, snapshot family,
  provisional peer tip, and locally validated Bitcoin tip.
- Pulse proofs are liveness and transport telemetry. Do not blend them into the
  order-statistic team work-rate estimate without a separately validated model.
- Adapter-reported local hashrate and proof-derived team work rate are different
  measurements and must be labeled separately.
- Show unsafe and degraded states plainly. Do not turn missing evidence into a
  green status.

## Production Dashboard

The React entry point is `src/main.tsx`. `App.tsx` builds one adaptive dashboard
from the registry in `src/modules/index.tsx`. A module receives
`DashboardModuleContext` and must remain independently removable or replaceable.

Production API:

| Route | Visibility | Purpose |
|---|---|---|
| `/api/dashboard/v1/summary` | Public, redacted | Node truth, snapshot, work rate, pulse and capabilities |
| `/api/dashboard/v1/history` | Public, redacted | Aggregated 6h, 24h or 7d history |
| `/api/dashboard/v1/address/{address}` | Public | Locked and provisional positions for one address |
| `/api/dashboard/v1/operator` | Admin key | Local adapters, peers and detailed diagnostics |
| `/api/dashboard/v1/schema` | Public | Machine-readable route and capability manifest |
| `/dashboardHub` | Public, redacted | Revision and changed-topic invalidations only |

SignalR never carries complete internal state. `DashboardChanged` tells clients
which topics changed; clients refetch the typed HTTP projection. Operator keys
stay in React memory only and must not enter URLs, browser storage, logs, or
exports.

To add a module:

1. Extend a versioned backend projection only when existing data is
   insufficient.
2. Update `src/types.ts`.
3. Implement the module in `src/modules/index.tsx` or a focused module file.
4. Register it in `dashboardModules`.
5. Add healthy, sparse, degraded, unsafe, dark, light, desktop, and mobile
   coverage as applicable.
6. Preserve public redaction and the headless-node contract.

## Simulator Architecture

`tools/GridPool.DashboardSimulator` is a separate ASP.NET process referencing
the production assembly for exact DTO reuse. It serves:

- the real dashboard at `/dashboard/`;
- exact dashboard HTTP projections under `/api/dashboard/v1`;
- a real `/dashboardHub`;
- the control laboratory at `/__sim/`;
- loopback-only mutation APIs under `/__sim/api/v1`.

The React laboratory source is under `simulator/` and has a separate Vite
configuration. Its preview iframe is the real production dashboard. Multiple
desktop or phone observers receive the same revision invalidations.

Launch locally:

```bash
scripts/run-dashboard-lab.sh
```

Expose only the synthetic observer dashboard to the LAN:

```bash
scripts/run-dashboard-lab.sh --lan
```

The launcher prints the desktop control URL, preview URL, LAN observer URL, and
synthetic operator key. `--lan` binds HTTP/SignalR to all interfaces, but
middleware rejects `/__sim` and `/__sim/api/*` unless the remote address is
loopback. There is no permissive CORS; phones load the same LAN origin.

Useful options:

```bash
scripts/run-dashboard-lab.sh --port 5199
scripts/run-dashboard-lab.sh --no-build
GRIDPOOL_SIM_OPERATOR_KEY=temporary-value scripts/run-dashboard-lab.sh
```

## Coherent State

Coherent mode is the default:

- connected adapter hashrates sum to local hashrate;
- team work rate cannot be below local hashrate;
- the displayed order-statistic boundary is derived from team work rate,
  observation window, and retained proof count;
- relative standard error is approximately `1 / sqrt(m)`;
- top-300 proofs are necessarily in the top 897;
- reserve size is fixed at a maximum of 897;
- ordinary Bitcoin boundaries do not remove reserve proofs;
- GridPool payments remove locked proof IDs once, then retain unpaid work;
- IDs and scenario data derive deterministically from the seed.

Advanced override mode allows contradictory state for defensive rendering
tests. It must never be presented as a valid consensus state.

## Controls And Actions

The laboratory directly edits node safety, RPC, ZMQ, IBD, compatibility,
outbound relay, team work rate, proof count, admission floor, pulse cadence,
peers, transports, latency, adapters, clients, and per-adapter hashrate.

Action names accepted by `POST /__sim/api/v1/actions`:

| Action | Arguments | Effect |
|---|---|---|
| `peer.disconnect`, `peer.reconnect` | `peer` | Toggle a peer session |
| `peer.transport` | `peer`, `transport`, `value` | Toggle HTTP, WebSocket or UDP |
| `adapter.disconnect`, `adapter.reconnect` | `adapter` | Toggle a local adapter |
| `adapter.hashrate` | `adapter`, `value` | Set adapter TH/s |
| `pool.hashrate` | `value` | Set team TH/s |
| `pulse.emit`, `proof.heartbeat` | none | Emit liveness proof |
| `proof.top897` | optional `address` | Add reserve-quality proof |
| `proof.top300` | optional `address` | Add top-300 proof |
| `proof.block` | optional `address` | Add block-quality proof |
| `chain.peer-header` | none | Freeze a provisional peer-tip boundary |
| `chain.local-validate` | none | Locally validate and commit the boundary |
| `chain.invalid-header` | none | Discard the provisional peer header |
| `snapshot.regular` | none | Ordinary boundary, no paid-proof removal |
| `snapshot.gridpool-paid` | none | Paid-once removal and new snapshot |
| `snapshot.sibling-merge` | optional `count` | Merge compatible sibling work |
| `state.diverge` | optional `peer` | Give one peer a divergent candidate |
| `state.converge` | none | Align peer state IDs |
| `chain.reorg` | optional `count` | Synthetic shallow reorganization |
| `fault.api` | `value` as 0/1 | Disable/enable dashboard API responses |
| `fault.api-latency` | milliseconds in `value` | Delay dashboard API responses |
| `fault.signalr` | `value` as 0/1 | Drop/restore invalidations |

Built-in scenarios are exposed by `GET /__sim/api/v1/scenarios` and cover cold
start, healthy mesh, sovereign operation, full reserve, small-miner retention,
peer-tip lead, stale node, pulse outage, adapter dropout, transport fallback,
sibling merge, divergence, version mismatch, both boundary types, shallow
reorganization, and dashboard transport interruption.

## Timelines

Timelines use YAML version 1. They have a deterministic seed, initial scenario,
and time-ordered events. Durations accept `ms`, `s`, or `m`.

```yaml
version: 1
name: peer-tip-recovery
seed: 42
initialScenario: healthy-mesh
events:
  - at: 5s
    action: peer.disconnect
    peer: dallas
  - at: 8s
    action: chain.peer-header
  - at: 10s
    action: chain.local-validate
```

Playback supports pause, play, one-event stepping, reset, loop, and speed from
0.25x through 20x. Manual event history exports as starter YAML. Add new actions
to the model, engine dispatch, timeline DTO, lab controls, this table, and tests
together.

## Simulator Control API

All routes below are loopback-only:

- `GET /__sim/api/v1/state`
- `PUT /__sim/api/v1/state`
- `POST /__sim/api/v1/actions`
- `GET /__sim/api/v1/scenarios`
- `POST /__sim/api/v1/scenarios/{id}/load`
- `GET /__sim/api/v1/events`
- `POST /__sim/api/v1/reset`
- `POST /__sim/api/v1/timeline/{play|pause|step|reset}`
- `POST /__sim/api/v1/import`
- `GET /__sim/api/v1/export`

## Validation

```bash
dotnet test tools/GridPool.DashboardSimulator.Tests/GridPool.DashboardSimulator.Tests.csproj
npm --prefix boot_portal/ui test
npm --prefix boot_portal/ui run build
npm --prefix boot_portal/ui run simulator:build
dotnet test
```

Before merging dashboard changes, manually inspect desktop and mobile layouts in
dark and light themes across healthy, sparse, degraded, unsafe, divergent, and
transport-failure scenarios. Confirm a second browser receives live updates.

Production-boundary check:

```bash
docker build -t gridpool-dashboard-boundary .
docker run --rm gridpool-dashboard-boundary sh -c \
  'test ! -e /app/wwwroot/sim && ! grep -R "/__sim" /app/wwwroot/dashboard'
```

## Style, Accessibility, And Privacy

- Preserve the monochrome graphite visual language; use green, amber, red, and
  testnet cyan only for semantic state.
- Keep fonts and assets local. Never add runtime CDN dependencies.
- Maintain keyboard navigation, visible focus, reduced-motion behavior, AA
  contrast, and responsive public layouts.
- Prefer an explicit quantity plus uncertainty over a confident-looking
  estimate without evidence.
- Never expose peer IPs, operator diagnostics, credentials, identity secrets,
  or local paths in public projections.
- The API console is curated HTTP inspection, not a shell.

## Troubleshooting

- Missing dashboard assets: run without `--no-build`.
- Phone cannot connect: use `--lan`, the printed LAN address, and permit the
  selected TCP port in the host firewall.
- Phone receives 403 on `/__sim`: expected; controls are desktop-only.
- Dashboard updates only every 15 seconds: SignalR fault injection may be on.
- Operator view is locked: use the synthetic key printed by the launcher.
- Contradictory values snap back: coherent mode is enforcing invariants; enable
  advanced overrides only for defensive UI tests.
- Timeline import fails: check version, scenario ID, duration suffixes, event
  ordering, and action names.
