# GridPool Blake2b Fork Handoff

Status: planning handoff; no implementation or deployment authorization

Evidence cutoff: 2026-08-23

Audience: a fresh Codex task and developers evaluating a GridPool deployment
for the BIP-110-associated Blake2b chain

## Purpose

Evaluate and, if the upstream chain and mining interfaces are sufficiently
specified, build an isolated GridPool fork for the proposed Blake2b proof-of-work
chain associated with the BIP-110 community.

The motivation is practical:

- The prospective miner community strongly values decentralized mining.
- GridPool can provide non-custodial reward sharing without a central pool
  ledger.
- A live, lower-hashrate network could provide useful field evidence for
  GridPool's networking, reconciliation, and payout behavior.
- A Blake2b-compatible DATUM fork is reportedly the dominant mining path, while
  open-source pooled alternatives are limited.

This is an experimental compatibility effort, not permission to weaken or
delay the SHA-256 GridPool security and appliance-release work.

## Terminology And Source Boundary

Do not conflate two different changes:

1. BIP-110 is the Reduced Data Temporary Softfork specification.
2. The Blake2b work is a subsequent proposed hard fork that changes block
   headers and proof of work.

Use **BIP-110-associated Blake2b chain** until that chain publishes stable
naming and identifiers. Do not claim that BIP-110 itself specifies Blake2b.

Primary sources known at the evidence cutoff:

- BIP-110 specification:
  <https://github.com/bitcoin/bips/blob/master/bip-0110.mediawiki>
- Bitcoin Knots PR #359, `Hardfork: New BLAKE2b proof-of-work algorithm`:
  <https://github.com/bitcoinknots/bitcoin/pull/359>
- Upstream DATUM Gateway:
  <https://github.com/OCEAN-xyz/datum_gateway>

PR #359 was open and actively changing at the cutoff. The reviewed page showed
an unfinished 164-byte version-two header, activation-dependent SHA256d/Blake2b
PoW handling, additional nonce/header fields, ASIC profiles, transaction-count
commitment, merge-mining preparation, and optional withholding-related fields.
It also documented incomplete `getblocktemplate` communication for some v2
header parameters. Re-query the PR and pin an exact commit before designing or
coding against it.

The authoritative Blake2b DATUM fork URL and commit are not yet recorded here.
Do not infer them from the local `datum_gateway` repository, which currently
tracks GridPool's force-coinbase-selection work over OCEAN upstream. Obtain and
pin the actual Blake2b fork before adapter implementation.

## Required Isolation

The Blake2b network must be unable to contaminate SHA-256 GridPool state.

- Use a distinct GridPool network ID and explicit PoW algorithm ID.
- Use separate peer seeds, public endpoints, ports, UDP magic/version, data
  directories, identity keys, state files, metrics, and monitor groups.
- Never import SHA-256 Work Sets, snapshots, paid lineage, peer bundles, or
  proof spools into the Blake2b network, or vice versa.
- Reject missing, unknown, or mismatched network, chain, header-format, PoW,
  payout-policy, and consensus-version fields before state evaluation.
- Do not share bootstrap discovery between networks unless discovery records
  are cryptographically and structurally domain-separated.
- Keep appliance packages and images clearly named and independently pinned.
- Do not deploy this fork to Main, Oregon, or the SHA-256 public seed fleet.

Use a separate repository or worktree, provisionally named
`gridpool-blake2b`. Generic improvements may later be proposed upstream, but
the experimental fork must not become an unreviewed conditional branch inside
the production SHA-256 process.

## Baseline Constraint

Do not fork from the packaged `9ac862a` runtime: it predates critical security
fixes. Relevant defensive work at the cutoff includes:

- Immutable critical-fix retest target:
  `boot-protocol-security`, commit
  `400fc6e1352ebba72cd557b3c782df52c54d77c8`.
- Follow-on P1 hardening:
  `boot-protocol-security-p1`, commit
  `f09ce5e6e2f90cf85c009a586b2d02db792ea4c4`.

Those branches may still be awaiting independent review or integration. The
new task must identify the final reviewed security-integrated SHA-256 baseline
before creating a long-lived fork. It may perform a read-only architecture
audit before that baseline is available.

