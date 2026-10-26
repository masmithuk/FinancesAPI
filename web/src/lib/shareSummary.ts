const gbp = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" });

export interface ShareSummaryInput {
  income: number;
  expenditure: number;
  discretionaryIncome: number;
  leftToSpend?: number;
  paydayLabel?: string;
}

/**
 * Builds a short plain-text money summary for sharing.
 * It only contains pay-period totals: no account names, balances or transactions.
 */
export function buildShareSummary(input: ShareSummaryInput): string {
  const lines = [
    input.paydayLabel
      ? `My money this pay period (from ${input.paydayLabel})`
      : "My money this pay period",
    `Income: ${gbp.format(input.income)}`,
    `Bills & expenses: ${gbp.format(input.expenditure)}`,
    `Left after bills: ${gbp.format(input.discretionaryIncome)}`,
  ];
  if (input.leftToSpend !== undefined) {
    lines.push(`Spending money left: ${gbp.format(input.leftToSpend)}`);
  }
  return lines.join("\n");
}

/** Returns a WhatsApp link that opens a chat picker with the text prefilled. */
export function whatsAppShareUrl(text: string): string {
  return `https://wa.me/?text=${encodeURIComponent(text)}`;
}
