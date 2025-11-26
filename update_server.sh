#!/bin/bash

SERVICE_NAME="bootserverapp"
APP_DIR="/home/keegreil/Documents/GitHub/boot-protocol/boot_portal"

echo "--- Starting Update Process ---"

echo "Stopping service..."
sudo systemctl stop $SERVICE_NAME

echo "Pulling from Git..."
cd $APP_DIR
git pull origin main

echo "Rebuilding..."
dotnet publish -c Release -o ./publish

echo "Restarting service..."
sudo systemctl start $SERVICE_NAME

echo "--- Update Complete ---"
