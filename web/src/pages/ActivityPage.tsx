import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { claimKey, nearestPlanDate } from "../lib/planMatch";
import {
  api,
  type Account,
  type Occurrence,
  type OccurrenceUpdate,
  type Schedule,
  type ScheduleUpdate,
} from "../api";
import { Card } from "@/components/ui/card";
import { Button, SelectField, TextField, ToggleField } from "../shared/Controls";
import { dateLabel, money, today } from "../shared/format";
import { EmptyState, ErrorMessage, PageHeading, Panel } from "../shared/Ui";
import StatementImport from "../shared/StatementImport";

const PAGE_SIZE = 25;

type ScheduleEdit = Omit<ScheduleUpdate, "amount"> & {
  id: string;
  sourceAccountId: string;
  amount: string;
};
type OccurrenceEdit = Omit<OccurrenceUpdate, "amount"> & {
  id: string;
  sourceAccountId: string;
  isReconciled: boolean;
  amount: string;
};

export default function ActivityPage({ token }: { token: string }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [accountId, setAccountId] = useState("");
  const [occurrences, setOccurrences] = useState<Occurrence[]>([]);
  const [schedules, setSchedules] = useState<Schedule[]>([]);
  const [editingOccurrence, setEditingOccurrence] = useState<OccurrenceEdit | null>(null);
  const [editingSchedule, setEditingSchedule] = useState<ScheduleEdit | null>(null);
  const [error, setError] = useState("");
  const [occurrenceTotal, setOccurrenceTotal] = useState(0);
  const [claimedPlanDates, setClaimedPlanDates] = useState<Set<string>>(new Set());
  const [loadingMore, setLoadingMore] = useState(false);
  const loadedCount = useRef(0);
  const [loading, setLoading] = useState(true);

  const [occurrenceDate, setOccurrenceDate] = useState(today());
  const [occurrenceAmount, setOccurrenceAmount] = useState("");
  const [isIncome, setIsIncome] = useState(false);
  const [externalReference, setExternalReference] = useState("");
  const [scheduleName, setScheduleName] = useState("");
  const [scheduleAmount, setScheduleAmount] = useState("");
  const [scheduleDate, setScheduleDate] = useState(today());
  const [frequency, setFrequency] = useState(2);
  const [scheduleIncome, setScheduleIncome] = useState(false);

  const loadActivity = useCallback(
    async (id: string, keepLoaded = false) => {
      if (!id) {
        setOccurrences([]);
        setOccurrenceTotal(0);
        setSchedules([]);
        setClaimedPlanDates(new Set());
        return;
      }
      // Re-fetch as many rows as were already shown (the API serves at most 100 a request).
      const wanted = keepLoaded ? Math.max(PAGE_SIZE, loadedCount.current) : PAGE_SIZE;
      const items: Occurrence[] = [];
      let total = 0;
      while (items.length < wanted) {
        const page = await api.occurrencePage(
          token,
          id,
          items.length,
          Math.min(100, wanted - items.length),
        );
        total = page.total;
        items.push(...page.items);
        if (page.items.length === 0 || items.length >= total) break;
      }
      const [plans, links] = await Promise.all([
        api.schedules(token, id),
        api.occurrencePlanLinks(token, id),
      ]);
      loadedCount.current = items.length;
      setOccurrences(items);
      setOccurrenceTotal(total);
      setSchedules(plans);
      setClaimedPlanDates(
        new Set(links.map((link) => claimKey(link.scheduledItemId, link.planDate))),
      );
    },
    [token],
  );

  async function showMoreOccurrences() {
    setLoadingMore(true);
    setError("");
    try {
      const page = await api.occurrencePage(token, accountId, occurrences.length, PAGE_SIZE);
      loadedCount.current = occurrences.length + page.items.length;
      setOccurrences((current) => [...current, ...page.items]);
      setOccurrenceTotal(page.total);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to load more activity.");
    } finally {
      setLoadingMore(false);
    }
  }

  useEffect(() => {
    api
      .accounts(token)
      .then((result) => {
        setAccounts(result);
        setAccountId(result[0]?.id ?? "");
      })
      .catch((reason: unknown) =>
        setError(reason instanceof Error ? reason.message : "Unable to load accounts."),
      )
      .finally(() => setLoading(false));
  }, [token]);

  useEffect(() => {
    loadActivity(accountId).catch((reason: unknown) =>
      setError(reason instanceof Error ? reason.message : "Unable to load activity."),
    );
  }, [accountId, loadActivity]);

  async function linkOccurrence(item: Occurrence, planId: string | null) {
    setError("");
    try {
      const plan = planId ? schedules.find((entry) => entry.id === planId) : undefined;
      await api.linkOccurrencePlan(token, accountId, item.id, {
        scheduledItemId: plan?.id ?? null,
        planDate: plan ? nearestPlanDate(plan, item.date) : null,
      });
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update the plan link.");
    }
  }

  async function createOccurrence(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    try {
      await api.createOccurrence(token, accountId, {
        date: occurrenceDate,
        amount: Number(occurrenceAmount),
        isIncome,
        scheduledItemId: null,
        externalReference: externalReference.trim() || null,
      });
      setOccurrenceAmount("");
      setExternalReference("");
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to record activity.");
    }
  }

  async function createSchedule(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    try {
      await api.createSchedule(token, accountId, {
        name: scheduleName.trim(),
        amount: Number(scheduleAmount),
        startDate: scheduleDate,
        frequency,
        isIncome: scheduleIncome,
      });
      setScheduleName("");
      setScheduleAmount("");
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to create schedule.");
    }
  }

  async function saveOccurrence(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingOccurrence) return;
    setError("");
    try {
      await api.updateOccurrence(token, editingOccurrence.sourceAccountId, editingOccurrence.id, {
        accountId: editingOccurrence.accountId,
        date: editingOccurrence.date,
        amount: Number(editingOccurrence.amount),
        isIncome: editingOccurrence.isIncome,
        externalReference: (editingOccurrence.externalReference ?? "").trim() || null,
      });
      setEditingOccurrence(null);
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update activity.");
    }
  }

  async function deleteOccurrence(item: Occurrence) {
    const message = item.isReconciled
      ? "Delete this reconciled activity? A correcting balance entry will be added. This cannot be undone."
      : "Delete this recorded activity? This cannot be undone.";
    if (!window.confirm(message)) return;
    setError("");
    try {
      await api.deleteOccurrence(token, accountId, item.id);
      setEditingOccurrence(null);
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to delete activity.");
    }
  }

  async function saveSchedule(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingSchedule) return;
    setError("");
    try {
      await api.updateSchedule(token, editingSchedule.sourceAccountId, editingSchedule.id, {
        accountId: editingSchedule.accountId,
        name: editingSchedule.name.trim(),
        amount: Number(editingSchedule.amount),
        startDate: editingSchedule.startDate,
        frequency: Number(editingSchedule.frequency),
        isIncome: editingSchedule.isIncome,
      });
      setEditingSchedule(null);
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update recurring plan.");
    }
  }

  async function deleteSchedule(item: Schedule) {
    if (!window.confirm("Delete this recurring plan? This cannot be undone.")) return;
    setError("");
    try {
      await api.deleteSchedule(token, accountId, item.id);
      setEditingSchedule(null);
      await loadActivity(accountId, true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to delete recurring plan.");
    }
  }

  return (
    <>
      <PageHeading
        eyebrow="INCOME, EXPENSES & PLANS"
        title="Activity"
        subtitle="Record what happened and plan what’s coming next."
      />
      <ErrorMessage message={error} />
      {accounts.length > 0 && (
        <div className="toolbar">
          <SelectField
            id="activity-account"
            label="Account"
            value={accountId}
            onChange={(value) => {
              setAccountId(value);
              setEditingOccurrence(null);
              setEditingSchedule(null);
            }}
            options={accounts.map((account) => ({ value: account.id, label: account.name }))}
          />
        </div>
      )}
      {loading ? (
        <div className="loading-state">Loading activity…</div>
      ) : accounts.length ? (
        <>
          <section className="activity-forms">
            <Panel
              title="Record activity"
              subtitle="Add an income or expense that’s already happened"
            >
              <form className="form-stack" onSubmit={createOccurrence}>
                <TextField
                  label={
                    <>
                      Description reference <span className="optional-label">Optional</span>
                    </>
                  }
                  value={externalReference}
                  onChange={setExternalReference}
                  placeholder="e.g. Groceries or invoice #123"
                />
                <div className="form-grid">
                  <TextField
                    label="Date"
                    type="date"
                    required
                    value={occurrenceDate}
                    onChange={setOccurrenceDate}
                  />
                  <TextField
                    label="Amount (£)"
                    type="number"
                    required
                    min="0.01"
                    step="0.01"
                    value={occurrenceAmount}
                    onChange={setOccurrenceAmount}
                    placeholder="0.00"
                  />
                </div>
                <ToggleField label="This is income" checked={isIncome} onChange={setIsIncome} />
                <Button variant="primary">
                  Record activity <span>→</span>
                </Button>
              </form>
            </Panel>
            <Panel title="Plan recurring activity" subtitle="Set up a repeating income or expense">
              <form className="form-stack" onSubmit={createSchedule}>
                <TextField
                  label="Name"
                  required
                  maxLength={100}
                  value={scheduleName}
                  onChange={setScheduleName}
                  placeholder="e.g. Rent or monthly salary"
                />
                <div className="form-grid">
                  <TextField
                    label="Amount (£)"
                    type="number"
                    required
                    min="0.01"
                    step="0.01"
                    value={scheduleAmount}
                    onChange={setScheduleAmount}
                    placeholder="0.00"
                  />
                  <TextField
                    label="Starts on"
                    type="date"
                    required
                    value={scheduleDate}
                    onChange={setScheduleDate}
                  />
                </div>
                <SelectField
                  label="Repeats"
                  value={String(frequency)}
                  onChange={(value) => setFrequency(Number(value))}
                  options={[
                    { value: "0", label: "Every week" },
                    { value: "1", label: "Every two weeks" },
                    { value: "2", label: "Every month" },
                    { value: "3", label: "Every year" },
                  ]}
                />
                <ToggleField
                  label="This is income"
                  checked={scheduleIncome}
                  onChange={setScheduleIncome}
                />
                <Button variant="primary">
                  Save schedule <span>→</span>
                </Button>
              </form>
            </Panel>
          </section>
          <StatementImport
            token={token}
            accountId={accountId}
            plans={schedules}
            claimed={claimedPlanDates}
            onImported={() => loadActivity(accountId)}
          />
          <section className="content-grid activity-lists">
            <Panel
              title="Recent activity"
              subtitle="Applied to the account balance automatically on its date"
            >
              {occurrences.length ? (
                <div className="data-list">
                  {occurrences.map((item) =>
                    editingOccurrence?.id === item.id ? (
                      <form className="activity-edit-form" key={item.id} onSubmit={saveOccurrence}>
                        <div className="activity-edit-grid">
                          <TextField
                            label="Description"
                            value={editingOccurrence.externalReference ?? ""}
                            onChange={(externalReference) =>
                              setEditingOccurrence({ ...editingOccurrence, externalReference })
                            }
                            placeholder={item.isIncome ? "Income" : "Expense"}
                          />
                          <SelectField
                            label="Account"
                            value={editingOccurrence.accountId}
                            onChange={(accountId) =>
                              setEditingOccurrence({ ...editingOccurrence, accountId })
                            }
                            options={accounts.map((account) => ({
                              value: account.id,
                              label: account.name,
                            }))}
                          />
                          <TextField
                            label="Date"
                            type="date"
                            required
                            value={editingOccurrence.date}
                            onChange={(date) =>
                              setEditingOccurrence({ ...editingOccurrence, date })
                            }
                          />
                          <TextField
                            label="Amount (£)"
                            type="number"
                            min="0.01"
                            step="0.01"
                            required
                            value={editingOccurrence.amount}
                            onChange={(amount) =>
                              setEditingOccurrence({ ...editingOccurrence, amount })
                            }
                          />
                        </div>
                        <ToggleField
                          label="This is income"
                          checked={editingOccurrence.isIncome}
                          onChange={(isIncome) =>
                            setEditingOccurrence({ ...editingOccurrence, isIncome })
                          }
                        />
                        {item.isReconciled && (
                          <small className="activity-edit-note">
                            Saving applies balance corrections for this reconciled item.
                          </small>
                        )}
                        <div className="activity-edit-actions">
                          <Button variant="primary" size="small">
                            Save changes
                          </Button>
                          <Button
                            variant="outline"
                            size="small"
                            type="button"
                            onClick={() => setEditingOccurrence(null)}
                          >
                            Cancel
                          </Button>
                        </div>
                      </form>
                    ) : (
                      <div className="activity-row" key={item.id}>
                        <span className={`activity-sign ${item.isIncome ? "positive" : ""}`}>
                          {item.isIncome ? "↙" : "↗"}
                        </span>
                        <div className="activity-description">
                          <strong>
                            {item.description ||
                              item.externalReference ||
                              (item.isIncome ? "Income" : "Expense")}
                          </strong>
                          <small>
                            {dateLabel(item.date)} · {item.isReconciled ? "Applied" : "Scheduled"}
                          </small>
                          <small className="activity-plan-link">
                            {(item.scheduledItemId ||
                              schedules.some((plan) => plan.isIncome === item.isIncome)) && (
                              <label>
                                Plan:{" "}
                                <select
                                  aria-label={`Plan paid by ${item.description || "this activity"}`}
                                  value={item.scheduledItemId ?? ""}
                                  onChange={(event) =>
                                    linkOccurrence(item, event.target.value || null)
                                  }
                                >
                                  <option value="">Not linked to a plan</option>
                                  {schedules
                                    .filter(
                                      (plan) =>
                                        plan.isIncome === item.isIncome ||
                                        plan.id === item.scheduledItemId,
                                    )
                                    .map((plan) => (
                                      <option key={plan.id} value={plan.id}>
                                        {plan.name}
                                      </option>
                                    ))}
                                </select>
                                {item.scheduledItemId && item.planDate
                                  ? ` · pays the ${dateLabel(item.planDate)} occurrence`
                                  : ""}
                              </label>
                            )}
                          </small>
                        </div>
                        <strong className={item.isIncome ? "amount-positive" : ""}>
                          {item.isIncome ? "+" : "−"}
                          {money(item.amount)}
                        </strong>
                        <div className="activity-row-actions">
                          <Button
                            variant="outline"
                            size="small"
                            onClick={() =>
                              setEditingOccurrence({
                                id: item.id,
                                sourceAccountId: accountId,
                                accountId,
                                date: item.date.slice(0, 10),
                                amount: String(item.amount),
                                isIncome: item.isIncome,
                                externalReference: item.externalReference,
                                isReconciled: item.isReconciled,
                              })
                            }
                          >
                            Edit
                          </Button>
                          <Button
                            variant="danger"
                            size="small"
                            onClick={() => deleteOccurrence(item)}
                          >
                            Delete
                          </Button>
                        </div>
                      </div>
                    ),
                  )}
                  {occurrences.length < occurrenceTotal && (
                    <Button
                      variant="outline"
                      type="button"
                      disabled={loadingMore}
                      onClick={showMoreOccurrences}
                    >
                      {loadingMore
                        ? "Loading…"
                        : `Load more (${occurrenceTotal - occurrences.length} remaining)`}
                    </Button>
                  )}
                </div>
              ) : (
                <EmptyState
                  title="Nothing recorded yet"
                  text="Your one-off income and expenses will appear here."
                />
              )}
            </Panel>
            <Panel title="Upcoming plans" subtitle="Recurring income and expenses for this account">
              {schedules.length ? (
                <div className="data-list">
                  {schedules.map((item) =>
                    editingSchedule?.id === item.id ? (
                      <form className="activity-edit-form" key={item.id} onSubmit={saveSchedule}>
                        <div className="activity-edit-grid">
                          <TextField
                            label="Name"
                            required
                            maxLength={100}
                            value={editingSchedule.name}
                            onChange={(name) => setEditingSchedule({ ...editingSchedule, name })}
                          />
                          <SelectField
                            label="Account"
                            value={editingSchedule.accountId}
                            onChange={(accountId) =>
                              setEditingSchedule({ ...editingSchedule, accountId })
                            }
                            options={accounts.map((account) => ({
                              value: account.id,
                              label: account.name,
                            }))}
                          />
                          <TextField
                            label="Amount (£)"
                            type="number"
                            min="0.01"
                            step="0.01"
                            required
                            value={editingSchedule.amount}
                            onChange={(amount) =>
                              setEditingSchedule({ ...editingSchedule, amount })
                            }
                          />
                          <TextField
                            label="Starts on"
                            type="date"
                            required
                            value={editingSchedule.startDate}
                            onChange={(startDate) =>
                              setEditingSchedule({ ...editingSchedule, startDate })
                            }
                          />
                          <SelectField
                            label="Repeats"
                            value={String(editingSchedule.frequency)}
                            onChange={(value) =>
                              setEditingSchedule({
                                ...editingSchedule,
                                frequency: Number(value),
                              })
                            }
                            options={[
                              { value: "0", label: "Every week" },
                              { value: "1", label: "Every two weeks" },
                              { value: "2", label: "Every month" },
                              { value: "3", label: "Every year" },
                            ]}
                          />
                        </div>
                        <ToggleField
                          label="This is income"
                          checked={editingSchedule.isIncome}
                          onChange={(isIncome) =>
                            setEditingSchedule({ ...editingSchedule, isIncome })
                          }
                        />
                        <div className="activity-edit-actions">
                          <Button variant="primary" size="small">
                            Save changes
                          </Button>
                          <Button
                            variant="outline"
                            size="small"
                            type="button"
                            onClick={() => setEditingSchedule(null)}
                          >
                            Cancel
                          </Button>
                        </div>
                      </form>
                    ) : (
                      <div className="activity-row" key={item.id}>
                        <span className={`activity-sign ${item.isIncome ? "positive" : ""}`}>
                          {item.isIncome ? "↙" : "↗"}
                        </span>
                        <div className="activity-description">
                          <strong>{item.name}</strong>
                          <small>
                            {["Weekly", "Fortnightly", "Monthly", "Annual"][item.frequency] ??
                              "Recurring"}{" "}
                            · starts {dateLabel(item.startDate)}
                          </small>
                        </div>
                        <strong className={item.isIncome ? "amount-positive" : ""}>
                          {item.isIncome ? "+" : "−"}
                          {money(item.amount)}
                        </strong>
                        <div className="activity-row-actions">
                          <Button
                            variant="outline"
                            size="small"
                            onClick={() =>
                              setEditingSchedule({
                                id: item.id,
                                sourceAccountId: accountId,
                                accountId,
                                name: item.name,
                                amount: String(item.amount),
                                startDate: item.startDate.slice(0, 10),
                                frequency: item.frequency,
                                isIncome: item.isIncome,
                              })
                            }
                          >
                            Edit
                          </Button>
                          <Button
                            variant="danger"
                            size="small"
                            onClick={() => deleteSchedule(item)}
                          >
                            Delete
                          </Button>
                        </div>
                      </div>
                    ),
                  )}
                </div>
              ) : (
                <EmptyState
                  title="No recurring plans yet"
                  text="Add a schedule above to plan regular income or expenses."
                />
              )}
            </Panel>
          </section>
        </>
      ) : (
        <Card className="welcome-card">
          <span className="welcome-flower">↗</span>
          <h2>Start with an account.</h2>
          <p>Add an account before recording activity or setting up recurring plans.</p>
        </Card>
      )}
    </>
  );
}
