import type { AccountRole, PlannedTransfer, TransferKind } from "../api";
import { dateLabel, money } from "../shared/format";

const TRANSFER_GROUPS: Array<{ title: string; kinds: TransferKind[] }> = [
  { title: "For bills", kinds: ["bills"] },
  { title: "Put aside for savings", kinds: ["savings", "pot"] },
  { title: "Spending money", kinds: ["spending"] },
];

/** Suggested transfers grouped by what they are for, in the order to make them; empty groups are left out. */
export function transferGroups(
  transfers: PlannedTransfer[],
): Array<{ title: string; transfers: PlannedTransfer[] }> {
  return TRANSFER_GROUPS.map((group) => ({
    title: group.title,
    transfers: transfers.filter((transfer) => group.kinds.includes(transfer.kind)),
  })).filter((group) => group.transfers.length > 0);
}

/** Where a transfer goes: the account, or the pot (and its account when the money comes from elsewhere). */
export function transferDestination(transfer: PlannedTransfer): string {
  if (!transfer.toPotName) return transfer.toAccountName;
  return transfer.toAccountId === transfer.fromAccountId
    ? `${transfer.toPotName} pot`
    : `${transfer.toPotName} pot in ${transfer.toAccountName}`;
}

/**
 * In the next pay period, a nudge to choose a spending account when spare money would sit idle, or to
 * mark a savings account when none is, so savings aren't used for bills or swept to spending.
 */
export function spareMoneyTip(
  mode: "current" | "upcoming",
  accounts: Array<{ role: AccountRole; leftAfterTransfers: number }>,
): string | null {
  if (mode !== "upcoming") return null;
  if (!accounts.some((account) => account.leftAfterTransfers >= 0.005)) return null;
  if (!accounts.some((account) => account.role === "spending")) {
    return "Spare money stays where your salary lands. On the Accounts page, choose a spending account and it will be sent there each payday. Mark savings accounts and give pots an amount to save each payday, and they’ll be paid first.";
  }
  if (!accounts.some((account) => account.role === "savings")) {
    return "None of your accounts is marked as savings, so any of them can be used for bills and all spare money goes to your spending account. On the Accounts page, mark your savings account and give it an amount to save each payday.";
  }
  return null;
}

/** Shown in place of this pay period's totals before the first payday, when they are all £0. */
export function noPaydayYetMessage(firstPayday: string): string {
  return `No payday has happened yet: your income plans first pay on ${dateLabel(firstPayday)}, so this pay period has no income or totals. If you were paid before then, set your salary plan’s “Starts on” date to your last payday.`;
}

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
