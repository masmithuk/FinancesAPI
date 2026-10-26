import { afterEach, describe, expect, it, vi } from "vite-plus/test";
import { ApiError, api } from "./api";

function respond(body: string | null, init: ResponseInit) {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, init)));
}

async function failure(call: () => Promise<unknown>) {
  try {
    await call();
  } catch (error) {
    return error as ApiError;
  }
  throw new Error("Expected the call to fail");
}

afterEach(() => vi.unstubAllGlobals());

describe("api request", () => {
  it("returns parsed JSON and sends the bearer token", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(
        new Response(JSON.stringify({ userId: "1", userName: "a" }), { status: 200 }),
      );
    vi.stubGlobal("fetch", fetchMock);

    expect(await api.me("tok")).toEqual({ userId: "1", userName: "a" });
    const headers = fetchMock.mock.calls[0][1].headers as Headers;
    expect(headers.get("Authorization")).toBe("Bearer tok");
    expect(headers.get("Accept")).toBe("application/json");
  });

  it("returns undefined for an empty success body", async () => {
    respond(null, { status: 204 });
    expect(await api.deleteBalance("tok", "a", "b")).toBeUndefined();
  });

  it("sends a JSON body with a content type on POST", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    await api.createAccount("tok", "Current");

    const [, init] = fetchMock.mock.calls[0];
    expect(init.method).toBe("POST");
    expect(init.body).toBe(JSON.stringify({ name: "Current" }));
    expect((init.headers as Headers).get("Content-Type")).toBe("application/json");
  });

  describe("when the server cannot be reached", () => {
    it("throws a friendly ApiError without exposing the browser message", async () => {
      vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
      const error = await failure(() => api.me("tok"));

      expect(error).toBeInstanceOf(ApiError);
      expect(error.status).toBe(0);
      expect(error.message).toMatch(/can't reach the server/i);
      expect(error.message).not.toMatch(/failed to fetch/i);
    });

    it("throws a friendly ApiError when the response body cannot be read", async () => {
      const broken = {
        ok: true,
        status: 200,
        text: () => Promise.reject(new Error("socket hang up")),
      };
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(broken));
      const error = await failure(() => api.me("tok"));

      expect(error).toBeInstanceOf(ApiError);
      expect(error.message).toMatch(/interrupted/i);
    });
  });

  describe("when the server responds with an error", () => {
    it("shows validation errors from an errors array", async () => {
      respond(JSON.stringify({ errors: ["Name is required.", "Too long."] }), { status: 400 });
      const error = await failure(() => api.me("tok"));
      expect(error.status).toBe(400);
      expect(error.message).toBe("Name is required. Too long.");
    });

    it("falls back to the problem details detail, then title", async () => {
      respond(JSON.stringify({ detail: "Bad thing", title: "Title" }), { status: 409 });
      expect((await failure(() => api.me("tok"))).message).toBe("Bad thing");

      respond(JSON.stringify({ title: "Only title" }), { status: 400 });
      expect((await failure(() => api.me("tok"))).message).toBe("Only title");
    });

    it("uses a generic message for 4xx responses without details", async () => {
      respond("", { status: 404, statusText: "Not Found" });
      const error = await failure(() => api.me("tok"));
      expect(error.status).toBe(404);
      expect(error.message).toBe("The request could not be completed.");
    });

    it("preserves the status for 401 so sessions can expire", async () => {
      respond(JSON.stringify({ title: "Unauthorized" }), { status: 401 });
      expect((await failure(() => api.me("tok"))).status).toBe(401);
    });

    it.each([502, 503, 504])("reports %i as temporarily unavailable", async (status) => {
      respond("<html>Bad gateway at proxy-01</html>", { status });
      const error = await failure(() => api.me("tok"));
      expect(error.status).toBe(status);
      expect(error.message).toMatch(/temporarily unavailable/i);
      expect(error.message).not.toMatch(/proxy|html/i);
    });

    it("hides internal details for other 5xx responses", async () => {
      respond(JSON.stringify({ detail: "NullReferenceException at Foo.cs:12" }), { status: 500 });
      const error = await failure(() => api.me("tok"));
      expect(error.status).toBe(500);
      expect(error.message).toMatch(/something went wrong/i);
      expect(error.message).not.toMatch(/NullReference|Foo\.cs/);
    });

    it("never surfaces a raw text body", async () => {
      respond("Stack trace: secret", { status: 400, statusText: "Bad Request" });
      const error = await failure(() => api.me("tok"));
      expect(error.message).toBe("The request could not be completed.");
    });
  });

  it("rejects a malformed success body with a friendly error", async () => {
    respond("<html>not json</html>", { status: 200 });
    const error = await failure(() => api.me("tok"));
    expect(error).toBeInstanceOf(ApiError);
    expect(error.message).toMatch(/unexpected response/i);
  });
});

describe("api endpoints", () => {
  it("posts statement rows to the import endpoint", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ imported: 1, duplicates: 0, skipped: 0 })));
    vi.stubGlobal("fetch", fetchMock);
    const rows = [{ date: "2026-09-25", amount: -41.37, name: "Examplemart", importId: "tx_2" }];

    expect(await api.importOccurrences("tok", "acc", rows)).toEqual({
      imported: 1,
      duplicates: 0,
      skipped: 0,
    });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/accounts/acc/occurrences/import");
    expect(fetchMock.mock.calls[0][1].body).toBe(JSON.stringify({ rows }));
  });

  it("includes the note when creating and updating balances", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response("{}")));
    vi.stubGlobal("fetch", fetchMock);

    await api.createBalance("tok", "acc", "2026-01-01", 5, "Rent");
    await api.updateBalance("tok", "acc", "bal", "2026-01-01", 5, "Rent");

    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
      date: "2026-01-01",
      amount: 5,
      note: "Rent",
    });
    expect(fetchMock.mock.calls[1][1].method).toBe("PUT");
    expect(JSON.parse(fetchMock.mock.calls[1][1].body).note).toBe("Rent");
  });
});
