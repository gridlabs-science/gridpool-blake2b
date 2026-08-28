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

## Deliberate discovery mode

RC3 requires `blake2b_headline` to be set, but the signed tag does not embed the
operator-selected Testnet4 headline. The initial configuration deliberately
sets an empty value so the node can sync and the activation block can be
inspected without guessing. This is not the public-mining configuration.

Before public mining:

1. obtain and independently review the exact Testnet4 headline;
2. replace the empty value;
3. discard/resync the disposable Testnet4 chainstate with that value;
4. verify activation height `150027`, first target `0x1a00ffff`, the activation
   coinbase headline, post-fork peers, and current tip;
5. keep attached-node confirmation authoritative and only then consider opening
   mining ingress.

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
