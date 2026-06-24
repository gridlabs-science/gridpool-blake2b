# GridPool
GridPool is a decentralized reward-sharing protocol for sovereign Bitcoin miners. It reduces solo-mining payout variance without a custodial pool wallet, a centralized share ledger, or a separate sharechain.

- It is like P2Pool in spirit, but much simpler: miners coordinate on coinbase payout lists instead of maintaining a secondary blockchain.
- It is not a traditional pool. It is closer to shared lottery mining: smaller payouts, better odds, and local block-template control.
- This reference implementation works with DATUM today. Hydrapool and other HTTP share submitters are planned.

## Naming Note
GridPool was originally developed under the working name "Boot Protocol." Some internal code, repository names, API headers, config keys, service names, and scripts still use `boot` for compatibility during the beta transition. Public docs, UI, and operator-facing language should use **GridPool**. When precision is needed, use **GridPool protocol** for the reward-sharing rules and **GridPool internode protocol** for peer-to-peer state synchronization and share relay.

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
2. Copy the displayed Pool Host, Pool Port, and Pool Pubkey.
3. Paste those into DATUM.
4. Point ASICs at DATUM, not directly at GridPool.

If you need a full fresh sovereign stack, including pruned Bitcoin Core and DATUM Gateway, use the full-stack installer in `docs/raspberry-pi-one-shot-installer.md`.

## Docker Compose Quickstart
GridPool includes a basic Docker Compose packaging path for manual public beta testing.

Beta defaults now assume `299` shared Winners List slots, with slot `0` reserved for the block finder. Some of the longer discussion below still uses older 15/16-slot toy examples for intuition.

1. Review `docker/boot_portal_config.sample.json`.
2. Bring the stack up with `docker compose up -d`.
3. The container persists runtime files under `./data` by default:
   - `./data/boot_portal_config.json`
   - `./data/pool_state.json`
4. The WebUI is exposed on port `5000` and the DATUM listener on port `3008`.
5. Run `scripts/boot-self-check.sh http://127.0.0.1:5000` after startup to verify health, peer state, round state, and local DATUM hashrate.

Notes:
- The default Docker Compose file pulls the prebuilt image `ghcr.io/gridlabs-science/boot-protocol:latest`.
- To build locally from source instead, run `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`.
- To pin a specific published image, set `GRIDPOOL_BOOT_IMAGE`, for example `GRIDPOOL_BOOT_IMAGE=ghcr.io/gridlabs-science/boot-protocol:sha-abc1234 docker compose up -d`.
- The container is set up for HTTP on the WebUI by default.  Terminate TLS at a reverse proxy, Cloudflare tunnel, or similar edge layer.
- The image runs as non-root UID/GID `1000` and creates `/data/boot_portal_config.json` from `docker/boot_portal_config.sample.json` if no config exists.
- The default sample uses `NotificationSource = "MempoolSpace"` so a local `bitcoind` is not required for first boot.
- If you want local ZMQ block notifications, change the config and make sure the container can reach your Bitcoin node.
- The mainnet beta bootstrap seed is `https://main.gridpool.net` unless you override `bootstrap_peers`.
- Testnet4 beta nodes should use `bitcoin_network = "testnet4"`, `boot_network_id = "testnet4-beta"`, and bootstrap from `https://test.gridpool.net`.
- Health probes are exposed at `/health/live` and `/health/ready`.
- The default DATUM primary coinbase tag is `Grid Pool`; set `coinbase_tag` to another string, or `""` for unbranded blocks.
- Back up the Docker `./data` directory before machine moves, package upgrades, or host rebuilds. It contains live config, server identity keys, pool state, and history.
- Hydrapool and other direct HTTP submitters should follow `docs/hydrapool-http-submission.md`.

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
2. The 30-second block times excacerbated the negative effects of block propagation time.  In all blockchains, when a new block is found it takes a brief amount of time before the rest of the network knows about the new block.  This means that nodes which discover the new block first have an advantage of being able to work on building on the new block before the rest of the network finds out.  If the difference between average block times and block propogation speed is great, than this effect is negligible, for example Bitcoin's ~10 minute block times (600 seconds) vs. about 6 seconds for network propogation.  However if block times are very fast (eg. 30 seconds), then physically centralizing hashrate becomes quite advantageous.  Nodes near the network center will earn significantly higher rewards.

The second (upcoming) solution is Braid Pool, an exciting new decentralized pool which solves the second problem (block propogation advantage).  Braid Pool is a much more advanced attempt using Directed Acyclic Graphs (akin to Kaspa) to eliminate the block propegation problem while still having very fast "block" times on the share-chain (about 1 second in theory).  In theory a block witholding attack would reduce miner revenue.  The pool itself could be 51% attacked, so this requires additional complexity to protect against.  

