#!/usr/bin/env bash
# Read-only check: is this verification instance worth driving?
# Confirms the API on 5206 is healthy and is this build (knows ?period=), the web app on 5274
# proxies to it, and the seeded user signs in (it exists only in the verification database).
# Usage: bash .claude/skills/verify-northstar/scripts/doctor.sh   (exit 0 = drive it)
set -uo pipefail
fail() { echo "doctor: FAIL - $1" >&2; exit 1; }
ROOT=$(git rev-parse --show-toplevel)

# The API resolves a relative Data Source against its project folder ("Finance API/"), hence "../".
[[ -f "$ROOT/.verify/data/verify.db" ]] || fail "no database at .verify/data/verify.db: the API is using another file"

health=$(curl -s --max-time 5 http://localhost:5206/api/health) || fail "API not answering on 5206"
[[ "$health" == *'"healthy"'* ]] || fail "API on 5206 not healthy: $health"

proxied=$(curl -s --max-time 5 http://localhost:5274/api/health) || fail "web app not answering on 5274"
[[ "$proxied" == *'"healthy"'* ]] || fail "web app on 5274 does not reach the API: $proxied"

login=$(curl -s --max-time 5 -X POST http://localhost:5206/api/auth/login -H 'Content-Type: application/json' \
  -d '{"userName":"verify-user","password":"Verify-P@ss1"}')
[[ "$login" == *'"token"'* ]] || fail "verify-user cannot sign in: not seeded, or 5206 is not the verification database"
token=$(echo "$login" | sed -E 's/.*"token":"([^"]+)".*/\1/')

code=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" "http://localhost:5206/api/payday-plan?period=bogus")
[[ "$code" == 400 ]] || fail "API on 5206 is an older build (period=bogus gave $code, expected 400)"

echo "doctor: OK - API 5206 healthy and current, web 5274 proxies to it, verify-user signs in"
