# GridPool Release Process

Status: experimental Blake2b fork policy; no stable release authorized.

GridPool is still moving quickly, but public nodes need a stable update path. The goal is lightweight discipline, not enterprise ceremony.

## Branches

### `main`

`main` is an archival pre-fork reference fixed at `97bc68c`.

- Do not merge Blake2b work into `main` or force-push it.
- Do not deploy `main` as the Blake reference node.
- The Blake2b fork does not publish `latest`. CI publishes only immutable
  commit-derived tags and the experimental branch tag; deployments must record
  and use the resolved image digest.

### `develop`

`develop` is the default and only deployable experimental integration branch.

- Staging nodes, friendly testers, and temporary VPS nodes may track `develop`.
- Experimental fixes can soak here before promotion to `main`.
- GitHub Actions may publish `ghcr.io/gridlabs-science/gridpool-blake2b:develop`
  and `sha-*` tags from this branch, but deployments must resolve and record the
  immutable image digest.
- Consensus-breaking work may be developed here, but should not be promoted without a coordinated-upgrade release note.

## Tags

Do not create GitHub releases, stable tags, or a `latest` container tag during
the experimental phase. Record exact commits, source locks, binary hashes, and
container digests instead. A later explicit owner decision is required before
introducing a release-tag policy.

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

Bitcoin-height-activated upgrades must ship the new rules before activation,
publish one network-specific activation height, and derive the active consensus
version from the trusted local Bitcoin tip. Operators must not manually flip a
protocol setting at activation. An unknown tip height fails closed to the
pre-activation rules.

Operator action:

- all active public peers must upgrade before the published activation height;
- do not run mixed versions on the same network after activation;
- verify that peer compatibility shows no consensus/schema mismatch;
- keep a rollback plan and state backup.

### Operator Action Required

Use when config, ports, Docker compose, public endpoint routing, Bitcoin/DATUM setup, or key backup behavior changes.

Operator action:

- follow the explicit migration section before restarting.

## Promotion Flow

1. Land work on `develop` after CI and the scoped acceptance gates pass.
2. Deploy only an immutable commit/image digest to the isolated Blake staging
   or public-experimental environment.
3. Record soak evidence, source pins, and rollback instructions.
4. Keep `main` archival and publish no stable release/tag.
5. Require an explicit owner decision before any future promotion policy.

## Rollback

Before state or consensus releases:

- back up the GridPool data directory;
- record the previous Docker image tag or commit;
- record current `/api/network/summary` state IDs;
- avoid destructive state wipes unless explicitly instructed.

For ordinary UI/docs/monitoring releases, rollback is usually just redeploying the previous tag.
