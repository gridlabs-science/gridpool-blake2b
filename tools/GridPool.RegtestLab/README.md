# GridPool Regtest Lab

This is a disposable, development-only integration lab. It runs one private
Bitcoin Core regtest node and three GridPool nodes. The Docker network is
internal; the three dashboard observer ports bind to host loopback only.

It is intended for ordinary integration, upgrade, restart, reorganization,
state-recovery, and SV2 testing. It does not contain security finding-specific
reproduction procedures.

With the optional SV2 profile, the local SV2 client port is `13465` and the
synthetic miner is started by `lab.sh start-sv2`.

## Prerequisites

- Docker Engine and Compose v2.
- `jq`, `curl`, and `openssl`.
- Verified Bitcoin Core `bitcoind` and `bitcoin-cli` at
  `/usr/local/bin/bitcoind` and `/usr/local/bin/bitcoin-cli`.
- A local checkout of `gridpool-sv2-pool` if the SV2 profile is enabled.

The scripts copy the verified Core binaries into the disposable lab build
context and never use the host testnet datadir.

## Commands

```bash
./tools/GridPool.RegtestLab/lab.sh prepare
./tools/GridPool.RegtestLab/lab.sh init
./tools/GridPool.RegtestLab/lab.sh start
./tools/GridPool.RegtestLab/lab.sh status
./tools/GridPool.RegtestLab/lab.sh logs 200
./tools/GridPool.RegtestLab/lab.sh stop
./tools/GridPool.RegtestLab/lab.sh reset --confirm
```

The default data root is `/home/gridlabs/gridpool-regtest-lab`. Override it with
`GRIDPOOL_LAB_ROOT`. Override the source checkouts with `GRIDPOOL_SOURCE` and
`GRIDPOOL_SV2_SOURCE`.

Observer URLs are:

- Node A: `http://127.0.0.1:15001`
- Node B: `http://127.0.0.1:15002`
- Node C: `http://127.0.0.1:15003`

The lab generates a unique `gridpool-regtest-v22-*` network ID, fresh node
identities, a disposable regtest wallet, and a `bcrt1` payout address. It
generates 101 initial blocks before starting GridPool.

## Ordinary validation

```bash
source /home/gridlabs/gridpool-regtest-lab/lab.env
docker compose --env-file /home/gridlabs/gridpool-regtest-lab/lab.env \
  -f tools/GridPool.RegtestLab/compose.yaml \
  --project-directory /home/gridlabs/gridpool-regtest-lab \
  exec -T bitcoin bitcoin-cli -regtest \
  -rpcuser="$RPC_USER" -rpcpassword="$RPC_PASSWORD" \
  -rpcwallet=lab generatetoaddress 1 "$LAB_PAYOUT_ADDRESS"

for port in 15001 15002 15003; do
  curl -fsS "http://127.0.0.1:$port/api/network/summary" |
    jq '{networkId,bitcoinNetwork,currentTipBlockHeight,currentStateId,
         candidateStateId,peerCount,miningWorkSafe,bitcoinNotification}'
done
```

All three nodes should report `bitcoinNetwork: regtest`, the same Bitcoin tip,
and converged state identifiers. The lab must never contain public bootstrap
peers or production identity/state files.

## Mode controller

The laptop-specific mode controller lives in the private operations repository.
It owns startup and shutdown of this lab, the existing testnet stack, and
`umbrel-dev`. Use it rather than manually starting overlapping stacks:

```bash
gridpool-laptop-mode status
gridpool-laptop-mode switch regtest
gridpool-laptop-mode switch umbrel
gridpool-laptop-mode switch testnet
gridpool-laptop-mode switch off
```

The selected mode persists across reboot. Normal mode switches do not remove
Docker volumes, Core chain data, Umbrel state, or GridPool identities.
