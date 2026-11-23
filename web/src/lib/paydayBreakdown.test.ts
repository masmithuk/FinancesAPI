import { describe, expect, it } from "vite-plus/test";
import {
  alreadyThereLabel,
  fundingNote,
  needsBalanceCheck,
  showsBreakdown,
} from "./paydayBreakdown";

describe("showsBreakdown", () => {
  it("explains every account that has bills or receives a transfer", () => {
    expect(showsBreakdown({ expensesDue: 300, toSend: 0 })).toBe(true);
    expect(showsBreakdown({ expensesDue: 0, toSend: 500 })).toBe(true);
  });

  it("skips accounts with nothing due and nothing to send", () => {
    expect(showsBreakdown({ expensesDue: 0, toSend: 0 })).toBe(false);
  });
});

describe("alreadyThereLabel", () => {
  it("shows counted money as a deduction", () => {
    expect(alreadyThereLabel(250)).toBe("-£250.00");
  });

  it("shows £0.00, not -£0.00, when nothing was counted", () => {
    expect(alreadyThereLabel(0)).toBe("£0.00");
  });
});

describe("fundingNote", () => {
  it("explains that money already in the account was counted", () => {
    expect(fundingNote({ fromExisting: 100, toSend: 100 })).toBe(
      "£100.00 already in this account was counted. If you take it out first, send £200.00.",
    );
  });

  it("has nothing to say when no existing money was counted", () => {
    expect(fundingNote({ fromExisting: 0, toSend: 300 })).toBeNull();
  });
});

describe("needsBalanceCheck", () => {
  it("asks for a check when the balance has never been checked", () => {
    expect(needsBalanceCheck(null, "2026-11-23")).toBe(true);
  });

  it("is satisfied by a check made today", () => {
    expect(needsBalanceCheck("2026-11-23T00:00:00", "2026-11-23")).toBe(false);
  });

  it("asks again when the last check was on an earlier day", () => {
    expect(needsBalanceCheck("2026-11-20T00:00:00", "2026-11-23")).toBe(true);
  });
});
