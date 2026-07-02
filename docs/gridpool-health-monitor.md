# GridPool Health Monitor

This monitor is a small one-shot Node.js script intended to run every five
minutes from a user-level `systemd` timer. It checks GridPool, Hydrapool,
systemd services, miner identities, peer identities, payout-list addresses,
hashrate trend changes, multi-node consensus status, and actual GridPool
block/payment events.

Runtime state and secrets live outside the repository.

The monitor requires Node 18 or newer. The systemd unit calls
`scripts/gridpool-health-monitor-launcher.sh`, which sources `nvm` when needed
so user-level timers do not accidentally run the old distro `node` binary.

## What It Checks

- GridPool node health:
  - `/health/live`
  - `/health/ready`
  - `/api/network/summary`
  - `/api/network/state/{candidateStateId}`
  - `/api/mining/payouts`
  - `/api/network/local-miners`
- Hydrapool health and Prometheus metrics:
  - `/health`
  - `/metrics`
- Local services:
  - `bootserverapp.service`
  - `hydrapool-gridpool.service`
  - `cloudflared.service`
  - `docker.service`
- Attention-worthy changes:
  - endpoints down for two consecutive checks
  - actual GridPool block found / payment snapshot paid
  - public nodes in the same consensus group disagreeing on protocol version,
    active snapshot, current state, or candidate state for multiple checks
  - sustained hashrate drop or spike
  - high DATUM reject rate on any monitored node
  - new local DATUM miner addresses
  - new Hydrapool Stratum users/workers
  - new GridPool peers
  - addresses on current/candidate payout lists that are not known through local DATUM, Hydrapool, or the configured allowlist

Protocol V2 creates a new active payout snapshot on every ordinary Bitcoin
block. Those snapshot changes are recorded for status and digest context, but
they do not create Telegram alerts. The monitor should wake operators only when
a real GridPool block/payment transition is observed.

## Telegram Bot Setup

1. Open Telegram and start a chat with `@BotFather`.
2. Send `/newbot`.
3. Pick a bot name and username.
4. Copy the HTTP API token that BotFather gives you.
5. Start a direct chat with your new bot and send `/start`.
6. On the GridPool host, run:

```bash
export TELEGRAM_BOT_TOKEN='PASTE_TOKEN_HERE'
curl -fsS "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/getUpdates" | jq
```

7. Find your chat ID in the output:

```json
"chat": {
  "id": 123456789
}
```

8. If using a group chat, add the bot to the group, send a message in the
   group, then run the same `getUpdates` command. Group chat IDs are often
   negative numbers.

9. Install the monitor files:

```bash
cd /home/keegreil/Documents/GitHub/boot-protocol
scripts/install-gridpool-health-monitor.sh
```

10. Edit the local env file:

```bash
nano ~/.config/gridpool-health-monitor/monitor.env
```

Set:

```bash
TELEGRAM_BOT_TOKEN=PASTE_TOKEN_HERE
TELEGRAM_ALLOWED_CHAT_IDS=123456789
TELEGRAM_COMMAND_CHAT_IDS=123456789
HYDRAPOOL_API_USER=hydrapool
HYDRAPOOL_API_PASSWORD=hydrapool
```

Use comma-separated chat IDs if more than one chat should receive alerts. To add receive-only observers, include them in `TELEGRAM_ALLOWED_CHAT_IDS` but not in `TELEGRAM_COMMAND_CHAT_IDS`.

Example with you as the only command operator and a tester as receive-only:

```bash
TELEGRAM_ALLOWED_CHAT_IDS=YOUR_CHAT_ID,TESTER_CHAT_ID
TELEGRAM_COMMAND_CHAT_IDS=YOUR_CHAT_ID
```

If `TELEGRAM_COMMAND_CHAT_IDS` is empty, the monitor preserves old behavior and allows every `TELEGRAM_ALLOWED_CHAT_IDS` chat to use commands.

11. Send a test message:

```bash
source ~/.config/gridpool-health-monitor/monitor.env
node scripts/gridpool-health-monitor.mjs \
  --config ~/.config/gridpool-health-monitor/config.json \
  --state-dir ~/.local/state/gridpool-monitor \
  --test-telegram
```

## Running Manually

Dry-ish local check without Telegram or Codex:

```bash
node scripts/gridpool-health-monitor.mjs \
  --config ~/.config/gridpool-health-monitor/config.json \
  --state-dir /tmp/gridpool-monitor-test \
  --telegram-disabled \
  --codex-disabled \
  --print-summary
```

Force the morning digest now:

