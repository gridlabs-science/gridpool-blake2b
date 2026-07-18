# GridPool Reference Node Agent Guide

This repository contains the C#/.NET GridPool reference node, DATUM-facing
server, HTTP API, UI, peer networking, deployment scripts, and operator tools.

Read the public project map first:

- `../gridpool-handbook/AGENTS.md`
- `../gridpool-handbook/handbook/protocol-v21.md`
- `../gridpool-handbook/handbook/project-architecture.md`
- `docs/README.md`

## Repository Rules

- Consensus/state changes require tests in `boot.tests`, protocol-version and
  migration analysis, and coordinated rollout notes.
- Attribute miners from the actual slot-0 coinbase output, never metadata.
- Preserve paid-once lineage, retained snapshot contexts, bounded Work Set
  behavior, and V2.1 current-parent merge/boundary finality.
- Keep local DATUM/SV2 mining hot paths distinct from peer rate limits.
- Reliable HTTP/WebSocket validation remains canonical even when UDP forwards a
  provisional proof after proof-of-work precheck.
- Do not expose secrets in tracked config. Use local override files/environment.
- Public terminology is GridPool; preserve legacy `Boot` identifiers unless a
  compatibility migration is part of the change.

## Validation

```bash
dotnet test
```

Run targeted smoke scripts documented under `scripts/` and `docs/` for changes
to state bundles, relay, monitoring, DATUM, or SV2 APIs.
