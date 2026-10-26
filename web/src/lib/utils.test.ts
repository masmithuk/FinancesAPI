import { describe, expect, it } from "vite-plus/test";
import { cn } from "./utils";

describe("cn", () => {
  it("joins class names", () => {
    expect(cn("a", "b")).toBe("a b");
  });

  it("drops falsy values", () => {
    expect(cn("a", false, null, undefined, "b")).toBe("a b");
  });

  it("supports conditional object syntax", () => {
    expect(cn("a", { b: true, c: false })).toBe("a b");
  });
});
