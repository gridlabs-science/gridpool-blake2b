# GridPool Blake2b Evidence And Profile-Seam Checkpoint

Status: authorized experimental development; SHA-only profile seam; no Blake
runtime or public mining ingress yet

Original evidence cutoff: 2026-08-24. Baseline and source pins updated
2026-08-27.

## Authorization And Baseline

The later
[implementation plan](blake2b-gridpool-implementation-plan.md) supersedes the
older handoff and evidence documents only where they prohibit implementation,
public fork development, testnet4 deployment, or conditionally gated
experimental mainnet deployment. Their isolation, validation, and
security findings remain controlling.

Development now starts from
`b4c92a9090c11efd74298e06b02cfe56727373ea`, tagged
`security-rt-076-retest-candidate`:

- `400fc6e1352ebba72cd557b3c782df52c54d77c8` is an ancestor;
- `f09ce5e6e2f90cf85c009a586b2d02db792ea4c4` remains excluded;
- independent RT-2026-041 and RT-2026-042 retesting passed;
- RT-2026-076 and its regtest completeness follow-up remain an optimistic
  development candidate, not a stable release;
- the clean baseline passes 216 tests.

RT-076 adds a narrowly gated empty-bootstrap reconciliation path. It remains
disabled by default and is allowed only in non-production regtest.
`allow_empty_snapshot_bootstrap` must not appear in Blake testnet4 or mainnet
configuration. Production payout plans remain nonempty.

## Current Upstream Pins

The tracked source of truth is
[`config/blake2b-source-lock.json`](../config/blake2b-source-lock.json).

The selected Testnet4 node pin is signed tag
`v29.4.1.knots20260508rc3`, peeled commit
`afbe91c299e16519f03902939fdbda8af9bd527d`. It supersedes RC2 for deployment.
Relative to RC2, RC3:

- moves Testnet4 Blake activation from height 149537 to 150027;
- uses compact target `0x1a00ffff` for the first Blake block while retaining
  the profile's 20-bit general target-shift rule;
- moves RDTS expiry to Unix time 1788013800;
- fixes `nonce2` and `nonce3` RPC serialization to wire byte order;
- adds a seed advertising the Blake service capability.

The exact PR #359 head `fee27ccf...` remains the canonical five-vector source
until RC3 vector equivalence is checked into GridPool tests. The experimental
DATUM source remains pinned at `e894b8a...`.

Mainnet has no assigned source pin or finite activation in this repository.
Mainnet startup and mining ingress must fail closed until those values are
published, reviewed, and added to the lock.

## SHA-Only Profile Seam

`IChainHeaderProfile` centralizes the existing SHA behavior without adding
runtime profile selection:

- explicit PoW-algorithm and header-format identifiers;
- exact header length;
- canonical parsing and hashing;
- explicit numeric/display byte order;
- compact-target decoding;
- network-specific PoW limits, including native regtest `0x207fffff`.

Only `BitcoinSha256dHeaderV1` is registered. `BitcoinHashes` and
`BootShareVerifier` delegate duplicated header parsing and work calculation to
that profile. Transaction IDs, coinbase IDs, Merkle hashing, address checksums,
share IDs, state IDs, and identity cryptography are unchanged.

The seam preserves the distinction between a header satisfying its encoded
target and a confirmed chain block. Payment, state rotation, and paid-lineage
mutation still require exact active-chain confirmation from the configured
local full node.

Four characterization tests lock the existing header hash, fixed offsets,
byte order, display difficulty, share identity, and error behavior. The
security baseline plus seam passes 220/220 tests: 216 baseline tests and four
new characterization tests.

## Remaining Blake Runtime Gates

Before Blake proofs can enter state:

1. Implement all five canonical vectors and all four ASIC profiles.
2. Make expected target and activation context authoritative from the attached
   pinned node, never submitted `nBits`.
3. Add the consensus-v23 domain fingerprint to proofs, APIs, peer handshakes,
   state, persistence, identities, KDF/AEAD domains, and new UDP codecs.
4. Replace floating difficulty in consensus ordering with the assigned exact
   `uint256-complement-v1` work score.
5. Keep the 897-proof bound, complete proof-backed sibling union, paid-once
   lineage, and coinbase-derived slot-0 attribution.
6. Add variable 80/164-byte raw block, RPC, ZMQ, and test coverage before
   enabling Blake ingress.
7. Integrate DATUM only through the pinned, bounded job/session protocol and
   retain reliable full-proof validation as canonical.

No stable tag, `latest` image, package release, or security certification is
authorized. SHA production repositories, state, peers, identities, and
deployments remain out of scope.
