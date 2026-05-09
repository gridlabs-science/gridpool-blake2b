# Waiting For Funding And Launch Infrastructure Backlog

## Purpose

This document tracks useful work that can be done before the hosted launch infrastructure and external funding are ready.

The goal is to focus on tasks that:

- improve launch readiness without requiring paid infrastructure
- reduce technical or game-theoretic uncertainty
- make the protocol easier to explain to miners, partners, and reviewers
- preserve Boot's core trust model

These are not all launch blockers. Each item is marked as:

- `Launch`: should be done before a public launch
- `Beta`: useful before a wider beta
- `Research`: worth exploring, but not required before launch
- `Polish`: improves user experience or positioning

## Core Constraints

Boot's share-validation model should stay tag-agnostic and transaction-policy-agnostic:

- share attribution comes from the coinbase slot-0 payout address
- a submitted share proves work over a block header
- the header commits to the coinbase and transaction merkle root
- peers normally do not need the full transaction list to validate a share
- Boot should not require any specific coinbase branding tag for consensus
- Hydrapool and HTTP-submitted shares must remain valid with or without a Grid Pool tag

## Task 1: Coinbase Tag Default

Status:
- `Polish`
- `Beta`

Decision:
- Use `Grid Pool` as the default primary coinbase tag.
- Allow operators to override it in config without recompiling.
- Allow an empty tag for operators who prefer untagged blocks.

Rationale:
- Explorer visibility is useful for launch and miner recruitment.
- A short stable tag is easier for mempool.space and similar explorers to recognize.
- `Grid Pool` is simple, ASCII, short, and fits DATUM/Hydrapool tag-size constraints.
- The UI/website can still explain that this is not a conventional custodial pool.

Important distinction:
- A static coinbase tag is branding, not proof.
- The real protocol proof is the payout structure plus round state.
- A tag can be spoofed by any miner, while the payout list and slot-0 commitment are cryptographically tied to the hashed block header.

Implementation notes for later:
- Change Boot's default `coinbase_tag` from `Boot protocol` to `Grid Pool`.
- Keep the value configurable in `boot_portal_config.json`.
- Validate configured tag length at startup instead of silently truncating.
- Keep share verification independent of tag contents.
- Add a diagnostic field that records observed coinbase tag text from submitted shares, if easy.

Acceptance criteria:
- Fresh config with no explicit tag sends `Grid Pool` to DATUM clients.
- Explicit config override changes the DATUM primary tag without recompilation.
- Explicit empty string is allowed and produces an unbranded primary tag.
- HTTP/Hydrapool shares with no tag still validate when payouts and proof-of-work are valid.
- Peer-relayed shares with no tag still validate when payouts and proof-of-work are valid.
- A too-long configured tag fails fast or logs a clear startup error.

References:
- DATUM supports server-overridden primary coinbase tags through client configuration.
- DATUM locally supports primary and secondary coinbase tags.
- mempool.space's mining-pools database supports coinbase-tag matching by regex.

## Task 2: Coinbase Tag Explorer Submission

Status:
- `Polish`
- `Research`

Goal:
- Prepare a future mempool.space mining-pool metadata PR for `Grid Pool`.

Recommended approach:
- Do not submit this until the project name and public URL are stable.
- Prefer a coinbase tag match over payout-address matching.
- Use a slug like `gridpool`.
- Link to the public project page or docs, not a temporary dev UI.

Acceptance criteria:
- Project name is finalized.
- Public landing page exists.
- Coinbase tag is stable on mainnet/testnet examples.
- Candidate `pools-v2.json` entry is drafted.

Open question:
- Whether mempool.space maintainers prefer waiting until a real mainnet block exists before adding the tag.

## Task 3: Censorship Detection Design

Status:
- `Research`
- `Beta`

Problem statement:
- A miner may want to know if some Boot peers are rejecting, delaying, or failing to relay their valid shares for non-consensus reasons.
- Possible targets include transaction-policy censorship, payout-address censorship, jurisdictional filtering, or arbitrary peer discrimination.

Current protocol reality:
- A normal Boot share submission contains enough to validate:
  - the block header
  - the coinbase transaction
  - the merkle path connecting the coinbase to the header merkle root
  - the payout outputs
  - the achieved proof-of-work
- It does not normally reveal the full transaction set.
- Therefore, a peer usually cannot know whether a share includes a specific "unfavorable" transaction.

Implication:
- Transaction-content censorship is hard for peers to perform at the share-proof layer unless the protocol later exposes full block templates or extra transaction commitments.
- Address or payout-list censorship is much easier to perform and detect, because slot-0 and shared payout outputs are visible.
- Silent dropping, non-relay, and inconsistent acceptance are the practical behaviors to measure first.

