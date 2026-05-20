# Raspberry Pi Sovereign Stack Installer

This document describes the first one-shot installer path for a small sovereign Grid Pool node.

Target hardware:
- Raspberry Pi 5, 8 GB preferred
- 64-bit Ubuntu Server 24.04 LTS or newer
- fast SSD strongly preferred for Bitcoin initial block download
- reliable LAN connection

Raspberry Pi 3-class hardware should not be treated as a full sovereign stack target. In live testing, a Pi 3 with under 1 GB RAM and a 29 GB SD card could run Boot and DATUM, but local Bitcoin Core sync/template service was too resource constrained. For that hardware class, use edge mode: run Boot + DATUM locally and point DATUM at a trusted Bitcoin RPC server on the LAN.

## Quick Start: Fresh Raspberry Pi 5

This is the shortest manual path for a fresh Pi 5. It assumes:

- Ubuntu Server 24.04 LTS or newer is already installed
- SSH is enabled
- the Pi has wired LAN or stable Wi-Fi
- the Pi has at least 30 GiB free disk space
- you have one Bitcoin payout address ready

Run this from your laptop or desktop, replacing the username, host, and payout address:

```bash
ssh ubuntu@raspberrypi.local
```

Then run this on the Pi:

```bash
sudo apt-get update &&
sudo apt-get install -y git ca-certificates curl &&
git clone https://github.com/gridlabs-science/boot-protocol.git &&
cd boot-protocol &&
sudo ./scripts/install-sovereign-stack.sh \
  --yes \
  --payout-address bc1qYOUR_PAYOUT_ADDRESS_HERE
```

That command installs the full stack:

- pruned Bitcoin Core
- Grid Pool / Boot web UI
- DATUM Gateway

After the install completes, check the services:

```bash
/opt/grid-pool/boot-protocol/scripts/boot-self-check.sh http://127.0.0.1:5000 &&
sudo systemctl is-active bitcoind datum-gateway &&
cd /opt/grid-pool/boot-protocol &&
sudo docker compose -f docker-compose.sovereign.yml ps
```

Open the Grid Pool UI from a browser on the same LAN:

```text
http://PI_LAN_IP:5000
```

Point ASICs to DATUM, not directly to Boot:

```text
stratum+tcp://PI_LAN_IP:23334
```

The ASIC username is ignored by default. The payout address is the one passed to `--payout-address`.

For a quick smoke test before using your real payout address, omit `--payout-address`. That uses the 256 Foundation donation address. Do not leave that default in place for real mining.

### Faster Edge-Mode Smoke Test

If you only want to test Boot + DATUM first, and you already have a trusted Bitcoin Core RPC server on your LAN, skip local Bitcoin Core:

```bash
sudo apt-get update &&
sudo apt-get install -y git ca-certificates curl &&
git clone https://github.com/gridlabs-science/boot-protocol.git &&
cd boot-protocol &&
BITCOIN_RPC_USER=bitcoin \
BITCOIN_RPC_PASSWORD=replace-me \
sudo -E ./scripts/install-sovereign-stack.sh \
  --yes \
  --no-bitcoin \
  --bitcoin-rpc-url http://TRUSTED_BITCOIN_RPC_LAN_IP:8332 \
  --payout-address bc1qYOUR_PAYOUT_ADDRESS_HERE
```

Use edge mode for Pi 3-class devices, tiny disks, or quick network tests. Use full-stack mode for sovereign mining.

## Fast Sovereign Mode With assumeUTXO

Fast sovereign mode means:

- the Pi runs its own local Bitcoin Core node
- the Pi loads a UTXO snapshot to become usable quickly
- Bitcoin Core still background-validates the historical chain after startup
- DATUM and Boot build templates from the Pi's local Bitcoin node

This is the preferred low-friction full-stack path for a Pi 5 once a trusted snapshot workflow is available.

Important constraint: do not generate an arbitrary `latest` snapshot unless you know the target Bitcoin Core release supports that exact snapshot base. Bitcoin Core only accepts assumeUTXO snapshots whose base block/hash is committed in the target binary's chain parameters. The recommended generation command is therefore `dumptxoutset ... rollback`, which rolls back to the latest snapshot base that the source node knows how to generate for assumeUTXO.

