# GridPool Blake2b Fork Evidence and Architecture

Status: historical architecture evidence; superseded for authorization only by
the implementation plan.

Authorization precedence (2026-08-27): experimental implementation, public
fork development, testnet4 deployment, and conditionally gated experimental
mainnet deployment are authorized by
[`blake2b-gridpool-implementation-plan.md`](blake2b-gridpool-implementation-plan.md).
This document's evidence, isolation requirements, and defensive findings remain
controlling. Current source pins and baseline status are recorded in
[`blake2b-gridpool-evidence-and-profile-seam-2026-08-24.md`](blake2b-gridpool-evidence-and-profile-seam-2026-08-24.md).

Evidence cutoff: 2026-08-23 15:20 UTC. Upstream references are pinned to exact commits where possible. This document does not authorize deployment, mutation of SHA-256 GridPool nodes, or a public fork.

## Executive decision

Do **not** create or publish `gridpool-blake2b` yet.

Three independent gates are open:

1. GridPool has no reviewed, deliberately integrated security baseline from which to fork. The critical fix at `400fc6e1352ebba72cd557b3c782df52c54d77c8` still requires its exact independent retest, and the descendant P1 line at `f09ce5e6e2f90cf85c009a586b2d02db792ea4c4` explicitly remains a follow-on review candidate.
2. Knots PR #359 is open, marked mergeable but `unstable`, still calls itself an unfinished draft, and leaves its GBT extension BIP unwritten. The exact head also has live review findings as of this cutoff.
3. The only concrete Blake2b DATUM implementation located is an experimental fork. It is pinned below as an evidence source, but neither Knots nor OCEAN identifies it as the authoritative adapter, it supports only the Sia/profile-0 work layout, and its DATUM extension is not understood by GridPool.

The correct next artifact is a reviewed chain-profile seam plus characterization fixtures on an isolated development line, after the owner accepts this architecture. The existing SHA-256 profile must remain byte-for-byte and behavior-for-behavior unchanged.

## Evidence labels and scope

This report uses these labels deliberately:

- **Verified upstream fact**: observed in an upstream repository, GitHub API response, exact commit, or exact source file.
- **Local code observation**: observed in the local GridPool checkout named below.
- **Inference**: an engineering conclusion drawn from verified facts or code.
- **Unresolved**: requires an upstream decision, GridPool owner decision, independent review, or runtime evidence.

No source or configuration was changed while producing this report. The only repository change is this additive Markdown document. No tests were run because there is no implementation change to validate.

## Evidence pins

