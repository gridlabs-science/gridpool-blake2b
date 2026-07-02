# GridPool Release Notes Template

Release: `vX.Y.Z-beta.N`

Date: `YYYY-MM-DD`

Commit: `<git-sha>`

Docker image: `ghcr.io/gridlabs-science/boot-protocol:vX.Y.Z-beta.N`

## Release Type

Choose one:

- [ ] Normal update
- [ ] State migration
- [ ] Coordinated consensus upgrade
- [ ] Operator action required

## Compatibility

- Consensus version:
- State bundle schema version:
- HTTP API version:
- Peer transport version:
- UDP relay version:
- Compatible with previous public beta? `yes/no`
- Mixed-version operation allowed? `yes/no`

## Summary

Short operator-facing summary of what changed.

## Upgrade Instructions

```bash
docker compose pull
docker compose up -d
```

Add any release-specific commands here.

## Required Backups

State whether operators should back up the data directory first.

## Validation

After upgrade, check:

```bash
curl -fsS http://127.0.0.1:5000/api/network/summary
```

Expected:

- node is reachable;
- network ID is correct;
- version fields match this release;
- peer compatibility has no consensus/schema mismatch;
- candidate/current state converge with public peers.

## Rollback

State whether rollback is safe and list the previous recommended image tag.

## Notes For Public Operators

Plain-English message suitable for Telegram/Discord/X.