Recommended first version:
- Build a peer acceptance matrix.
- For each high-enough share submitted or relayed, track which peers:
  - accepted it
  - rejected it with a reason
  - ignored it
  - later advertised it in their own candidate state
  - relayed it onward
- Compare peer behavior against local validation.

Signals worth tracking:
- valid local share accepted by some peers but rejected by others
- repeated rejection of one slot-0 address by a specific peer
- repeated rejection of shares containing a specific payout list
- peer advertises a candidate state missing shares it previously acknowledged
- peer's on-deck threshold claim does not match its advertised list
- peer frequently lags only on shares from specific addresses
- peer rejects shares with reasons that do not match local verification

Possible UI:
- `Network Fairness` panel in Nerd mode.
- `This team appears healthy` or `Possible peer filtering detected` summary in Business mode.
- Per-peer score:
  - acceptance rate for locally valid shares
  - median propagation delay
  - unexplained drop count
  - address-specific anomaly count

Game-theoretic interpretation:
- A peer can choose not to work with a miner or team.
- That is not preventable at the protocol layer.
- Detection lets honest miners exit bad teams and coordinate around peers that relay valid work fairly.
- Public reputation may be enough to discourage obvious filtering if miners can compare evidence.

Acceptance criteria:
- Every locally valid share above the advertised relay floor gets a peer-delivery record.
- For each peer, Boot can report accepted, rejected, ignored, and later-observed counts.
- Rejection reasons are normalized enough to compare across peers.
- A peer that rejects a locally valid share records the exact local validation facts that contradicted the rejection.
- The system distinguishes normal race windows from persistent peer-specific filtering.

Non-goals for first version:
- Do not require peers to reveal full transaction templates.
- Do not reject shares because they lack a specific transaction.
- Do not make transaction-policy commitments consensus-critical.
- Do not assume all unexplained drops are malicious.

## Task 4: Optional Transaction-Policy Attestation

Status:
- `Research`
- `Low priority`

Goal:
- Explore whether miners can voluntarily prove or attest that their candidate blocks include certain classes of transactions.

Possible designs:
- A miner can publish the full block template or txid list for a high-value share.
- A miner can publish compact transaction-set evidence after a real block is found.
- A miner can publish inclusion proofs for specific watched transactions.
- A Boot node can compare its local mempool against observed solved blocks and estimate missing-fee or missing-transaction behavior.

Major caveats:
- Full transaction templates are large compared with share proofs.
- Revealing full templates may leak miner policy, fee strategy, and timing information.
- Requiring full templates would hurt scaling and complicate Hydrapool compatibility.
- Mempools differ naturally, so absence of a transaction is not proof of censorship.
- A merkle root alone does not prove that a specific transaction was included. A txid plus a merkle branch can prove inclusion against the merkle root, but Boot's current share proof normally only includes the coinbase branch.
- A censoring peer therefore cannot reliably detect arbitrary blacklisted transaction inclusion from the normal Boot share proof unless it receives a transaction inclusion proof, full transaction list, or out-of-band template data.

Recommendation:
- Keep this out of launch consensus.
- Treat it as optional observability for miners who explicitly want to advertise non-censoring behavior.
- Keep priority low until there is evidence that peers are rejecting otherwise-valid shares based on transaction policy.

Acceptance criteria for a research prototype:
- Produce a sample report comparing one Boot node's local mempool to a mined block's included transactions.
- Clearly separate "not seen locally", "seen but absent", "possibly unavailable", and "likely policy filtered".
- Avoid declaring censorship without repeated evidence.

## Task 5: Peer Reputation And Exit Guidance

Status:
- `Beta`

Goal:
- Help miners decide whether to keep working with a team or switch teams.

Inputs:
- peer acceptance matrix
- convergence history
- orphaned/alternate round frequency
- peer latency
- peer uptime
- valid-share unexplained drop rate
- local versus network hashrate visibility

Possible output:
- `Team looks healthy`
- `Team is split`
- `Some peers are not relaying your valid shares`
- `Your node is isolated`
- `Consider switching seed peers`

Acceptance criteria:
- The UI can explain why a warning is shown using concrete evidence.
- Warnings suppress normal short race windows after Bitcoin tip changes.
- Operator logs include the share IDs and peer IDs behind each warning.

## Task 6: Round And State Commitment Research

Status:
- `Research`

Goal:
- Decide whether Boot should eventually commit the round state ID into coinbase data.

Current state:
- Boot can compute a state commitment preview.
- Embedding a dynamic per-round commitment requires miner-side template support.
- DATUM and Hydrapool compatibility would need careful design.