### 1. Pick A Trusted Snapshot Source

Use a fully synced node that you already trust, such as:

- your laptop Knots node, if it supports the `dumptxoutset` rollback mode
- your main dev machine Bitcoin Core/Knots node
- another fully synced archival node you control

Avoid doing the snapshot dump on a production mining node if possible. Snapshot generation can take several minutes, and rollback-mode dumping can suspend normal network activity while the snapshot is produced.

On the trusted source node:

```bash
bitcoin-cli getblockchaininfo
```

Confirm:

- `initialblockdownload` is `false`
- `blocks` is close to public chain tip
- `verificationprogress` is very close to `1`

### 2. Choose A Recent assumevalid Hash

`assumevalid` is separate from assumeUTXO. It does not replace the snapshot, but it can reduce historical signature-checking work during background validation.

On the trusted source node:

```bash
HEIGHT="$(bitcoin-cli getblockcount)"
ASSUMEVALID_HEIGHT="$((HEIGHT - 10))"
ASSUMEVALID_HASH="$(bitcoin-cli getblockhash "$ASSUMEVALID_HEIGHT")"
printf 'height=%s\nhash=%s\n' "$ASSUMEVALID_HEIGHT" "$ASSUMEVALID_HASH"
```

Save the printed hash. You will pass it to the installer with `--assumevalid`.

### 3. Generate The Snapshot

On the trusted source node:

```bash
bitcoin-cli -rpcclienttimeout=0 dumptxoutset /tmp/gridpool-assumeutxo.dat rollback
```

Expected output looks like:

```json
{
  "coins_written": 123456789,
  "base_hash": "0000000000000000000...",
  "base_height": 880000,
  "path": "/tmp/gridpool-assumeutxo.dat",
  "txoutset_hash": "...",
  "nchaintx": 1234567890
}
```

Record the full output somewhere. The key fields for troubleshooting are `base_hash`, `base_height`, and `txoutset_hash`.

If the source node does not support rollback-mode `dumptxoutset`, do not use `latest` as a blind substitute. Either upgrade the source node or use a known-compatible snapshot for the Bitcoin Core version installed by the Pi script.

### 4. Copy The Snapshot To The Pi

From your laptop or desktop:

```bash
scp /tmp/gridpool-assumeutxo.dat ubuntu@raspberrypi.local:/home/ubuntu/gridpool-assumeutxo.dat
```

Adjust the username and host as needed.

### 5. Run The Full Installer With The Snapshot

SSH into the Pi:

```bash
ssh ubuntu@raspberrypi.local
```

Then run:

```bash
sudo apt-get update &&
sudo apt-get install -y git ca-certificates curl &&
git clone https://github.com/gridlabs-science/boot-protocol.git &&
cd boot-protocol &&
sudo ./scripts/install-sovereign-stack.sh \
  --yes \
  --payout-address bc1qYOUR_PAYOUT_ADDRESS_HERE \
  --assumevalid ASSUMEVALID_HASH_FROM_STEP_2 \
  --assumeutxo-snapshot /home/ubuntu/gridpool-assumeutxo.dat
```

For the Grid Labs Pi 5 test host, the snapshot path would be:

```bash
sudo ./scripts/install-sovereign-stack.sh \
  --yes \
  --payout-address bc1qYOUR_PAYOUT_ADDRESS_HERE \
  --assumevalid ASSUMEVALID_HASH_FROM_STEP_2 \
  --assumeutxo-snapshot /home/gridlabs/gridpool-assumeutxo.dat
```

The installer copies the snapshot into the Bitcoin datadir, runs `bitcoin-cli loadtxoutset`, and deletes the copied datadir snapshot after loading to recover disk space. The original file in the user's home directory is not deleted by the installer.

### 6. Monitor Snapshot Load And Catch-Up

Check Bitcoin Core service state:

```bash
sudo systemctl status bitcoind --no-pager
```

Check chainstates:

