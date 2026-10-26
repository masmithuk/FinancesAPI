import { afterEach, beforeEach, describe, expect, it, vi } from "vite-plus/test";
import { dateLabel, money, today } from "./format";

describe("money", () => {
  it("formats pounds with two decimals and grouping", () => {
    expect(money(1250.4)).toBe("£1,250.40");
    expect(money(10000)).toBe("£10,000.00");
  });

  it("formats zero", () => {
    expect(money(0)).toBe("£0.00");
  });

  it("formats negative amounts with a leading minus", () => {
    expect(money(-41.37)).toBe("-£41.37");
  });
});

describe("dateLabel", () => {
  it("formats an ISO date as day, short month and year", () => {
    expect(dateLabel("2026-09-25")).toMatch(/^25 Sep(t)? 2026$/);
  });

  it("ignores a time component", () => {
    expect(dateLabel("2026-01-05T23:59:59Z")).toBe("5 Jan 2026");
  });

  it("does not shift the day across timezones", () => {
    expect(dateLabel("2026-12-31")).toBe("31 Dec 2026");
    expect(dateLabel("2026-03-01")).toBe("1 Mar 2026");
  });
});

describe("today", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("returns the local date as YYYY-MM-DD", () => {
    vi.setSystemTime(new Date(2026, 9, 26, 13, 30));
    expect(today()).toBe("2026-10-26");
  });

  it("uses the local date late in the evening, not the UTC date", () => {
    vi.setSystemTime(new Date(2026, 9, 26, 23, 59));
    expect(today()).toBe("2026-10-26");
  });

  it("uses the local date just after midnight", () => {
    vi.setSystemTime(new Date(2026, 9, 27, 0, 1));
    expect(today()).toBe("2026-10-27");
  });
});
