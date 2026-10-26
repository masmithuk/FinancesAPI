import { describe, expect, it } from "vite-plus/test";
import {
  claimKey,
  namesMatch,
  nearestPlanDate,
  planDates,
  suggestPlanMatches,
  type PlanLike,
} from "./planMatch";

const rent: PlanLike = {
  id: "rent",
  name: "Rent",
  amount: 900,
  startDate: "2026-01-31",
  frequency: 2,
  isIncome: false,
};
const groceries: PlanLike = {
  id: "groceries",
  name: "Tesco / Lidl groceries",
  amount: 100,
  startDate: "2026-01-05",
  frequency: 0,
  isIncome: false,
};
const salary: PlanLike = {
  id: "salary",
  name: "Salary",
  amount: 2000,
  startDate: "2026-01-28",
  frequency: 2,
  isIncome: true,
};

describe("planDates", () => {
  it("expands weekly and fortnightly plans", () => {
    expect(planDates(groceries, "2026-01-01", "2026-01-20")).toEqual([
      "2026-01-05",
      "2026-01-12",
      "2026-01-19",
    ]);
    expect(planDates({ ...groceries, frequency: 1 }, "2026-01-01", "2026-02-10")).toEqual([
      "2026-01-05",
      "2026-01-19",
      "2026-02-02",
    ]);
  });

  it("clamps month-end dates like the server does", () => {
    expect(planDates(rent, "2026-01-01", "2026-04-30")).toEqual([
      "2026-01-31",
      "2026-02-28",
      "2026-03-31",
      "2026-04-30",
    ]);
  });

  it("expands annual plans and skips dates before the start", () => {
    const annual = { ...rent, frequency: 3, startDate: "2026-06-15" };
    expect(planDates(annual, "2025-01-01", "2028-12-31")).toEqual([
      "2026-06-15",
      "2027-06-15",
      "2028-06-15",
    ]);
  });
});

describe("namesMatch", () => {
  it("matches shared words and partial words", () => {
    expect(namesMatch("Tesco / Lidl groceries", "TESCO GROCERY TOWNVILLE")).toBe(true);
    expect(namesMatch("Netflix", "NETFLIX.COM")).toBe(true);
  });

  it("ignores generic words and unrelated names", () => {
    expect(namesMatch("Card payment", "Card payment Costa")).toBe(false);
    expect(namesMatch("Rent", "Coffee")).toBe(false);
  });
});

describe("suggestPlanMatches", () => {
  it("matches by amount close to the plan date", () => {
    const matches = suggestPlanMatches(
      [{ date: "2026-02-02", amount: -900, name: "Landlord" }],
      [rent],
    );
    expect(matches.get(0)).toEqual({ planId: "rent", planDate: "2026-01-31", reason: "amount" });
  });

  it("matches variable spend by name to the nearest occurrence", () => {
    const matches = suggestPlanMatches(
      [
        { date: "2026-01-06", amount: -41.37, name: "Tesco" },
        { date: "2026-01-07", amount: -18.4, name: "Lidl" },
        { date: "2026-01-14", amount: -33, name: "Lidl" },
      ],
      [groceries],
    );
    expect(matches.get(0)).toEqual({ planId: "groceries", planDate: "2026-01-05", reason: "name" });
    expect(matches.get(1)?.planDate).toBe("2026-01-05");
    expect(matches.get(2)?.planDate).toBe("2026-01-12");
  });

  it("does not match across income and expense or unrelated spend", () => {
    const matches = suggestPlanMatches(
      [
        { date: "2026-01-28", amount: 900, name: "Rent" },
        { date: "2026-01-29", amount: -4.5, name: "Coffee" },
      ],
      [rent, salary],
    );
    expect(matches.size).toBe(0);
  });

  it("does not suggest amount-only matches for occurrences that are already paid", () => {
    const claimed = new Set([claimKey("rent", "2026-01-31")]);
    const taken = suggestPlanMatches(
      [{ date: "2026-02-01", amount: -900, name: "Landlord" }],
      [rent],
      claimed,
    );
    expect(taken.size).toBe(0);
    const free = suggestPlanMatches(
      [{ date: "2026-02-27", amount: -900, name: "Landlord" }],
      [rent],
      claimed,
    );
    expect(free.get(0)?.planDate).toBe("2026-02-28");
  });

  it("returns nothing without plans or rows", () => {
    expect(suggestPlanMatches([], [rent]).size).toBe(0);
    expect(suggestPlanMatches([{ date: "2026-01-01", amount: -1, name: "x" }], []).size).toBe(0);
  });
});

describe("nearestPlanDate", () => {
  it("finds the closest unpaid occurrence", () => {
    expect(nearestPlanDate(rent, "2026-03-20")).toBe("2026-03-31");
    expect(nearestPlanDate(rent, "2026-03-05")).toBe("2026-02-28");
  });

  it("projects occurrences before the plan start", () => {
    expect(nearestPlanDate(rent, "2025-06-02")).toBe("2025-05-31");
    expect(nearestPlanDate(groceries, "2025-12-30")).toBe("2025-12-29");
    expect(nearestPlanDate({ ...groceries, frequency: 3 }, "2024-01-20")).toBe("2024-01-05");
  });
});

describe("variable spend plans", () => {
  const apple: PlanLike = {
    ...rent,
    id: "apple",
    name: "Apple",
    amount: 2.99,
    startDate: "2026-09-20",
  };
  const prime: PlanLike = {
    ...rent,
    id: "prime",
    name: "Amazon Prime",
    amount: 8.99,
    startDate: "2026-09-20",
  };

  it("links many transactions to one plan, including ones before the plan started", () => {
    const matches = suggestPlanMatches(
      [
        { date: "2026-01-06", amount: -41.37, name: "Tesco" },
        { date: "2026-01-07", amount: -18.4, name: "Lidl" },
        { date: "2026-01-08", amount: -33, name: "Lidl" },
      ],
      [groceries],
    );
    expect([...matches.values()].map((match) => match.planId)).toEqual([
      "groceries",
      "groceries",
      "groceries",
    ]);
  });

  it("matches Apple and Amazon Prime on name even when the amount differs", () => {
    const matches = suggestPlanMatches(
      [
        { date: "2026-03-12", amount: -3.49, name: "Apple" },
        { date: "2026-03-14", amount: -8.99, name: "AMAZON PRIME*AB12" },
        { date: "2026-03-15", amount: -20, name: "Amazon Marketplace" },
      ],
      [apple, prime],
    );
    expect(matches.get(0)?.planId).toBe("apple");
    expect(matches.get(1)?.planId).toBe("prime");
    expect(matches.get(2)?.planId).toBe("prime");
  });
});
