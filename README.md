# GridPool
GridPool is a decentralized reward-sharing protocol for sovereign Bitcoin miners. It reduces solo-mining payout variance without a custodial pool wallet, a centralized share ledger, or a separate sharechain.

- It is like P2Pool in spirit, but much simpler: miners coordinate on coinbase payout lists instead of maintaining a secondary blockchain.
- It is not a traditional pool. It is closer to shared lottery mining: smaller payouts, better odds, and local block-template control.
- This reference implementation works with DATUM today. Hydrapool and other HTTP share submitters can integrate through the documented HTTP API.

For cross-project architecture, decisions, research findings, and agent-ready
context, see the [GridPool handbook](https://github.com/gridlabs-science/gridpool-handbook).
This repository remains canonical for reference-node implementation and
operator documentation.

## Naming Note
GridPool was originally developed under the working name "Boot Protocol." Some internal code, repository names, API headers, config keys, service names, and scripts still use `boot` for compatibility during the beta transition. Public docs, UI, and operator-facing language should use **GridPool**. When precision is needed, use **GridPool protocol** for the reward-sharing rules and **GridPool internode protocol** for peer-to-peer state synchronization and share relay.

## Critic-Facing Design Notes
GridPool is early, and several claims still need rigorous public modeling.

- `docs/README.md` is the index for current docs.
- `docs/umbrel-start9-launch-checklist.md` is the primary launch-gating checklist before broad Umbrel / Start9 packaging.
- `docs/project-architecture-map.md` explains which work belongs in this reference node repo versus `gridpool-spec`, `gridpool-web`, and `gridpool-simulations`.
- `docs/consensus-selection-audit.md` documents the V2.1 snapshot-boundary and merge rule plus remaining scoring questions.
- `docs/simulation-findings-2026-06.md` summarizes which current simulation findings look publishable and which are still too early.
- `docs/critic-faq.md` answers common technical objections such as pool hopping, loose consensus, sharechain comparisons, Sybil accounting, and majority-miner team splits.
- `docs/modeling-and-simulation-roadmap.md` defines the open simulation work needed to turn those answers into reproducible evidence.
- `docs/scaling-analysis.md` and `docs/stress-test-plan.md` cover bandwidth, latency, peer topology, and load-test targets.
- `docs/release-process.md` defines the public beta branch, Docker image, tag, and coordinated-upgrade policy.

Historical debugging notes and completed investigations live under `docs/archive/`.

## GridPool Node Quickstart
Most miners should start by running only a GridPool node, then pointing an existing DATUM Gateway at it. This keeps Bitcoin and DATUM under your own control while adding the GridPool reward-sharing network layer.

On a Linux host with Docker, or on a Raspberry Pi / Ubuntu box where Docker can be installed:

```bash
curl -fsSL https://raw.githubusercontent.com/gridlabs-science/boot-protocol/main/scripts/install-gridpool-node.sh \
  | sudo bash -s -- --payout-address YOUR_BTC_ADDRESS
```

The installer:
- pulls `ghcr.io/gridlabs-science/boot-protocol:latest`
- starts the WebUI on port `5000`
- starts the DATUM-facing GridPool listener on port `3008`
- advertises your LAN IP and DATUM pubkey in the local WebUI
- uses local Bitcoin ZMQ notifications if it detects `127.0.0.1:28332`; otherwise it falls back to MempoolSpace notifications for first boot

After install:

1. Open the local WebUI printed by the script, usually `http://LAN_IP:5000`.
2. Copy the displayed Pool Host, Pool Port, and Pool Pubkey. Scripts can fetch the same live values from `http://LAN_IP:5000/api/mining/connect-info`.
3. Paste those into DATUM.
4. Point ASICs at DATUM's local Stratum V1 listener, not directly at GridPool.

Block-submission safety note: with DATUM, your gateway builds the Bitcoin block template from your Bitcoin node and submits any network-valid block directly to that node with Bitcoin RPC `submitblock`. GridPool receives the share proof for reward-sharing/accounting, but it is not the only broadcast path for a found block.

GridPool node ports are intentionally split by role:
- `5000` is the WebUI and HTTP API.
- `3008` is the DATUM Gateway upstream connection to GridPool.
- `23334` is the conventional DATUM Gateway Stratum V1 listener used by ASICs when DATUM is installed locally.

`boot-portal` itself is not a native Stratum V1 pool server. If you want to connect ASIC firmware directly without running DATUM, use a compatible gateway such as Hydrapool or the public beta Stratum endpoint when available.

Firmware compatibility warning:
- The main 300-slot GridPool beta requires miner firmware that can accept large DATUM coinbase templates.
- Older stock Bitmain firmware and NiceHash-style clients may be fingerprinted by DATUM into small coinbase variants. Those templates omit required GridPool payout outputs, so GridPool rejects the resulting shares as consensus-invalid.
- Prefer DATUM setups using firmware known to support larger coinbases, such as ePIC / PowerPlay-BM, VNish / xminer-class firmware, Whatsminer-class firmware, Bitaxe, or other firmware that DATUM fingerprints into larger coinbase classes.
- If older Antminer hardware cannot run compatible alternative firmware, wait for future smaller-team GridPool compatibility tiers rather than mining on the 300-slot beta.
- Operators can temporarily set `coinbase_uncondensed_outputs_enabled: true` in non-production lab configs to force DATUM to serve the full uncondensed payout-output set for firmware or rental-provider compatibility testing. Do not use that mode on production nodes.
- Compatibility results are tracked in `docs/firmware-coinbase-compatibility-matrix.md`. This is a community-maintained matrix; please submit PRs with exact firmware, gateway, and test-mode details.

If you need a full fresh sovereign stack, including pruned Bitcoin Core and DATUM Gateway, use the full-stack installer in `docs/raspberry-pi-one-shot-installer.md`.

## Docker Compose Quickstart
GridPool includes a basic Docker Compose packaging path for manual public beta testing.

Beta defaults now assume `300` total conceptual payout slots. Slot `0` is reserved for the block finder and receives transaction fees plus any subsidy remainder. With the default support fee enabled, one post-slot-0 slot is the canonical Grid Labs support output and up to `298` shared proof slots are paid. With the support fee disabled, up to `299` shared proof slots are paid. Some of the longer discussion below still uses older 15/16-slot toy examples for intuition.

1. Review `docker/boot_portal_config.sample.json`.
2. Bring the stack up with `docker compose up -d`.
3. The container persists runtime files under `./data` by default:
   - `./data/boot_portal_config.json`
   - `./data/pool_state.json`
4. The WebUI is exposed on port `5000` and the DATUM listener on port `3008`.
5. Run `scripts/boot-self-check.sh http://127.0.0.1:5000` after startup to verify health, peer state, round state, and local DATUM hashrate.

Notes:
- The default Docker Compose file pulls the prebuilt image `ghcr.io/gridlabs-science/boot-protocol:latest`.
- Public beta operators should use `:latest`, `:main`, or a pinned release tag. Staging nodes may intentionally track `:develop`.
- To build locally from source instead, run `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`.
- To pin a specific published image, set `GRIDPOOL_BOOT_IMAGE`, for example `GRIDPOOL_BOOT_IMAGE=ghcr.io/gridlabs-science/boot-protocol:sha-abc1234 docker compose up -d`.
- The container is set up for HTTP on the WebUI by default.  Terminate TLS at a reverse proxy, Cloudflare tunnel, or similar edge layer.
- The image runs as non-root UID/GID `1000` and creates `/data/boot_portal_config.json` from `docker/boot_portal_config.sample.json` if no config exists.
- The default sample uses `NotificationSource = "MempoolSpace"` so a local `bitcoind` is not required for first boot.
- If you want local ZMQ block notifications, change the config and make sure the container can reach your Bitcoin node.
- The mainnet beta bootstrap seed is `https://main.gridpool.net` unless you override `bootstrap_peers`.
- Testnet4 beta nodes should use `docker/boot_portal_config.testnet4.sample.json`, or set `bitcoin_network = "testnet4"`, `boot_network_id = "testnet4-beta"`, and bootstrap from `https://test.gridpool.net`.
- Health probes are exposed at `/health/live` and `/health/ready`.
- The default DATUM primary coinbase tag is `Grid Pool`; set `coinbase_tag` to another string, or `""` for unbranded blocks.
- Back up the Docker `./data` directory before machine moves, package upgrades, or host rebuilds. It contains live config, server identity keys, pool state, and history.
- Hydrapool and other direct HTTP submitters should follow `docs/hydrapool-http-submission.md`.
- DATUM-server implementers should review `docs/datum-server-compatibility-notes.md` for coinbaser ID, payout snapshot, and share hot-path requirements.
- Stratum V2 is supported by the separate `gridpool-sv2-pool` SRI fork. It uses this node's work-selection API plus authenticated local telemetry/proof endpoints; JDC/JDS are not required.

## Raspberry Pi Full-Stack Sovereign Install
For a one-shot Raspberry Pi / Ubuntu install that sets up a pruned Bitcoin Core node, DATUM Gateway, and GridPool together, see `docs/raspberry-pi-one-shot-installer.md`.

The installer entrypoint is:

```bash
sudo ./scripts/install-sovereign-stack.sh --payout-address bc1q...
```

If no payout address is provided, the installer uses the 256 Foundation donation address as a placeholder. Use `--payout-address` for any real mining setup.

## Config And Secret Handling
Tracked config files are now treated as safe defaults, not as the place to keep live secrets.

Rules:
- Keep placeholder values in tracked `boot_portal_config.json`.
- Put real private keys, admin keys, and machine-specific overrides in an adjacent untracked file:
  - `boot_portal_config.local.json`
- The app loads:
  1. `boot_portal_config.json`
  2. `boot_portal_config.local.json` if it exists
- The local file wins on key conflicts.

Examples:
- repo root dev path:
  - tracked: `./boot_portal/boot_portal_config.json`
  - local override: `./boot_portal/boot_portal_config.local.json`
- Docker data path:
  - tracked/sample: `./data/boot_portal_config.json`
  - local override: `./data/boot_portal_config.local.json`

Environment variables:
- `BOOT_PORTAL_CONFIG_PATH` overrides the base config path
- `BOOT_PORTAL_LOCAL_CONFIG_PATH` optionally overrides the local config path

Production guidance:
- keep `enable_admin_api` disabled unless you actively need admin reset endpoints
- if admin is enabled, use a strong random `admin_api_key`
- never commit live private keys or admin keys into the repository

## The Problem
Block construction, and therefore transaction selection are laughably centralized.  The vast majority of new coinbase rewards go to one of only a handful of wallets.  
This happened because centralized, low variance payout structures (FPPS) can outcompete higher variance methods like PPLNS.  It is difficult for any small competing pool to get past the minimum hashrate required to have a manageable variance.  

## Prior Art
The best known example is P2Pool.  *(NOTE: P2PoolV2 is in active development and aims to solve many of these early problems.)*  This used a secondary blockchain with faster blocktimes to track individual shares in a decentralized manner.  P2Pool (version 1) failed largely for two reasons.
1. The extra overhead required to run the secondary blockchain was cumbersome
2. The 30-second block times exacerbated the negative effects of block propagation time.  In all blockchains, when a new block is found it takes a brief amount of time before the rest of the network knows about the new block.  This means that nodes which discover the new block first have an advantage of being able to work on building on the new block before the rest of the network finds out.  If the difference between average block times and block propagation speed is great, than this effect is negligible, for example Bitcoin's ~10 minute block times (600 seconds) vs. about 6 seconds for network propagation.  However if block times are very fast (eg. 30 seconds), then physically centralizing hashrate becomes quite advantageous.  Nodes near the network center will earn significantly higher rewards.

The second (upcoming) solution is Braid Pool, an exciting new decentralized pool which solves the second problem (block propagation advantage).  Braid Pool is a much more advanced attempt using Directed Acyclic Graphs (akin to Kaspa) to eliminate the block propagation problem while still having very fast "block" times on the share-chain (about 1 second in theory).  In theory a block withholding attack would reduce miner revenue.  The pool itself could be 51% attacked, so this requires additional complexity to protect against.  

The third (also upcoming) solution is Ocean Pool.  Ocean Pool of course is already operating, and allows hashers the choice of three different templates.  They are working hard on adding the ability for miners to build their own templates, which will solve the problem of centralized block template creation.  Being a centralized entity themselves (albeit with nicely decentralized block template construction), there is the black swan risk that Ocean could get shut down by regulators.  Miners on Ocean also run the risk of reduced revenue from a block withholding attack.  

GridPool attacks the variance problem from the other side. Traditional pools estimate miner effort by tracking many shares in a centralized database. P2Pool-style systems decentralize share accounting with a sharechain. GridPool never centralizes the block reward and does not maintain a separate chain. Miners build Bitcoin templates that pay the active payout snapshot, then relay high-difficulty proofs into a bounded unpaid Work Set so the next snapshot can be verified by peers. Conceptually, GridPool is much closer to solo mining, but with up to roughly 300x lower variance when the shared list is full.

## Advantages of GridPool
Compared to solo mining, GridPool should have up to 300x reduced variance.  
Compared to standard pooled mining (eg. Ocean with sovereign block template construction), GridPool should offer reduced bandwidth requirements and much more resiliance to regulatory attack.
Compared to decentralized pooled mining, GridPool should have reduced bandwidth requirements, reduced computational overhead, and a vastly simpler code base.  

### Block Withholding attacks
GridPool is designed to be more resistant to pool-layer block withholding attacks because the block finder receives slot 0 plus transaction fees directly from the coinbase. This is a core research claim, not something to hand-wave. See `docs/critic-faq.md` and `docs/modeling-and-simulation-roadmap.md` for the modeling work needed to quantify the claim.

---
## GridPool Pseudocode
Key terms:
- Active payout snapshot:  The post-slot-0 payout template miners are currently building on. It is rebuilt from unpaid work whenever a new Bitcoin block is observed.
- Unpaid Work Set:  A bounded reserve of high-difficulty, unpaid share proofs. The default reserve is `3 * 299 = 897` proofs. The top slice of this reserve is what the UI often calls the On Deck List.
- GridPool Share Proof: This consists of the block header, the coinbase transaction, and just enough information from the Merkle tree for other nodes to verify the coinbase outputs, Merkle root, slot-0 attribution, parent block, and proof-of-work difficulty.
- Support slot: By default, one of the 300 conceptual payout slots is a canonical Grid Labs support output. This can be disabled by config, but the support address itself is not configurable.
- Team: In this context, a team is the loose grouping of miners that are all working on templates built from the same active payout snapshot and sharing proofs into the same unpaid Work Set.

1. Create a block template using a local Bitcoin node. The coinbase pays your own address in slot `0`, then pays the active GridPool payout snapshot after slot `0`.
2. Start hashing on this template. When a share reaches the advertised Work Set admission floor, create a GridPool share proof and broadcast it to peers.
3. When a node receives a share proof, it verifies the header proof-of-work, Merkle root, coinbase outputs, slot-0 attribution, parent context, payout snapshot, and duplicate status. Sender metadata is not trusted for payout attribution.
4. If the proof is valid and ranks high enough, insert it into the unpaid Work Set. Do not clear existing unpaid proofs just because a Bitcoin block arrived.
5. On every new Bitcoin block, build a fresh active payout snapshot from the highest-ranked unpaid proofs. This updates the template miners should work on, but it does not remove proofs from the Work Set.
6. When a valid GridPool block is found, the block pays the active snapshot directly from its coinbase transaction. The node records the paid snapshot lineage and removes only the proof IDs that were actually paid. Unpaid reserve proofs remain eligible for the next snapshot.
---
## Plain Language Discussion
Imagine 16 frens who want to get into mining.  They each buy an identical 1TH Bitaxe to start solo hashing.  They don't trust pools, but they'd rather have 16x better odds of getting 1/16 of a block, so they all agree to put each other's addresses in the coinbase split evenly.  Simple.  They have now reduced their variance.  

Next, one of their Bitaxes dies, and gets replaced by a 15TH Future Bit, bringing their team to 30TH.  But since that machine is putting out 50% of the team's hashpower, they all agree that the block reward should be 50% his, and the rest gets split by the 15 Bitaxes.  

After that, a few of the frens start experiencing power issues and aren't on 100% of the time.  Being good Bitcoiners, they decide to verify, not trust.  They agree to send each other their block solution proofs (shares) and keep a rolling reserve of the strongest unpaid shares.  If someone stops hashing, their old weak shares eventually get pushed out by newer stronger work.

Over time they notice that if they list out all the shares sorted by difficulty, the Future Bit produces about 8 of the top 16 shares.  They also get 100's more frens that want to join.  Since they don't want to split the coinbase 100's of ways, they decide to run a little competition.  On each new Bitcoin block, they snapshot the best unpaid shares into the coinbase template.  Those who have bigger machines, or stay on 24/7, have a lot more chances to get high difficulties, and so end up in the live payout snapshot more often.  The little Bitaxes can still play, but they have to get pretty lucky.  Not as lucky as pure solo though.

### What must I do to get paid?
To actually get paid at all, two things must happen.  You have to submit a share to the team which is strong enough to enter the active payout snapshot.  If you control 1/15th of your team's hashrate in this toy example, then on average you should occupy about one of the 15 shared slots.  Sometimes you might win two or three spots, sometimes none.  Next, someone on your team must actually find a real GridPool block while your snapshot is active.

### Extremes analysis
Sometimes it can help to push an idea to extremes to see how it behaves.  Imagine your team grows in total hashrate to the size of the entire Bitcoin network.  Every single block is won by someone in this team.  Then every coinbase gets split 16 ways.  But now, for a Bitaxe to earn a shared reward, they don't need to hit a block with full network difficulty.  They only need to hit one of the strongest unpaid shares before a snapshot gets paid.  Even in this extreme case, the "centralized" hashrate is no threat to the network because everyone is producing their own templates.  The "team" cannot collude to 51% attack the network, because the only component they are colluding on is the coinbase share.

### Team splits and joins
GridPool V2.1 treats Bitcoin-block snapshot boundaries as local finality points.  When a node observes a new Bitcoin block, it snapshots the valid unpaid Work Set it already knows about and starts building on that active payout snapshot.

There are edge cases due to network latency.  When a Bitcoin block is found, different miners may snapshot slightly different unpaid Work Sets.  If peers later reconnect, they do not blindly rewrite the already-active payout snapshot just because another branch claims to be stronger.  Instead, nodes merge valid current-parent proofs from retained snapshot contexts into the unpaid Work Set for future snapshots.  Late previous-parent proofs are rejected or quarantined from the canonical reserve because peers cannot prove when they first saw the Bitcoin block.  This preserves honest split work going forward while closing the stale-work attack path.

### Layering 
This concept of coinbase splitting can be used as a base layer underneath other standard payout schemes like PPLNS and FPPS.  Any pool or solo miner could use this to "join forces" and decrease their collective variance.  In fact, given the very minimal downsides, they stand to lose out long term against pools and miners that do use this mechanism.  Unfortunately, the only pool that can't benefit from this is Ocean Pool, because they also use the coinbase transaction for payout splitting.  I love Ocean Pool and hope they continue.  I think this protocol and Ocean are serving different needs.  

### Block withholding thoughts
The protocol is intended to make block withholding less attractive than in conventional pooled mining.  Given that each miner puts their own address in slot 0, they have an immediate incentive to submit a real block and collect the slot-0 reward plus transaction fees.  No value is ever promised, tallied, or accounted long term.  If an adversary consisted of 50% of a team's hashrate, they could expect to claim roughly 50% of the shared proof slots on average.  However if they chose to block withhold, then the team on average would find fewer blocks, and the attacker would forfeit every slot-0 reward and all transaction fees they could have claimed by playing honestly.  Someone who consistently earns many high-difficulty shared slots but never finds and publishes slot 0 may be worth watching.

The current beta keeps this simple: 300 conceptual payout slots, fixed slot value `subsidy / 300`, slot 0 receives the subsidy remainder and transaction fees, and the optional Grid Labs support slot uses one post-slot-0 slot.  Future protocol versions could experiment with different slot-0 weighting, but that is not part of the current consensus.

### A few toy variance examples
Coming next...
