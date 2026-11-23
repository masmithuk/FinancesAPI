import { describe, expect, it } from "vite-plus/test";
import { parseSaveAmount, roleLabel } from "./accountRoles";

describe("roleLabel", () => {
  it("describes each role for the account list", () => {
    expect(roleLabel("everyday")).toBe("Everyday account");
    expect(roleLabel("spending")).toBe("Spending account");
    expect(roleLabel("savings")).toBe("Savings account");
  });
});

describe("parseSaveAmount", () => {
  it("reads a positive amount", () => {
    expect(parseSaveAmount("250")).toBe(250);
  });

  it("treats blank or zero as no amount", () => {
    expect(parseSaveAmount("")).toBeNull();
    expect(parseSaveAmount("  ")).toBeNull();
    expect(parseSaveAmount("0")).toBeNull();
  });
});
