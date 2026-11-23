# Spare money: savings, pots and the spending account

In the next pay period, once bill transfers are covered, the plan shares out the money left over. Each pot gets its "save each payday" amount (or its dated goal spread over the paydays left). Each savings account gets its amount. Whatever is left in everyday accounts goes to the one account marked as spending. Savings accounts are never swept to spending, and are only drawn on for bills once everyday money runs out. Payments made for this payday (from the day after the last payday, and at most a week before this one) count towards it, so a row marked as done isn't suggested again.

## Sub-features

- Accounts page "At payday" panel: "This account is for" (Everyday / Spending / Savings) and, for savings, "Save each payday (£)". Choosing a new spending account makes the old one everyday.
- Pots & goals: "Save each payday (£)" on each pot card, with a "Save amount" button.
- Overview → Next pay period: transfers grouped as "For bills", "Put aside for savings" and "Spending money". A pot row reads "Current → Holiday pot".
- With no spending account and spare money left, a tip offers a "Choose accounts" button.
- "Mark as done" on a pot row records a pot contribution from the source account. On other rows it records a transfer.

## How to get to it (user POV)

Overview → "Choose accounts" (or the sidebar `Accounts`) → add an account → set "This account is for". Pots & goals → create a pot → "Save each payday". Back on the Overview → `Next pay period`.

## Driving it with the built-in browser

Preconditions: baseline seed, signed in, on the Overview, `Next pay period` tab (the default three days before payday).

1. Baseline: transfers are only "For bills" (Current → Bills £20.00, Current → Joint £600.00). "Left in each account after payday" shows Current £1,530.00, with the tip and button `Choose accounts`.
2. Click `Choose accounts`. Add account `Spending` (textbox `e.g. Everyday account`, button `Add`; the new account is selected). Set the combobox (its accessible name is the current option, e.g. "Everyday") to `spending` and click button `Save`. The list subtitle becomes "Spending account".
3. Add account `Savings`, set the combobox to `savings`, type `200` in the `0.00` textbox that appears under "Save each payday (£)", and click `Save`.
4. Pots & goals: with account Current, create pot `Holiday`. Type `100` in textbox `Leave blank to spread a dated goal` and click `Save amount`. "Saved." appears.
5. Overview: "Put aside for savings" shows Current → Holiday pot £100.00 and Current → Savings £200.00. "Spending money" shows Current → Spending £1,230.00. "Left in each account after payday" shows Spending £1,230.00 and Savings £200.00. The tip is gone.
6. Mark the pot, savings and spending rows as done, one at a time. Each row disappears and the other amounts stay put. `capture.sh` then shows on Current a −100 entry (the pot), "Transfer to Savings" −200 and "Transfer to Spending" −1,230. Savings shows "Transfer from Current" +200 and Spending +1,230.

## Gotchas

- Navigating to the same `#/overview` URL doesn't refetch. Use `location.reload()` after restarting the API.
- `form_input` on the number fields works only for the account name. Click the number field and `type` the amount instead.
- Proven end to end on 2026-11-23 (artifacts in `.verify/artifacts/spare-money/`).
