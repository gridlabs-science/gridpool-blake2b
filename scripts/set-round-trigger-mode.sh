#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/set-round-trigger-mode.sh CONFIG_PATH test [LOW_NIBBLE_THRESHOLD]
  scripts/set-round-trigger-mode.sh CONFIG_PATH production

Examples:
  scripts/set-round-trigger-mode.sh data/boot_portal_config.local.json test 6
  scripts/set-round-trigger-mode.sh data/boot_portal_config.local.json production

The production mode only changes round rotation behavior:
  testing_round_reset_mode = "none"
  testing_round_reset_low_nibble_threshold = 0

Set node_mode/admin/public endpoint settings separately as part of the launch checklist.
USAGE
}

if [[ $# -lt 2 || $# -gt 3 ]]; then
  usage >&2
  exit 2
fi

config_path="$1"
mode="$2"
threshold="${3:-6}"

if [[ ! -f "$config_path" ]]; then
  echo "Config file not found: $config_path" >&2
  exit 1
fi

case "$mode" in
  test)
    python3 - "$config_path" "$threshold" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
threshold = int(sys.argv[2])
if threshold < 1 or threshold > 16:
    raise SystemExit("LOW_NIBBLE_THRESHOLD must be between 1 and 16")

data = json.loads(path.read_text())
data["testing_round_reset_mode"] = "block_hash_low_nibble"
data["testing_round_reset_low_nibble_threshold"] = threshold
path.write_text(json.dumps(data, indent=2) + "\n")
PY
    ;;
  production|prod|launch)
    python3 - "$config_path" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
data = json.loads(path.read_text())
data["testing_round_reset_mode"] = "none"
data["testing_round_reset_low_nibble_threshold"] = 0
path.write_text(json.dumps(data, indent=2) + "\n")
PY
    ;;
  *)
    usage >&2
    exit 2
    ;;
esac

echo "Updated $config_path for $mode round mode."
