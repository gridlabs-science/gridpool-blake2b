#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG_DIR="${GRIDPOOL_HEALTH_CONFIG_DIR:-${HOME}/.config/gridpool-health-monitor}"
STATE_DIR="${GRIDPOOL_HEALTH_STATE_DIR:-${HOME}/.local/state/gridpool-monitor}"
USER_UNIT_DIR="${HOME}/.config/systemd/user"

mkdir -p "$CONFIG_DIR" "$STATE_DIR" "$USER_UNIT_DIR"

if [[ ! -f "${CONFIG_DIR}/config.json" ]]; then
  cp "${REPO_DIR}/config/gridpool-health-monitor.example.json" "${CONFIG_DIR}/config.json"
  echo "Created ${CONFIG_DIR}/config.json"
else
  echo "Keeping existing ${CONFIG_DIR}/config.json"
fi

if [[ ! -f "${CONFIG_DIR}/monitor.env" ]]; then
  cat > "${CONFIG_DIR}/monitor.env" <<'EOF'
# Telegram bot token from @BotFather.
TELEGRAM_BOT_TOKEN=

# Comma-separated Telegram chat IDs that receive alerts and digests.
TELEGRAM_ALLOWED_CHAT_IDS=

# Optional comma-separated Telegram chat IDs allowed to issue bot commands.
# If empty, TELEGRAM_ALLOWED_CHAT_IDS can issue commands for backward compatibility.
# Keep receive-only observers out of this list.
TELEGRAM_COMMAND_CHAT_IDS=

# Hydrapool API credentials. Defaults are shown here for the current beta setup.
HYDRAPOOL_API_USER=hydrapool
HYDRAPOOL_API_PASSWORD=hydrapool
EOF
  chmod 600 "${CONFIG_DIR}/monitor.env"
  echo "Created ${CONFIG_DIR}/monitor.env"
else
  chmod 600 "${CONFIG_DIR}/monitor.env"
  echo "Keeping existing ${CONFIG_DIR}/monitor.env"
fi

cp "${REPO_DIR}/deploy/systemd/user/gridpool-health-monitor.service" "${USER_UNIT_DIR}/gridpool-health-monitor.service"
cp "${REPO_DIR}/deploy/systemd/user/gridpool-health-monitor.timer" "${USER_UNIT_DIR}/gridpool-health-monitor.timer"

systemctl --user daemon-reload
systemctl --user enable --now gridpool-health-monitor.timer

cat <<EOF
GridPool health monitor installed.

Config: ${CONFIG_DIR}/config.json
Secrets: ${CONFIG_DIR}/monitor.env
State: ${STATE_DIR}

Next checks:
  systemctl --user list-timers gridpool-health-monitor.timer
  systemctl --user start gridpool-health-monitor.service
  journalctl --user -u gridpool-health-monitor.service -n 100 --no-pager

If this machine runs headless, enable user-service linger once:
  sudo loginctl enable-linger ${USER}
EOF
