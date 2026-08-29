# GridPool Blake2b Evidence And Profile-Seam Checkpoint

Status: authorized experimental development; Blake header-v2 profile implemented;
public mining ingress remains disabled

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

## Profile Seam And Blake Header Runtime

`IChainHeaderProfile` centralizes the existing SHA behavior without adding
runtime profile selection:

- explicit PoW-algorithm and header-format identifiers;
- exact header length;
- canonical parsing and hashing;
- explicit numeric/display byte order;
- compact-target decoding;
- network-specific PoW limits, including native regtest `0x207fffff`.

`BitcoinSha256dHeaderV1` preserves the legacy path. The activation-format
selector also registers `BitcoinBlake2bHeaderV2`, which implements the pinned
RC3 164-byte serialization, all four ASIC input profiles, tagged H1/H2 and XOR
commitments, BLAKE2b-256 hashing, effective-time handling, and reserved flag
rejection. Transaction IDs, coinbase IDs, Merkle hashing, address checksums,
share IDs, state IDs, and identity cryptography remain unchanged.

The seam preserves the distinction between a header satisfying its encoded
target and a confirmed chain block. Payment, state rotation, and paid-lineage
mutation still require exact active-chain confirmation from the configured
local full node.

Four characterization tests lock the existing SHA header hash, fixed offsets,
byte order, display difficulty, share identity, and error behavior. Three
additional tests cover all five pinned upstream Blake vectors, all four ASIC
profiles, exact output byte order, 80/164-byte profile selection, effective
time, the v2 marker, and reserved high flags. The suite passes 240/240 tests.

The profile computes a monotonic exact integer work value in addition to
display-only floating difficulty. V23 proof ordering, reconciliation, state
identifiers, and imported proof validation now use that integer and the pinned
chain-domain fingerprint. Job-bound submitted-target authority remains a gate
before Blake proofs may enter state.

## Testnet4 Node Checkpoint

The constrained VPS source-builds signed Knots RC3 at the exact peeled commit.
Its `bitcoind` SHA-256 is recorded in the source lock. Upstream verification
passes 130/130 CTest targets plus the four targeted Blake/RDTS functional tests.
The pruned Testnet4 node runs under systemd with loopback RPC/ZMQ, UFW allowing
only SSH and Testnet4 P2P, and a five-minute local health timer. A fresh
headline-locked sync completed with IBD false at height `150245` on August 28,
2026. Block `150027` independently reproduced the activation boundary: its hash is
`000000000000007a178eb03e6619f0420d7d38e278e6bb5ee16f15ac5b32cee6`,
its header is 164 bytes, its target is `0x1a00ffff`, and its coinbase contains
the exact 30-byte headline `PyBLOCK-LOTTO-BLAKE2b-t4-ASIC`; block `150026` has
an 80-byte header. The active deployment reports `reduced_data` at height
`150027`, and the node reported three live peers at verification. The health
timer was restored after validation and the obsolete discovery datadir was
removed. Mining ingress remains closed.

## Remaining Blake Runtime Gates

Before Blake proofs can enter state:

1. Bind each mining job and submitted share to the expected target and
   activation context obtained from the attested node, never submitted `nBits`.
2. Finish domain-bound job and API submission contexts plus the new GPBS/GPBT
   UDP codecs; legacy UDP, pulse, and optimistic relay remain disabled for Blake.
3. Keep floating difficulty display-only; v23 consensus ordering uses the
   assigned exact `uint256-complement-v1` work score.
4. Keep the 897-proof bound, complete proof-backed sibling union, paid-once
   lineage, and coinbase-derived slot-0 attribution.
5. Add variable 80/164-byte raw block, RPC, ZMQ, and test coverage before
   enabling Blake ingress.
6. Integrate DATUM only through the pinned, bounded job/session protocol and
   retain reliable full-proof validation as canonical.

The attached-node RPC layer now fails mining safety until it verifies the
configured Blake profile's genesis and Knots identity. At/after activation it
also verifies the pinned Testnet4 activation hash, the linked 80-to-164-byte
header transition, embedded height, and first-Blake compact target. The result
and sanitized evidence are exposed in RPC health. This attests the node/chain
boundary but does not yet replace job-bound expected-target validation.

No stable tag, `latest` image, package release, or security certification is
authorized. SHA production repositories, state, peers, identities, and
deployments remain out of scope.

## DATUM Fork Checkpoint

The community Blake2b gateway moved from `justinfilip/datum_gateway` to
`innerhat-dev/datum_gateway`. On August 29, 2026 its current head was
`2fea7e51286d3821c19dc1c240b8caa92bd92532`, eight commits beyond the former
`e894b8a` pin. The additional history includes modular time-wrap behavior,
Knots' published profile-0 vector, submitted-time reconstruction, and a
fail-safe offset fallback. A clean out-of-tree build and the gateway's internal
`--test` suite pass locally.

The plan-specified fork now exists at
`gridlabs-science/datum-gateway-blake2b-gridpool`, with `develop` as its default
branch. Existing force-coinbase and authenticated client-telemetry work was
ported commit-by-commit onto the reviewed Blake2b base. Commit `70670c5` closes
the Blake-specific selection gap: the chosen full coinbase class is bound into
both the Stratum job ID and H2 commitment, forced Blake work is withheld while
the DATUM coinbaser is incomplete, invalid force configuration fails startup,
and the risky known-incompatible-miner override is disabled by default. Commit
`d3fb38b` enables explicit CI dispatch for the fork.

A clean out-of-tree build and internal `datum_gateway --test` run pass at the
fork head. Public listeners must set `coinbase_selection_mode` to `force`,
`coinbase_selection` to `yuge`, and `allow_unsafe_coinbase_override` to `false`.
GridPool listener policy and per-payout session multiplexing remain
unimplemented, so DATUM/SV1 ingress remains closed.
