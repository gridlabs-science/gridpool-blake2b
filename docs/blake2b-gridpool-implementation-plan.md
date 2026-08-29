# Build and Launch the Experimental GridPool Blake2b Fork

## Summary

Create two public GridLabs repositories:

- `gridlabs-science/gridpool-blake2b`: standalone repository preserving `boot-protocol` history, default branch `develop`.
- `gridlabs-science/datum-gateway-blake2b-gridpool`: GitHub fork of the community Blake2b DATUM implementation, now maintained at `innerhat-dev/datum_gateway`. The reviewed upstream base is `2fea7e5`; GridLabs `develop` is pinned at `1356c65`, including forced-coinbase compatibility at `70670c5` and a stable GridLabs CI gate. The older `e894b8a` pin is superseded.

Target September 1 as an experimental launch, not a stable release. Independent
retesting closed RT-2026-041 and RT-2026-042. Development now proceeds from the
RT-2026-076 follow-up candidate and must display an unverified-security warning,
use kill switches, and publish no stable release/tag until its remaining gates
close.

A mainnet launch remains technically blocked until upstream publishes a commit with a finite Blake2b activation height, exact chain/replay/DAA rules, and usable vectors. The current [Knots PR #359](https://github.com/bitcoinknots/bitcoin/pull/359) still leaves mainnet Blake2b disabled; [Start9 likewise describes September 1 as an intention rather than a finalized schedule](https://start9.com/bip110/).

## Repository and Source Control

- Start `gridpool-blake2b` at `b4c92a9090c11efd74298e06b02cfe56727373ea`.
  - Require `git merge-base --is-ancestor 400fc6e HEAD` to succeed.
  - Require `f09ce5e` not to be an ancestor.
  - Commit the existing SHA-only chain-profile seam, characterization tests, and evidence document as the first fork commit.
  - Retain `gridlabs-science/boot-protocol` as `upstream`; never automatically merge from it.
- Add a tracked source-lock manifest containing:
  - GridPool baseline `b4c92a9` and its `security-rt-076-retest-candidate` tag.
  - Critical candidate `400fc6e`.
  - Explicitly excluded P1 pin `f09ce5e`.
  - Testnet Knots RC3 peeled commit `afbe91c299e16519f03902939fdbda8af9bd527d`.
  - Superseded RC2 evidence pin `c25ad6bcd18fa65cd78f176a52be062411507741`.
  - Current PR head `fee27ccfe950e998bb6d36e2b81f4ec97e3e89a3`.
  - DATUM base `e894b8ac29ae06bf6e3b14dafd21f72dcd65fb84`.
  - Built image/binary digests.
- Make `develop` the default branch, require CI, and publish no `latest` image or GitHub release. Deploy only immutable commit/image digests.
- Fork DATUM from the community Blake2b branch. Port the existing force-coinbase/fingerprinting work commit-by-commit after compatibility review; submit generic force-YUGE and fail-fast improvements upstream, but keep GridPool-specific fee routing and payout-session multiplexing in the GridLabs fork.

## Protocol and Mining Implementation

### Chain profile and domain separation

- Complete the activation-aware header profile:
  - Parse legacy 80-byte SHA256d headers and 164-byte Blake2b v2 headers.
  - Implement all four upstream ASIC profiles, H1/H2 commitments, XOR behavior, byte order, first-block target shift, and exact canonical vectors.
  - Keep transaction IDs, coinbase IDs, Merkle hashing, addresses, GridPool IDs, and identity cryptography on their existing algorithms.
- Use an exact integer achieved-work score for consensus ordering; retain floating-point difficulty only for display.
- Start Blake GridPool state at the Blake activation boundary. Do not import or rank pre-activation SHA proofs.
- Add a mandatory domain fingerprint containing chain/genesis, PoW algorithm, header format, activation rule, target rule, work-score rule, and profile revision.
- Bind that fingerprint into proofs, peer handshakes, state bundles, API submissions, identities/KDF domains, capabilities, persistence paths, and new UDP codecs. Reject mismatches before proof processing.
- Use distinct network IDs, identities, state roots, bootstrap peers, UDP magic/version, ports, metrics, and packages for mainnet, testnet4, and regtest.
- Keep attached Knots RPC/ZMQ as the authority for expected target and active-chain block confirmation. Header-supplied `nBits` alone can never rotate the round or consume paid lineage.

### Public interface/configuration changes

Introduce:

- `ChainDomainFingerprint` in capabilities, proofs, peer hello/state DTOs, and persistence.
- An activation-aware `IChainHeaderProfile` returning parsed fields, header length, canonical PoW bytes, block ID, target, and integer work score.
- Multiple configured DATUM listeners, each with:
  - bind address/port;
  - policy ID;
  - support-template basis points;
  - network-specific support address;
  - scheduler-key path.
- Public status fields for chain profile, source pins, canonical payout policy, hosted slot-0 policy, template counts, accepted work by slot-0 role, and experimental-security status.

Use these endpoint defaults:

- Mainnet: `blake.gridpool.net`, `datum.blake.gridpool.net:3008`, `stratum.blake.gridpool.net:3333`, GridPool UDP `5101`.
- Testnet4: `testnet4.blake.gridpool.net`, `datum.testnet4.blake.gridpool.net:3009`, `stratum.testnet4.blake.gridpool.net:3334`, GridPool UDP `5102`.
- Internal hosted-gateway DATUM listeners: loopback-only `3018` mainnet and `3019` testnet4.

HTTP hosts may use a reverse proxy; DATUM, SV1, chain P2P, and UDP records must be DNS-only/direct TCP or UDP.

### Fee-free GridPool and hosted-service policy

- Make the Blake network’s only valid synchronized payout profile fee-free:
  - 299 shared winner proofs plus slot 0.
  - Reject `grid_labs_support_fee_enabled=true`.
  - Remove the current one-slot/0.3% support output from state-family construction and validation.
- Hosted fees operate only by selecting slot 0:
  - Public DATUM listener: 500 basis points of templates use the dedicated GridLabs support address.
  - Internal SV1 gateway listener: 5,000 basis points use the support address.
  - All 299 shared winner outputs remain unchanged.
- Select miner/support templates with a persisted HMAC-SHA256 scheduler bound to the chain fingerprint, listener policy, client identity, normalized payout address, parent hash, and per-client template sequence.
- Persist the decision with the coinbaser/job ID and verify submitted work against that exact decision. Reconnects using the same client identity must resume their sequence.
- Document that operators controlling their own DATUM gateway can idle during support templates; the 5% policy is enforceable for served templates but is not cryptographically unavoidable.
- Use new, separately controlled mainnet and testnet4 GridLabs support addresses supplied before public fee endpoints are enabled.

### GridLabs DATUM/SV1 fork

- Force `yuge` coinbase selection so all 300 uncondensed outputs are present.
- Keep fingerprinting enabled:
  - known-incompatible firmware is rejected before receiving work;
  - unknown firmware is served and clearly labeled unverified;
  - unsafe override is disabled on public services and retained only for the private lab.
- Parse each SV1 username as `payoutAddress[.worker]`.
- Preserve non-custodial payouts by maintaining a bounded logical DATUM session per payout address:
  - derive a stable upstream client identity from a protected gateway master seed and normalized payout address;
  - cap active payout sessions at 512;
  - expire idle sessions after 15 minutes;
  - rate-limit creation of new payout identities;
  - route each miner’s jobs and shares only through its corresponding session.
- Keep the public DATUM protocol compatible with unmodified clients from the pinned community Blake2b fork.

## Regtest, VPS, and Rollout

- Selectively port the disposable lab from `codex/regtest-lab-v1@46170e0`; never merge that old branch.
- Run a private Knots regtest chain with `-testactivationheight=blake2b@110`, three isolated GridPool nodes, stock Blake DATUM, the GridLabs DATUM fork, and a synthetic CPU/SV1 Blake miner.
- Bind every lab interface to loopback and keep the lab stopped except during tests. It must have fresh identities, state, RPC credentials, scheduler keys, and Docker networks.
- Provision one provider-neutral x86_64 VPS. The initial constrained deployment
  is explicitly accepted at 6 vCPU, 12GB RAM, and 100GB SSD because it runs only
  one public chain profile at a time:
  - run pruned Testnet4 only through the temporary validation window;
  - stop Testnet4 and preserve only its configuration/evidence before preparing
    the separately rooted pruned mainnet node;
  - never run simultaneous mainnet and Testnet4 chainstate, GridPool, DATUM, or
    mining services on this host;
  - cap node caches and container memory, use a bounded prune target, and retain
    at least 15GB disk headroom with alerts at 20GB and 15GB;
  - static IPv4 is required; IPv6 remains optional until OVH routing is configured;
  - provider firewall/DDoS controls and Ubuntu 24.04 remain required.
- Expose only HTTPS, chain P2P, the documented DATUM/SV1 ports, and the two GridPool UDP ports. Keep RPC, ZMQ, DATUM administration, telemetry collectors, the internal 50% listener, and regtest reachable only through loopback or Tailscale.
- Add systemd/Docker health checks, disk and chain-tip alerts, peer/session counts, rejected-share reasons, scheduler distributions, restart monitoring, sanitized firmware telemetry, and one-command mining-port kill switches.
- Back up only identities, scheduler/master seeds, support-address configuration, and deployment manifests; chain and disposable lab data are rebuildable.

### Target sequence

1. **August 25–26:** create repositories, commit the existing seam, add source locks and CI.
2. **August 26–28:** finish Blake header/work/domain support and enforce the fee-free payout profile.
3. **August 28–29:** implement stock DATUM compatibility, dual listener policies, forced YUGE, and per-payout SV1 multiplexing.
4. **August 29–30:** run full regtest activation, payment, reorg, fee-scheduler, and firmware scenarios.
5. **August 27–31:** provision the constrained VPS, source-build and sync RC3
   Testnet4, expose testnet endpoints only after validation, and complete the
   longest available soak before the temporary Testnet4 environment is retired.
6. **September 1:** enable experimental mainnet endpoints only if a pinned upstream commit supplies a finite activation height and complete chain parameters. Otherwise keep mainnet mining ports closed and continue testnet4 operation.

## Verification and Acceptance Gates

- Preserve the 216-test RT-076 baseline plus the four SHA profile
  characterization tests: 220 tests at the first fork commit, then at least
  those 220 regressions plus new Blake tests.
- Pass all five upstream Blake vectors, all four profiles, header-boundary, target-shift, byte-order, invalid-flag, malformed-header, and compact-target cases.
- Prove transaction/Merkle/address/GridPool identity hashes remain unchanged.
- Reject copied SHA state, mixed profiles, wrong network/genesis, old UDP messages, malformed DATUM extensions, and attacker-selected easy `nBits`.
- Verify fee-free coinbases contain slot 0 plus 299 shared outputs with no protocol support slot.
- Verify deterministic 5% and 50% scheduler vectors, restart persistence, correct slot-0 attribution, and unchanged winner payouts.
- Run multi-miner SV1 tests with distinct payout addresses, session expiry/caps, reconnects, and simultaneous support/miner templates.
- Verify known-incompatible firmware fails fast, unknown firmware receives YUGE work, and truncated coinbases are rejected with actionable diagnostics.
- Exercise share-first and notification-first block events, paid-once lineage, sibling union, bounded 897-proof reserve, one/two-block reorgs, and three-node convergence.
- On testnet4, verify signed tag `v29.4.1.knots20260508rc3`, peeled commit
  `afbe91c299e16519f03902939fdbda8af9bd527d`, activation height `150027`,
  first-Blake target `0x1a00ffff`, post-fork peers, complete sync, stock DATUM
  interoperability, real accepted shares, and a zero-unplanned-restart soak.
- Mainnet enablement additionally requires an exact upstream pin with finite activation, replay/DAA characterization, successful source build, correct chain peers, and an operator-reviewed pin manifest. The pending RT-2026-076 follow-up retest is displayed as an accepted experimental risk; any adverse result immediately disables public mining ingress.

## Assumptions

- `gridpool.net` DNS remains available for the proposed Blake subdomains.
- New dedicated GridLabs support addresses will be supplied before public fee policies are enabled.
- The public fork and mainnet service remain explicitly experimental; no stable release, package publication, StartOS integration, or recommendation to hold/transact fork assets is implied.
- The broader P1 hardening commit `f09ce5e` remains excluded unless reviewed and authorized separately.
