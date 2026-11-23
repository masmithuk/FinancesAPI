---
name: verify-northstar
description: Launch and drive Northstar (the Finance API's React web app and its ASP.NET Core API) the way a user does, on its own ports and throwaway database, and capture proof. Use to verify a change to the payday plan, transfers, balances or sign-in actually works in the running app, not just in unit tests, or when asked to "verify", "prove" or "check in the app".
---

# Verify Northstar

Northstar is a personal payday planner: a Vite + React web app (`web/`) over an ASP.NET Core minimal API (`Finance API/`) with SQLite. Users sign in, keep account balances up to date, and follow the Overview's "Transfers to make" panel each payday. This skill runs a **separate** copy of both on ports 5206 (API) and 5274 (web) with a throwaway database in `.verify/data/`, drives it through the built-in browser (or `curl` for the API), and keeps proof in `.verify/artifacts/`.

Scripts in this skill are bash (Git Bash on Windows). Run them from anywhere in the repo.

## Isolation: never drive the developer's app

The developer usually runs their own API on **5106** (real data in `Finance API/App_Data/finance-api.db`) and web on **5173**. Never open, sign in to, seed or stop those. Never point anything at `App_Data`. The launch configs here use only 5206, 5274 and `.verify/data/verify.db`, and `autoPort` is off, so a busy port makes the launch fail rather than silently moving. If 5206 or 5274 is busy and `doctor.sh` fails, something else owns it: report it, don't kill it.

## Launch

1. Make sure no verification run is still up, then empty the data folder:
   `bash .claude/skills/verify-northstar/scripts/reset.sh` (refuses if 5206/5274 answer).
2. Start the API with the `preview_start` tool, name `verify-api`, from `.claude/launch.json`. The first start builds into `.verify/build` (about a minute). It applies EF Core migrations to the empty `.verify/data/verify.db` at startup. Ready when `curl -s http://localhost:5206/api/health` returns `{"status":"healthy","database":"available"}`.
3. Seed the baseline: `bash .claude/skills/verify-northstar/scripts/seed.sh`, which prints `seed: ok {...ids...}`. Ready means usable: the API answers *and* the seeded user exists.
4. Start the web app with `preview_start`, name `verify-web`. It serves `http://localhost:5274` and proxies `/api` to 5206 through `vite.verify.config.mts` in this folder. Its preview tab is the one to drive.

Outside the Claude desktop app (no `preview_start`), run the same commands from `.claude/launch.json` in background shells: `dotnet run --project "Finance API/Finance API.csproj" --no-launch-profile --artifacts-path .verify/build -- --urls=http://localhost:5206 --environment=Development "--ConnectionStrings:Finance=Data Source=../.verify/data/verify.db"` from the repo root, and `npx vp dev --config ../.claude/skills/verify-northstar/vite.verify.config.mts` from `web/`.

The API resolves a relative `Data Source` against its project folder (`Finance API/`), which is why the path is `../.verify/data/verify.db`. Don't "fix" it to `.verify/...`: that creates `Finance API/.verify/data/verify.db`.

## Doctor

`bash .claude/skills/verify-northstar/scripts/doctor.sh` is read-only. Run it first, and again whenever anything looks off. `doctor: OK` means:
- the API on 5206 is healthy and is this build (it rejects `?period=bogus` with 400);
- the web app on 5274 reaches it;
- `verify-user` signs in, which proves 5206 is on the verification database.

Anything else, stop and report the line it printed.

## Drive

- **Browser:** use the built-in browser tools on the `verify-web` tab (`http://localhost:5274`). Find elements by accessible name with `find` or `read_page` (`filter: interactive`), then click by `ref`. Don't use coordinates. Read results with `get_page_text` or `read_page`.
- **Sign in:** fill textbox `Your username` = `verify-user` and textbox `Your password` = `Verify-P@ss1`, then button `Sign in`. These are test values in the verification database only.
- **Navigation:** buttons `Overview`, `Accounts`, `Pots & goals`, `Activity` in the sidebar (or bottom bar on narrow windows).
- **API:** `curl` against `http://localhost:5206` with `Authorization: Bearer <token>` from `POST /api/auth/login`. Use it only to set up preconditions or take a second view. The proof of a user-facing feature is the browser path.

Feature recipes, with exact handles and expected values for the seeded data, are in [`features/`](features/README.md). Read the index first.

## Evidence

Proof goes in `.verify/artifacts/<feature-id>/` (git-ignored; cleanup never touches it).
- **The action and its result:** for each step, write the `get_page_text` output (or the relevant `read_page` subtree) to `.verify/artifacts/<feature-id>/<step>.txt` with the Write tool. Take a screenshot too: it is shown inline in the conversation, but it can't be saved to disk.
- **A second, read-only view of stored state:** after a change, run `bash .claude/skills/verify-northstar/scripts/capture.sh <feature-id> <step>`. It saves both payday plan periods, the account list and every account's balance entries as JSON. A side effect, such as balance rows written by "Mark as done", must show up there, not only on screen.
- **Standards:**
  - Drive the real user path: no test-only endpoints, no direct database edits.
  - Prove the action and the end state.
  - Record which entry point you used.
  - A feature with several entry points isn't verified by one of them.
  - If a precondition is missing, report the journey as failed and why. Don't weaken it to pass.

## Cleanup

1. Stop what this run started, with `preview_stop` for the `verify-web` and `verify-api` server ids (use `preview_list` if lost). Never kill by process name: `dotnet` and `node` processes may be the developer's. Outside the desktop app, stop only the background shells you started.
2. `bash .claude/skills/verify-northstar/scripts/reset.sh` empties `.verify/data`. `.verify/build` may stay to speed up the next run.
3. Confirm `.verify/artifacts/<feature-id>/` still holds the proof.

## Helpers

| Script | What it does | Run |
|---|---|---|
| `scripts/reset.sh` | Empties `.verify/data`; refuses while 5206/5274 answer | `bash .claude/skills/verify-northstar/scripts/reset.sh` |
| `scripts/seed.sh` | Registers `verify-user` and seeds 4 accounts relative to today; writes `.verify/data/seed.json` | `bash .claude/skills/verify-northstar/scripts/seed.sh` |
| `scripts/doctor.sh` | Read-only readiness and identity check | `bash .claude/skills/verify-northstar/scripts/doctor.sh` |
| `scripts/capture.sh` | Saves plans and balances as JSON proof | `bash .claude/skills/verify-northstar/scripts/capture.sh <feature-id> <step>` |

`seed.sh` uses GNU `date -d` (Git Bash, Linux). On macOS, swap in the `date -v` form noted in the script.
