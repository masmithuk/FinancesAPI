import { dateLabel, money } from "../shared/format";

/**
 * Wording for the "money left" section. In the next pay period it is what each account holds after
 * payday's transfers and bills; in this pay period it is money no bill needs before `until` (the end of
 * the next pay period, the furthest this pay period keeps money back for).
 */
export function moneyLeftCopy(
  mode: "current" | "upcoming",
  until: string,
): { title: string; note: string; empty: string } {
  const date = dateLabel(until);
  return mode === "upcoming"
    ? {
        title: "Left in each account after payday",
        note: `What each account still holds once the transfers above are made and every bill before ${date} is paid.`,
        empty: "Nothing is left over once this pay period’s bills are paid.",
      }
    : {
        title: "Spare money right now",
        note: `Money no planned bill needs before ${date}, after any transfers above. It’s free to spend or save.`,
        empty: `No money is spare: everything in your accounts is needed for bills before ${date}.`,
      };
}

/** The panel's subtitle for a plan: this pay period runs until payday, the next one payday to payday. */
export function periodLabel(mode: "current" | "upcoming", start: string, end: string): string {
  return mode === "upcoming"
    ? `Next pay period · Payday ${dateLabel(start)} → ${dateLabel(end)}`
    : `This pay period · ${dateLabel(start)} until payday on ${dateLabel(end)}`;
}

/**
 * Explains that money already in an account was counted towards its bills, and how much to send
 * instead if the user moves that money out first. Returns null when no existing money was counted.
 */
export function fundingNote(account: { fromExisting: number; toSend: number }): string | null {
  if (account.fromExisting <= 0) return null;
  return `${money(account.fromExisting)} already in this account was counted. If you take it out first, send ${money(account.toSend + account.fromExisting)}.`;
}

export type PaydayPeriod = "current" | "next";

const UPCOMING_LOOK_AHEAD_DAYS = 7;

/**
 * The pay period the Overview opens on: the next one within a week of payday (matching the server's
 * own look-ahead), otherwise this one. Dates are `YYYY-MM-DD`, optionally with a time.
 */
export function defaultPeriod(nextPeriodStart: string | null, today: string): PaydayPeriod {
  if (!nextPeriodStart) return "current";
  const days = (Date.parse(nextPeriodStart.slice(0, 10)) - Date.parse(today)) / 86_400_000;
  return days <= UPCOMING_LOOK_AHEAD_DAYS ? "next" : "current";
}

/** Accounts holding spare money once bills are paid and transfers made, largest first. */
export function moneyLeftByAccount(
  accounts: Array<{ accountId: string; accountName: string; leftAfterTransfers: number }>,
): Array<{ accountId: string; accountName: string; amount: number }> {
  return accounts
    .filter((account) => account.leftAfterTransfers >= 0.005)
    .map((account) => ({
      accountId: account.accountId,
      accountName: account.accountName,
      amount: account.leftAfterTransfers,
    }))
    .sort((a, b) => b.amount - a.amount);
}

/** Whether an account gets a breakdown row: it has bills, or a transfer to it (an overdraft) needs explaining. */
export function showsBreakdown(account: { expensesDue: number; toSend: number }): boolean {
  return account.expensesDue > 0 || account.toSend > 0;
}

/** The "Already there" deduction; zero is shown as £0.00 rather than -£0.00. */
export function alreadyThereLabel(fromExisting: number): string {
  return money(fromExisting > 0 ? -fromExisting : 0);
}

/** True when an account's balance hasn't been checked against the bank today (`today` is `YYYY-MM-DD`). */
export function needsBalanceCheck(balanceCheckedOn: string | null, today: string): boolean {
  return !balanceCheckedOn || balanceCheckedOn.slice(0, 10) !== today;
}
