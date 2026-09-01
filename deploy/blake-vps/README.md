# Constrained Blake2b VPS deployment

This directory defines the one-chain-at-a-time Testnet4 and activated-mainnet
profiles for the 6-vCPU, 12-GB RAM, 100-GB VPS. RPC, ZMQ, HTTP, GridPool peer,
and UDP remain private. Stop Testnet4 services before starting the separately
rooted mainnet node.

## Activated mainnet phase

Mainnet uses signed tag `v29.4.1.knots20260508rc4`, peeled commit
`dc82be77dd741dfa63e1f816367b15364d55b051`, the exact height-961640
activation checkpoint, and the headline `8-30 NYPost Deride And Conquer`.
RC4 applies RDTS from the compiled Blake activation schedule; no separate
mainnet RDTS flag is needed or permitted.

`knots-mainnet.conf` uses a 12-GiB prune target, 4-GiB database cache, and the
activation hash as `assumevalid`. The optional one-shot AssumeUTXO unit downloads
the height-910000 snapshot into the dedicated mainnet datadir. The snapshot may
come from an untrusted mirror because `loadtxoutset` verifies it against the
hash, base height, transaction count, and block hash compiled into RC4. Full
background validation continues from genesis after the snapshot chainstate
becomes usable. Keep at least 15 GiB free throughout that validation.

Do not enable mainnet GridPool or mining ingress until
`check-knots-mainnet.sh` reports the exact RC4 subversion, the activation hash,
a synced tip, and the attached GridPool profile attests successfully.

## Pinned node source

- Tag: `v29.4.1.knots20260508rc3`
- Peeled commit: `afbe91c299e16519f03902939fdbda8af9bd527d`
- Signing fingerprint: `1A3E 761F 19D2 CC77 85C5 502E A291 A2C4 5D0C 504A`
- Build: headless, wallet disabled, ZMQ enabled, tests enabled
- `bitcoind` SHA-256: `50694fc6fd4fe0dc8aa66e4695654cdb1dffbe00e6b2192919a476efa44b3ad2`
- Upstream verification: 130/130 CTest targets and the four Blake/RDTS
  functional tests passed on the VPS

The deployed binary hash and build/test evidence belong in
`config/blake2b-source-lock.json` before any mining endpoint is enabled.

## Headline-locked Testnet4 mode

The initial discovery sync completed at height `150240`. A new datadir then
completed a clean headline-locked sync with IBD false at height `150245` on
August 28, 2026. Reinspection of block `150027` found the exact 30-byte activation headline
`PyBLOCK-LOTTO-BLAKE2b-t4-ASIC`. The activation block hash is
`000000000000007a178eb03e6619f0420d7d38e278e6bb5ee16f15ac5b32cee6`;
its header is 164 bytes with compact target `0x1a00ffff`, while block `150026`
has an 80-byte header. The attached node reports the `reduced_data` deployment
active from height `150027`, the health timer is active, and the obsolete
discovery datadir has been removed. The configured headline was also checked
against the pinned RC3 source's activation validation and coinbase construction
paths.

The clean node checkpoint is complete. Before public mining:

1. keep activation height `150027`, first target `0x1a00ffff`, the activation
   coinbase headline, post-fork peers, and current tip in health/soak checks;
2. keep attached-node confirmation authoritative and only then consider opening
   mining ingress.

## GridPool staging harness

`docker-compose.testnet4-staging.yml` is a deliberately local-only harness for
the Blake GridPool node. It maps its HTTP and DATUM ports to `127.0.0.1` only;
it does not expose SV1, GridPool peer, UDP, RPC, or ZMQ traffic. The committed
configuration uses the exact Testnet4 Blake profile, attached-node RPC/ZMQ,
the fee-free 299-winner payout policy, and full coinbase outputs.

The harness uses the dedicated Docker bridge `172.30.0.0/24`. The attached
Knots service may allow only that bridge CIDR and bind RPC/ZMQ to its bridge
gateway (`172.30.0.1`) in addition to `127.0.0.1`; neither endpoint is routed
or permitted through the provider firewall. This is the sole container-to-node
trust boundary for the staging service.

Before a staging start, copy the testnet node's RPC cookie into an untracked
`bitcoin-cookie/.cookie` directory readable by container UID 1000, create the
untracked `data/` directory, and place any generated identity keys only in
`data/boot_portal_config.local.json`. ASP.NET Core protection keys are stored
under `data/data-protection-keys/`, alongside the mounted state; this directory
must remain private to the container user and persist across image recreation.
Create the dedicated bridge before the first start (and do not substitute a
routable subnet):

```bash
docker network create --subnet 172.30.0.0/24 gridpool-blake2b-testnet4-staging
```

Start only with a commit-addressed image:

```bash
cd /opt/gridpool-blake2b/src/gridpool-blake2b/deploy/blake-vps
GRIDPOOL_BOOT_IMAGE=gridpool-blake2b:<immutable-commit> \
  docker compose -f docker-compose.testnet4-staging.yml up
```

On the VPS, install and enable
`gridpool-blake2b-testnet4-staging.service` after the image is built. It
requires the precreated bridge and manages only this container; it never
changes the firewall policy.

The Testnet4 DATUM protocol listener may be published only after the DATUM
gateway, synthetic miner, and attached-node confirmation checks pass. Its
default public endpoint is `datum.testnet4.blake.gridpool.net:3009`; the HTTP
port remains loopback-only.

## Local DATUM gateway

`gridpool-blake2b-datum-testnet4.service` runs the pinned GridLabs DATUM build
as the `bitcoin` user. Its untracked JSON configuration must use the node's
RPC cookie, `http://127.0.0.1:48332`, GridPool's local DATUM endpoint
`127.0.0.1:3009`, and the GridPool server public key generated in the staging
data directory. For the public Testnet4 firmware window, bind Stratum to
`0.0.0.0:3334` and permit only that TCP port through UFW. Force the `yuge`
coinbase selection, keep firmware fingerprinting enabled, and keep the unsafe
override disabled.

For the CPU Blake2b Testnet4 lab, set both `min_diff` in the GridPool config
and `stratum.vardiff_min` in the untracked DATUM config to `1`. This is a
test-only setting: it intentionally permits frequent low-difficulty shares and
must not be carried into a public production deployment.

The staging validation requires both outcomes: a known undersized firmware
fingerprint must be rejected before it receives work, while an unrecognized
client must receive a forced `yuge` Blake2b job and be recorded as unverified.
The public Stratum endpoint is a firmware test service using its configured
test payout address, not a multi-payout hosted pool.

The DATUM reference gateway pads encrypted PoW-submit payloads with up to 80
opaque bytes after their `0xFE` section terminator. GridPool accepts only this
bounded, post-terminator padding; it never parses it as a section and rejects
missing terminators or longer padding.

## Resource and network policy

- `prune=12000`, `dbcache=2048`, `maxmempool=100`, 64 peer connections.
- Maintain at least 15 GiB free; alert at 20 GiB and fail health at 15 GiB.
- RPC `48332`, hashblock ZMQ `28332`, and rawblock ZMQ `28333` bind loopback.
- UFW denies inbound traffic by default and permits SSH, Testnet4 P2P TCP
  `48333`, DATUM TCP `3009`, and test Stratum TCP `3334`. HTTP, RPC, ZMQ,
  GridPool peer, and UDP ingress remain closed.
- A systemd timer runs the local node/disk health check every five minutes and
  records failures in the journal; external alert delivery remains a later
  deployment gate.
- SSH hardening is intentionally left to the owner until a second session is
  verified.
