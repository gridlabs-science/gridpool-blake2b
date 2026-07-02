# GridPool Release Process

Status: active beta policy.

GridPool is still moving quickly, but public nodes need a stable update path. The goal is lightweight discipline, not enterprise ceremony.

## Branches

### `main`

`main` is the public beta branch.

- Public Docker users and ordinary public nodes should track `main` or a tagged release.
- Changes merged to `main` should be safe for public beta nodes to run.
- GitHub Actions publishes `ghcr.io/gridlabs-science/boot-protocol:latest` and `:main` from this branch.

### `develop`

`develop` is the integration branch.

- Staging nodes, friendly testers, and temporary VPS nodes may track `develop`.
- Experimental fixes can soak here before promotion to `main`.
- GitHub Actions publishes `ghcr.io/gridlabs-science/boot-protocol:develop` from this branch.
- Consensus-breaking work may be developed here, but should not be promoted without a coordinated-upgrade release note.

## Tags

Release tags use semantic-ish beta tags:

```text
v0.2.0-beta.1
v0.2.1-beta.1
v0.3.0-beta.1
```

Pushing a `v*` tag publishes an immutable GHCR image with the same tag.

Use tags when:

- a public beta build is worth pinning;
- a tester needs a known rollback point;
- release notes document a state migration or coordinated upgrade.

## Version Classes

GridPool exposes separate version fields because not every update has the same risk.

- `consensusVersion`: reward and state-transition rules. A mismatch is a hard incompatibility.
- `stateBundleSchemaVersion`: state-bundle serialization and validation. A mismatch is a hard incompatibility.
- `httpApiVersion`: canonical HTTP API compatibility. A mismatch is a hard incompatibility.
- `peerTransportVersion`: WebSocket/session transport compatibility. A mismatch should fall back to HTTP if consensus-compatible.
- `udpRelayVersion`: fast UDP relay compatibility. A mismatch disables UDP relay for that peer but should not block HTTP sync.
- `releaseVersion`: human/operator build identifier.

## Release Types

Every release note must classify the update.

### Normal Update

Use for UI, docs, monitoring, non-consensus performance work, and compatible bug fixes.

Operator action:

- pull or restart when convenient.

### State Migration

Use when persisted state is read or repaired differently, but the network protocol remains compatible.

Operator action:

- back up the data directory before upgrade;
- verify `/api/network/summary` after restart;
- confirm peer compatibility and state convergence.

### Coordinated Consensus Upgrade

Use when `consensusVersion`, `stateBundleSchemaVersion`, or payout validation rules change in a way that old nodes cannot safely interoperate.

Operator action:

- all active public peers must upgrade in the same window;
- do not run mixed versions on the same network after activation;
- verify that peer compatibility shows no consensus/schema mismatch;
- keep a rollback plan and state backup.

### Operator Action Required

Use when config, ports, Docker compose, public endpoint routing, Bitcoin/DATUM setup, or key backup behavior changes.

Operator action:

- follow the explicit migration section before restarting.

## Promotion Flow

1. Land work on `develop`.
2. Let staging nodes soak long enough to exercise the changed subsystem.
3. Promote to `main` with a concise release note.
4. Tag known-good public beta builds.
5. Tell public operators whether the release is optional, recommended, or coordinated.

## Rollback

Before state or consensus releases:

- back up the GridPool data directory;
- record the previous Docker image tag or commit;
- record current `/api/network/summary` state IDs;
- avoid destructive state wipes unless explicitly instructed.

For ordinary UI/docs/monitoring releases, rollback is usually just redeploying the previous tag.