## GridPool Invariants To Preserve

The fork changes chain-specific proof validation, not GridPool's economic
model. Preserve these invariants unless a separately reviewed protocol change
explicitly replaces one:

- V2.2 paid-once lineage.
- A bounded unpaid Work Set, currently 897 proofs by default.
- An active payout snapshot distinct from the provisional unpaid reserve.
- Deterministic monotonic union of fully validated sibling reserves in one
  exact snapshot family.
- No heaviest-list, peer-count, identity-vote, or claimed-total-work election.
- Direct-ingress snapshot-boundary finality and deterministic reconciliation.
- Exact proof removal when a valid GridPool payout block is accepted.
- Preservation of other unpaid reserve work across boundaries and payments.
- Slot-0 attribution from the actual coinbase output, never username or sender
  metadata.
- Non-custodial coinbase settlement and sovereign template construction.
- Full proof validation before canonical state mutation.
- Pulse proofs remain liveness telemetry and cannot mutate payout state unless
  they independently satisfy normal Work Set admission rules.
- Fast peer notifications optimize mining and operations but do not activate
  consensus snapshots without an explicit future protocol rule.

Read first:

- `../gridpool-handbook/AGENTS.md`
- `../gridpool-handbook/handbook/project-overview.md`
- `../gridpool-handbook/handbook/protocol-v21.md`
- `../gridpool-handbook/handbook/protocol-v22.md`
- `../gridpool-handbook/handbook/statistical-foundation.md`
- `../gridpool-handbook/handbook/security-and-threat-model.md`
- `docs/gridpool-v2.2-monotonic-snapshot-reconciliation-draft.md`
- `docs/v2.2-cutover.md`
- `docs/security-privacy-review.md`

## Why This Is Not A Hash Substitution

The current reference node assumes Bitcoin's 80-byte header and SHA256d in
several independent domains. The proposed upstream v2 header is structurally
different. At minimum, audit:

- `boot_portal/Utils/BitcoinHashes.cs`
  - hard-coded 80-byte headers;
  - SHA256d block hash;
  - parent, time, and compact-target offsets;
  - Bitcoin/regtest PoW limits.
- `boot_portal/Services/BootShareVerifier.cs`
  - 80-byte header parsing;
  - SHA256d achieved-work calculation;
  - block-target classification;
  - share ID construction;
  - Merkle-root verification.
- `boot_portal/Utils/BitcoinBlockParser.cs`
  - fixed 80-byte block-header boundary.
- `boot_portal/Services/BootPeerUdpShareCodec.cs`
  - fixed 80-byte share header and wire-size calculation.
- `boot_portal/Services/BootPeerUdpChainTipCodec.cs`
  - fixed 80-byte header, wire magic, and version.
- `boot_portal/HostedServices/BitcoinZmqSubscriber.cs`
  - extraction and hashing of raw block headers.
- `boot_portal/HostedServices/BitcoinRpcReconciliationService.cs`
  - assumptions about RPC header serialization and hash comparison.
- `boot_portal/Program.cs`
  - DATUM framing, fixed header buffers, legacy verification paths, target and
    difficulty calculations.
- `boot_portal/Utils/BootRequestGuards.cs`
  - public API length validation.
- Protocol DTOs, state persistence, state bundles, dashboard projections,
  monitor reports, test fixtures, and scripts containing 80-byte assumptions.

Also audit `gridpool-sv2-pool`, Hydrapool, CKPool, firmware integrations, and
packaging before claiming support. Initial scope should remain the pinned
Blake2b DATUM path unless another adapter has independently compatible header
and job semantics.

## Keep Hash Domains Separate

Do not replace every SHA256 use with Blake2b.

The chain specification must identify each domain independently:

- block-header PoW hash;
- block identifier/display byte order;
- transaction ID and witness transaction ID;
- coinbase transaction ID and Merkle-tree hashing;
- address/Base58 checksums;
- GridPool content/share identifiers;
- peer/session cryptography and signatures.

