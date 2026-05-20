#!/bin/bash

SERVICE_NAME="bootserverapp"
ROOT_DIR="${GRIDPOOL_ROOT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)}"
APP_DIR="$ROOT_DIR/boot_portal"
PUBLISH_DIR="$APP_DIR/publish"
TEMP_PUBLISH_DIR="/tmp/${SERVICE_NAME}-publish"
SERVICE_FILE_SRC="$ROOT_DIR/systemd/${SERVICE_NAME}.service"
SERVICE_USER="${GRIDPOOL_SERVICE_USER:-$(id -un)}"

sed_escape() {
    printf '%s' "$1" | sed -e 's/[&|]/\\&/g'
}

echo "--- Starting Update Process ---"

echo "Stopping service..."
sudo systemctl stop $SERVICE_NAME

echo "Pulling from Git..."
cd $APP_DIR
git pull origin main

echo "Rebuilding..."
dotnet publish boot_portal.csproj -c Release -o "$TEMP_PUBLISH_DIR"
rsync -a --delete "$TEMP_PUBLISH_DIR"/ "$PUBLISH_DIR"/

echo "Installing systemd unit..."
SERVICE_FILE_RENDERED="$(mktemp)"
sed \
    -e "s|__GRIDPOOL_SERVICE_USER__|$(sed_escape "$SERVICE_USER")|g" \
    -e "s|__GRIDPOOL_PUBLISH_DIR__|$(sed_escape "$PUBLISH_DIR")|g" \
    -e "s|__GRIDPOOL_CONFIG_PATH__|$(sed_escape "$APP_DIR/boot_portal_config.json")|g" \
    -e "s|__GRIDPOOL_STATE_PATH__|$(sed_escape "$APP_DIR/pool_state.json")|g" \
    "$SERVICE_FILE_SRC" > "$SERVICE_FILE_RENDERED"
sudo install -m 0644 "$SERVICE_FILE_RENDERED" "/etc/systemd/system/${SERVICE_NAME}.service"
rm -f "$SERVICE_FILE_RENDERED"
sudo systemctl daemon-reload

echo "Restarting service..."
sudo systemctl start $SERVICE_NAME

echo "--- Update Complete ---"
