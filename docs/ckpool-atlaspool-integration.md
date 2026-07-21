# CKPool And AtlasPool Integration

Status: early public-beta adapter contract.

GridPool exposes a generic coinbase work plan for local mining gateways:

- `GET /api/mining/work-plan` is the public read-only representation.
- `GET /api/mining/local/work-plan` is the token-authenticated local alias.
- `GET /api/mining/local/work-plan/events` streams atomic plan changes as SSE.
- `POST /api/mining/local/share` validates complete GridPool proofs.
- `POST /api/mining/local/share-telemetry` records non-consensus vardiff work.

The existing SV2 route remains available. The generic contract uses the same
payout construction and adds a deterministic `planId` binding the network,
consensus version, active snapshot, Bitcoin parent, safety state, and serialized
payout suffix.

The reference integration is split between `gridpool-ckpool`, a current-upstream
CKPool fork, and `gridpool-ckpool-adapter`, a Rust sidecar for authenticated API
access, durable proof delivery, validation, and deterministic fee scheduling.

CKPool must retain the plan and exact coinbase for every issued job. A share
must never be reconstructed with a newer plan merely because the active
snapshot changed after issuance.

Hosted users select GridPool with the exact `USE_GRIDPOOL_SPLIT` password token
and use `address.worker` as their username. Ordinary connections continue
receiving ordinary CKPool templates.

During configured Atlas fee buckets, the operator address replaces the user in
slot 0 while the GridPool suffix remains unchanged. Metadata never overrides
slot-0 attribution.

