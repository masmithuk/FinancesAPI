import type { AccountRole } from "../api";

export const ROLE_OPTIONS: Array<{ value: AccountRole; label: string }> = [
  { value: "everyday", label: "Everyday" },
  { value: "spending", label: "Spending: spare money is sent here each payday" },
  { value: "savings", label: "Savings: never used for spending" },
];

/** The account list's one-line description of an account's role. */
export function roleLabel(role: AccountRole): string {
  return role === "spending"
    ? "Spending account"
    : role === "savings"
      ? "Savings account"
      : "Everyday account";
}

/** An amount to save each payday from a form field; blank or zero means none. */
export function parseSaveAmount(value: string): number | null {
  const amount = Number(value.trim() || "0");
  return Number.isFinite(amount) && amount > 0 ? amount : null;
}
