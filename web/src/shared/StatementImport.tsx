import { useMemo, useRef, useState, type ChangeEvent } from "react";
import { api, type Schedule } from "../api";
import { nearestPlanDate, suggestPlanMatches } from "../lib/planMatch";
import {
  detectMapping,
  mapStatement,
  parseCsv,
  type ColumnMapping,
  type DateFormat,
  type ParsedCsv,
} from "../lib/statementCsv";
import { Button, SelectField, ToggleField } from "./Controls";
import { dateLabel, money } from "./format";
import { ErrorMessage, Panel } from "./Ui";

const MAX_ROWS = 20000;
const MAX_FILE_BYTES = 10 * 1024 * 1024;

const dateFormats: Array<{ value: DateFormat; label: string }> = [
  { value: "auto", label: "Detect automatically" },
  { value: "dmy", label: "DD/MM/YYYY" },
  { value: "mdy", label: "MM/DD/YYYY" },
  { value: "ymd", label: "YYYY-MM-DD" },
];

export default function StatementImport({
  token,
  accountId,
  plans,
  claimed,
  onImported,
}: {
  token: string;
  accountId: string;
  plans: Schedule[];
  /** Plan occurrences already paid by existing activity, as `claimKey` values. */
  claimed: Set<string>;
  onImported: () => Promise<void>;
}) {
  const [fileName, setFileName] = useState("");
  const [parsed, setParsed] = useState<ParsedCsv | null>(null);
  const [mapping, setMapping] = useState<ColumnMapping | null>(null);
  const [dateFormat, setDateFormat] = useState<DateFormat>("auto");
  const [unlinked, setUnlinked] = useState<Set<number>>(new Set());
  const [chosen, setChosen] = useState<Map<number, string>>(new Map());
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState("");
  const [busy, setBusy] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  const statement = useMemo(
    () =>
      parsed && mapping && mapping.date >= 0 ? mapStatement(parsed, mapping, dateFormat) : null,
    [parsed, mapping, dateFormat],
  );

  const suggestions = useMemo(
    () => (statement ? suggestPlanMatches(statement.rows, plans, claimed) : new Map()),
    [statement, plans, claimed],
  );
  const activeLinks = [...suggestions.keys()].filter((index) => !unlinked.has(index));

  const hasAmount =
    mapping !== null && (mapping.amount >= 0 || mapping.moneyIn >= 0 || mapping.moneyOut >= 0);

  function reset() {
    setFileName("");
    setParsed(null);
    setMapping(null);
    setDateFormat("auto");
    setUnlinked(new Set());
    setChosen(new Map());
    setConfirming(false);
  }

  function toggleLink(index: number, linked: boolean) {
    setUnlinked((current) => {
      const next = new Set(current);
      if (linked) next.delete(index);
      else next.add(index);
      return next;
    });
  }

  async function chooseFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    setError("");
    setResult("");
    reset();
    if (!file) return;
    if (file.size > MAX_FILE_BYTES) {
      setError("That file is too large. Please choose a statement under 10 MB.");
      return;
    }
    try {
      const csv = parseCsv(await file.text());
      if (!csv.headers.length || !csv.rows.length) {
        setError("We couldn't find any rows in that file. Is it a CSV statement export?");
        return;
      }
      setFileName(file.name);
      setParsed(csv);
      setMapping(detectMapping(csv.headers));
    } catch {
      setError("We couldn't read that file. Please check it is a CSV statement export.");
    }
  }

  async function runImport() {
    if (!statement || !statement.rows.length) return;
    if (statement.rows.length > MAX_ROWS) {
      setError(`Please import at most ${MAX_ROWS.toLocaleString("en-GB")} rows at a time.`);
      return;
    }
    setBusy(true);
    setError("");
    try {
      const payload = statement.rows.map((row, index) => {
        const link = unlinked.has(index) ? undefined : suggestions.get(index);
        if (!link) return row;
        const chosenId = chosen.get(index);
        const plan = chosenId ? plans.find((entry) => entry.id === chosenId) : undefined;
        return plan
          ? {
              ...row,
              scheduledItemId: plan.id,
              planDate: nearestPlanDate(plan, row.date),
            }
          : { ...row, scheduledItemId: link.planId, planDate: link.planDate };
      });
      const outcome = await api.importOccurrences(token, accountId, payload);
      setResult(
        `Imported ${outcome.imported.toLocaleString("en-GB")} transactions` +
          (outcome.duplicates
            ? `, ${outcome.duplicates.toLocaleString("en-GB")} already imported`
            : "") +
          (outcome.skipped ? `, ${outcome.skipped.toLocaleString("en-GB")} skipped` : "") +
          (payload.some((row) => "scheduledItemId" in row)
            ? `, ${activeLinks.length.toLocaleString("en-GB")} linked to plans`
            : "") +
          ".",
      );
      reset();
      await onImported();
    } catch (reason) {
      setConfirming(false);
      setError(reason instanceof Error ? reason.message : "Unable to import the statement.");
    } finally {
      setBusy(false);
    }
  }

  const columnOptions = (optional: boolean) => [
    { value: "-1", label: optional ? "— None —" : "— Choose a column —" },
    ...(parsed?.headers ?? []).map((header, index) => ({
      value: String(index),
      label: header || `Column ${index + 1}`,
    })),
  ];

  function setColumn(key: keyof ColumnMapping, value: string) {
    setUnlinked(new Set());
    setChosen(new Map());
    setConfirming(false);
    setMapping((current) => (current ? { ...current, [key]: Number(value) } : current));
  }

  const rows = statement?.rows ?? [];
  const dates = rows.map((row) => row.date).sort();
  const totalIn = rows.filter((row) => row.amount > 0).reduce((sum, row) => sum + row.amount, 0);
  const totalOut = rows.filter((row) => row.amount < 0).reduce((sum, row) => sum + row.amount, 0);

  return (
    <Panel
      className="statement-import"
      title="Import a bank statement"
      subtitle="Upload a CSV export. Already-imported transactions are skipped, so overlapping statements are safe."
    >
      <div className="form-stack">
        <ErrorMessage message={error} />
        {result && <p className="text-sm">{result}</p>}
        <input
          ref={fileInput}
          type="file"
          accept=".csv,.tsv,.txt,text/csv"
          onChange={chooseFile}
          disabled={busy}
          hidden
          aria-label="Bank statement CSV file"
        />
        <div className="flex flex-wrap items-center gap-3">
          <Button
            variant="primary"
            type="button"
            disabled={busy}
            onClick={() => fileInput.current?.click()}
          >
            {parsed ? "Choose a different file" : "Choose CSV file"} <span>↑</span>
          </Button>
          {!parsed && (
            <span className="text-sm text-muted-foreground">
              No file selected. Monzo and most bank CSV exports work.
            </span>
          )}
        </div>
        {parsed && mapping && (
          <>
            <p className="text-sm text-muted-foreground">
              {fileName} · {parsed.rows.length.toLocaleString("en-GB")} rows found. Check the
              columns below.
            </p>
            <div className="form-grid">
              <SelectField
                label="Date"
                value={String(mapping.date)}
                onChange={(value) => setColumn("date", value)}
                options={columnOptions(false)}
              />
              <SelectField
                label="Date format"
                value={dateFormat}
                onChange={(value) => {
                  setUnlinked(new Set());
                  setChosen(new Map());
                  setConfirming(false);
                  setDateFormat(value as DateFormat);
                }}
                options={dateFormats}
              />
              <SelectField
                label="Amount (negative = money out)"
                value={String(mapping.amount)}
                onChange={(value) => setColumn("amount", value)}
                options={columnOptions(true)}
              />
              <SelectField
                label="Description"
                value={String(mapping.name)}
                onChange={(value) => setColumn("name", value)}
                options={columnOptions(true)}
              />
              <SelectField
                label="Money in (if no amount column)"
                value={String(mapping.moneyIn)}
                onChange={(value) => setColumn("moneyIn", value)}
                options={columnOptions(true)}
              />
              <SelectField
                label="Money out (if no amount column)"
                value={String(mapping.moneyOut)}
                onChange={(value) => setColumn("moneyOut", value)}
                options={columnOptions(true)}
              />
              <SelectField
                label="Transaction ID (prevents duplicates)"
                value={String(mapping.id)}
                onChange={(value) => setColumn("id", value)}
                options={columnOptions(true)}
              />
            </div>
            {mapping.date < 0 || !hasAmount ? (
              <p className="text-sm text-muted-foreground">
                Choose the date column and an amount (or money in / money out) column to continue.
              </p>
            ) : (
              statement && (
                <div className="grid gap-2 text-sm">
                  <strong>
                    {rows.length.toLocaleString("en-GB")} transactions ready
                    {dates.length > 0 &&
                      ` · ${dateLabel(dates[0])} to ${dateLabel(dates[dates.length - 1])}`}
                  </strong>
                  <span>
                    Money in {money(totalIn)} · Money out {money(totalOut)}
                  </span>
                  {statement.invalid > 0 && (
                    <span className="text-muted-foreground">
                      {statement.invalid.toLocaleString("en-GB")} rows can’t be read and will be
                      skipped (for example {statement.invalidExamples.join(", ")}).
                    </span>
                  )}
                  {rows.slice(0, 3).map((row, index) => (
                    <span className="text-muted-foreground" key={index}>
                      {dateLabel(row.date)} · {row.name || "No description"} · {money(row.amount)}
                    </span>
                  ))}
                  {suggestions.size > 0 && (
                    <div className="grid gap-2">
                      <strong>
                        {suggestions.size.toLocaleString("en-GB")} transactions look like your
                        recurring plans
                      </strong>
                      <span className="text-muted-foreground">
                        Linked transactions replace that plan occurrence in your forecast so they
                        aren’t counted twice. Untick any that are wrong. You can link the rest to a
                        plan later from the activity list.
                      </span>
                      <div className="grid max-h-72 gap-1 overflow-y-auto">
                        {[...suggestions.entries()].map(([index, match]) => {
                          const planId = chosen.get(index) ?? match.planId;
                          const date = chosen.has(index)
                            ? nearestPlanDate(
                                plans.find((plan) => plan.id === planId)!,
                                rows[index].date,
                              )
                            : match.planDate;
                          return (
                            <div className="flex flex-wrap items-center gap-2" key={index}>
                              <ToggleField
                                label={`${dateLabel(rows[index].date)} · ${rows[index].name || "No description"} · ${money(rows[index].amount)} → ${dateLabel(date)}${chosen.has(index) ? "" : `, matched on ${match.reason}`}`}
                                checked={!unlinked.has(index)}
                                onChange={(linked) => toggleLink(index, linked)}
                              />
                              <select
                                aria-label={`Plan for ${rows[index].name || "transaction"}`}
                                value={planId}
                                disabled={unlinked.has(index)}
                                onChange={(event) =>
                                  setChosen((current) =>
                                    new Map(current).set(index, event.target.value),
                                  )
                                }
                              >
                                {plans
                                  .filter((plan) => plan.isIncome === rows[index].amount > 0)
                                  .map((plan) => (
                                    <option key={plan.id} value={plan.id}>
                                      {plan.name}
                                    </option>
                                  ))}
                              </select>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}
                  <p className="text-muted-foreground">
                    Imported transactions change your account balance. If your balance already
                    includes this period, import only newer transactions.
                  </p>
                </div>
              )
            )}
            {confirming ? (
              <div className="grid gap-2 text-sm" role="alertdialog" aria-label="Confirm import">
                <strong>Are you sure you want to import?</strong>
                <span>
                  {rows.length.toLocaleString("en-GB")} transactions will be added and applied to
                  your balance
                  {activeLinks.length > 0
                    ? `, ${activeLinks.length.toLocaleString("en-GB")} of them linked to plans`
                    : ""}
                  . Transactions already imported are skipped.
                </span>
                <div className="activity-edit-actions">
                  <Button variant="primary" type="button" disabled={busy} onClick={runImport}>
                    {busy ? "Importing…" : "Yes, import"}
                  </Button>
                  <Button
                    variant="outline"
                    type="button"
                    disabled={busy}
                    onClick={() => setConfirming(false)}
                  >
                    Go back
                  </Button>
                </div>
              </div>
            ) : (
              <div className="activity-edit-actions">
                <Button
                  variant="primary"
                  type="button"
                  disabled={busy || !rows.length || !hasAmount}
                  onClick={() => setConfirming(true)}
                >
                  {`Review ${rows.length.toLocaleString("en-GB")} transactions`}
                </Button>
                <Button variant="outline" type="button" disabled={busy} onClick={reset}>
                  Cancel
                </Button>
              </div>
            )}
          </>
        )}
      </div>
    </Panel>
  );
}
