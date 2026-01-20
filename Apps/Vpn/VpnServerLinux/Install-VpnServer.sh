#!/usr/bin/env bash
set -euo pipefail

SERVICE_FILE_SRC="/opt/vpn/vpnserverlinux.service"
SERVICE_FILE_DST="/etc/systemd/system/vpnserverlinux.service"

if [[ ! -f "$SERVICE_FILE_SRC" ]]; then
  echo "ERROR: $SERVICE_FILE_SRC not found."
  echo "Place vpnserverlinux.service in /opt/vpn and re-run."
  exit 1
fi

echo "Installing systemd unit: $SERVICE_FILE_DST"
cp -f "$SERVICE_FILE_SRC" "$SERVICE_FILE_DST"
chmod 0644 "$SERVICE_FILE_DST"

echo "Reloading systemd..."
systemctl daemon-reload

echo "Enabling service at boot..."
systemctl enable "vpnserverlinux.service"

echo "Starting (or restarting) service..."
systemctl restart "vpnserverlinux.service"

echo "Service status:"
systemctl status "vpnserverlinux.service" --no-pager

echo
echo "Follow logs with:"
echo "  journalctl -u vpnserverlinux.service -f"
