#!/usr/bin/env bash
# Seeds the verification API (5206) with the baseline user and accounts, all dated relative to today.
# Writes account ids to .verify/data/seed.json. Run once after launch, on a fresh database.
# Test login (verification database only): verify-user / Verify-P@ss1
# Usage: bash .claude/skills/verify-northstar/scripts/seed.sh
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
API=http://localhost:5206
day() { date -u -d "$1 days" +%F; }   # GNU date (Git Bash, Linux); on macOS use: date -u -v"$1"d +%F

curl -sf -o /dev/null -X POST "$API/api/auth/register" -H 'Content-Type: application/json' \
  -d '{"userName":"verify-user","email":"verify-user@example.test","password":"Verify-P@ss1"}' \
  || { echo "seed: register failed (already seeded? run reset.sh on a stopped instance)" >&2; exit 1; }
TOKEN=$(curl -sf -X POST "$API/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"userName":"verify-user","password":"Verify-P@ss1"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
H=(-H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json')

account() { curl -sf -X POST "$API/api/accounts" "${H[@]}" -d "{\"name\":\"$1\"}" | sed -E 's/.*"id":"([^"]+)".*/\1/'; }
plan() {  # account name amount startDate frequency(0 weekly,1 fortnightly,2 monthly,3 annual) isIncome
  curl -sf -o /dev/null -X POST "$API/api/accounts/$1/schedules" "${H[@]}" \
    -d "{\"name\":\"$2\",\"amount\":$3,\"startDate\":\"$4\",\"frequency\":$5,\"isIncome\":$6}"
}
bank() { curl -sf -o /dev/null -X POST "$API/api/accounts/$1/balances/actual" "${H[@]}" -d "{\"amount\":$2}"; }

CURRENT=$(account "Current")
JOINT=$(account "Joint")
BILLS=$(account "Bills")
CARD=$(account "Credit card")

# Salary every fortnight, last paid 11 days ago: payday is today + 3, so "Next pay period" opens first.
plan "$CURRENT" "Salary" 2000 "$(day -11)" 1 true
plan "$JOINT" "Mortgage" 900 "$(day 5)" 2 false     # due after payday
plan "$BILLS" "Phone" 40 "$(day 1)" 2 false         # due before payday: Bills runs £20 short
bank "$CURRENT" 150
bank "$JOINT" 300
bank "$BILLS" 20
bank "$CARD" -500                                     # a credit card: never topped up in full

cat > "$ROOT/.verify/data/seed.json" <<EOF
{"current":"$CURRENT","joint":"$JOINT","bills":"$BILLS","creditCard":"$CARD","payday":"$(day 3)"}
EOF
echo "seed: ok $(cat "$ROOT/.verify/data/seed.json")"
