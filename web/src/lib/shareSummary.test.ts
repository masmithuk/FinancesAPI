import { describe, expect, it } from "vite-plus/test";
import { buildShareSummary, whatsAppShareUrl } from "./shareSummary";

describe("buildShareSummary", () => {
  it("lists the pay-period totals", () => {
    const text = buildShareSummary({
      income: 1000,
      expenditure: 500,
      discretionaryIncome: 500,
      leftToSpend: 50,
    });
    expect(text).toContain("Income: £1,000.00");
    expect(text).toContain("Bills & expenses: £500.00");
    expect(text).toContain("Left after bills: £500.00");
    expect(text).toContain("Spending money left: £50.00");
  });

  it("omits spending money when it is unknown", () => {
    const text = buildShareSummary({ income: 1, expenditure: 1, discretionaryIncome: 0 });
    expect(text).not.toContain("Spending money");
  });
});

describe("whatsAppShareUrl", () => {
  it("encodes the text", () => {
    expect(whatsAppShareUrl("a b\n£1")).toBe("https://wa.me/?text=a%20b%0A%C2%A31");
  });
});
