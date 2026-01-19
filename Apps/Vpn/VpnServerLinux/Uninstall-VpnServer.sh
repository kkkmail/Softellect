#!/usr/bin/env bash
set -euo pipefail

SERVICE_FILE_DST="/etc/systemd/system/vpnserverlinux.service"

echo "Stopping service (if running)..."
systemctl stop "vpnserverlinux.service" 2>/dev/null || true

echo "Disabling service..."
systemctl disable "vpnserverlinux.service" 2>/dev/null || true

echo "Removing unit file: $SERVICE_FILE_DST"
rm -f "$SERVICE_FILE_DST"

echo "Reloading systemd..."
systemctl daemon-reload

echo "Resetting failed state (if any)..."
systemctl reset-failed "vpnserverlinux.service" 2>/dev/null || true

echo "Uninstalled."
