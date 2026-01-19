#!/usr/bin/env bash
set -euo pipefail

UNIT="vpnserverlinux.service"
MARKER_FILE=".vpnserverlinux_journal_since"

# Usage:
#   ./Tail-VpnServerLog.sh            -> follows into vpn__YYYYMMDD__NNN.log (Ctrl+C to stop)
#   ./Tail-VpnServerLog.sh out.log    -> follows into out.log (Ctrl+C to stop)

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
  MAX=0
  shopt -s nullglob
  for f in "vpn__${YYYYMMDD}"__*.log; do
    base="$(basename "$f")"
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

echo "Following logs into $OUT_PATH (Ctrl+C to stop)"
# -f follows; tee writes to file and also shows on screen
journalctl -u "$UNIT" --no-pager -f "${SINCE_ARGS[@]}" | tee -a "$OUT_PATH"