The expected design is that Blake2b applies to the new header PoW domain while
many Bitcoin transaction, Merkle, address, and GridPool identity domains remain
unchanged. Confirm this against the pinned node implementation and vectors.
Changing unrelated hash domains would create needless incompatibility and may
silently invalidate proof reconstruction.

## Suggested Chain Profile Boundary

First introduce a narrow, testable chain-header/PoW profile rather than
scattering network conditionals. The exact interface is a design task, but it
should cover concepts such as:

- algorithm and header-format identifiers;
- accepted header lengths and activation rules;
- canonical parsing and serialization;
- parent hash, Merkle root, timestamp, target, and chain-specific fields;
- PoW hash and display encoding;
- compact target and difficulty-one normalization;
- target/pow-limit validation;
- achieved-work calculation;
- authoritative block-target classification;
- raw-block header extraction;
- canonical vectors and capability reporting.

Transaction parsing and Merkle reconstruction should remain separate services.
Prove with regression tests that selecting the SHA-256 profile produces no
behavioral change before adding Blake2b behavior.

## Difficulty And Economic Accounting

GridPool ranks proofs by achieved work. Correct normalization is therefore
consensus-critical.

- Do not reuse Bitcoin's `0x1d00ffff` difficulty-one target unless the fork
  specification explicitly defines that normalization.
- Do not compare a Blake2b hash against a target using unreviewed byte order.
- Do not trust header-declared `nBits` as proof that a block was valid for the
  active chain.
- Derive the expected target and activation context from the attached node or
  independently validated chain rules.
- Distinguish "meets GridPool admission difficulty" from "is a valid chain
  block." The latter requires the authoritative expected target and complete
  header context.
- Define how pre-activation SHA256d proofs and post-activation Blake2b proofs
  relate. Prefer a hard network/state boundary unless the fork requires a
  transition-aware GridPool history.
- Recheck pulse vardiff, Work Set admission, dashboard estimates, block cadence,
  and all order-statistic units under the new work normalization.

This area must inherit the defensive lesson from the validated-header security
work: a cheap share with attacker-selected easy target metadata must never be
classified as a real block or trigger paid-lineage mutation.

## Proof And State Domain Separation

Add explicit domain fields to proofs and synchronized state before accepting
Blake2b work:

- GridPool network ID;
- underlying chain ID;
- PoW algorithm ID;
- header format/version;
- consensus version;
- payout-policy/support-fee variant;
- activation regime or chain epoch where relevant.

Consider domain-separating newly computed share IDs with these identifiers.
Do not silently reinterpret persisted SHA-256 proof IDs under Blake2b rules.
State migration should normally reject and start an empty Blake2b reserve,
while preserving the old data as an operator backup.

## DATUM Integration Questions

Before implementation, obtain the exact Blake2b DATUM fork and answer:

1. What node commit and chain parameters does it target?
2. How does it obtain or construct the v2 header fields not represented by
   ordinary `getblocktemplate`?
3. What exact bytes does the ASIC hash, and which fields may the worker roll?
4. How are achieved work, share target, and block target represented?
5. Does the DATUM protocol framing change header size or field layout?
6. Does it preserve full coinbase and Merkle material for GridPool validation?
7. Can the pool force the exact GridPool payout suffix deterministically?
8. How does per-miner slot-0 attribution work?
9. Can a found block be submitted directly to the miner's attached node?
10. What backwards-compatible failure occurs with an unmodified DATUM peer?

Treat existing force-coinbase-selection work as a candidate component, not an
assumption. The Blake2b fork may have diverged from that code.

## Security Gates

No public-value deployment until all of the following are demonstrated:

- Header parsing and PoW vectors match the pinned node implementation.
- Expected target is validated independently of untrusted share metadata.
- State bundles cannot introduce proofless or wrong-domain payout state.
- Wrong-network and wrong-algorithm peers fail before bundle/proof processing.
- All input lengths, allocations, queues, timeouts, and rates are bounded.
- Direct DATUM ingress and peer ingress use the same canonical verifier.
- Coinbase payouts, slot-0 attribution, support policy, Merkle path, and block
  submission are validated end to end.
- Activation boundary and shallow reorganization behavior have deterministic
  tests.
