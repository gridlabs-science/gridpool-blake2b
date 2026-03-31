#!/usr/bin/env bash
set -euo pipefail

REMOTE_ALIAS="${BOOT_LAPTOP_SSH_ALIAS:-boot-laptop}"
REMOTE_REPO="${BOOT_LAPTOP_REPO:-C:\\Users\\keegr\\Documents\\GitHub\\boot-protocol}"
DEFAULT_LOG_LINES="${BOOT_LAPTOP_LOG_LINES:-200}"

usage() {
    cat <<EOF
Usage: $(basename "$0") <command> [args]

Commands:
  ps                     Show docker compose status on the laptop node
  logs [lines]           Show recent docker compose logs
  logs-follow [lines]    Follow docker compose logs
  restart                Restart the docker compose service
  rebuild                Rebuild and restart the docker compose service
  down                   Stop the docker compose service
  up                     Start the docker compose service
  git-pull-rebuild       Pull from origin/main with git.exe, then rebuild

Environment overrides:
  BOOT_LAPTOP_SSH_ALIAS  SSH host alias (default: boot-laptop)
  BOOT_LAPTOP_REPO       Windows repo path (default: ${REMOTE_REPO})
  BOOT_LAPTOP_LOG_LINES  Default log tail length (default: ${DEFAULT_LOG_LINES})
EOF
}

run_remote() {
    local encoded
    encoded="$(printf '%s' "\$ProgressPreference = 'SilentlyContinue'; $1" | iconv -f UTF-8 -t UTF-16LE | base64 -w 0)"
    ssh "$REMOTE_ALIAS" powershell -NoProfile -NonInteractive -EncodedCommand "$encoded"
}

run_compose() {
    local compose_args="$1"
    run_remote "Set-Location '$REMOTE_REPO'; docker compose $compose_args"
}

case "${1:-}" in
    ps)
        run_compose "ps"
        ;;
    logs)
        lines="${2:-$DEFAULT_LOG_LINES}"
        run_compose "logs --tail $lines"
        ;;
    logs-follow)
        lines="${2:-$DEFAULT_LOG_LINES}"
        run_compose "logs -f --tail $lines"
        ;;
    restart)
        run_compose "restart"
        ;;
    rebuild)
        run_compose "up -d --build"
        ;;
    down)
        run_compose "down"
        ;;
    up)
        run_compose "up -d"
        ;;
    git-pull-rebuild)
        run_remote "& git -C '$REMOTE_REPO' pull --ff-only origin main; Set-Location '$REMOTE_REPO'; docker compose up -d --build"
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
