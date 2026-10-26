import { describe, expect, it } from "vite-plus/test";
import { detectMapping, mapStatement, parseCsv, type ColumnMapping } from "./statementCsv";

const monzoHeader = [
  "Transaction ID",
  "Date",
  "Time",
  "Type",
  "Name",
  "Emoji",
  "Category",
  "Amount",
  "Currency",
  "Local amount",
  "Local currency",
  "Notes and #tags",
  "Address",
  "Receipt",
  "Description",
  "Category split",
  "Money Out",
  "Money In",
].join("\t");

const monzoRows = [
  [
    "tx_1",
    "25/09/2026",
    "06:35:36",
    "Faster payment",
    "Alex Example",
    "",
    "Transfers",
    "1250.40",
    "GBP",
    "1250.40",
    "GBP",
    "BUDGETING",
    "",
    "",
    "BUDGETING",
    "",
    "",
    "1250.40",
  ].join("\t"),
  [
    "tx_2",
    "25/09/2026",
    "06:37:03",
    "Card payment",
    "Examplemart",
    "🍏 ",
    "Groceries",
    "-41.37",
    "GBP",
    "-41.37",
    "GBP",
    "",
    "1 Example Street",
    "",
    "EXAMPLEMART STORES      TOWNVILLE GBR",
    "",
    "-41.37",
    "",
  ].join("\t"),
];

const monzo = [monzoHeader, ...monzoRows].join("\n");

function mapping(overrides: Partial<ColumnMapping> = {}): ColumnMapping {
  return { date: 0, amount: 1, moneyIn: -1, moneyOut: -1, name: 2, id: -1, ...overrides };
}

describe("parseCsv", () => {
  it("parses comma separated values", () => {
    expect(parseCsv("a,b,c\n1,2,3\n4,5,6")).toEqual({
      headers: ["a", "b", "c"],
      rows: [
        ["1", "2", "3"],
        ["4", "5", "6"],
      ],
    });
  });

  it("detects tab and semicolon delimiters", () => {
    expect(parseCsv("a\tb\n1\t2").rows).toEqual([["1", "2"]]);
    expect(parseCsv("a;b\n1;2").rows).toEqual([["1", "2"]]);
  });

  it("handles quoted fields containing delimiters, quotes and newlines", () => {
    const parsed = parseCsv('a,b\n"Shop, Main St","He said ""hi"""\n"line1\nline2",x');
    expect(parsed.rows).toEqual([
      ["Shop, Main St", 'He said "hi"'],
      ["line1\nline2", "x"],
    ]);
  });

  it("handles CRLF and lone CR line endings", () => {
    expect(parseCsv("a,b\r\n1,2\r\n3,4").rows).toEqual([
      ["1", "2"],
      ["3", "4"],
    ]);
    expect(parseCsv("a,b\r1,2").rows).toEqual([["1", "2"]]);
  });

  it("strips a byte order mark and trims headers", () => {
    expect(parseCsv("\uFEFF Date , Amount \n1,2").headers).toEqual(["Date", "Amount"]);
  });

  it("skips blank lines and keeps a final row without a trailing newline", () => {
    expect(parseCsv("a,b\n\n1,2\n\n3,4").rows).toEqual([
      ["1", "2"],
      ["3", "4"],
    ]);
  });

  it("keeps empty fields", () => {
    expect(parseCsv("a,b,c\n1,,3").rows).toEqual([["1", "", "3"]]);
  });

  it("returns no headers or rows for empty input", () => {
    expect(parseCsv("")).toEqual({ headers: [], rows: [] });
  });

  it("parses a large file", () => {
    const lines = ["id,amount"];
    for (let i = 0; i < 20000; i++) lines.push(`${i},${i}.50`);
    expect(parseCsv(lines.join("\n")).rows).toHaveLength(20000);
  });
});

describe("detectMapping", () => {
  it("detects the Monzo layout", () => {
    const headers = parseCsv(monzo).headers;
    expect(detectMapping(headers)).toEqual({
      date: 1,
      amount: 7,
      moneyIn: 17,
      moneyOut: 16,
      name: 4,
      id: 0,
    });
  });

  it("detects money in and money out without an amount column", () => {
    const result = detectMapping(["Date", "Details", "Paid in", "Paid out"]);
    expect(result.amount).toBe(-1);
    expect(result.moneyIn).toBe(2);
    expect(result.moneyOut).toBe(3);
    expect(result.name).toBe(1);
  });

  it("matches headers case insensitively", () => {
    const result = detectMapping(["DATE", "AMOUNT", "PAYEE"]);
    expect([result.date, result.amount, result.name]).toEqual([0, 1, 2]);
  });

  it("returns -1 for columns it cannot find", () => {
    expect(detectMapping(["foo", "bar"])).toEqual({
      date: -1,
      amount: -1,
      moneyIn: -1,
      moneyOut: -1,
      name: -1,
      id: -1,
    });
  });
});

