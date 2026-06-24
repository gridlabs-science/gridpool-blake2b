#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run with sudo: sudo $0" >&2
  exit 1
fi

REPO_DIR="${REPO_DIR:-/home/keegreil/Documents/GitHub/boot-protocol}"
HYDRAPOOL_DIR="${HYDRAPOOL_DIR:-/home/keegreil/Documents/GitHub/hydrapool}"
HYDRAPOOL_SERVICE_SRC="${HYDRAPOOL_SERVICE_SRC:-${REPO_DIR}/deploy/systemd/hydrapool-gridpool.service}"
HYDRAPOOL_SERVICE_DEST="/etc/systemd/system/hydrapool-gridpool.service"
HYDRAPOOL_LAUNCHER="${REPO_DIR}/scripts/hydrapool-gridpool-launcher.sh"
BOOTSERVERAPP_OVERRIDE_DIR="/etc/systemd/system/bootserverapp.service.d"
BOOTSERVERAPP_OVERRIDE="${BOOTSERVERAPP_OVERRIDE_DIR}/10-gridpool-public-beta.conf"

if [[ ! -f "$HYDRAPOOL_SERVICE_SRC" ]]; then
  echo "Missing service template: ${HYDRAPOOL_SERVICE_SRC}" >&2
  exit 1
fi

if [[ ! -f "$HYDRAPOOL_LAUNCHER" ]]; then
  echo "Missing Hydrapool launcher: ${HYDRAPOOL_LAUNCHER}" >&2
  exit 1
fi

chmod 0755 "$HYDRAPOOL_LAUNCHER"
install -m 0644 "$HYDRAPOOL_SERVICE_SRC" "$HYDRAPOOL_SERVICE_DEST"

mkdir -p "$BOOTSERVERAPP_OVERRIDE_DIR"
cat > "$BOOTSERVERAPP_OVERRIDE" <<'EOF'
[Service]
LimitNOFILE=65535
EOF

systemctl daemon-reload

# These already exist on the current public-beta host. Enabling them here makes
# the script safe to re-run after OS/package changes.
systemctl enable docker.service
systemctl enable cloudflared.service
systemctl enable bootserverapp.service
systemctl enable hydrapool-gridpool.service

if systemctl is-active --quiet hydrapool-gridpool.service; then
  systemctl stop hydrapool-gridpool.service
fi

# Replace the previous hand-launched Hydrapool beta process, but only if it is
# the Hydrapool binary from this local repo.
manual_pids=()
for pid in $(pgrep -f "${HYDRAPOOL_DIR}/target/.*/hydrapool" || true); do
  exe="$(readlink "/proc/${pid}/exe" 2>/dev/null || true)"
  case "$exe" in
    "${HYDRAPOOL_DIR}/target/"*/hydrapool)
      echo "Stopping manual Hydrapool process ${pid} (${exe})"
      kill "$pid" || true
      manual_pids+=("$pid")
      ;;
  esac
done

if ((${#manual_pids[@]} > 0)); then
  for _ in {1..10}; do
    still_running=0
    for pid in "${manual_pids[@]}"; do
      [[ -d "/proc/${pid}" ]] && still_running=1
    done
    ((still_running == 0)) && break
    sleep 1
  done

  for pid in "${manual_pids[@]}"; do
    exe="$(readlink "/proc/${pid}/exe" 2>/dev/null || true)"
    case "$exe" in
      "${HYDRAPOOL_DIR}/target/"*/hydrapool)
        echo "Force-stopping manual Hydrapool process ${pid} (${exe})"
        kill -KILL "$pid" || true
        ;;
    esac
  done
fi

systemctl restart hydrapool-gridpool.service

cat <<'MSG'
Installed/updated hydrapool-gridpool.service.

Recommended verification:
  systemctl status hydrapool-gridpool.service --no-pager
  /home/keegreil/Documents/GitHub/boot-protocol/scripts/main-beta-status.sh
MSG
