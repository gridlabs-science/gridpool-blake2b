# Starting Prompt: GridPool Blake2b Fork

You are starting a scoped GridPool compatibility effort for the proposed
BIP-110-associated Blake2b proof-of-work chain.

Your first job is evidence collection and architecture design. Do not deploy
anything, do not modify SHA-256 production nodes, and do not begin by replacing
SHA256 calls.

## Read First

- `/home/keegreil/Documents/GitHub/boot-protocol/docs/blake2b-gridpool-fork-handoff.md`
- `/home/keegreil/Documents/GitHub/gridpool-handbook/AGENTS.md`
- `/home/keegreil/Documents/GitHub/gridpool-handbook/handbook/project-overview.md`
- `/home/keegreil/Documents/GitHub/gridpool-handbook/handbook/protocol-v21.md`
- `/home/keegreil/Documents/GitHub/gridpool-handbook/handbook/protocol-v22.md`
- `/home/keegreil/Documents/GitHub/gridpool-handbook/handbook/statistical-foundation.md`
- `/home/keegreil/Documents/GitHub/gridpool-handbook/handbook/security-and-threat-model.md`
- `/home/keegreil/Documents/GitHub/boot-protocol/docs/gridpool-v2.2-monotonic-snapshot-reconciliation-draft.md`
- `/home/keegreil/Documents/GitHub/boot-protocol/docs/v2.2-cutover.md`
- `/home/keegreil/Documents/GitHub/boot-protocol/docs/security-privacy-review.md`

Relevant repositories:

- Reference node: `/home/keegreil/Documents/GitHub/boot-protocol`
- Protocol docs: `/home/keegreil/Documents/GitHub/gridpool-spec`
- Project handbook: `/home/keegreil/Documents/GitHub/gridpool-handbook`
- Simulations: `/home/keegreil/Documents/GitHub/gridpool-simulations`
- Existing DATUM work: `/home/keegreil/Documents/GitHub/datum_gateway`
- SV2 integration: `/home/keegreil/Documents/GitHub/gridpool-sv2-pool`
- Immutable critical-fix target:
  `/home/keegreil/Documents/GitHub/boot-protocol-security` at `400fc6e`
- Follow-on P1 hardening:
  `/home/keegreil/Documents/GitHub/boot-protocol-security-p1` at `f09ce5e`

Development baseline update (2026-08-24):

- `develop` now contains the exact critical candidate `400fc6e` through merge
  commit `d542af6`.
- The immutable candidate is tagged
  `security-rt-041-042-retest-candidate` and remains independently testable.
- Independent red-team verification is still pending. Treat `develop` as an
  unverified development baseline, not a stable or deployable security release.
- The broader P1 branch remains separate. Do not silently copy or merge it into
  the Blake2b work.

Primary upstream sources to verify directly:

- <https://github.com/bitcoin/bips/blob/master/bip-0110.mediawiki>
- <https://github.com/bitcoinknots/bitcoin/pull/359>
- <https://github.com/OCEAN-xyz/datum_gateway>

## Critical Facts And Constraints

- BIP-110 itself is a reduced-data softfork. The Blake2b work is a subsequent
  proposed hard fork; do not conflate them.
- Knots PR #359 was open and changing at the handoff's 2026-08-23 evidence
  cutoff. Query its current head commit and status before relying on it.
- The authoritative Blake2b DATUM fork URL/commit is currently unknown. Ask for
  it or locate and verify a primary source; do not guess.
- The current GridPool reference node hard-codes 80-byte headers and SHA256d in
  validation, block parsing, UDP codecs, ZMQ/RPC reconciliation, DATUM framing,
  tests, and telemetry.
- Do not change transaction ID, Merkle, address checksum, share-ID, or identity
  hash domains merely because header PoW changes. Classify each domain first.
- GridPool proof ranking depends on achieved work. Target normalization,
  byte order, expected target, and activation context are consensus-critical.
- Never trust untrusted header target metadata to classify a real block.
- Preserve V2.2 paid-once lineage, exact-family monotonic sibling union, bounded
  reserve behavior, coinbase-derived slot-0 attribution, and full proof
  validation.
- Use a distinct GridPool network ID, PoW algorithm ID, header-format version,
  UDP magic/version, state directory, identities, peers, ports, and packages.
- Cross-network traffic must fail before proof or state processing.
- Start from current `origin/develop` and verify that `400fc6e` is an ancestor.
  Do not fork from packaged runtime `9ac862a`; it predates critical fixes.
- Do not interfere with the SHA-256 release candidate, Main/Oregon, appliance
  testing, red-team retest, or soak.

## First Deliverables

1. Re-query Knots PR #359 and record repository, head commit, merge target,
   status, header format, activation behavior, PoW algorithm/profile details,
   target rules, vectors, RPC/GBT behavior, and unresolved review comments.
2. Identify and pin the actual Blake2b DATUM fork. If unavailable, mark DATUM
   implementation blocked and continue only with the reference-node audit.
3. Create only an experimental branch/worktree from current `origin/develop`,
   record its exact commit, and verify that it contains `400fc6e`. Do not call
   the fork release-ready until the independent retest and later security merge
   gates are complete.
4. Audit every SHA256d, fixed-header, target, difficulty, raw-block, ZMQ, RPC,
   UDP, DATUM, state, API, persistence, and test assumption in `boot-protocol`.
5. Classify hash uses into PoW/header, transaction/Merkle, address/checksum,
   GridPool content ID, and cryptographic identity domains.
6. Propose the smallest chain-header/PoW profile that preserves current SHA-256
   behavior exactly and supports the pinned Blake2b format.
7. Propose explicit network, proof, wire, state, UDP, and capability domain
   separation.
8. Produce a defensive test matrix covering canonical vectors, target
   validation, activation boundary, reorgs, payout attribution, paid-once
   behavior, state-bundle validation, cross-network rejection, and resource
   bounds.
9. Recommend whether to create `gridpool-blake2b` immediately or wait for the
   reviewed security baseline and stable upstream interfaces.
10. Save the findings and implementation plan in a durable Markdown document.

Do not implement beyond characterization tests or a behavior-preserving
interface seam until the exact node and DATUM sources are pinned and the owner
has reviewed the architecture plan. Clearly separate verified upstream facts,
GridPool code observations, inferences, and unresolved decisions.