Pros:
- Stronger on-chain proof that a block belongs to a specific Boot round/team state.
- Easier independent verification.
- Could make third-party explorers more accurate.

Cons:
- Requires more client integration work.
- Can fragment compatibility if made mandatory.
- Consumes coinbase script space.
- Does not replace payout validation.

Recommendation:
- Keep `Grid Pool` as static optional branding for now.
- Keep dynamic state commitments as a later protocol-extension track.

Acceptance criteria for later prototype:
- Commitment format is documented.
- Commitment can be computed deterministically from locked round state.
- Shares without the commitment remain valid unless a specific team explicitly opts into requiring it.

## Task 7: Hydrapool Compatibility Notes

Status:
- `Launch`

Goal:
- Make sure branding, censorship detection, and state commitments do not break Hydrapool.

Rules:
- HTTP share validation must remain based on proof-of-work, merkle consistency, parent validity, slot-0 attribution, and payout outputs.
- HTTP shares must not require a `Grid Pool` tag.
- Any recommended tag should be advertised as metadata only.
- If Hydrapool has its own coinbase signature field, using it should be optional.

Acceptance criteria:
- Hydrapool-style HTTP shares validate with:
  - `Grid Pool` tag
  - a different tag
  - no recognizable tag
- Forged caller-supplied attribution still resolves to coinbase slot `0`.
- Documentation explicitly says tags are non-consensus metadata.

## Task 8: Public Developer Preview Safety

Status:
- `Beta`

Goal:
- Keep the public developer preview useful without letting accidental users mistake it for production.

Possible improvements:
- Stronger warning banner in Lottery and Business modes.
- Public status page showing `developer preview`, `test node`, or `production`.
- Config flag that marks a node as experimental.
- UI copy explaining that payout estimates are not guarantees.

Acceptance criteria:
- First-time visitor sees the preview warning before connecting hashrate.
- Node mode is visible in the UI.
- API exposes node mode for third-party dashboards.

## Task 9: Funding/Partner Packet

Status:
- `Polish`

Goal:
- Make it easier to brief 256 Foundation, early miners, and infrastructure sponsors.

Deliverables:
- one-page explainer
- protocol trust-model diagram
- launch-readiness checklist snapshot
- Hydrapool compatibility summary
- screenshots of Lottery, Business, and Nerd modes
- plain-English explanation of why this is not a custodial pool

Acceptance criteria:
- Packet can be sent without requiring a live demo.
- Claims are tied to current tests or marked as planned.
- Security assumptions are explicit.

## Task 10: Long-Run Data Review Pack

Status:
- `Beta`

Goal:
- Make soak-test evidence easy to understand and archive.

Deliverables:
- standardized soak summary
- peer convergence chart
- reject reason timeline
- local versus team hashrate chart
- DATUM session churn summary
- orphan/alternate round summary

Acceptance criteria:
- A completed soak writes enough data to disk even if stopped early.
- A single command generates a markdown summary.
- The summary includes clear pass/fail thresholds where available.

## Task 11: Proof-Of-Work-Gated DoS Protection

Status:
- `Research`
- `Beta`
- `Needs more planning before implementation`

Goal:
- Use Boot's native proof-of-work environment to make spam and expensive peer traffic costly while keeping honest miners and lightweight nodes able to join.

Planning state:
- This idea is promising but not fully baked.
- Do not implement until the traffic classes, peer-identity model, small-miner impact, and overload behavior are specified more precisely.

Background:
- Proof-of-work was originally useful as an anti-spam / anti-DoS primitive.
- Boot has a natural source of useful work: valid mining shares.
- A peer that can produce a high-enough share has paid a real cost that is cheap for honest miners and expensive for attackers to fake at scale.

Design principle:
- Work should earn trust, priority, and rate-limit budget.
- Work should not be required for every packet or every basic query.

Why not require a valid share on every non-share message:
- New peers may need to sync before they can mine useful shares.
- Watch-only, seed-only, and low-hash nodes may be legitimate even if they rarely produce shares.
- Stale tips and round races could make otherwise honest work temporarily unusable as a generic API token.
- Verifying full share proofs is more expensive than verifying a simple hashcash-style stamp.
- It could leak miner identity or tie unrelated control traffic to a specific slot-0 payout address.

Recommended first version:
- Keep cheap baseline limits:
  - per-IP and per-peer token buckets
  - maximum request body sizes
  - connection caps
  - fast network/version checks
  - early rejection for malformed JSON or invalid protocol IDs
- Add work-based peer credit:
  - accepted local DATUM shares increase local credit for that session/address
  - accepted peer-relayed shares increase credit for that peer
  - higher-difficulty shares earn more credit than lower-difficulty shares
  - credit decays over time
  - peers with credit get higher relay and sync budgets
