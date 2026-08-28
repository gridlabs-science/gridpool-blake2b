# Blake2b Consensus V23 Domain Migration

Status: implementation in progress; mining ingress disabled

## Decision

Blake GridPool uses consensus/protocol version 23, state-bundle schema 4,
HTTP API version 2, peer transport version 3, and UDP relay capability version
6. Version 23 is selected only by an assigned Blake chain profile and becomes
active only when the locally attached node reports a trusted height at or above
that profile's activation boundary. Existing SHA v21/v22 version selection and
wire constants remain unchanged.

The canonical proof ranking is exact uint256 complement ordering:

```text
M = 2^256 - 1
H = unsigned 256-bit numeric PoW result
WorkScore = M - H
```

Higher `WorkScore` ranks first; equal scores use lexicographically ascending
`ShareId`. JSON uses exactly 64 lowercase hexadecimal characters in numeric
big-endian order. Binary transports use exactly 32 numeric-big-endian bytes.
Floating difficulty is display and telemetry only.

## Testnet4 domain

The RC3 assignment supersedes the older RC2 height/profile values from the
owner clarification:

- network ID: `gridpool-blake2b-testnet4-v1`
- chain ID: `bip110-blake2b-testnet4`
- genesis: `00000000da84f2bafbbc53dee25a72ae507ff4914b867c565be350b0da8bf043`
- activation rule: `height-150027-headline-v1`
- PoW algorithm: `knots-blake2b-v2`
- header format: `knots-header-v2-164`
- target rule: `knots-blake2b-target-shift20-v1`
- work-score rule: `uint256-complement-v1`
- profile revision: `knots-rc3-afbe91c-v1`
- payout policy: `fee-free-299-v1`
- domain fingerprint:
  `2ad111b42ae7bd90e41e385d838853455cacc54aefe5f61cbc094c01ee6908d0`

The fingerprint is SHA-256 over the owner-assigned canonical LF-terminated
transcript. Tests pin both the transcript size and resulting digest.

## Migration analysis

There is no SHA-to-Blake state migration. A Blake node must use a new network
ID, identity, persistence root, peer set, and empty activation-boundary state.
It must not import SHA Work Sets, winners, snapshots, retained contexts, or paid
lineage. Missing v23 domain and integer-work fields are rejected rather than
filled from floating difficulty.

The initial Testnet4 profile requires:

- `chain_profile_id: knots-rc3-afbe91c-testnet4-v1`
- `bitcoin_network: testnet4`
- `boot_network_id: gridpool-blake2b-testnet4-v1`
- `boot_protocol_version: 23`
- `winners_list_size: 299`
- `grid_labs_support_fee_enabled: false`

Regtest additionally requires one shared 12-character lowercase hexadecimal
lab ID in `gridpool-blake2b-regtest-v1:<lab-id>`. Mainnet remains unassigned and
fails configuration validation; no placeholder activation or profile revision
is permitted.

## Coordinated rollout

1. Keep public GridPool, DATUM, Stratum, and UDP ingress closed while v23 fields
   are being bound into every proof, peer, API, state, identity, and job surface.
2. Complete exact-work ordering and equality migration, attached-node target
   authority, and fail-closed persistence checks before creating Blake state.
3. Initialize three fresh, isolated v23 regtest nodes with the same domain and
   lab ID. Reject v21/v22, absent-domain, and wrong-domain peers and bundles.
4. Exercise activation, restart, share/notification ordering, sibling union,
   paid-once lineage, and one/two-block reorgs before Testnet4 mining ingress.
5. Start Testnet4 with a fresh identity/state root and the pinned RC3 domain.
   Rollback means closing ingress and discarding that experimental Blake state;
   it never means loading SHA state or downgrading an established v23 lineage.

This document records the migration and rollout contract, not completion of all
v23 bindings. Until those bindings and tests land, the VPS remains a node-only
environment.