The third (also upcoming) solution is Ocean Pool.  Ocean Pool of course is already operating, and allows hashers the choice of three different templates.  They are working hard on adding the ability for miners to build their own templates, which will solve the problem of centralized block template creation.  Being a centralized entity themselves (albeit with nicely decentralized block template construction), there is the black swan risk that Ocean could get shut down by regulators.  Miners on Ocean also run the risk of reduced revenue from a block witholding attack.  

GridPool attacks the variance problem from the other side. Traditional pools estimate miner effort by tracking many shares in a centralized database. P2Pool-style systems decentralize share accounting with a sharechain. GridPool never centralizes the block reward and does not maintain a separate chain. Miners build Bitcoin templates that pay a shared Winners List, then relay high-difficulty proofs so the next payout list can be verified by peers. Conceptually, GridPool is much closer to solo mining, but with up to roughly 300x lower variance when the shared list is full.

## Advantages of GridPool
Compared to solo mining, GridPool should have up to 300x reduced variance.  
Compared to standard pooled mining (eg. Ocean with sovereign block template construction), GridPool should offer reduced bandwidth requirements and much more resiliance to regulatory attack.
Compared to decentralized pooled mining, GridPool should have reduced bandwidth requirements, reduced computational overhead, and a vastly simpler code base.  

### Block Witholding attacks (killer feature?)
GridPool should be far more resistant against *(and possibly immune to?) block witholding attacks*.  If proven, this would be the only known method of sharing block rewards in a decentralized permissionless manner that is not vulnerable to block witholding.  

---
## GridPool Pseudocode
Key terms:
- Winners List:  A list of 299 shared payout addresses that have provided the highest difficulty hashes on the current template.  The miner's own address remains slot `0`, so the full payout set has 300 total slots.  This list is finalized once a new real Bitcoin block is found.
- GridPool Share Proof: This consists of the block header, and just enough information from the Merkle Tree that other nodes can verify the addresses listed in the coinbase transaction
- WL Threshold Difficulty: Defined as 1/2 the difficulty of the lowest difficulty proof from the previous round's Winners List. This threshold could be raised to reduce bandwidth requirements, or lowered if necessary.
- Team: In this context, a team is the loose grouping of miners that are all working on templates built from the same Winners List.  They are sharing their proofs with each other, attempting to get on the next Winners List

1. Create a block template using a local Bitcoin node.  The Coinbase payout should split the block reward evenly between your own address and the 299 shared addresses in the primary Winners List (see later steps).  If the WL has less than 299 entries (not including your own address in spot 0), divide the rewards equally between however many addresses are on the list.  Your own address is always spot '0'.
2. Start hashing on this template.  Once the first solution is found that meets the WL Threshold Difficulty, move to step 3:
3. Create a GridPool share proof.  Using the found solution (which is just a Bitcoin block with a lower difficulty target), broadcast this proof to other nodes.
4. Continue hashing while listening for other GridPool share proofs.
   If one is recieved, validate it:
     It must be a valid Bitcoin block, albeit with lower difficulty.
     If spots 1-15 of the coinbase transaction match our own template's, then this proof of work is from our team.
       Check the difficulty against our current Winner's List.
       If it is better than any of those in spots 1-15, then insert it into the list in the appropriate spot and remove the lowest difficulty proof from the WL.  Then re-broadcast it to other nodes.
       Else if it is lower than spot 15, ignore it.  It this happens repeatedly, then this node might be trying a DOS attack, consider disconnecting or blacklisting them.
     If spots 1-15 of the coinbase do NOT match our own template's, then this proof of work is from a different team also using GridPool.  Create a second (or 3rd, or 4th) Winner's List, and add this proof of work to it, and finally re-broadcast the message.
   If we have created multiple Winner's Lists, periodically check which list has the highest total difficulty.  Sum up the difficulty from each of the 15 proofs on each list.  Select the one with the highest total difficulty as the primary list.  Note:  This might need to be highest average difficulty, or highest median dificulty.  I'm not sure yet, and not good enough at statistics to figure it out.  
5. When a new real Bitcoin block is found, freeze the Winner's List(s).  Only keep the primary list, secondary lists can be deleted.  Go to Step 1.
---
## Plain Language Discussion
Imagine 16 frens who want to get into mining.  They each buy an identical 1TH Bitaxe to start solo hashing.  They don't trust pools, but they'd rather have 16x better odds of getting 1/16 of a block, so they all agree to put each other's addresses in the coinbase split evenly.  Simple.  They have now reduced their variance.  

Next, one of their Bitaxes dies, and gets replaced by a 15TH Future Bit, bringing their team to 30TH.  But since that machine is putting out 50% of the team's hashpower, they all agree that the block reward should be 50% his, and the rest gets split by the 15 Bitaxes.  

