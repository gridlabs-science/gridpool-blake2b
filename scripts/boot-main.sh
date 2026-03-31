#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="${BOOT_MAIN_SERVICE:-bootserverapp.service}"
DEFAULT_LOG_LINES="${BOOT_MAIN_LOG_LINES:-200}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
    cat <<EOF
Usage: $(basename "$0") <command> [args]

Commands:
  status                 Show systemd service status
  logs [lines]           Show recent journal lines
  logs-follow [lines]    Follow the journal
  restart                Restart the systemd service
  update                 Run update_server.sh

Environment overrides:
  BOOT_MAIN_SERVICE      systemd unit name (default: ${SERVICE_NAME})
  BOOT_MAIN_LOG_LINES    Default journal tail length (default: ${DEFAULT_LOG_LINES})
EOF
}

case "${1:-}" in
    status)
        sudo systemctl status "$SERVICE_NAME" --no-pager
        ;;
    logs)
        lines="${2:-$DEFAULT_LOG_LINES}"
        sudo journalctl -u "$SERVICE_NAME" -n "$lines" --no-pager -o cat
        ;;
    logs-follow)
        lines="${2:-$DEFAULT_LOG_LINES}"
        sudo journalctl -u "$SERVICE_NAME" -n "$lines" -f -o cat
        ;;
    restart)
        sudo systemctl restart "$SERVICE_NAME"
        ;;
    update)
        "$ROOT_DIR/update_server.sh"
        ;;
    ""|-h|--help|help)
        usage
        ;;
    *)
        echo "Unknown command: $1" >&2
        usage >&2
        exit 1
        ;;
esac
