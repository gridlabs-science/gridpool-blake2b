#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ui_dir="$repo_root/boot_portal/ui"
sim_project="$repo_root/tools/GridPool.DashboardSimulator/GridPool.DashboardSimulator.csproj"
dashboard_root="$repo_root/boot_portal/wwwroot/dashboard"
lab_root="$repo_root/tools/GridPool.DashboardSimulator/wwwroot/sim"
bind_host="0.0.0.0"
port="${GRIDPOOL_SIM_PORT:-5099}"
lan_mode=true

usage() {
    cat <<'EOF'
Usage: scripts/run-dashboard-lab.sh [--lan|--local-only] [--port PORT] [--no-build]

  --lan        Expose the synthetic dashboard to this LAN (the default).
  --local-only Bind only to loopback and disable phone/LAN observers.
  --port PORT  HTTP port (default: 5099).
  --no-build   Reuse existing dashboard, lab, and .NET builds.
EOF
}

build=true
while (($#)); do
    case "$1" in
        --lan)
            lan_mode=true
            bind_host="0.0.0.0"
            shift
            ;;
        --local-only)
            lan_mode=false
            bind_host="127.0.0.1"
            shift
            ;;
        --port)
            port="${2:?--port requires a value}"
            shift 2
            ;;
        --no-build)
            build=false
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown option: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if ! [[ "$port" =~ ^[0-9]+$ ]] || ((port < 1024 || port > 65535)); then
    printf 'Port must be an integer from 1024 through 65535.\n' >&2
    exit 2
fi

if "$build"; then
    if [[ ! -d "$ui_dir/node_modules" ]]; then
        npm --prefix "$ui_dir" ci
    fi
    npm --prefix "$ui_dir" run build
    npm --prefix "$ui_dir" run simulator:build
    dotnet build "$sim_project"
fi

if [[ ! -f "$dashboard_root/index.html" || ! -f "$lab_root/index.html" ]]; then
    printf 'Simulator assets are missing. Run without --no-build first.\n' >&2
    exit 1
fi

export GRIDPOOL_SIM_DASHBOARD_ROOT="$dashboard_root"
export GRIDPOOL_SIM_LAB_ROOT="$lab_root"
if [[ -z "${GRIDPOOL_SIM_OPERATOR_KEY:-}" ]]; then
    if command -v openssl >/dev/null 2>&1; then
        GRIDPOOL_SIM_OPERATOR_KEY="$(openssl rand -hex 16)"
    else
        GRIDPOOL_SIM_OPERATOR_KEY="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
    fi
fi
export GRIDPOOL_SIM_OPERATOR_KEY
export Logging__LogLevel__Microsoft="${Logging__LogLevel__Microsoft:-Warning}"

printf '\nGridPool dashboard laboratory\n'
printf 'Desktop controls: http://127.0.0.1:%s/__sim/\n' "$port"
printf 'Desktop preview:  http://127.0.0.1:%s/dashboard/\n' "$port"
printf 'Synthetic operator key: %s\n' "$GRIDPOOL_SIM_OPERATOR_KEY"

if "$lan_mode"; then
    lan_ip="$(
        ip -4 route get 1.1.1.1 2>/dev/null |
            awk '{for (i=1;i<=NF;i++) if ($i=="src") {print $(i+1); exit}}'
    )"
    if [[ -n "$lan_ip" ]]; then
        printf 'LAN observer:     http://%s:%s/dashboard/\n' "$lan_ip" "$port"
    else
        printf 'LAN observer:     http://<this-machine-LAN-IP>:%s/dashboard/\n' "$port"
    fi
    printf '\nWARNING: the simulator exposes synthetic dashboard data and public simulator APIs to the local network.\n'
    printf 'Control pages and mutation APIs remain restricted to loopback.\n\n'
else
    printf 'LAN observer:     disabled (omit --local-only to use a phone)\n\n'
fi

exec dotnet run \
    --project "$sim_project" \
    --no-build \
    --no-launch-profile \
    --urls "http://${bind_host}:${port}"
