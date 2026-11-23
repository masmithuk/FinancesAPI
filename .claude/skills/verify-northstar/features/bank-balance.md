# Update to bank balance

The user types the balance their banking app shows. Northstar records the difference as one entry, so the account matches the bank, and marks the account as checked today. The Overview reminds the user to do this before transferring.

## Sub-features

- `bank-reminder` means the Overview lists accounts not checked today, each with a `Bank balance (£)` field and an `Update` button.
- `bank-overview-update` updates a balance from the reminder, which then recalculates the plan.
- `bank-accounts-update` uses the `Update to bank balance` panel on the Accounts page.
- `bank-difference` records only the difference, so the account total equals the typed figure.

## How to get to it (user POV)

- Overview → `Transfers to make` → the reminder `Before you transfer, log into each bank and update the balance here.`
- `Accounts` → pick an account → panel `Update to bank balance` → `Amount (£)` → `Update balance`.

## Driving it with the built-in browser

Preconditions:

- Baseline seeded state, signed in as `verify-user`.
- The seed checks every balance today, so the reminder starts with `All balances were checked today.` To see the list, an account must not have been checked today. The seed can't backdate that through the user path, so the Overview journey can only be fully driven on a day after seeding. Run on the seed day, it reports `bank-reminder` as unreachable (see Gotchas).

- **Accounts page.** Choose `Accounts`. With `Current` selected (the first account), find the `Amount (£)` textbox in the `Update to bank balance` panel (the second `Amount (£)` on the page) and type `180`. Choose `Update balance`. `Balance history` gains a `Bank balance update` row of `£30.00` dated today, and the running balance shows `£180.00`.
- **Second view.** Run `scripts/capture.sh bank-balance accounts-update`. The Current balances JSON sums to `180`.
- **Overview recalculates.** Choose `Overview`, `Next pay period`. `Left in each account after payday` now lists `Current £1,560.00` (£30 more).
- **Reminder (day after seeding).** Each account is listed as `Last checked <date>`. Type the bank figure in its `Bank balance (£)` field and choose `Update`. The account leaves the list and the panel recalculates.
- **Proof.** Save the page text to `.verify/artifacts/bank-balance/`.

## Gotchas

- There are two `Amount (£)` fields on the Accounts page: `Add a balance` (it *adds* to the balance) and `Update to bank balance` (it *sets* it). Use the second. Typing a bank figure into the first double-counts.
- "Checked today" uses the server's UTC date. Just after midnight in summer time (BST), an account checked a moment ago can still be listed.
- Typing the same figure again adds no balance row, but it still marks the account as checked.
