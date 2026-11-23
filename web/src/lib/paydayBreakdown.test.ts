import { describe, expect, it } from "vite-plus/test";
import {
  alreadyThereLabel,
  defaultPeriod,
  fundingNote,
  moneyLeftByAccount,
  needsBalanceCheck,
  periodLabel,
  showsBreakdown,
} from "./paydayBreakdown";

describe("periodLabel", () => {
  it("describes this pay period as running until payday", () => {
    expect(periodLabel("current", "2026-11-23T00:00:00", "2026-11-25T00:00:00")).toBe(
      "This pay period · 23 Nov 2026 until payday on 25 Nov 2026",
    );
  });

  it("describes the next pay period from payday to payday", () => {
    expect(periodLabel("upcoming", "2026-11-25T00:00:00", "2026-12-25T00:00:00")).toBe(
      "Next pay period · Payday 25 Nov 2026 → 25 Dec 2026",
    );
  });
});

describe("defaultPeriod", () => {
  it("opens on the next pay period within a week of payday", () => {
    expect(defaultPeriod("2026-11-26T00:00:00", "2026-11-23")).toBe("next");
    expect(defaultPeriod("2026-11-30T00:00:00", "2026-11-23")).toBe("next");
  });

  it("opens on this pay period when payday is further away", () => {
    expect(defaultPeriod("2026-12-01T00:00:00", "2026-11-23")).toBe("current");
  });

  it("opens on this pay period when there is no next payday", () => {
    expect(defaultPeriod(null, "2026-11-23")).toBe("current");
  });
});

describe("moneyLeftByAccount", () => {
  it("lists accounts holding spare money after transfers, largest first", () => {
    expect(
      moneyLeftByAccount([
        { accountId: "a", accountName: "Joint", leftAfterTransfers: 0 },
        { accountId: "b", accountName: "Savings", leftAfterTransfers: 40 },
        { accountId: "c", accountName: "Current", leftAfterTransfers: 1060 },
        { accountId: "d", accountName: "Bills", leftAfterTransfers: -20 },
      ]),
    ).toEqual([
      { accountId: "c", accountName: "Current", amount: 1060 },
      { accountId: "b", accountName: "Savings", amount: 40 },
    ]);
  });

  it("ignores rounding dust", () => {
    expect(
      moneyLeftByAccount([{ accountId: "a", accountName: "Joint", leftAfterTransfers: 0.004 }]),
    ).toEqual([]);
  });
});

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
