# Mark a transfer as done

After making a recommended transfer at their bank, the user chooses "Mark as done". The app records money out of one account and into the other, the transfer leaves the list, and the plan recalculates from the new balances.

## Sub-features

- `transfer-mark` records a recommended transfer from its row.
- `transfer-recalculate` makes the transfer disappear and updates the receiving account's "Already there".
- `transfer-ledger` writes balance rows on both accounts ("Transfer to …" / "Transfer from …").

## How to get to it (user POV)

- Overview → `Transfers to make` → `Recommended transfers` → `Mark as done` on a row, on either tab.

## Driving it with the built-in browser

Preconditions:

- Baseline seeded state, signed in as `verify-user`, on the Overview, `Next pay period` tab.
- The row `Current → Joint £600.00` is listed.

- **Before.** Run `scripts/capture.sh mark-transfer-done before`.
- **Mark it.** Use `find` with query `Mark as done` and pick the button in the `Current → Joint` row (check with `read_page`). Click it. Within a couple of seconds, `Recommended transfers` lists only `Current → Bills £20.00`.
- **Recalculated.** `Joint` in the breakdown shows Already there `-£900.00` and Send `£0.00`. `Left in each account after payday` still lists `Current £1,530.00`: the money moved, but none was spent.
- **Side effect.** Run `scripts/capture.sh mark-transfer-done after`. In `after-balances-<joint id>.json`, a new `600` entry with note `Transfer from Current` is dated today. In `after-balances-<current id>.json` there is a `-600` entry with note `Transfer to Joint`. The ids are in `.verify/data/seed.json`.
- **Proof.** Save the page text before and after to `.verify/artifacts/mark-transfer-done/`.
- **Back to baseline.** Stop both instances, `reset.sh`, launch and `seed.sh` again.

## Gotchas

- The button disables while saving. A double-click records the transfer only once; check the balance JSON shows one pair.
- Marking a Next-period transfer before payday puts the sending account into its overdraft until the salary lands. The This tab can then show it as overdrawn. That's expected: today's balance really is lower.
- There's no undo in the UI. Deleting the two balance rows on the Accounts page reverses it.