```bash
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf -datadir=/var/lib/bitcoind getchainstates
```

Expected behavior:

- one chainstate is based on the loaded snapshot and catches up from the snapshot base to tip
- one background chainstate continues validating historical blocks
- the node can become useful for current templates before historical validation fully completes

Check ordinary blockchain status:

```bash
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf -datadir=/var/lib/bitcoind getblockchaininfo
```

Once the snapshot chainstate catches up to tip, check Boot and DATUM:

```bash
/opt/grid-pool/boot-protocol/scripts/boot-self-check.sh http://127.0.0.1:5000 &&
sudo systemctl is-active bitcoind datum-gateway &&
cd /opt/grid-pool/boot-protocol &&
sudo docker compose -f docker-compose.sovereign.yml ps
```

Open the UI:

```text
http://PI_LAN_IP:5000
```

Point ASICs at DATUM:

```text
stratum+tcp://PI_LAN_IP:23334
```

### 7. Clean Up The Source Snapshot

After the Pi has loaded the snapshot successfully, you can remove the copied user-home snapshot on the Pi:

```bash
rm -f ~/gridpool-assumeutxo.dat
```

Also remove the source snapshot if you no longer need it:

```bash
rm -f /tmp/gridpool-assumeutxo.dat
```

### Troubleshooting

If `loadtxoutset` fails with an unrecognized snapshot/base hash, the snapshot height is not supported by the target Bitcoin Core binary. Regenerate using `dumptxoutset ... rollback` on a compatible source node, or use a target Bitcoin Core version that supports that snapshot base.

If the Pi runs out of disk, use a larger SSD-backed install. Pruned assumeUTXO nodes can temporarily need more space because Bitcoin Core maintains both the snapshot chainstate and the background-validation chainstate.

If DATUM starts before Bitcoin catches up, that is normal. Mining templates are only useful once Bitcoin Core is close to tip.

## What It Installs

`scripts/install-sovereign-stack.sh` installs:

- Bitcoin Core, pruned, with RPC, ZMQ block notifications, and DATUM `blocknotify`
- Boot/Grid Pool from Docker Compose using host networking
- DATUM Gateway from upstream source
- `bitcoind.service`
- `datum-gateway.service`
- Boot container `boot-portal`

For edge mode, pass `--no-bitcoin --bitcoin-rpc-url http://HOST:PORT`. That skips local Bitcoin Core and configures DATUM to fetch templates from the external RPC source.

Default public ports:

- `5000/tcp`: Boot/Grid Pool Web UI
- `3008/tcp`: Boot DATUM pool endpoint
- `23334/tcp`: DATUM Stratum V1 endpoint for ASICs
- `7152/tcp`: DATUM API bound to localhost only

## Trust Model Defaults

The generated DATUM config assumes one payout address per DATUM Gateway:

- `mining.pool_address` is the payout address for this DATUM client
- `datum.pool_pass_full_users = false`
- `datum.pool_pass_workers = false`
- miner usernames are ignored by default; the configured DATUM payout address is the single slot-0 identity
- raw per-ASIC payout addresses behind one DATUM client are intentionally unsupported

This matches the current protocol rule that share attribution comes from the slot-0 payout address committed into the hashed coinbase transaction.

If no payout address is provided, the installer currently defaults to the 256 Foundation donation address:

```text
bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3
```

That default is only a safe placeholder. Sovereign miners should pass their own address with `--payout-address`.

## Bitcoin Node Mode

The installer configures Bitcoin Core as a lightweight mining node, not a hot wallet:

- `disablewallet=1`
- `prune=1100` by default
- `txindex=0`
- `blockfilterindex=0`
- `coinstatsindex=0`
- small mempool by default, currently `150` MiB
- Bitcoin ZMQ hashblock/rawblock notifications enabled locally
- DATUM `blocknotify` enabled locally

`assumevalid`:

- Bitcoin Core ships with a release-default assumevalid block.
- To use a newer block from a trusted archival node, pass `--assumevalid BLOCKHASH` or set `BITCOIN_ASSUMEVALID`.
- Do not source this hash from an untrusted website if your goal is sovereign verification.