- Add a hashcash-style challenge for non-mining peers:
  - challenge binds to peer ID, endpoint, route, node ID, and time bucket
  - target can rise when the node is overloaded
  - verification is a cheap hash check, not a full Bitcoin share validation
- Add low-difficulty spam handling:
  - advertise the current on-deck admission floor
  - accept low-difficulty local DATUM shares as miner feedback when appropriate
  - do not relay low-difficulty shares to peers
  - disconnect or deprioritize peers that repeatedly submit shares far below the advertised floor after being told the floor

Possible credit formula:
- `credit += log2(max(1, shareDifficulty / floorDifficulty))`
- `credit += 1` for a share that is valid but below the current on-deck floor, only for directly connected DATUM clients
- `credit *= exp(-elapsedSeconds / halfLifeSeconds)`
- cap credit per peer to prevent one large miner from gaining unlimited API privilege

Traffic classes:
- Public UI and status:
  - no proof required
  - strict rate limit
- Basic peer handshake and summary:
  - no proof required
  - moderate rate limit
- Historical/debug sync:
  - proof or existing peer credit required under load
- Share relay:
  - proof is the share itself
  - reject or throttle below-floor spam
- Admin endpoints:
  - API key required
  - proof-of-work does not replace authentication

Acceptance criteria:
- A valid high-difficulty share increases a peer's relay budget.
- A peer repeatedly sending below-floor shares after receiving the floor is throttled or disconnected.
- A non-mining seed node can still join and perform basic peer sync without mining hardware.
- Under simulated overload, expensive endpoints require either peer credit or a valid hashcash challenge.
- The UI exposes enough rate-limit state to debug false positives.

Open questions:
- Whether credit should key by peer public key, endpoint, slot-0 address, or a combination.
- Whether work credit should be shared across connected peers or remain strictly local.
- How aggressive overload targets should be before small miners are harmed.
- Whether hashcash challenges should be standardized for interoperability between Boot implementations.

## Task 12: Public Network Map

Status:
- `Research`
- `Polish`
- `Needs more planning before implementation`

Goal:
- Add a visual map of public Boot nodes so users can see network spread, robustness, and nearby connection options.

User-facing concept:
- Show public nodes on a map.
- Let users click a node to view:
  - node name or operator label
  - approximate region
  - current status
  - recent uptime
  - observed latency from the user's browser, if measurable
  - DATUM endpoint
  - WebUI URL
  - supported network/team ID
- Let users open a selected node's UI or copy its DATUM connection details.

Why this is useful:
- Makes the decentralized network feel tangible.
- Helps miners pick a nearby low-latency node.
- Helps users avoid isolated or unhealthy nodes.
- Gives a compelling launch visual as the network grows.
- Supports the "sovereign but cooperative" story better than a plain peer table.

Security and privacy risks:
- A public map can become a target list for DoS attackers.
- Publishing exact node locations may reveal operator homes, small businesses, or data centers.
- Browser-side latency tests can leak user location or browsing behavior.
- Malicious nodes could advertise fake locations or healthy-looking metadata.
- A polished map could make unofficial or untrusted nodes look endorsed.
- If DATUM endpoints are too prominent, attackers may scan or flood them.

Recommended design constraints:
- Use approximate regions, not exact coordinates.
- Make public listing opt-in.
- Separate `official seed`, `community node`, and `unknown peer` categories.
- Require signed node metadata before showing rich details.
- Show health and latency as observed data, not promises.
- Include a warning that operators choose their own policy and users should verify.
- Do not expose admin endpoints or internal IPs.

Possible implementation path:
- Start with a non-map node directory in Nerd mode.
- Add optional node metadata:
  - display name
  - region code
  - approximate latitude/longitude
  - public WebUI URL
  - public DATUM endpoint
  - operator website or nostr/pubkey
  - team/network ID
- Add a static map only for configured public seed nodes.
- Later, allow opt-in community nodes to advertise signed metadata.
- Eventually add browser-measured latency checks to listed nodes.

Acceptance criteria before implementation:
- Public-node metadata schema is documented.
- Exact location is not required.
- Opt-in and opt-out behavior is clear.
- UI distinguishes official seeds from community-advertised nodes.
- The map can be disabled by config.
- Threat model is written before exposing public peer lists broadly.

Open questions:
- Should the default public UI show only official seed nodes or all discovered public peers?
- Should node location be self-reported, inferred from IP geolocation, or both?
- Should latency be measured browser-to-node, node-to-node, or both?
- Should nodes earn a reputation score before appearing on the public map?
- Should DATUM connection details be hidden behind an explicit "connect to this node" action?