| Component | Pin at cutoff | Status and use |
|---|---|---|
| BIP 110 | [`bitcoin/bips:bip-0110.mediawiki`](https://github.com/bitcoin/bips/blob/master/bip-0110.mediawiki) | Status `Closed`; reduced-data temporary **soft fork**, not a PoW proposal. |
| Knots hard-fork PR | [`bitcoinknots/bitcoin#359`](https://github.com/bitcoinknots/bitcoin/pull/359) | Open, not merged, API `mergeable_state=unstable`, 36 commits. |
| Knots PR head | [`luke-jr/bitcoin@fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3`](https://github.com/luke-jr/bitcoin/tree/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3) | Exact `pow_hf_blake2b` head and `refs/pull/359/head`. |
| Knots PR base | `bitcoinknots/bitcoin:__base_29_pre_blake2b@b2ecf238f32cf49073da3f03707cbdc237dad7a7` | Exact merge target. |
| Experimental DATUM evidence fork | [`justinfilip/datum_gateway@56c31f40c83c3c8315694617082456677799e43a`](https://github.com/justinfilip/datum_gateway/tree/56c31f40c83c3c8315694617082456677799e43a) | Concrete implementation pin; authority unresolved. |
| DATUM pin named in PR review | [`justinfilip/datum_gateway@900269a7128344c18b82f69ac387429dbc1919e3`](https://github.com/justinfilip/datum_gateway/commit/900269a7128344c18b82f69ac387429dbc1919e3) | Superseded by `56c31f4`, which fixes the header-v2 bit in H1. |
| OCEAN DATUM upstream | [`OCEAN-xyz/datum_gateway`](https://github.com/OCEAN-xyz/datum_gateway) | Parent project; does not contain the four experimental Blake2b commits at cutoff. |
| Local reference-node audit | `boot-protocol@d8c1b540b58348181694779e8105f56f8a1ce66b` | Branch `codex/monitor-disk-retention`; not a security-integrated baseline. |
| Critical security candidate | `boot-protocol-security@400fc6e1352ebba72cd557b3c782df52c54d77c8` | Immutable exact retest target; not approved as stable. |
| Follow-on P1 candidate | `boot-protocol-security-p1@f09ce5e6e2f90cf85c009a586b2d02db792ea4c4` | Descends from `400fc6e`; not independently reviewed or deliberately integrated. |
| Current local main | `boot-protocol origin/main@97bc68ca772c9cd14406a6e365d60be4819ef235` | Predates both security candidates. |
| Local protocol spec | `gridpool-spec@81ca92750d0317e3fc806bab9e0cad2895be054e` | Read-only protocol context. |
| Local handbook | `gridpool-handbook@c611176655cfcdf238b142fb8166d25418b2e488` | V2.1/V2.2, statistical, and threat-model context. |
| Local simulations | `gridpool-simulations@4b9b921880c2bd540a7c9e07253c5fa9f60b18e3` | Read-only modeling context; no Blake normalization exists here. |
| Local DATUM work | `datum_gateway@c033cd6d888433c4de2ec835ec201fe60d6c4f3b` | Existing coinbase-selection work; **not** the Blake evidence fork. |
| Local SV2 integration | `gridpool-sv2-pool@1151f926c92f653d5a603559b9ad023929dcd3f6` | Future profile consumer; not part of the exhaustive reference-node audit. |

The packaged runtime `9ac862a` is explicitly excluded as a fork point because it predates the critical state-validation fixes.

## 1. BIP 110 and Knots PR #359

### 1.1 Do not conflate the proposals

**Verified upstream fact.** BIP 110 is titled “Reduced Data Temporary Softfork,” is a consensus soft fork, and is currently `Closed`. Its rules constrain data-bearing transaction and script fields for a temporary deployment. It does not specify Blake2b or a PoW change.

**Verified upstream fact.** Blake2b is implemented in the separate proposed hard-fork work in Knots PR #359, titled “Hardfork: New BLAKE2b proof-of-work algorithm.” “BIP-110-associated” is historical or community context, not a protocol identity.

### 1.2 Pull-request state

**Verified upstream fact.** GitHub and `git ls-remote` agreed at the cutoff:

- repository: `bitcoinknots/bitcoin`;
- PR: `#359`;
- state: open, not merged, not a GitHub draft;
- GitHub mergeability: mergeable, state `unstable`;
- head repository/branch: `luke-jr/bitcoin:pow_hf_blake2b`;
- head commit: `fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3`;
- base repository/branch: `bitcoinknots/bitcoin:__base_29_pre_blake2b`;
- base commit: `b2ecf238f32cf49073da3f03707cbdc237dad7a7`;
- commit count: 36;
- milestone: `29.4.1`;
- last API update observed: `2026-08-23T15:13:24Z`.

**Verified upstream fact.** Despite no longer using GitHub's draft flag, the PR description still says it is an unfinished draft and not ready for review.

### 1.3 Header format at the exact head

The first four bytes remain the serialized version. Bit 31, `0x80000000`, marks header v2 on the wire. Internally, `nVersion` has that marker cleared and `GetCompleteVersion()` restores it.

| Offset | Bytes | Field | Notes |
|---:|---:|---|---|
| 0 | 4 | complete version | High bit marks v2. |
| 4 | 32 | previous block hash | Existing field. |
| 36 | 32 | Merkle root | Existing transaction Merkle domain. |
| 68 | 4 | time on wire | `nTime`, or `nTime - m_time_offset` when flags bit 2 is set. |
| 72 | 4 | `nBits` | Existing compact target encoding. |
| 76 | 4 | `nNonce` | Existing nonce field. |
| 80 | 4 | `m_nonce2` | v2 only. |
| 84 | 4 | `m_nonce3` | v2 only. |
| 88 | 16 | `m_extranonce` | v2 only; 128-bit machine-level extranonce. |
| 104 | 4 | `m_time_offset` | v2 only. |
| 108 | 2 | `m_txcount` | v2 only; must equal the block transaction count. |
| 110 | 1 | `m_flags` | Bits 0–1 select ASIC profile; bit 2 selects time-offset use. |
| 111 | 1 | XOR-mask clear bits | v2 only. |
| 112 | 16 | XOR key | v2 only. |
| 128 | 4 | height | Signed 32-bit contextual height. |
| 132 | 32 | merge-mining right-hand side | v2-only commitment hook. |

Header v1 remains exactly 80 bytes. Header v2 is exactly 164 bytes. Source: [`src/primitives/block.h`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/src/primitives/block.h).

**Verified upstream fact.** Consensus rejects v2 headers before the configured deployment height, requires v2 at and after that height, requires the embedded height to equal the previous index height plus one, rejects flags bits 6–7, and requires `m_txcount` to match the actual transaction count. Bits 3–5 are not constrained at this head.

### 1.4 Exact PoW construction

**Verified upstream fact.** Header v1 still hashes by SHA256d. Header v2 does not hash the 164 serialized bytes directly. The exact construction in [`src/primitives/block.cpp`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/src/primitives/block.cpp) is:

1. Compute tagged-SHA256 commitments for the XOR key and optional XOR mask.
2. Compute a tagged-SHA256 hidden previous-header value. Profile 0 clears its first six bytes before ASIC work construction.
3. Compute tagged hash H1 over complete version, byte-reordered previous hash, embedded height, Merkle root, wire time, one reserved time-extension byte, `nBits`, transaction count widened to 32 bits, flags, XOR-mask clear bits, and the tagged XOR-key hash. H1's untagged payload is 119 bytes.
4. Compute tagged hash H2 over H1, 32 zero bytes, and the 32-byte merge-mining field.
5. First BLAKE2b-256 input: four zero bytes + H2 (32) + extranonce (16), for 52 bytes.
6. Second BLAKE2b-256 input depends on `m_flags & 3`:

| Profile | ASIC input | Length |
|---:|---|---:|
| 0 | hidden previous hash with six leading bytes cleared; nonce, nonce2, time offset, nonce3; first Blake hash | 80 |
| 1 | nonce, nonce2, nonce3, time offset; first Blake hash; H2 | 80 |
| 2 | 48 zero bytes; H2; nonce, nonce2, time offset, nonce3; first Blake hash | 128 |
| 3 | 80 zero bytes; H2; nonce, nonce2, time offset, nonce3; first Blake hash | 160 |

7. XOR the second Blake digest with the optional mask while reversing byte order into the returned `uint256`. A null XOR key produces a zero mask.

**Inference.** A generic `Blake2b(headerBytes)` or `Blake2b(first80)` implementation is consensus-wrong. A GridPool verifier must reproduce the full H1/H2/two-stage construction and all four profile layouts even if the first mining adapter only emits profile 0.

### 1.5 Activation and target rules

**Verified upstream fact.** `Consensus::Params::Blake2bHeight` defaults to `INT_MAX`. No production activation height is selected in this exact head. Regtest can override it with `-testactivationheight=blake2b@HEIGHT`.

**Verified upstream fact.** The node requires `-blake2b_headline` at startup. At the activation block, the configured headline must occur in the coinbase scriptSig. GBT exposes the activation-block value as `coinbaseaux.blake2b_headline`.

**Verified upstream fact.** The first Blake2b block applies a one-time target relaxation: the previously derived target is shifted left by `Blake2bTargetShift=20`, capped at the existing chain `powLimit`. Later blocks use the existing retarget/min-difficulty rules. Compact-target validity and PoW comparison remain the standard positive, non-overflowing, at-or-below-powLimit target with `hash <= target`. See [`src/pow.cpp`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/src/pow.cpp) and [`src/consensus/params.h`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/src/consensus/params.h).

**Unresolved.** The upstream node defines no Blake2b “difficulty one” normalization for GridPool share accounting. Reusing Bitcoin's SHA256d `0x1d00ffff` normalization, using the experimental DATUM Sia normalization, or introducing an exact chainwork-like score are different consensus choices. This must be specified before a Blake2b GridPool network accepts proofs.

**Required rule.** A submitted header's own `nBits` is not authority for “real block” classification. GridPool must obtain the expected target and active-chain membership from its attached node/validated chain context. A header satisfying an easier self-declared target is, at most, an invalid proof candidate.

### 1.6 Canonical vectors and upstream tests

**Verified upstream fact.** The exact head contains five header-v2 vectors in [`src/test/data/block_header_v2.json`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/src/test/data/block_header_v2.json). They cover all four profiles and include time-offset, non-null XOR-key, XOR-mask, and merge-hook cases. Each includes serialized header, H1, H2, both Blake digests, mask, ASIC input, and final block hash.

**Verified upstream fact.** [`test/functional/feature_powchange.py`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/test/functional/feature_powchange.py) exercises vector reproduction, startup headline requirements, the activation boundary, GBT opt-in, marker behavior, flags, height, transaction count, XOR behavior, and merge-hook fields.

### 1.7 RPC, GBT, and ZMQ behavior

**Verified upstream fact.** At/after activation, `getblocktemplate`:

- requires the client to include `blake2b` in its rules request;
- returns `!blake2b` in `rules`;
- returns `version` with bit 31 set;
- exposes the activation headline through `coinbaseaux` for the first v2 block;
- leaves `noncerange` as the original 32-bit range;
- does not return standardized fields for flags/profile, time offset, XOR key/mask, or merge-hook value.

The GBT implementation is in [`src/rpc/mining.cpp`](https://github.com/luke-jr/bitcoin/blob/fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3/src/rpc/mining.cpp).

**Verified upstream fact.** In the PR discussion, the maintainer states that flags, time offset, and the merge hook are legitimately miner-owned; the XOR key is pooled-mining scope; and a GBT Blake2b extension BIP is still “to be written.” This makes the current zero/default behavior possible but the interoperable pool interface unfinished.

**Verified upstream fact.** `getblockheader HASH false` returns the raw variable-length header: 80 bytes for v1 and 164 for v2. The high version bit self-identifies the raw encoding. At this head, verbose `getblockheader` clears the v2 marker from `version`/`versionHex` and does not report the v2 fields. PR #363 is a separate proposed RPC change, not part of PR #359.

**Verified upstream fact.** ZMQ `rawblock` carries the full serialized block, so consumers must parse a variable header boundary. `hashblock` remains a 32-byte hash notification. The PR's tests include v2 ZMQ coverage.

### 1.8 Live unresolved review items at cutoff

The following are material, current items rather than historical comments already fixed:

- The PR text still says unfinished/not ready for review.
- The exact head still checks only whether `-blake2b_headline` was supplied, not whether its value is non-empty. Review demonstrated that an explicit empty value starts the node and makes the activation-block substring check trivially succeed, while the functional test expects startup rejection.
- GitHub reports merge state `unstable`. Review also reported multiple CI failures associated with the stale special base; this audit did not establish a green exact-head CI run.
- The pool/GBT extension BIP and stable field vocabulary do not exist.
- ASIC-profile-to-vendor mapping and multi-vendor selection are not standardized.
- `noncerange` still describes only the 32-bit `nNonce`; deprecation versus changed behavior is undecided.
- Verbose header RPC detection is delegated to separate PR #363.
- The v2 fields increase `CBlockIndex` from 152 to 240 bytes, estimated in review at about 85 MB across the then-current mainnet index; the maintainer deferred optimization.
- A current `pow.h` review notes loss of the prior runtime null assertion while the proposed nonnull annotation creates a compile-time problem for a literal null.
- Light-client impact, timestamp extension, replay protection, and complete merge-mining semantics are not resolved in this PR. Some are explicitly considered out of scope.

## 2. DATUM source pin and compatibility

### 2.1 What was located

**Verified upstream fact.** A PR #359 reviewer named `justinfilip/datum_gateway@900269a` as an experimental implementation. The repository is a fork of `OCEAN-xyz/datum_gateway`. At the cutoff its `master` is `56c31f40c83c3c8315694617082456677799e43a`, four commits ahead and zero behind its OCEAN parent. The fourth commit fixes a consensus-relevant bug by including the header-v2 marker bit in H1.

The four-commit comparison is available at [`OCEAN-xyz:master...justinfilip:master`](https://github.com/justinfilip/datum_gateway/compare/OCEAN-xyz:master...master).

**Decision.** Pin `56c31f40c83c3c8315694617082456677799e43a` as the **evidence/fixture source**, not as an approved production dependency. Pinning `900269a` would knowingly omit the subsequent H1 correctness fix.

**Unresolved authority.** No primary statement from OCEAN, Knots, or a designated GridPool owner says this fork is the authoritative Blake2b DATUM implementation. Therefore DATUM implementation work remains blocked pending explicit source approval and a stable handshake/spec decision.

**Local code observation.** The existing checkout at `/home/keegreil/Documents/GitHub/datum_gateway` is on `force-coinbase-selection-mode@c033cd6...` and is not this Blake2b fork. It must not be treated as the pinned source by path alone.

### 2.2 What the experimental fork implements

**Verified upstream fact.** The fork preserves SHA256d as its default behavior and auto-detects `!blake2b` in GBT. It builds 164-byte blocks and locally verifies Blake2b shares. Transaction IDs and the transaction Merkle tree continue to use SHA256d.

**Verified upstream fact.** Its mining work is specifically Sia-style/profile 0:

- the second Blake pass always hashes an 80-byte work buffer;
- it supports 64-bit submitted nonce and 64-bit submitted time material;
- it serializes those into the v2 nonce/time fields;
- it does not implement selectable profiles 1–3 for attached miners;
- if `allow_hasher_time_rolling` is set, it assigns the header flags to `UseTimeOffset`, which also means profile bits remain zero.

**Verified upstream fact.** The fork reads experimental GBT names such as `header_version`, `transaction_count`, `h1_flags`/`header_flags`, `time_offset`, `xor_key`, `xor_key_mask_clear_bits`, and `merge_mining_rhs`. Exact Knots PR #359 emits none of those names. Against the exact node it falls back to the rule/version marker and zero/default parameters, yielding profile 0, null XOR, null merge hook, and optional gateway-configured time rolling.

### 2.3 DATUM wire extension

The fork extends the client-to-pool PoW submission while preserving the legacy prefix:

- sets submission flag `0x08` for Blake2b;
- uses reserved-byte bit `0x01` to signal time-offset use;
- adds optional section `0x03`: algorithm byte `1`, then 64-bit time and 64-bit nonce;
- adds optional section `0x04`: 32-bit time-on-wire;
- then sends the existing job/Merkle section `0x01`, coinbase section `0x02`, and terminator `0xFE` as needed.

**Local code observation.** `boot-protocol/Program.cs:3702-3833` recognizes only legacy flags `0x01`/`0x02`/`0x04`, requires a 12-byte extranonce, and only parses optional sections `0x01` and `0x02`. It throws on unknown `0x03` or `0x04`. It ignores the candidate's algorithm flag `0x08` and time-offset reserved bit. Current GridPool is therefore wire-incompatible with the pinned evidence fork.

**Verified upstream fact.** The fork's Blake share-target base is 28 `ff` bytes followed by four zero bytes, shifted right by a power-of-two difficulty exponent. It also rounds accounting difficulty to a power of two. This is not a target normalization specified by Knots PR #359 or GridPool V2.2.

## 3. Security baseline decision

### 3.1 Observed branches

**Local code observation.** The exact critical branch documentation says `400fc6e1352ebba72cd557b3c782df52c54d77c8` is a release candidate for independent red-team verification and prohibits a stable tag before that retest.

Its essential controls are:

- a header meeting its own target is only a block candidate;
- payout/round/paid-lineage transitions require exact active-chain confirmation from the attached node;
- proofless state bundles cannot introduce winners or fast-forward state;
- imported winners/state IDs are reconstructed from fully validated proofs;
- remote state cannot replace locally established paid lineage.

**Local code observation.** `f09ce5e6e2f90cf85c009a586b2d02db792ea4c4` descends from `400fc6e` and adds P1 controls including bounded DATUM reads, payout fail-closed behavior, authoritative parent handling, state-fetch budgets, one/two-block reorg handling, RPC grace hardening, dependency locks, and image provenance. Its own documentation says it must not be described as independently verified until review and deliberate integration are recorded.

**Local code observation.** The audited reference checkout `d8c1b540...` and `origin/main@97bc68c...` do not contain these two security commits. The security candidates live in separate worktrees/branches.

### 3.2 Baseline result

**Conclusion.** There is no reviewed security-integrated GridPool baseline at this cutoff.

The minimum acceptable fork-point gate is all of:

1. exact `400fc6e` RT-2026-041 and RT-2026-042 independent retest recorded;
2. P1 follow-on review recorded;
3. both lines deliberately integrated into the chosen current runtime branch;
4. full locked restore/test and security checks green on the integrated commit;
5. attached-node regtest acceptance covers share-first and notification-first block events, valid proof-backed V2.2 sibling merge, and at least a two-block reorg;
6. the resulting exact commit is named and protected as the Blake fork point.

## 4. Exhaustive local assumption audit

Audit target: `boot-protocol@d8c1b540b58348181694779e8105f56f8a1ce66b`. Line numbers below refer to that pin.

### 4.1 Runtime validation, parsing, and scoring

| Area and files | Local observation | Required fork treatment |
|---|---|---|
| `Utils/BitcoinHashes.cs:9,109-176` | Hard-codes mainnet difficulty-one/powLimit `0x1d00ffff`; requires 160 hex chars; hashes header with SHA256d; fixed parent/time/`nBits` offsets 4/68/72; only main/regtest powLimit switch. Hash-display helpers guess byte order by leading zeros. | Route through a pinned chain/header profile. Replace heuristic byte-order handling inside consensus paths with explicit profile rules. Preserve existing behavior in the SHA profile. |
| `Services/BootShareVerifier.cs:42,118-176,185-303` | Requires 80 bytes, fixed offsets 4/36/72, SHA256d header PoW, SHA256d Merkle, `0x1d00ffff/hash` difficulty. It accepts any positive encoded target and sets `IsBlock` from that untrusted target. | Profile owns header parsing, PoW, target decoding, and score. Real-block status must come only from attached-node active-chain confirmation. |
| `Utils/BitcoinBlockParser.cs:5-18` | Assumes the transaction count begins at byte 80 in every raw block. | Header codec returns the exact consumed header length; block parser begins after that boundary. |
| `Utils/BitcoinTransactionParser.cs:80-92,244-246` | Computes legacy txid/wtxid-stripped txid with SHA256d. | Keep unchanged; this is transaction identity, not header PoW. |
| `Models/MiningModels.cs:8` | Public DTO comment and shape imply an 80-byte header and have no chain/PoW/header identifiers. | Add an explicit proof-domain envelope; do not infer algorithm from length alone. |
| `Utils/BootRequestGuards.cs:24-66` | HTTP guard rejects anything except 80-byte header hex before profile-aware validation. | Bound request size first, validate network/domain, then apply profile header-length rules. |
| `Services/BootProtocolStateService.cs:2012-2850` | Admission, pulse/optimistic thresholds, ordering, best share, and state mutation all depend on `double Difficulty`. | A chain profile must return a canonical, deterministic work score plus display value. Any score representation change is a fork-network consensus/schema change. |
| `Models/BootSnapshotReconciliation.cs:103-116` | V2.2 exact-family union ranks `Difficulty` descending then share ID ascending. | Preserve this ordering invariant using the Blake network's specified canonical work score; do not mix profiles or networks in one family. |
| `Services/DashboardReadModelService.cs:9,496-578` | Network difficulty, block-quality labels, survival estimates, and hashrate displays use SHA difficulty-one and assume comparable `double` units. | Treat as profile-derived telemetry. Label the algorithm and unit; do not let display math feed consensus. |

### 4.2 Attached node, raw blocks, ZMQ, and RPC

| Area and files | Local observation | Required fork treatment |
|---|---|---|
| `HostedServices/BitcoinZmqSubscriber.cs:126-146` | `rawblock` accepts length >=80, slices exactly 80 bytes, SHA256d-hashes it, and passes the raw block to an 80-byte-boundary coinbase parser. | Parse the version marker and exact profile length before hashing or locating transactions. Reject malformed/truncated v2 payloads. |
| `BitcoinZmqSubscriber.cs:204-221` | Telemetry hard-codes raw-header payload bytes to 80. | Record actual header bytes and header-format ID. |
| `HostedServices/BitcoinRpcReconciliationService.cs:65-89` | Verifies only RPC `chain` against `PoolConfig.BitcoinNetwork` (`main`, `test`, or `regtest`). A Blake hard fork could still report the same chain string. | Require an explicit attached-node fingerprint/capability: genesis, chain-profile ID, activation parameters, head implementation pin/version, and expected `blake2b` support. |
| `BitcoinRpcReconciliationService.cs:211-229` and `Services/BitcoinRpcClient.cs:76-80` | Calls raw `getblockheader HASH false`, then feeds it to the fixed 80-byte evaluator. | Variable header decoding is required. Until verbose RPC is stable, use the raw marker plus node capability/profile pin. |
| RPC health/telemetry | `getnetworkhashps` and dashboard conversions assume SHA-like presentation and TH/s semantics. | Keep operational telemetry algorithm-qualified; Blake hashrate must not be labeled or compared as SHA TH/s without an explicit conversion. |
| `Models/BitcoinNotificationModels.cs` | Carries hash/height but not chain profile or header format. | Attach the local authoritative chain profile to observations before state processing. |

### 4.3 DATUM ingress

| Area and files | Local observation | Required fork treatment |
|---|---|---|
| `Program.cs:3702-3833` | Fixed legacy PoW-submit layout, 32-bit time/nonce/version, 12-byte extranonce, optional sections only `0x01` and `0x02`; no algorithm capability or version negotiation. | Implement only after authoritative DATUM spec/pin. Parse an explicit algorithm/version and bound every extension before allocation. Preserve legacy parsing as a separate SHA code path. |
| `Program.cs:2639-2663` | Rebuilds coinbase/Merkle with SHA256d, then constructs an 80-byte header and SHA256d-hashes it. | Keep coinbase/Merkle SHA256d; delegate only header construction/PoW to profile and authoritative adapter data. |
| `Program.cs:2665-2674` | DATUM achieved difficulty uses `(2^224-1)/hash`, which already differs slightly from `BootShareVerifier`'s `0x1d00ffff/hash`. | Remove duplicate consensus math behind one profile result. Characterize existing SHA behavior before refactoring. |
| `Program.cs:3013-3071` | Legacy helper reconstructs 80 bytes/SHA256d and currently returns success after incomplete target logic. | Confirm call reachability; delete or isolate only in a later reviewed implementation. It must never become Blake authority accidentally. |
| DATUM cached job model | Caches previous hash, `nBits`, Merkle/coinbase data, and version without an algorithm/header-format identity. | Cache key and submission must bind job ID to chain profile, activation regime, and exact target context. |
| Pinned experimental extension | Uses flag `0x08`, sections `0x03`/`0x04`, 64-bit time/nonce, and profile 0. | Current parser must fail closed, as it does. Future acceptance requires negotiation and conformance vectors, not opportunistic parsing. |

### 4.4 UDP, peer sessions, and HTTP APIs

| Area and files | Local observation | Required fork treatment |
|---|---|---|
| `Services/BootPeerUdpShareCodec.cs:8-159` | Codec version 3 embeds a fixed 80-byte header. The payload itself contains no magic, GridPool network, chain, PoW, or header-format ID. | New codec version and new encrypted associated-data domain; variable bounded header with explicit proof-domain tuple. |
| `Services/BootPeerUdpChainTipCodec.cs:8-86` | Magic `GPT1`, version 1, fixed 80-byte header, fixed 93-byte payload, SHA256d hash. | New magic and version for the Blake network; profile-aware header length/hash; never accept `GPT1` in Blake mode. |
| `Models/BootProtocolVersions.cs` | Compatibility compares GridPool network ID plus consensus/schema/API/transport/UDP versions. It has no chain ID, PoW ID, header format, or activation fingerprint. | Extend the signed hello and every compatibility envelope with a proof-domain tuple. Missing fields fail closed on the Blake network. |
| `Services/BootPeerIdentity.cs:10-164` | Signed hello domain is `GridPool peer session v2 hello` and signs `NetworkId`/versions but no chain profile. | Use a new hello domain/version and sign the complete proof-domain tuple before deriving/using a session. Keep Ed25519/X25519 algorithms unchanged. |
| `Models/BootProtocolModels.cs:1-45` | `RecordedShareSubmission` and `BootShareProof` contain header, coinbase, Merkle, parent, and difficulty but no proof domain. | Store the domain tuple in every proof; validation must compare it before header/Pow processing. |
| `PeerShareAnnouncement`, `BootPeerSessionHello`, `BootChainTipAnnouncement`, `BootStateBundle` | Carry `NetworkId` and protocol versions, but not underlying chain/PoW/header/activation IDs. | Add domain tuple to all four. Cross-domain input fails before proof validation, state comparison, peer learning, or chain-tip telemetry. |
| `Controllers/BootPeerController.cs:50-116` | HTTP peer share guard validates fixed header shape before network compatibility; then compatibility runs before state submission. | Check envelope/domain as early as bounded JSON parsing permits; do not teach peers or call proof code first. |
| `Controllers/MiningApiController.cs:247-309` | Local/adapter share API has no explicit chain-profile field and relies on node config. | Require an authenticated adapter capability/job binding; reject mismatched profile before proof processing. |

### 4.5 State IDs, persistence, and configuration

| Area and files | Local observation | Required fork treatment |
|---|---|---|
| `Utils/PoolState.cs` | Persists tip target/hash, proofs, state bundles, payout lineage, and telemetry without chain/PoW/header-format/activation fingerprint. Metadata contains only GridPool `NetworkId` and protocol versions. | Persist a mandatory chain-profile fingerprint at the root and in proof/bundle records. Refuse mismatched state; never rewrite it into the configured domain. |
| `Utils/BootPortalPaths.cs` | State/history paths are environment-configurable but default to generic `pool_state.json` and sibling history/telemetry names. | Blake package must use a separate data root and explicit paths, not merely alternate filenames in the SHA directory. |
| `BootProtocolStateService.cs:5481-5580` | On load, missing persisted network is filled from config, but a non-empty mismatched persisted network is not rejected. Local protocol versions overwrite persisted metadata. | Blake profile requires fail-closed exact metadata comparison before proof/state normalization. No implicit cross-network migration. |
| `BootProtocolStateService.cs:9480-9530` | Candidate/state IDs use SHA256 over domain strings that include consensus version, GridPool network ID, payout variant, ordered proof identity/difficulty, and boundary hash. | Keep SHA256 as GridPool content-ID hash. Introduce a new GridPool network ID and include the full proof-domain fingerprint in the preimage. |
| `BootSnapshotReconciliation.cs:52-75` | Snapshot-family ID is SHA256 over consensus, network, predecessor, boundary hash/height, and payout variant. | Keep SHA256; make its `network` input the complete chain-separated family network identifier. |
| `PoolConfig.cs` and package/install scripts | Separate `BitcoinNetwork` and `BootNetworkId`, but no PoW/header/activation profile. Default ports and state paths are SHA deployment values. | Add immutable profile selection and package-specific data/identity/ports. Do not permit runtime flipping of a populated node. |
| State persistence | Atomic temp write + replace/backup is present; history is a separate bounded file. | Preserve atomicity/backups. Profile metadata must be validated for primary, backup, and history before import. |

### 4.6 Test assumptions and gaps

| Test area | Existing assumption/coverage | Blake requirement |
|---|---|---|
| `boot.tests/BootRequestGuardsTests.cs` | Defines valid header as 160 hex chars and asserts “80 bytes.” | Parameterized v1/v2 profile guards plus malformed marker/length cases. |
| `boot.tests/ShareAttributionTests.cs` | Central fixture is an 80-byte SHA header; helper mutations use fixed Merkle/parent offsets and SHA256d; asserts relayed length 160; covers current UDP, chain-tip, payout, state, and many V2.2 behaviors. | Preserve all SHA assertions unchanged. Add separate Blake fixtures through the profile API; never rewrite the shared fixture globally. |
| `boot.tests/SegwitShareValidationTests.cs` | Valuable txid-versus-wtxid/Merkle coverage with 80-byte header. | Run unchanged under SHA and add a v2 header wrapper proving transaction/Merkle hash domains remain SHA256d. |
| `boot.tests/BitcoinZmqNotificationTests.cs` | Raw-block fixture places coinbase after byte 80; tests sequence/health but no variable header. | Add 164-byte rawblock, truncation, marker/length mismatch, and hashblock/rawblock correlation. |
| `boot.tests/BitcoinRpcClientTests.cs` | Covers RPC parsing/topics, not v2 raw headers or node capability fingerprints. | Mock 80/164 `getblockheader`, `!blake2b`, missing/incorrect capability, and same `chain` string on wrong profile. |
| `boot.tests/BitcoinRpcRecoveryPlannerTests.cs` | Height/hash reorg planner is algorithm-agnostic. | Reuse it, then add activation-boundary one/two-block reorg integration tests with authoritative targets. |
| `boot.tests/SnapshotReconciliationTests.cs` | Covers bounded exact-family union and deterministic ranking using current proof shape. | Add domain mismatch rejection and exact work-score serialization; retain 64-member and reserve bounds. |
| DATUM tests | Current node has no unit fixture for the experimental `0x08`/`0x03`/`0x04` extension. | Import authoritative byte vectors only after source approval; include legacy SHA byte-for-byte regression and malformed-length fuzzing. |
| Persistence tests | Temporary state paths and some metadata behavior are covered; no fail-closed chain-profile mismatch. | Test primary, backup, and history mismatch, mixed proofs, and no automatic migration. |

## 5. Hash-domain classification

Changing header PoW does not authorize changing any other hash domain.

| Domain | Current implementation | Blake fork rule |
|---|---|---|
| Header/PoW | SHA256d over 80-byte header in `BitcoinHashes`, `BootShareVerifier`, ZMQ, UDP chain tip, and DATUM reconstruction. | Profile-specific. SHA profile remains exact; Blake v2 uses pinned H1/H2/two-stage BLAKE2b construction and explicit byte order. |
| Transaction ID and Merkle | SHA256d in `BitcoinTransactionParser`, `BootShareVerifier`, and DATUM coinbase/Merkle helpers. | **Do not change.** Knots v2 still commits the ordinary Bitcoin transaction Merkle root. |
| Address/checksum | Base58Check uses SHA256d; Bech32/Bech32m uses its own polymod; script/address network rules live in `BitcoinScript`. | **Do not change algorithms.** Configure the intended address prefixes/HRP only if the fork chain actually changes them. |
| GridPool proof/content IDs | SHA256 share ID over normalized `header|coinbase`; legacy variant also includes miner; SHA256 state/candidate/family/placeholder IDs with textual domains. | Keep SHA256, but version/domain the preimages and include the complete proof-domain tuple. A share-ID migration changes persisted/relayed identity and needs explicit schema/version handling. |
| Cryptographic peer identity/session | Ed25519 node IDs/signatures, X25519 shared keys, SHA256-based key derivation, AES-GCM frames/UDP, random nonces. | **Do not change primitives because PoW changed.** Change signed/KDF/AEAD domain strings and use separate keys/identity files for the Blake network. |
| Operational anonymization/jitter | SHA256 used for dashboard journal keys, notification jitter, node-derived selection, and non-consensus labels. | Algorithm can remain SHA256. Include network/profile in inputs where cross-network correlation or collision could matter. |

## 6. Minimal chain-header/PoW profile seam

This is an architecture proposal, not an implementation instruction.

### 6.1 Separate responsibilities

Use one immutable `ChainProfile` selected at process startup. It composes four narrow responsibilities:

1. `HeaderCodec`
   - identify format from a bounded prefix and configured activation context;
   - parse/serialize exact bytes;
   - expose parent, Merkle root, wire/logical time, compact target, embedded height, transaction count, and profile flags;
   - return exact consumed bytes for raw-block parsing.
2. `PowAlgorithm`
   - compute the canonical 32-byte numeric hash from a parsed header;
   - make byte order explicit at the API boundary;
   - verify canonical upstream vectors.
3. `TargetRules`
   - decode compact target with sign/overflow/canonical checks;
   - obtain/validate the **expected** target from attached-node/chain context;
   - apply activation/retarget rules or reject when authoritative context is unavailable;
   - return a deterministic GridPool work score and a non-consensus display value.
4. `ActivationRules`
   - decide allowed header version from trusted height/parent;
   - validate v2 embedded height, flags, transaction count, and activation-only requirements;
   - expose a stable activation fingerprint for peer/state compatibility.

### 6.2 Required profile identity

Every profile has immutable, serialized identifiers:

```text
chain_id
genesis_hash
pow_algorithm_id
header_format_id
activation_rule_id
target_rule_id
work_score_rule_id
profile_revision
```

The tuple, not a display name, is the `proof_domain_id`. Its canonical encoding is hashed into a fixed fingerprint used by API, peer, state, and job envelopes.

### 6.3 Two initial profiles

`bitcoin-sha256d-header-v1`:

- exact 80-byte serialization;
- exact current fixed offsets;
- exact SHA256d and current display byte order;
- exact current compact target and `0x1d00ffff/hash` GridPool difficulty behavior;
- no activation transition;
- all existing SHA tests remain unchanged.

`knots-pr359-blake2b-header-v2-fee27ccf`:

- evidence-only name until upstream stabilizes;
- v1 below trusted activation and v2 at/above it;
- exact 164-byte serialization and all four PoW profiles;
- exact one-time 20-bit target shift and existing subsequent target rules;
- exact vector pin `fee27ccf...`;
- activation height/headline and node implementation fingerprint mandatory, never defaulted;
- mining ingress may advertise `profile-0-sia` only if the approved DATUM adapter is profile-0-only, while verifier support remains all four profiles.

**Inference.** Pinning the upstream commit in the provisional profile name prevents silent consensus drift while PR #359 is force-pushed. A later upstream revision is a new profile revision until vectors prove equivalence.

### 6.4 Work-score decision

The seam must not expose `double` as its only consensus result. A result should contain:

```text
pow_hash_u256
expected_target_u256
meets_expected_target
canonical_work_score   # exact integer or canonical decimal/rational string
display_difficulty     # explicitly non-authoritative
```

**Unresolved owner decision.** Choose and specify the Blake canonical work score. Options include:

- an exact profile-defined reference target divided by achieved hash;
- an exact chainwork-like inverse-hash score;
- the experimental DATUM Sia/power-of-two accounting rule.

Whichever is chosen must preserve deterministic descending order, stable serialization, tie-breaking by share ID, threshold semantics, statistical interpretation, and bounded arithmetic. It must not be inferred from the submitted `nBits`.

## 7. Required domain separation

| Surface | Required Blake separation |
|---|---|
| GridPool network | New explicit network ID; never `mainnet-beta` or an existing V2.2 ID. |
| Proof | Store and validate `proof_domain_id` on every submission/proof before PoW. |
| Peer hello | New signed hello domain/version containing full profile fingerprint. |
| HTTP/session APIs | Capability endpoint and request envelope identify profile revision; mismatch fails before peer learning/state/proof work. |
| DATUM | Explicit algorithm/extension version negotiation and job binding. Do not infer solely from header length or a flag in an unbound submission. |
| UDP shares | New codec version, explicit domain fingerprint, bounded variable header. |
| UDP chain tip | New magic distinct from `GPT1`, new version, new AEAD associated-data domain. |
| State bundles | Full profile fingerprint in bundle and every family member; mismatches cannot enter candidate comparison. |
| State IDs | Full profile fingerprint in SHA256 preimage. |
| Persistence | Separate absolute data root, state/history/telemetry/config files, backups, logs, and temporary files. Fail closed on metadata mismatch. |
| Identity | Separate Ed25519/X25519 keys and node ID; never copy SHA-network private identity. |
| Discovery/peers | Separate seeds, address book, DNS names, allowlists, and public endpoints. |
| Ports | Separate Web/API, peer TCP/WebSocket, peer UDP, DATUM, SV2, RPC/ZMQ bindings. No shared listener that guesses the network. |
| Packages/images | Separate package/service/container names, registry tags, volumes, health checks, dashboards, and update channel. |
| Attached node | Separate datadir, RPC credentials, cookie, ZMQ endpoints, and capability fingerprint. Never point the fork at SHA production RPC/ZMQ. |

Cross-network traffic must fail before proof validation, state ID comparison, state mutation, peer discovery, payout logic, or chain-tip measurement. Encryption alone is not domain separation unless the signed hello, KDF/AEAD associated data, and payload envelope all bind the same profile.

## 8. Defensive test matrix

| Category | Required cases | Pass condition |
|---|---|---|
| SHA regression | Existing full suite; byte-for-byte header hashes, share IDs, state IDs, UDP frames, DATUM legacy submissions, payout order. | No existing SHA fixture or externally visible behavior changes. |
| Canonical v2 vectors | All five upstream vectors; serialized bytes, H1, H2, both Blake hashes, ASIC inputs, mask, final hash; all profiles. | Exact equality to `fee27ccf` data. |
| Byte order | Raw digest, `uint256` numeric value, display hash, target comparison, RPC hash, ZMQ hash. Include reversed-only false positives. | One explicit conversion at each boundary; no leading-zero heuristic in consensus. |
| Header parsing | 80/164 exact lengths; marker/length mismatch; truncated every field; extra trailing bytes; v1 with v2 data; flags 6/7. | Malformed input rejected before hashing/state. |
| Target decoding | zero mantissa, negative bit, overflow, noncanonical compact, above powLimit, equality edge, one above/below. | Matches attached node/reference implementation. |
| Expected target | easier submitted `nBits`, harder submitted `nBits`, wrong retarget, first-v2 20-bit shift/cap, next block, testnet min-difficulty cases. | `IsBlock`/payment never derives from self-declared target; invalid expected target rejects proof. |
| Activation boundary | H-1 v1 valid/v2 invalid; H v1 invalid/v2 valid; H+1; wrong embedded height; missing/wrong headline; txcount mismatch. | Exact Knots behavior with trusted height. |
| GBT/RPC | Missing `blake2b` opt-in; `!blake2b`; high version bit; activation headline; absent extension fields; raw 80/164 headers; verbose RPC ambiguity. | Adapter refuses unsupported capability and records exact provisional interface. |
| ZMQ raw block | v1 and v2 rawblock; coinbase starts after correct header; hashblock first/rawblock first; duplicate; sequence gap; truncation. | Same authoritative block correlated once; correct height and header bytes. |
| DATUM legacy | Existing SHA submission bytes and responses. | Exact behavior preserved. |
| DATUM Blake | Approved extension vectors for `0x08`, `0x03`, `0x04`; 64-bit nonce/time; time-on-wire; cached/uncached job; profile capability. | Job/profile bound; unsupported profiles reject explicitly. |
| DATUM malformed/resource | Unknown algorithm, duplicate/reordered sections, truncated 64-bit fields, excessive username/body, stalled read, invalid job ID, profile downgrade. | Bounded fail-closed behavior with no large allocation/state mutation. |
| Transaction/Merkle | Legacy and SegWit coinbase txid, wtxid trap, Merkle branch order, v2 header Merkle field. | SHA256d txid/Merkle unchanged and slot 0 derived from actual coinbase. |
| Payout attribution | Correct GridPool output set, truncated/fallback template, miner-label spoof, profile mismatch. | Actual coinbase output 0 is miner; all expected winners validated. |
| Paid once | candidate before node confirmation, share-first, notification-first, duplicate share/notification, competing candidate. | Exactly one transition only after attached-node active-chain confirmation. |
| Reorg | One- and two-block reorg before/at/after activation; paid boundary removed/replaced; RPC outage/grace. | Snapshot families unwind/reapply safely; paid lineage never forged or paid twice. |
| V2.2 sibling union | Valid siblings, omissions, duplicates, paid proofs, 64-member bound, reserve 897, rank ties. | Idempotent/commutative monotonic union and deterministic payout list. |
| State bundles | Full valid proofs; proofless winners; invalid proof; claimed total difficulty lie; mixed domain; missing profile; wrong payout variant. | Reconstruct from proofs; cross-domain/proofless bundles fail before mutation. |
| Persistence | Correct profile restart; wrong profile in primary, backup, or history; copied SHA state; changed identity; partial temp file. | Exact-profile load or fail closed; no implicit rewrite/migration. |
| Cross-network | SHA peer/API/UDP/DATUM/state into Blake and vice versa; same chain string but wrong profile; replayed encrypted frame. | Rejected at the earliest envelope/capability layer. |
| Resource bounds | 164-byte headers under request/UDP limits, max coinbase/Merkle, DATUM connection/body/read limits, state fetch budgets, archived bundles, telemetry retention. | Existing bounds preserved or explicitly raised with memory/disk tests; no unbounded family/history growth. |
| End-to-end regtest | Approved Knots pin + approved DATUM pin + isolated GridPool profile; mine across activation; submit shares; find block; restart; one/two-block reorg. | Node accepts canonical blocks, GridPool pays once, converges, survives restart/reorg, and SHA lab remains untouched. |

## 9. Implementation sequence after gates clear

1. Record the exact reviewed, integrated GridPool baseline and protect it.
2. Obtain owner approval of the authoritative Knots and DATUM commits plus licensing/provenance.
3. Freeze upstream vectors and add characterization tests without changing runtime behavior.
4. Introduce the profile interfaces with only the existing SHA implementation; prove the full SHA suite and serialized artifacts are unchanged.
5. Add fail-closed proof-domain fields, capability negotiation, persistence metadata, and new UDP/session domains under new protocol/schema versions.
6. Add the pinned Blake header codec/PoW verifier and vector tests; no network or DATUM ingress yet.
7. Specify and review exact work-score normalization and statistical meaning.
8. Add attached-node capability/target authority and variable rawblock/RPC handling.
9. Add the approved DATUM adapter behind explicit negotiated capability.
10. Run isolated regtest activation, payout, restart, and reorg acceptance.
11. Only then create/publish the separate fork/package, with separate identities, state, peers, ports, and infrastructure.

At no step should SHA production nodes, Main/Oregon, the red-team retest, appliance acceptance, or the current soak be used as an experimental Blake environment.

## 10. Open decisions and owners

| Decision | Needed from | Blocking effect |
|---|---|---|
| Exact reviewed/integrated GridPool fork point | GridPool security/release owners | Blocks any public fork. |
| Exact Knots revision and activation parameters | Knots proposal owner + GridPool owner | Blocks consensus implementation beyond pinned characterization. |
| Authoritative DATUM repository/commit/spec | DATUM/Knots owner + GridPool owner | Blocks DATUM implementation. |
| Stable GBT extension/capability fields | Knots mining-interface owner | Blocks general multi-profile/pooled interoperability. |
| Supported ASIC profiles at launch | GridPool mining owner | Blocks adapter capability declaration. |
| Blake GridPool work-score normalization | Protocol/spec owner + statistical review | Blocks proof ranking, thresholds, persistence, and state consensus. |
| Fork chain ID/genesis/address rules | Chain owner | Blocks final domain fingerprint and package configuration. |
| Activation headline/height | Chain owner | Blocks end-to-end acceptance fixtures. |
| New GridPool network/protocol/schema/UDP versions | GridPool protocol owner | Blocks wire/state implementation. |

## Final recommendation

Wait. Preserve this report and the exact upstream pins, but do not create `gridpool-blake2b` publicly and do not branch from `d8c1b54`, `origin/main`, `9ac862a`, `400fc6e`, or `f09ce5e` as if any were the reviewed integrated baseline.

The safe near-term work is limited to owner-reviewed characterization vectors and a behavior-preserving SHA profile seam. Blake runtime, DATUM ingestion, packages, identities, peers, and deployment remain blocked until the security baseline and upstream interfaces are explicit and reviewed.
