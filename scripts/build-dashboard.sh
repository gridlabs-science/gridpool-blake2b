#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ui_dir="$repo_root/boot_portal/ui"

if ! command -v node >/dev/null 2>&1; then
    printf 'Node.js 24 or newer is required to build the GridPool dashboard.\n' >&2
    exit 1
fi

node_major="$(node --version | sed -E 's/^v([0-9]+).*/\1/')"
if (( node_major < 24 )); then
    printf 'Node.js 24 or newer is required; found %s.\n' "$(node --version)" >&2
    exit 1
fi

cd "$ui_dir"
npm ci
npm test
npm run build