```bash
source ~/.config/gridpool-health-monitor/monitor.env
node scripts/gridpool-health-monitor.mjs \
  --config ~/.config/gridpool-health-monitor/config.json \
  --state-dir ~/.local/state/gridpool-monitor \
  --force-digest
```

## Systemd Operation

The installer creates a user-level timer:

```bash
systemctl --user list-timers gridpool-health-monitor.timer
systemctl --user status gridpool-health-monitor.timer --no-pager
systemctl --user start gridpool-health-monitor.service
journalctl --user -u gridpool-health-monitor.service -n 100 --no-pager
```

If the machine runs unattended without an active login session, enable linger:

```bash
sudo loginctl enable-linger "$USER"
```

Stop the monitor:

```bash
systemctl --user disable --now gridpool-health-monitor.timer
```

## Telegram Commands

Send these to the bot from an allowed chat:

- `/status`: compact live status.
- `/digest`: full digest immediately.
- `/investigate`: launch a Codex investigation on the latest state.
- `/silence 2h`: mute non-critical alerts for two hours.
- `/help`: command list.

Only chats listed in `TELEGRAM_COMMAND_CHAT_IDS` can use commands. Other chats listed in `TELEGRAM_ALLOWED_CHAT_IDS` receive alerts and digests only.

## Codex Investigation

When warning or critical incidents are delivered, the monitor can run:

```bash
codex exec -C /home/keegreil/Documents/GitHub/boot-protocol \
  --sandbox read-only \
  --ask-for-approval never \
  resume --last
```

The prompt tells Codex to investigate only. It must not edit files, restart
services, or deploy. The monitor writes an incident packet and expects Codex to
return structured JSON with:

- root cause
- evidence
- resolved or not
- whether manual action is needed
- recommended manual action
- confidence

Incident packets and Codex findings are written under:

```bash
~/.local/state/gridpool-monitor/incidents/
```

## Configuration

The example config is:

```bash
config/gridpool-health-monitor.example.json
```

The local live config is copied to:

```bash
~/.config/gridpool-health-monitor/config.json
```

Tune these fields first:

- `nodes`: GridPool UI/API endpoints.
  - Use `consensusGroup` to compare only nodes that should agree. Example:
    `mainnet-beta` for `main.gridpool.net` and `evomining.farted.net`, and
    `testnet4-beta` for `test.gridpool.net`.
  - `minimumPeerCount` is checked per node, but hidden/NATed peers may still
    be visible only through another public node.
- `hydrapools`: Hydrapool API endpoints.
- `services`: local systemd services to check.
- `knownAddresses`: addresses that should not trigger unknown-address alerts.
- `thresholds.consensusDivergenceConsecutive`: default `2`.
- `thresholds.candidateDivergenceConsecutive`: default `3`, because candidate
  state can drift briefly while shares propagate.
- `thresholds.datumRejectRateMax`: default `0.10`.
- `thresholds.hashrateDropFraction`: default `0.35`.
- `thresholds.hashrateSpikeMultiplier`: default `2.0`.
- `alertCooldownMinutes`: default `60`.

The installer does not overwrite an existing
`~/.config/gridpool-health-monitor/config.json`. To adopt new public-node
checks on an existing install, copy the `nodes` and `thresholds` sections from
`config/gridpool-health-monitor.example.json` into the live config.

## Review Logs

The monitor writes compact logs intended for quick review by a human or Codex:

```bash
~/.local/state/gridpool-monitor/latest-summary.json
~/.local/state/gridpool-monitor/latest-consensus.json
~/.local/state/gridpool-monitor/snapshots/YYYY-MM-DD.jsonl
~/.local/state/gridpool-monitor/consensus/YYYY-MM-DD.jsonl
~/.local/state/gridpool-monitor/alerts/YYYY-MM-DD.jsonl
```

The JSONL files intentionally omit full state bundles. They preserve the
important operational facts: endpoint status, version numbers, state IDs,
snapshot IDs, Work Set counts, hashrate, local DATUM reject rate, peer counts,
and consensus-group divergence.

## Files

- Script: `scripts/gridpool-health-monitor.mjs`
- Launcher: `scripts/gridpool-health-monitor-launcher.sh`
- Installer: `scripts/install-gridpool-health-monitor.sh`
- Example config: `config/gridpool-health-monitor.example.json`
- User service: `deploy/systemd/user/gridpool-health-monitor.service`
- User timer: `deploy/systemd/user/gridpool-health-monitor.timer`
- Local env: `~/.config/gridpool-health-monitor/monitor.env`
- Local state: `~/.local/state/gridpool-monitor/`
