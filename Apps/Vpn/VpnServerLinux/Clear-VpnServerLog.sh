#!/usr/bin/env bash
set -euo pipefail

MARKER_FILE=".vpnserverlinux_journal_since"

# This does NOT delete systemd journal entries (systemd doesn't support per-unit deletion).
# Instead it sets a "since" marker so Get-/Tail-VpnServerLog.sh will only show logs after this point.

EPOCH="$(date +%s)"
echo "$EPOCH" > "$MARKER_FILE"
echo "Cleared (log marker set): $MARKER_FILE (epoch $EPOCH)"
