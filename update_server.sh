#!/bin/bash

SERVICE_NAME="bootserverapp"
ROOT_DIR="/home/keegreil/Documents/GitHub/boot-protocol"
APP_DIR="$ROOT_DIR/boot_portal"
PUBLISH_DIR="$APP_DIR/publish"
TEMP_PUBLISH_DIR="/tmp/${SERVICE_NAME}-publish"
SERVICE_FILE_SRC="$ROOT_DIR/systemd/${SERVICE_NAME}.service"

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
sudo install -m 0644 "$SERVICE_FILE_SRC" "/etc/systemd/system/${SERVICE_NAME}.service"
sudo systemctl daemon-reload

echo "Restarting service..."
sudo systemctl start $SERVICE_NAME

echo "--- Update Complete ---"
