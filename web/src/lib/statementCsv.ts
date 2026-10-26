export type DateFormat = "auto" | "dmy" | "mdy" | "ymd";

export interface ParsedCsv {
  headers: string[];
  rows: string[][];
}

/** Column indexes into a parsed row. `-1` means the column is not mapped. */
export interface ColumnMapping {
  date: number;
  amount: number;
  moneyIn: number;
  moneyOut: number;
  name: number;
  id: number;
}

export interface ImportRowData {
  date: string;
  amount: number;
  name: string;
  importId: string;
}

export interface MappedStatement {
  rows: ImportRowData[];
  invalid: number;
  invalidExamples: string[];
}

function detectDelimiter(headerLine: string) {
  const candidates = ["\t", ",", ";"];
  let best = ",";
  let bestCount = 0;
  for (const candidate of candidates) {
    const count = headerLine.split(candidate).length - 1;
    if (count > bestCount) {
      best = candidate;
      bestCount = count;
    }
  }
  return best;
}

/**
 * Parses CSV text into a header row and data rows.
 *
 * The delimiter (comma, tab or semicolon) is detected from the header line. Quoted fields may
 * contain delimiters, doubled quotes and newlines. A leading byte order mark and blank lines are
 * ignored, and headers are trimmed.
 */
export function parseCsv(input: string): ParsedCsv {
  const text = input.replace(/^\uFEFF/, "");
  const firstLine = text.split(/\r\n|\n|\r/, 1)[0] ?? "";
  const delimiter = detectDelimiter(firstLine);
  const records: string[][] = [];
  let record: string[] = [];
  let field = "";
  let quoted = false;

  // For each character in the input text, parse fields and handle quoted values, delimiters, and newlines.
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (quoted) {
      if (char === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          quoted = false;
        }
      } else {
        field += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === delimiter) {
      record.push(field);
      field = "";
    } else if (char === "\n" || char === "\r") {
      if (char === "\r" && text[i + 1] === "\n") i++;
      record.push(field);
      field = "";
      if (record.some((value) => value.trim() !== "")) records.push(record);
      record = [];
    } else {
      field += char;
    }
  }
  record.push(field);
  if (record.some((value) => value.trim() !== "")) records.push(record);

  const [headers = [], ...rows] = records;
  return { headers: headers.map((header) => header.trim()), rows };
}

function findColumn(headers: string[], names: string[], exact = false) {
  const lowered = headers.map((header) => header.toLowerCase());
  for (const name of names) {
    const index = lowered.findIndex((header) => (exact ? header === name : header.includes(name)));
    if (index >= 0) return index;
  }
  return -1;
}

/**
 * Guesses which columns hold the date, amount, money in/out, description and bank transaction id
 * by matching common header names. Columns that can't be found are `-1`.
 */
export function detectMapping(headers: string[]): ColumnMapping {
  let date = findColumn(headers, ["date"], true);
  if (date < 0) date = findColumn(headers, ["date"]);
  let amount = findColumn(headers, ["amount"], true);
  const moneyIn = findColumn(headers, ["money in", "paid in", "credit"]);
  const moneyOut = findColumn(headers, ["money out", "paid out", "debit"]);
  if (amount < 0 && moneyIn < 0 && moneyOut < 0) amount = findColumn(headers, ["amount", "value"]);
  return {
    date,
    amount,
    moneyIn,
    moneyOut,
    name: findColumn(headers, ["name", "payee", "merchant", "description", "details", "reference"]),
    id: findColumn(headers, ["transaction id", "id"]),
  };
}

function parseNumber(value: string | undefined) {
  if (!value) return null;
  let text = value.trim();
  if (!text) return null;
  let negative = false;
  if (/^\(.*\)$/.test(text)) {
    negative = true;
    text = text.slice(1, -1);
  }
  text = text.replace(/[£$€\s,]/g, "");
  if (text.endsWith("-")) {
    negative = true;
    text = text.slice(0, -1);
  }
  if (!/^[+-]?\d*\.?\d+$/.test(text)) return null;
  const parsed = Number(text);
  if (!Number.isFinite(parsed)) return null;
  return negative ? -Math.abs(parsed) : parsed;
}

/** Rounds half away from zero to 2 decimals, avoiding binary float errors such as 1.005 → 1. */
function roundToPennies(value: number) {
  const rounded = Number(`${Math.round(Number(`${Math.abs(value)}e2`))}e-2`);
  return value < 0 ? -rounded : rounded;
}

function splitDate(value: string) {
  const match = value.trim().match(/^(\d{1,4})[/.-](\d{1,2})[/.-](\d{1,4})/);
  return match ? [Number(match[1]), Number(match[2]), Number(match[3]), match[1].length] : null;
}

function resolveDateFormat(values: string[], requested: DateFormat): Exclude<DateFormat, "auto"> {
  if (requested !== "auto") return requested;
  let firstOver12 = false;
  let secondOver12 = false;
  for (const value of values) {
    const parts = splitDate(value);
    if (!parts) continue;
    if (parts[3] === 4) return "ymd";
    if (parts[0] > 12) firstOver12 = true;
    if (parts[1] > 12) secondOver12 = true;
  }
  if (secondOver12 && !firstOver12) return "mdy";
  return "dmy";
}

function toIsoDate(value: string, format: Exclude<DateFormat, "auto">) {
  const parts = splitDate(value);
  if (!parts) return null;
  let year: number;
  let month: number;
  let day: number;
  if (format === "ymd") [year, month, day] = parts;
  else if (format === "mdy") [month, day, year] = parts;
  else [day, month, year] = parts;
  if (year < 100) year += 2000;
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  )
    return null;
  return date.toISOString().slice(0, 10);
}

/**
 * Converts parsed CSV rows into importable transactions using the chosen column mapping.
 *
 * Amounts are signed (negative is money out). When there is no amount column they are built from
 * money in minus money out. Rows with an unreadable date, an unreadable amount or a zero amount are
 * counted in `invalid` rather than returned. `invalidExamples` lists the first few such rows by
 * their line number in the file.
 */
export function mapStatement(
  parsed: ParsedCsv,
  mapping: ColumnMapping,
  dateFormat: DateFormat,
): MappedStatement {
  const format = resolveDateFormat(
    parsed.rows.map((row) => row[mapping.date] ?? ""),
    dateFormat,
  );
  const rows: ImportRowData[] = [];
  const invalidExamples: string[] = [];
  let invalid = 0;

  parsed.rows.forEach((row, index) => {
    const date = toIsoDate(row[mapping.date] ?? "", format);
    let amount: number | null = null;
    if (mapping.amount >= 0) {
      amount = parseNumber(row[mapping.amount]);
    } else {
      const moneyIn = mapping.moneyIn >= 0 ? parseNumber(row[mapping.moneyIn]) : null;
      const moneyOut = mapping.moneyOut >= 0 ? parseNumber(row[mapping.moneyOut]) : null;
      if (moneyIn !== null || moneyOut !== null) {
        amount = Math.abs(moneyIn ?? 0) - Math.abs(moneyOut ?? 0);
      }
    }

    if (!date || amount === null || amount === 0) {
      invalid++;
      if (invalidExamples.length < 3) invalidExamples.push(`Row ${index + 2}`);
      return;
    }

    rows.push({
      date,
      amount: roundToPennies(amount),
      name: mapping.name >= 0 ? (row[mapping.name] ?? "").trim() : "",
      importId: mapping.id >= 0 ? (row[mapping.id] ?? "").trim() : "",
    });
  });

  return { rows, invalid, invalidExamples };
}