After that, a few of the frens start experiencing power issues and aren't on 100% of the time.  Being good Bitcoiners, they decide to verify, not trust.  They agree to send each other their block solution proofs (shares) for each new block they work on.  If someone doesn't submit a share for the current block, then they must have turned off and get cut out of the coinbase split for the next block.  

Over time they notice that if they list out all the shares sorted by difficulty for each block attempt, the Future Bit produces about 8 of the top 16 shares.  They also get 100's more frens that want to join.  Since they don't want to split the coinbase 100's of ways, they decide to run a little competition.  Whoever can produce one of the top 15 shares gets their address in the coinbase of the team's next attempted block.  Those who have bigger machines, or stay on 24/7, have a lot more chances to get high difficulties, and so end up in the coinbase of block attempts more often.  The little Bitaxes can still play, but they have to get pretty lucky.  Not as lucky as pure solo though.  

### What must I do to get paid?
To actually get paid at all, two things must happen.  You have to submit a share to the team which has a top 15 difficulty.  If you control 1/15th of your team's hashrate, then on average you should get into every coinbase they construct.  Sometimes you might win two or three of the top 15 spots, sometimes none.  But on average, you should get one on each round.  Next, someone on your team must actually find a real block of course.  

### Extremes analysis
Sometimes it can help to push an idea to extremes to see how it behaves.  Imagine your team grows in total hashrate to the size of the entire Bitcoin network.  Every single block is won by someone in this team.  Then every coinbase gets split 16 ways.  But now, for a Bitaxe to solo mine a reward, they don't need to hit a block with full network difficulty.  They only need to hit one with 1/15th of that.  If that happens, they will get in the next coinbase template, and thus will get their very lucky reward.  Even in this extreme case, the "centralized" hashrate is no threat to the network because everyone is producing their own templates.  The "team" cannot collude to 51% attack the network, because the only component they are colluding on is the coinbase share.

### Team splits and joins
This algorithm is written to always seek out the most powerful team to join.  That is always in each hasher's best interest because that should give them the lowest variance.  Even if the team's Threshold Difficulty is pretty high, it should be better to have a low chance of getting on a powerful team's coinbase than a high chance of getting on the list with a weak team.  This has the potential to take over the entire network.  

There are edge cases due to network latency.  When a block is found, different miners will see it at different times and thus freeze their current round Winners Lists in different states.  This could happen if a Bitcoin block and a top 15 team share are found at the same time, creating weird race conditions where some nodes include the new share in their Winners List and some do not.  This could split the team in two, effectively creating two separate teams working on different coinbase templates.  They should end up sharing their proofs across the seam.  Nodes that get messages from both teams can track both Winners Lists and then naturally flow over to the stronger team.  

### Layering 
This concept of coinbase splitting can be used as a base layer underneath other standard payout schemes like PPLNS and FPPS.  Any pool or solo miner could use this to "join forces" and decrease their collective variance.  In fact, given the very minimal downsides, they stand to lose out long term against pools and miners that do use this mechanism.  Unfortunately, the only pool that can't benefit from this is Ocean Pool, because they also use the coinbase transaction for payout splitting.  I love Ocean Pool and hope they continue.  I think this protocol and Ocean are serving different needs.  

### Block Witholding thoughts
I suspect this protocol is highly resistant to block witholding attacks.  Given that each miner puts their own address as spot 0 on the Winners List, they have an immediate incentive to go ahead and submit that block to the network if they find a real block and collect their reward.  No value is ever promised, tallied, or accounted long term.  If an adversary consisted of 50% of a team's hashrate, they could expect to claim 50% of the top 15 spots on average and would get 50% of the team's rewards.  However if they chose to block withold, then the team on average would find 50% fewer blocks.  So the attacker would get 1/2 the reward they would have recieved if they'd played honestly.  Lets say though that before an attack a team is winning 1 block per day, and an attacker with an equal amount of power joins and takes 50% of the top 15 spots.  The team's power should have doubled, but because the attacker doesn't submit any blocks, they still win about 1 block per day.  Now unfortunately that 1 block is split almost 50/50 with the attacker.  I say 'almost' because the attacker never gets to be in Spot 0 on the list.  That's for whoever actually finds AND submits that real block.  So the attack is costly for everyone, but the honest players should always have a slight advantage in that they always control Spot 0.  This might make detection easier as well.  Someone who consistently gets 7 or 8 top spots but never finds Spot 0 is probably not a fren.

This could be adjusted as well.  The block reward could be divided by 17 (instead of 16), with each spot on the Winners List getting 1/17th of the reward, but Spot 0 getting 2/17th.  Or 3/18ths, or 4/19ths.  This would put an attacker at a progressively more diadvantageous position.  Of course this re-introduces luck and variance to actually finding the block, and in the extreme (eg. 95/100ths) just turns back into solo mining.  However the main point is that this is a tunable version of solo mining, and varying degrees of luck can be shared among the team members.

### A few toy variance examples
Coming next...
