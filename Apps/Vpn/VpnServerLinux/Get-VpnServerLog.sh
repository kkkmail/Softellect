#!/usr/bin/env bash
set -euo pipefail

UNIT="vpnserverlinux.service"
MARKER_FILE=".vpnserverlinux_journal_since"

# Usage:
#   ./Get-VpnServerLog.sh            -> writes vpn__YYYYMMDD__NNN.log in current folder
#   ./Get-VpnServerLog.sh out.log    -> writes to out.log in current folder (or given path)

OUT_PATH="${1:-}"

# Determine --since based on marker file (epoch seconds)
SINCE_ARGS=()
if [[ -f "$MARKER_FILE" ]]; then
  EPOCH="$(cat "$MARKER_FILE" | tr -d ' \t\r\n')"
  if [[ "$EPOCH" =~ ^[0-9]+$ ]]; then
    SINCE_ARGS=(--since "@$EPOCH")
  fi
fi

# Default output filename: vpn__<yyyymmdd>__<NNN>.log (NNN increments per day within current folder)
if [[ -z "$OUT_PATH" ]]; then
  YYYYMMDD="$(date +%Y%m%d)"
  # Find max NNN for today in current directory
  MAX=0
  shopt -s nullglob
  for f in "vpn__${YYYYMMDD}"__*.log; do
    base="$(basename "$f")"
    # Extract NNN from vpn__YYYYMMDD__NNN.log
    n="${base##vpn__${YYYYMMDD}__}"
    n="${n%.log}"
    if [[ "$n" =~ ^[0-9]+$ ]]; then
      if (( 10#$n > MAX )); then MAX=$((10#$n)); fi
    fi
  done
  shopt -u nullglob
  NEXT=$((MAX + 1))
  OUT_PATH="vpn__${YYYYMMDD}__$(printf "%03d" "$NEXT").log"
fi

echo "Writing $OUT_PATH"
journalctl -u "$UNIT" --no-pager "${SINCE_ARGS[@]}" > "$OUT_PATH"
echo "Done."
