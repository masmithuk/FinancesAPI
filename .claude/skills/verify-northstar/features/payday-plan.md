# Payday plan

The Overview's "Transfers to make" panel tells the user how much to move into each account to cover its bills. Two tabs show this pay period (today until payday) and the next one (payday to the following payday). Each account's sum is shown, a warning appears for accounts that run short before payday, and a list shows where spare money sits once transfers are made.

## Sub-features

- `plan-tabs` shows "This pay period" and "Next pay period", opening on Next within 7 days of payday.
- `plan-next` gives the next period's transfers, counting money already in an account and overdraft before payday.
- `plan-current` covers this period's shortfalls using only money not needed after payday.
- `plan-breakdown` shows each account's bills due, income in, already there, overdrawn-before-payday and send, with a note when existing money was counted.
- `plan-warning` warns about accounts that run short before payday.
- `plan-money-left` shows where your money sits after transfers.
- `plan-credit-card` means a balance below zero today (the credit card) is not topped up.

## How to get to it (user POV)

- Sign in: the Overview opens with the panel at the top.
- Choose `Overview` in the navigation from any other page.

## Driving it with the built-in browser

Preconditions:

- Baseline seeded state, signed in as `verify-user`, on the Overview.
- `scripts/doctor.sh` prints `doctor: OK`.

- **Opens on Next.** Read the page text. The panel subtitle reads `Next pay period · Payday <D+3> → <D+17>` (dates as "26 Nov 2026"). The `Next pay period` button has `aria-pressed="true"`.
- **Next: transfers.** Under `Recommended transfers` there are exactly two rows, each with a `Mark as done` button: `Current → Bills £20.00` and `Current → Joint £600.00`. There is no row to `Credit card`.
- **Next: breakdown.**
  - `Bills` shows Bills due `£0.00`, Already there `£0.00`, Overdrawn before payday by `£20.00`, Send `£20.00`.
  - `Joint` shows Bills due `£900.00`, Already there `-£300.00`, Send `£600.00`, and the note `£300.00 already in this account was counted. If you take it out first, send £900.00.`
  - `Credit card` has no breakdown row.
- **Next: warning.** `Some accounts run short before payday.` lists `Bills: drops to -£20.00 on <D+1> before payday`.
- **Next: money left.** `Where your money sits after transfers` lists exactly `Current £1,530.00`.
- **Switch to This.** Choose the `This pay period` button. The subtitle reads `This pay period · <D−11> until payday on <D+3>`.
- **This: transfers keep Joint's money.** `Recommended transfers` has exactly one row, `Current → Bills £20.00`. There is no row from `Joint`: its £300 is needed for the mortgage after payday.
- **This: money left.** `Where your money sits after transfers` lists exactly `Current £130.00`. Joint isn't listed.
- **Proof.** Save the page text for each tab to `.verify/artifacts/payday-plan/next.txt` and `this.txt`. Run `scripts/capture.sh payday-plan baseline`. `baseline-plan-next.json` should show `"mode":"upcoming"` and `baseline-plan-current.json` `"mode":"current"`, with the same transfers as on screen.

## Gotchas

- Figures depend on the date. The seed is relative to today, so a run must seed and drive on the same day.
- The first load can take a few seconds while the API warms up. Wait for `Transfers to make` before reading values.
- Weekly or fortnightly pay keeps the app in "Next" mode for much of the cycle: the 7-day rule is measured to the next payday.
- If today is the payday itself, the app opens on This, not Next. The seed avoids that by putting payday at D+3.
