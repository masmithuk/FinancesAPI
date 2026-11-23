# Upcoming payday transfers — design

Date: 2026-11-23 · Status: awaiting approval · Size: small feature

## Why

Users have good recurring income and bill plans. Just before payday they want to know how much to move into each account to cover that account's bills until the following payday. Today the "Transfers to make" panel only covers today → next payday, so a few days before payday it shows the end of the current period, not the payday transfers. It also hides the maths, so a user can't see that money already in an account was counted.

## Goals

- Before payday, show the transfers to make **on the next payday** for the pay period that follows it.
- Count money expected to be in an account on payday towards its bills, and **show the sum for each account**, so the user can see that leftover money was counted and can choose to take it out first.
- Remind users to update each balance from their banking app before transferring, and make that a one-step "type what your bank shows" action.

## Non-goals

- Bank connections or automatic balance fetches.
- Changing how CSV import, reconciliation or the forecast work.
- Making transfers. Recommendations stay informational only.

## Anti-goals

- Two payday calculations that can disagree. Everything comes from `PaydayPlanService`.
- A balance update that double-counts. Typing £95 must leave the account at £95.

## Approaches considered

- **A. Extend the existing payday plan (chosen).** `GET /api/payday-plan` returns the look-ahead period, each account's breakdown, shortfalls before payday and when each balance was last checked. The web app only displays it. One source of truth, and it reuses the tested pay-window and occurrence logic.
- B. A second "upcoming" calculation alongside the current one. Less risk of breaking what works, but two calculations can drift apart, and recent fixes show how easily totals go wrong.
- C. Compute it in the web app. This duplicates the server's recurring-date rules in TypeScript. Rejected.

## Design

### Which pay period the plan targets

- If the next payday window starts within **7 days** of today, and today is not inside the current payday window, the plan is in **upcoming** mode. The target period is next payday → the payday after that.
- Otherwise it is in **current** mode: today → next payday, exactly as now.
- The response says which mode it is in (`mode: "current" | "upcoming"`) and gives the target period's start and end dates.

### Per-account breakdown (both modes)

For the target period, each account returns:

| Field | Meaning |
|---|---|
| `billsDue` | planned and unreconciled expenses in the target period (as now) |
| `incomeIn` | planned income into this account in the target period |
| `expectedBalance` | the balance at the start of the target period: today's balance plus every income and expense between today and the period start (just today's balance in current mode) |
| `toSend` | `max(0, billsDue − incomeIn − expectedBalance)` |
| `fromExisting` | how much of the bills the existing money covers: `min(max(0, expectedBalance), max(0, billsDue − incomeIn))` |
| `balanceCheckedOn` | date of the last "bank balance" update (null if never) |

Transfers between accounts use the existing greedy matching, with surpluses defined the same way: `max(0, incomeIn + expectedBalance − billsDue)`.

### Shortfalls before payday (upcoming mode only)

A list of accounts whose projected balance goes below zero between today and the next payday, with the lowest amount and the date. Shown as a warning above the payday transfers.

### "Update to bank balance"

- New `POST /api/accounts/{id}/balances/actual` with `{ amount, date? }` (date defaults to today). The server works out the difference between `amount` and the account's balance on that date, and adds one `Balance` entry for it with the note "Bank balance update". It always sets `Account.BalanceCheckedOn = date`, even when the difference is £0.
- New nullable column `Account.BalanceCheckedOn` (one EF migration).
- The Accounts page gets an "Update to bank balance" field next to the existing "Add a balance".

### Overview panel

- Header: "Payday 28 Nov → 28 Dec" plus an "upcoming" or "current" label.
- Reminder: "Before you transfer, log into each bank and update the balance here." Lists each account with its `balanceCheckedOn`. Any account not checked today is flagged, with an inline amount box that calls the new endpoint and refreshes the plan.
- For each account with bills, a breakdown:

```
Joint                           Bills due     £200.00
                                Income in       £0.00
                                Already there – £100.00
                                Send          £100.00
ⓘ £100 already in this account was counted. If you take it out first, send £200.
```

- Then the "Recommended transfers" list as now, followed by the shortfall warning when in upcoming mode.

## Decided by the user

- Use approach A.
- Existing money counts towards bills, and the breakdown says so explicitly.
- Remind users to update balances from their bank before transferring, and explain how.

## Builder's calls (flagged for review)

- A 7-day look-ahead before switching to upcoming mode.
- Store `BalanceCheckedOn` on `Account` instead of inferring it from balance notes.
- A negative expected balance (overdrawn) adds to `toSend`, and `fromExisting` is £0 in that case.
- Tests: extend `PaydayPlanTests` for upcoming mode, the breakdown sums and shortfalls before payday. Add an `AccountsTests` case for the bank-balance difference, including £0 and negative balances. Add web unit tests for the breakdown note text.
