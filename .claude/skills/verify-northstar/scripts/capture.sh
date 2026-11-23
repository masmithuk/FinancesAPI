#!/usr/bin/env bash
# Saves a read-only second view of stored state as proof: both payday plan periods and every
# account's balance entries, as JSON, under .verify/artifacts/<feature-id>/<step>-*.json.
# Usage: bash .claude/skills/verify-northstar/scripts/capture.sh <feature-id> <step-name>
set -euo pipefail
[[ $# -eq 2 ]] || { echo "usage: capture.sh <feature-id> <step-name>" >&2; exit 2; }
ROOT=$(git rev-parse --show-toplevel)
OUT="$ROOT/.verify/artifacts/$1"; mkdir -p "$OUT"
API=http://localhost:5206
TOKEN=$(curl -sf -X POST "$API/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"userName":"verify-user","password":"Verify-P@ss1"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
A=(-sf -H "Authorization: Bearer $TOKEN")
curl "${A[@]}" "$API/api/payday-plan?period=current" > "$OUT/$2-plan-current.json"
curl "${A[@]}" "$API/api/payday-plan?period=next" > "$OUT/$2-plan-next.json"
curl "${A[@]}" "$API/api/accounts" > "$OUT/$2-accounts.json"
for id in $(sed -E 's/"id":"([^"]+)"/\n\1\n/g' "$OUT/$2-accounts.json" | grep -E '^[0-9a-f-]{36}$'); do
  curl "${A[@]}" "$API/api/accounts/$id/balances" > "$OUT/$2-balances-$id.json"
done
echo "capture: wrote $(ls "$OUT" | grep -c "^$2-") files to $OUT"
