# Constrained Blake2b VPS deployment

This directory defines the first, Testnet4-only node phase for the 6-vCPU,
12-GB RAM, 100-GB VPS. It does not expose GridPool, DATUM, Stratum, RPC, or ZMQ
ports. Testnet4 data must be stopped and removed before a separately rooted
mainnet node is prepared.

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

Before a staging start, copy the testnet node's RPC cookie into an untracked
`bitcoin-cookie/.cookie` directory readable by container UID 1000, create the
untracked `data/` directory, and place any generated identity keys only in
`data/boot_portal_config.local.json`. Start only with a commit-addressed image:

```bash
cd /opt/gridpool-blake2b/src/gridpool-blake2b/deploy/blake-vps
GRIDPOOL_BOOT_IMAGE=gridpool-blake2b:<immutable-commit> \
  docker compose -f docker-compose.testnet4-staging.yml up
```

Do not change the port mappings to a public address or enable peer/mining
ingress until the DATUM gateway, a synthetic miner, and attached-node block
confirmation tests have passed.

## Resource and network policy

- `prune=12000`, `dbcache=2048`, `maxmempool=100`, 64 peer connections.
- Maintain at least 15 GiB free; alert at 20 GiB and fail health at 15 GiB.
- RPC `48332`, hashblock ZMQ `28332`, and rawblock ZMQ `28333` bind loopback.
- UFW denies inbound traffic by default and permits only SSH and Testnet4 P2P
  TCP `48333`. Mining, HTTP, RPC, and ZMQ ingress remain closed.
- A systemd timer runs the local node/disk health check every five minutes and
  records failures in the journal; external alert delivery remains a later
  deployment gate.
- SSH hardening is intentionally left to the owner until a second session is
  verified.