`assumeUTXO`:

- Optional snapshot loading is supported with `--assumeutxo-snapshot PATH_OR_URL`.
- A local file path copied from a canonical node you already trust is preferred.
- Bitcoin Core checks the snapshot against hashes committed in the binary.
- HTTP(S) snapshots stream through a FIFO by default, avoiding a second temporary 10+ GB file on small SD cards.
- The snapshot file is deleted after `loadtxoutset` by default to recover disk space.
- A pruned assumeUTXO node may temporarily use more disk because Core maintains both snapshot and background-validation chainstates.

Low-RAM systems:

- If RAM is below roughly 1.2 GB and no swap exists, the installer creates a 4 GB swapfile by default.
- This is primarily to survive package install, Docker build, and service startup on very small Pis.
- Override with `--swap-mb 0` if you prefer no installer-created swap.
- DATUM Stratum defaults are intentionally small in this installer: 4 max ASIC clients, 1 Stratum thread, and 30 target shares/minute. Increase `DATUM_MAX_CLIENTS`, `DATUM_MAX_CLIENTS_PER_THREAD`, and `DATUM_MAX_THREADS` only on hardware with enough RAM.
- By default, local Bitcoin Core install now refuses very low-resource targets: under 2 GB RAM or under 30 GiB free disk. Use edge mode instead, or set `GRID_ALLOW_LOW_RESOURCE_BITCOIN=1` only if intentionally forcing a risky experiment.

## First Run

From the target machine:

```bash
git clone https://github.com/gridlabs-science/boot-protocol.git
cd boot-protocol
sudo ./scripts/install-sovereign-stack.sh --payout-address bc1q...
```

For a donation-address smoke test:

```bash
sudo ./scripts/install-sovereign-stack.sh
```

For unattended install:

```bash
sudo ./scripts/install-sovereign-stack.sh \
  --yes \
  --noninteractive \
  --payout-address bc1q...
```

For Pi 3 or another lightweight edge node using a trusted LAN Bitcoin RPC server:

```bash
sudo ./scripts/install-sovereign-stack.sh \
  --yes \
  --no-bitcoin \
  --bitcoin-rpc-url http://TRUSTED_BITCOIN_RPC_LAN_IP:8334 \
  --payout-address bc1q...
```

If the external Bitcoin RPC requires credentials, pass them as environment variables:

```bash
BITCOIN_RPC_USER=bitcoin \
BITCOIN_RPC_PASSWORD=replace-me \
sudo -E ./scripts/install-sovereign-stack.sh \
  --yes \
  --no-bitcoin \
  --bitcoin-rpc-url http://TRUSTED_BITCOIN_RPC_LAN_IP:8334 \
  --payout-address bc1q...
```

For a dry run:

```bash
./scripts/install-sovereign-stack.sh \
  --dry-run \
  --yes \
  --payout-address bc1q...
```

## SSH Key Setup For Remote Testing

If Codex is driving the install from the dev machine, add the dev machine public key to the Pi first.

On the Pi, replace `PASTE_KEY_HERE` with the contents of `~/.ssh/id_rsa.pub` from the dev machine:

```bash
mkdir -p ~/.ssh
chmod 700 ~/.ssh
printf '%s\n' 'PASTE_KEY_HERE' >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
```

Then verify from the dev machine:

```bash
ssh ubuntu@PI_LAN_IP 'uname -a'
```

Use the actual Ubuntu username if it is not `ubuntu`.

## Important Overrides

Useful environment variables:

