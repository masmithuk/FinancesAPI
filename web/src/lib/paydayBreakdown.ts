import { money } from "../shared/format";

/**
 * Explains that money already in an account was counted towards its bills, and how much to send
 * instead if the user moves that money out first. Returns null when no existing money was counted.
 */
export function fundingNote(account: { fromExisting: number; toSend: number }): string | null {
  if (account.fromExisting <= 0) return null;
  return `${money(account.fromExisting)} already in this account was counted. If you take it out first, send ${money(account.toSend + account.fromExisting)}.`;
}

/** The "Already there" deduction; zero is shown as £0.00 rather than -£0.00. */
export function alreadyThereLabel(fromExisting: number): string {
  return money(fromExisting > 0 ? -fromExisting : 0);
}

/** True when an account's balance hasn't been checked against the bank today (`today` is `YYYY-MM-DD`). */
export function needsBalanceCheck(balanceCheckedOn: string | null, today: string): boolean {
  return !balanceCheckedOn || balanceCheckedOn.slice(0, 10) !== today;
}