describe("mapStatement", () => {
  it("maps the Monzo sample rows", () => {
    const parsed = parseCsv(monzo);
    const result = mapStatement(parsed, detectMapping(parsed.headers), "auto");
    expect(result.invalid).toBe(0);
    expect(result.rows).toEqual([
      { date: "2026-09-25", amount: 1250.4, name: "Alex Example", importId: "tx_1" },
      { date: "2026-09-25", amount: -41.37, name: "Examplemart", importId: "tx_2" },
    ]);
  });

  it("builds a signed amount from money in and money out", () => {
    const parsed = parseCsv(
      "Date,Name,Money In,Money Out\n2026-01-02,Pay,100.00,\n2026-01-03,Shop,,25.50",
    );
    const result = mapStatement(parsed, detectMapping(parsed.headers), "auto");
    expect(result.rows.map((row) => row.amount)).toEqual([100, -25.5]);
  });

  it("treats a positive money out value as an outgoing amount", () => {
    const parsed = parseCsv("Date,Money In,Money Out\n2026-01-03,,25.50");
    const result = mapStatement(parsed, detectMapping(parsed.headers), "auto");
    expect(result.rows[0].amount).toBe(-25.5);
  });

  it("parses currency symbols, thousands separators, parentheses and trailing minus", () => {
    const parsed = parseCsv(
      'date,amount\n2026-01-01,"£1,234.56"\n2026-01-02,(10.00)\n2026-01-03,5.00-\n2026-01-04,+7.5',
    );
    const result = mapStatement(parsed, mapping(), "ymd");
    expect(result.rows.map((row) => row.amount)).toEqual([1234.56, -10, -5, 7.5]);
  });

  it("rounds amounts to two decimal places", () => {
    const parsed = parseCsv("date,amount\n2026-01-01,1.005\n2026-01-02,0.1");
    expect(mapStatement(parsed, mapping(), "ymd").rows.map((row) => row.amount)).toEqual([
      1.01, 0.1,
    ]);
  });

  it("rounds negative amounts away from zero too", () => {
    const parsed = parseCsv("date,amount\n2026-01-01,-1.005");
    expect(mapStatement(parsed, mapping(), "ymd").rows[0].amount).toBe(-1.01);
  });

  it("counts unreadable and zero rows as invalid with examples", () => {
    const parsed = parseCsv(
      "date,amount\n2026-01-01,abc\nnot-a-date,5\n2026-01-03,0\n2026-01-04,\n2026-01-05,9",
    );
    const result = mapStatement(parsed, mapping(), "ymd");
    expect(result.rows).toHaveLength(1);
    expect(result.invalid).toBe(4);
    expect(result.invalidExamples).toEqual(["Row 2", "Row 3", "Row 4"]);
  });

  it("rejects impossible calendar dates", () => {
    const parsed = parseCsv("date,amount\n2026-02-30,5\n2026-13-01,5\n2026-02-28,5");
    const result = mapStatement(parsed, mapping(), "ymd");
    expect(result.rows.map((row) => row.date)).toEqual(["2026-02-28"]);
    expect(result.invalid).toBe(2);
  });

  describe("date formats", () => {
    const sample = (value: string) => parseCsv(`date,amount\n${value},5`);

    it("reads DD/MM/YYYY explicitly", () => {
      expect(mapStatement(sample("03/04/2026"), mapping(), "dmy").rows[0].date).toBe("2026-04-03");
    });

    it("reads MM/DD/YYYY explicitly", () => {
      expect(mapStatement(sample("03/04/2026"), mapping(), "mdy").rows[0].date).toBe("2026-03-04");
    });

    it("reads YYYY-MM-DD and YYYY/MM/DD", () => {
      expect(mapStatement(sample("2026-04-03"), mapping(), "ymd").rows[0].date).toBe("2026-04-03");
      expect(mapStatement(sample("2026/04/03"), mapping(), "ymd").rows[0].date).toBe("2026-04-03");
    });

    it("auto-detects year first", () => {
      expect(mapStatement(sample("2026-04-03"), mapping(), "auto").rows[0].date).toBe("2026-04-03");
    });

    it("auto-detects day first when a day exceeds 12 in the first position", () => {
      const parsed = parseCsv("date,amount\n25/09/2026,5\n03/04/2026,5");
      expect(mapStatement(parsed, mapping(), "auto").rows.map((row) => row.date)).toEqual([
        "2026-09-25",
        "2026-04-03",
      ]);
    });

    it("auto-detects month first when a day exceeds 12 in the second position", () => {
      const parsed = parseCsv("date,amount\n09/25/2026,5\n03/04/2026,5");
      expect(mapStatement(parsed, mapping(), "auto").rows.map((row) => row.date)).toEqual([
        "2026-09-25",
        "2026-03-04",
      ]);
    });

    it("defaults ambiguous dates to day first", () => {
      expect(mapStatement(sample("03/04/2026"), mapping(), "auto").rows[0].date).toBe("2026-04-03");
    });

    it("expands two digit years", () => {
      expect(mapStatement(sample("25/09/26"), mapping(), "dmy").rows[0].date).toBe("2026-09-25");
    });

    it("ignores a trailing time", () => {
      expect(mapStatement(sample("25/09/2026 06:35:36"), mapping(), "dmy").rows[0].date).toBe(
        "2026-09-25",
      );
    });
  });

  it("returns empty name and id when those columns are not mapped", () => {
    const parsed = parseCsv("date,amount\n2026-01-01,5");
    expect(mapStatement(parsed, mapping({ name: -1 }), "ymd").rows[0]).toEqual({
      date: "2026-01-01",
      amount: 5,
      name: "",
      importId: "",
    });
  });

  it("trims names and ids", () => {
    const parsed = parseCsv("date,amount,name,id\n2026-01-01,5,  Examplemart  ,  tx_9 ");
    expect(mapStatement(parsed, mapping({ name: 2, id: 3 }), "ymd").rows[0]).toMatchObject({
      name: "Examplemart",
      importId: "tx_9",
    });
  });
});