- `GRID_BOOT_REPO_REF`: Boot branch, tag, or commit. Defaults to `main`.
- `GRID_DATUM_REPO_REF`: DATUM branch, tag, or commit. Defaults to `master`.
- `BITCOIN_CORE_VERSION`: Bitcoin Core release. Defaults to `31.0`.
- `BITCOIN_PRUNE_MB`: prune target. Defaults to `1100`.
- `BITCOIN_DBCACHE_MB`: `auto` by default, tuned from installed RAM.
- `BITCOIN_MAX_MEMPOOL_MB`: defaults to `150`.
- `BITCOIN_ASSUMEVALID`: optional trusted recent block hash.
- `BITCOIN_ASSUMEUTXO_SNAPSHOT`: optional local path or HTTP(S) URL to a UTXO snapshot.
- `BITCOIN_ASSUMEUTXO_STREAM`: `auto` by default. HTTP(S) snapshots are streamed instead of saved locally.
- `BITCOIN_RPC_URL`: external Bitcoin RPC URL for DATUM when using `--no-bitcoin`. Defaults to `http://127.0.0.1:8332`.
- `BOOT_PUBLIC_BASE_URL`: advertised Boot Web UI URL. Defaults to `http://detected-lan-ip:5000`.
- `BOOT_DATUM_PUBLIC_HOST`: advertised DATUM host. Defaults to detected LAN IP.
- `GRID_BOOT_BOOTSTRAP_PEERS`: comma-separated bootstrap peers. Defaults to `https://gridpool.net`.
- `DATUM_POOLED_MINING_ONLY`: defaults to `false`, allowing solo fallback templates if Boot is unavailable.
- `GRID_SWAP_MB`: `auto` by default. Set `0` to disable installer-created swap.
- `GRID_ALLOW_LOW_RESOURCE_BITCOIN`: set to `1` to force local Bitcoin install on hardware that the installer would otherwise reject.
- `DATUM_MAX_CLIENTS`: defaults to `4`.
- `DATUM_MAX_CLIENTS_PER_THREAD`: defaults to `4`.
- `DATUM_MAX_THREADS`: defaults to `1`.

Example:

```bash
BOOT_PUBLIC_BASE_URL=http://PI_LAN_IP:5000 \
BOOT_DATUM_PUBLIC_HOST=PI_LAN_IP \
GRID_BOOT_REPO_REF=main \
sudo -E ./scripts/install-sovereign-stack.sh --payout-address bc1q...
```

## Generated Files

Important paths:

- `/etc/bitcoin/bitcoin.conf`
- `/var/lib/bitcoind`
- `/etc/datum_gateway/config.json`
- `/var/log/datum_gateway/datum.log`
- `/opt/grid-pool/boot-protocol`
- `/opt/grid-pool/boot-protocol/data`
- `/etc/grid-pool/install.env`

`/etc/grid-pool/install.env` contains generated RPC and DATUM admin secrets. Keep it private.

## Health Checks

After install:

```bash
/opt/grid-pool/boot-protocol/scripts/boot-self-check.sh http://127.0.0.1:5000
sudo systemctl status bitcoind --no-pager
sudo systemctl status datum-gateway --no-pager
cd /opt/grid-pool/boot-protocol
sudo docker compose -f docker-compose.sovereign.yml logs --tail 100 boot-portal
```

Bitcoin sync:

```bash
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf -datadir=/var/lib/bitcoind getblockchaininfo
```

External RPC check for edge mode:

```bash
curl --user "$BITCOIN_RPC_USER:$BITCOIN_RPC_PASSWORD" \
  --data-binary '{"jsonrpc":"1.0","id":"curl","method":"getblocktemplate","params":[{"rules":["segwit"]}]}' \
  -H 'content-type:text/plain;' \
  "$BITCOIN_RPC_URL"
```

DATUM logs:

```bash
sudo journalctl -u datum-gateway -f
sudo tail -f /var/log/datum_gateway/datum.log
```

## Current Caveats

- The script is a first-pass beta installer. It backs up overwritten config files, but it is not yet a polished uninstall/rollback tool.
- Bitcoin Core release tarballs are checked against `SHA256SUMS` fetched from `bitcoincore.org`; full maintainer-signature verification should be added before recommending this to nontechnical users.
- Boot currently uses host networking in this installer so the container can reach Bitcoin ZMQ at `127.0.0.1:28332`.
- DATUM may start before Bitcoin is fully synced. That is acceptable, but mining templates will not be useful until the Bitcoin node is caught up.
- Ubuntu 25.10 is useful for early testing, but Ubuntu 24.04 LTS should remain the primary documented target before launch.
