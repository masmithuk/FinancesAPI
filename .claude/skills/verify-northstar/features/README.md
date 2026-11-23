# Northstar verification map

This folder is the maintained source for verifying Northstar's user-facing behaviour. Read this index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- The `verify-api` (5206) and `verify-web` (5274) instances from this skill are running, launched on an emptied `.verify/data`.
- `scripts/seed.sh` has run once on that database.
- `scripts/doctor.sh` prints `doctor: OK`.
- Never drive an instance this run didn't start (5106 and 5173 are the developer's).

Seeded state (all dates relative to the day of the run, *D*):

| Account | Balance today | Plans |
|---|---|---|
| Current | £150 | Salary £2,000 every fortnight, last paid D−11, so **payday is D+3** and the next is D+17 |
| Joint | £300 | Mortgage £900 monthly from D+5 |
| Bills | £20 | Phone £40 monthly from D+1 |
| Credit card | −£500 | none |

Every balance was set through "Update to bank balance", so all four show as checked today.

## Driving conventions

- Start each recipe from the baseline state, signed in as `verify-user` on the Overview, unless its preconditions say otherwise. Recipes that change data say how to get back to baseline: stop, reset and re-seed.
- Find elements by accessible name with `find` or `read_page`; click by `ref`, never by coordinates.
- Amounts in the UI are GBP with pence ("£1,530.00"). Match them exactly.
- Overview figures depend on the day: run the recipe on one date, and don't compare it against artifacts from another day.

## Proof and skip reporting

- Save the page text for each step under `.verify/artifacts/<feature-id>/`. After any change, also run `scripts/capture.sh <feature-id> <step>`.
- Record the feature id and entry point with each artifact.
- Report an unreachable path with the attempted step and the unmet precondition.
- Don't report a skipped entry point as verified through another one.

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible behaviour. Then come exactly four H2 sections, in this order:
1. `Sub-features`
2. `How to get to it (user POV)`
3. `Driving it with the built-in browser`, which starts with `Preconditions:`
4. `Gotchas`

## Proof status

- `payday-plan` was proven end to end on 2026-11-23: launch, doctor, both tabs driven, capture, cleanup. All expected values matched.
- `sign-in`, `bank-balance` and `mark-transfer-done` are mapped from the code but haven't been driven yet. Treat their expected values as unconfirmed until a run does, and fix the file if the app disagrees. Never weaken a step to make it pass.

## Features

- [Sign in](./sign-in.md) covers signing in, a wrong password, and signing out.
- [Payday plan](./payday-plan.md) covers the two pay-period tabs, recommended transfers, the per-account breakdown, the pre-payday warning and where money sits after transfers.
- [Update to bank balance](./bank-balance.md) covers the Overview reminder and the Accounts page field.
- [Mark a transfer as done](./mark-transfer-done.md) covers recording a recommended transfer and the plan recalculating.