- SHA-256 regression tests remain green in the genericized code.
- A private three-node regtest/lab network converges through ordinary blocks,
  GridPool payout blocks, restarts, disconnects, and reorgs.
- Cross-network traffic is rejected and cannot alter telemetry or state.
- Secrets, operator endpoints, and identities follow existing privacy rules.
- Independent review covers PoW arithmetic and state-transition boundaries.

Do not recreate offensive security demonstrations in this effort. Convert known
findings into defensive regression tests using fixed local fixtures and normal
validation APIs.

## Proposed Milestones

### M0: Source Freeze And Decision Record

- Record the exact Knots/node repository, commit, status, activation rules,
  chain identifiers, and test vectors.
- Record the exact Blake2b DATUM fork and commit.
- Confirm licensing and attribution.
- Decide the GridPool fork name and network ID.
- Produce a source matrix and list unresolved upstream blockers.

Exit: two independent developers can reproduce the same header hash and target
evaluation from pinned vectors.

### M1: Reference-Node Assumption Audit

- Produce a complete inventory of SHA256d, 80-byte-header, target, byte-order,
  RPC, ZMQ, UDP, persistence, API, and UI assumptions.
- Classify each as PoW-specific, transaction-specific, GridPool-specific, or
  unrelated cryptography.
- Propose the smallest chain-profile boundary.

Exit: reviewed design with no code behavior change.

### M2: SHA-256-Preserving Abstraction

- Introduce the chain profile behind current behavior.
- Add characterization tests before changing implementations.
- Keep protocol and persisted schemas unchanged unless explicit capability
  fields are required.

Exit: complete SHA-256 test suite and package smoke tests remain unchanged.

### M3: Blake2b Header And Proof Validation

- Implement pinned header parsing, PoW hashing, target validation, achieved-work
  normalization, raw block parsing, and chain-tip reconciliation.
- Add canonical and negative vectors.
- Add explicit network/algorithm/header capabilities.

Exit: offline proof verification exactly matches the pinned node.

### M4: DATUM Adapter Path

- Implement only the pinned Blake2b DATUM protocol.
- Preserve deterministic payout construction and direct block submission.
- Validate low-difficulty shares, admitted Work proofs, and found blocks as
  distinct classes.

Exit: one local miner or deterministic client fixture receives valid work and
submits accepted proofs with correct slot-0 attribution.

### M5: Isolated Three-Node Lab

- Run three GridPool nodes and attached chain nodes with unique identities.
- Exercise boundaries, payments, paid-once behavior, V2.2 sibling merge,
  restarts, temporary splits, reconnection, and shallow reorgs.
- Confirm SHA-256 peers and state are rejected.

Exit: deterministic convergence and sanitized evidence bundle.

### M6: Private Canary And Public Beta Decision

- Run a private no-intervention canary.
- Publish exact versions, checksums, known limitations, and operator recovery
  procedures.
- Begin public beta only after upstream chain and DATUM interfaces are stable
  enough to support operators safely.

Exit: explicit human release decision. No automatic deployment from completion
of prior milestones.

## Decisions Required From The Project Owner

- Exact Blake2b node repository and commit.
- Exact Blake2b DATUM repository and commit.
- Whether the chain is treated as a continuation across an activation height or
  as a distinct chain from GridPool's perspective.
- Public network name, GridPool network ID, DNS namespace, ports, and seeds.
- Address/replay semantics and whether payout addresses are shared with the
  SHA-256 chain.
- Support-fee policy and payout-slot policy.
- Initial operator/test miners and private canary infrastructure.
- Whether generic PoW abstraction changes should be proposed to the main
  GridPool repository or remain entirely in the fork until field-tested.

## Deliverables For The First New Task

The first task should not begin by editing the verifier. It should deliver:

1. A pinned upstream source matrix.
2. A code-level SHA256d/header-format assumption inventory.
3. A proposed chain-profile interface and dependency diagram.
4. A protocol/wire/persistence versioning proposal.
5. A defensive test matrix.
6. A phased implementation plan with explicit blockers.
7. A recommendation on separate repository versus temporary worktree after the
   security-integrated baseline is identified.

Only then should implementation begin.
