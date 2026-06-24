# Mainnet Beta Service Runbook

This runbook records the current public-beta host layout and the commands needed
to make it survive a power cycle.

## Current Host Layout

- GridPool UI/API/DATUM endpoint: `bootserverapp.service`
  - UI/API: `0.0.0.0:5000`
  - DATUM: `0.0.0.0:3008`
  - Config: `/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/boot_portal_config.json`
  - State: `/home/keegreil/Documents/GitHub/boot-protocol/boot_portal/pool_state.json`
- Public Stratum V1 bridge: `hydrapool-gridpool.service`
  - Stratum: `0.0.0.0:3333`
  - API: `127.0.0.1:46884`
  - Config: `/home/keegreil/Documents/GitHub/hydrapool/config.toml`
- Bitcoin backend: Umbrel Docker container `bitcoin_bitcoind_1`
  - Host RPC: `0.0.0.0:8332`
  - Internal RPC used by Hydrapool: `http://10.21.21.8:8332`
  - Internal ZMQ hashblock used by Hydrapool: `tcp://10.21.21.8:28334`
- Public HTTP/TCP routing: `cloudflared.service` plus router forwards.

## One-Time Install

Run this from the GridPool repo:

```bash
cd /home/keegreil/Documents/GitHub/boot-protocol
chmod +x scripts/hydrapool-gridpool-launcher.sh scripts/install-main-beta-services.sh scripts/main-beta-status.sh
sudo ./scripts/install-main-beta-services.sh
```

The installer does not overwrite the existing GridPool service. It installs and
enables `hydrapool-gridpool.service`, then enables these already-existing
dependencies:

- `docker.service`
- `cloudflared.service`
- `bootserverapp.service`

## Verify After Install Or Reboot

```bash
/home/keegreil/Documents/GitHub/boot-protocol/scripts/main-beta-status.sh
```

Expected minimum result:

- `docker.service`: `active=active`, `enabled=enabled`
- `cloudflared.service`: `active=active`, `enabled=enabled`
- `bootserverapp.service`: `active=active`, `enabled=enabled`
- `hydrapool-gridpool.service`: `active=active`, `enabled=enabled`
- GridPool APIs: `ok`
- Hydrapool API health: `ok`
- Ports listening: `5000`, `3008`, `3333`, `46884`, `8332`

## Logs

```bash
journalctl -u bootserverapp.service -n 200 --no-pager
journalctl -u hydrapool-gridpool.service -n 200 --no-pager
journalctl -u cloudflared.service -n 100 --no-pager
docker logs --tail 100 bitcoin_bitcoind_1
```

Follow logs live:

```bash
journalctl -u bootserverapp.service -f
journalctl -u hydrapool-gridpool.service -f
```

## Controlled Restarts

```bash
sudo systemctl restart bootserverapp.service
sudo systemctl restart hydrapool-gridpool.service
sudo systemctl restart cloudflared.service
```

Restart Bitcoin backend only if necessary:

```bash
docker restart bitcoin_bitcoind_1
```

## Reboot Drill

Use this before leaving the machine unattended:

```bash
sudo reboot
```

Wait 3-5 minutes, then run:

```bash
/home/keegreil/Documents/GitHub/boot-protocol/scripts/main-beta-status.sh
```

If Hydrapool is down but GridPool and Bitcoin are up:

```bash
sudo systemctl restart hydrapool-gridpool.service
journalctl -u hydrapool-gridpool.service -n 200 --no-pager
```

## Notes

- The Hydrapool launcher reads the Umbrel bitcoind RPC cookie on each start, so
  a reboot or bitcoind cookie rotation should not strand the Stratum bridge with
  stale RPC credentials.
- The Hydrapool service starts after Docker and GridPool, then waits for the
  local GridPool payout API and bitcoind cookie before launching Hydrapool.
- Some unrelated Umbrel app containers may be restarting. The critical Bitcoin
  container for GridPool is `bitcoin_bitcoind_1`.
