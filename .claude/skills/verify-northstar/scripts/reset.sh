#!/usr/bin/env bash
# Empties the verification data folder so the next launch starts from a fresh database.
# Refuses while anything is listening on the verification ports (stop the run first).
# Usage: bash .claude/skills/verify-northstar/scripts/reset.sh
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
for port in 5206 5274; do
  if curl -s -o /dev/null --max-time 2 "http://localhost:$port/"; then
    echo "reset: something is listening on $port; stop the verification run first" >&2
    exit 1
  fi
done
rm -rf "$ROOT/.verify/data"
mkdir -p "$ROOT/.verify/data" "$ROOT/.verify/artifacts"
echo "reset: $ROOT/.verify/data is empty"
