import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ApiError, api, type Account, type Balance, type Forecast } from "../api";
import NameCreateForm from "../NameCreateForm";
import { Card } from "@/components/ui/card";
import { Button, TextField } from "../shared/Controls";
import { dateLabel, money, today } from "../shared/format";
import { EmptyState, ErrorMessage, PageHeading, Panel } from "../shared/Ui";

export default function AccountsPage({ token }: { token: string }) {
  type BalanceEdit = { id: string; date: string; amount: string; note: string };
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [balances, setBalances] = useState<Balance[]>([]);
  const [running, setRunning] = useState<Array<{ date: string; cumulativeAmount: number }>>([]);
  const [forecast, setForecast] = useState<Forecast | null>(null);
  const [accountName, setAccountName] = useState("");
  const [balanceDate, setBalanceDate] = useState(today());
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [editingBalance, setEditingBalance] = useState<BalanceEdit | null>(null);
  const [balanceHistoryError, setBalanceHistoryError] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const loadAccounts = useCallback(async () => {
    const result = await api.accounts(token);
    setAccounts(result);
    setSelectedId((current) =>
      result.some((account) => account.id === current) ? current : (result[0]?.id ?? ""),
    );
  }, [token]);

  const loadBalances = useCallback(
    async (accountId: string) => {
      if (!accountId) return;
      const [balanceList, runningList, accountForecast] = await Promise.all([
        api.balances(token, accountId),
        api.runningBalances(token, accountId),
        api.forecast(token, accountId),
      ]);
      setBalances(balanceList);
      setRunning(runningList);
      setForecast(accountForecast);
    },
    [token],
  );

  useEffect(() => {
    loadAccounts()
      .catch((reason: unknown) =>
        setError(reason instanceof Error ? reason.message : "Unable to load accounts."),
      )
      .finally(() => setLoading(false));
  }, [loadAccounts]);

  useEffect(() => {
    setBalanceHistoryError("");
    loadBalances(selectedId).catch((reason: unknown) =>
      setBalanceHistoryError(balanceErrorMessage(reason, "load")),
    );
  }, [loadBalances, selectedId]);

  async function createAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    try {
      const account = await api.createAccount(token, accountName.trim());
      setAccountName("");
      await loadAccounts();
      setSelectedId(account.id);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to create account.");
    }
  }

  async function createBalance(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedId) return;
    setError("");
    try {
      await api.createBalance(token, selectedId, balanceDate, Number(amount), note);
      setAmount("");
      setNote("");
      await loadBalances(selectedId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to add balance.");
    }
  }

  async function saveBalance(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedId || !editingBalance) return;
    setBalanceHistoryError("");
    try {
      await api.updateBalance(
        token,
        selectedId,
        editingBalance.id,
        editingBalance.date,
        Number(editingBalance.amount),
        editingBalance.note,
      );
      setEditingBalance(null);
    } catch (reason) {
      setBalanceHistoryError(balanceErrorMessage(reason, "update"));
      return;
    }

    try {
      await loadBalances(selectedId);
    } catch (reason) {
      setBalanceHistoryError(
        `Balance updated, but the history could not be refreshed. ${balanceErrorMessage(reason, "load")}`,
      );
    }
  }

  async function deleteBalance(balance: Balance) {
    if (!selectedId || !window.confirm("Delete this balance entry? This cannot be undone.")) return;
    setBalanceHistoryError("");
    try {
      await api.deleteBalance(token, selectedId, balance.id);
      setEditingBalance(null);
    } catch (reason) {
      setBalanceHistoryError(balanceErrorMessage(reason, "delete"));
      return;
    }

    try {
      await loadBalances(selectedId);
    } catch (reason) {
      setBalanceHistoryError(
        `Balance deleted, but the history could not be refreshed. ${balanceErrorMessage(reason, "load")}`,
      );
    }
  }

  const currentAccount = accounts.find((account) => account.id === selectedId);
  const latestBalance = running.at(-1)?.cumulativeAmount ?? 0;

  return (
    <>
      <PageHeading
        eyebrow="YOUR MONEY, ORGANIZED"
        title="Accounts"
        subtitle="Keep your balances together and up to date."
      />
      <ErrorMessage message={error} />
      <section className="accounts-layout">
        <div className="account-column">
          <Panel
            title="Your accounts"
            subtitle={`${accounts.length} ${accounts.length === 1 ? "account" : "accounts"}`}
          >
            {loading ? (
              <div className="loading-state compact">Loading accounts…</div>
            ) : accounts.length ? (
              <div className="select-account-list">
                {accounts.map((account, index) => (
                  <Button
                    variant="ghost"
                    key={account.id}
                    className={`select-account${selectedId === account.id ? " selected" : ""}`}
                    onClick={() => setSelectedId(account.id)}
                  >
                    <span className={`account-symbol symbol-${index % 3}`}>£</span>
                    <span>
                      <strong>{account.name}</strong>
                      <small>
                        {selectedId === account.id ? "Currently selected" : "Personal account"}
                      </small>
                    </span>
                    <span className="select-chevron">›</span>
                  </Button>
                ))}
              </div>
            ) : (
              <EmptyState
                title="No accounts yet"
                text="Add your first account to start building your financial picture."
              />
            )}
            <NameCreateForm
              id="account-name"
              label="Add an account"
              value={accountName}
              onChange={setAccountName}
              placeholder="e.g. Everyday account"
              submitLabel="Add"
              onSubmit={createAccount}
              variant="inline"
            />
          </Panel>
        </div>
        <div className="detail-column">
          {currentAccount ? (
            <>
              <Card className="account-detail-card">
                <div className="detail-card-top">
                  <div>
                    <span className="metric-label">CURRENT ACCOUNT</span>
                    <h2>{currentAccount.name}</h2>
                  </div>
                  <span className="detail-card-icon">£</span>
                </div>
                <div className="detail-total">{money(latestBalance)}</div>
                <div className="detail-card-bottom">
                  <span>Latest recorded total</span>
                  <span>
                    {balances.length} balance {balances.length === 1 ? "entry" : "entries"}
                  </span>
                </div>
              </Card>
              <Panel title="Add a balance" subtitle="Record an amount for a particular date">
                <form className="form-grid" onSubmit={createBalance}>
                  <TextField
                    label="Date"
                    type="date"
                    required
                    value={balanceDate}
                    onChange={setBalanceDate}
                  />
                  <TextField
                    label="Amount (£)"
                    type="number"
                    step="0.01"
                    required
                    value={amount}
                    onChange={setAmount}
                    placeholder="0.00"
                  />
                  <TextField
                    label="Note (optional)"
                    maxLength={200}
                    value={note}
                    onChange={setNote}
                    placeholder="What is this for?"
                  />
                  <Button variant="primary">
                    Save balance <span>→</span>
                  </Button>
                </form>
              </Panel>
              {forecast && (
                <Panel
                  title="This month’s forecast"
                  subtitle={`Projected from ${dateLabel(forecast.from)} to ${dateLabel(forecast.to)}`}
                >
                  <div className="forecast-stats">
                    <div>
                      <span>Safe to spend</span>
                      <strong>{money(forecast.safeToSpend)}</strong>
                    </div>
                    <div>
                      <span>Lowest point</span>
                      <strong>{dateLabel(forecast.safeToSpendDate)}</strong>
                    </div>
                    <div>
                      <span>Month-end projection</span>
                      <strong>{money(forecast.points.at(-1)?.cumulative ?? 0)}</strong>
                    </div>
                  </div>
                </Panel>
              )}
              <Panel title="Balance history" subtitle="Your recorded balance entries, newest first">
                <ErrorMessage message={balanceHistoryError} />
                {balances.length ? (
                  <div className="data-list">
                    {balances.map((balance) => (
                      <div className="data-row" key={balance.id}>
                        {editingBalance?.id === balance.id ? (
                          <form className="activity-edit-form w-full" onSubmit={saveBalance}>
                            <div className="form-grid">
                              <TextField
                                label="Date"
                                type="date"
                                required
                                value={editingBalance.date}
                                onChange={(date) => setEditingBalance({ ...editingBalance, date })}
                              />
                              <TextField
                                label="Amount (£)"
                                type="number"
                                step="0.01"
                                required
                                value={editingBalance.amount}
                                onChange={(value) =>
                                  setEditingBalance({ ...editingBalance, amount: value })
                                }
                              />
                              <TextField
                                label="Note (optional)"
                                maxLength={200}
                                value={editingBalance.note}
                                onChange={(value) =>
                                  setEditingBalance({ ...editingBalance, note: value })
                                }
                              />
                            </div>
                            <div className="activity-edit-actions">
                              <Button variant="primary" size="small">
                                Save changes
                              </Button>
                              <Button
                                variant="outline"
                                size="small"
                                type="button"
                                onClick={() => setEditingBalance(null)}
                              >
                                Cancel
                              </Button>
                            </div>
                          </form>
                        ) : (
                          <>
                            <div className="date-stamp">{dateLabel(balance.date)}</div>
                            {balance.note && (
                              <span className="min-w-0 flex-1 truncate text-sm text-muted-foreground">
                                {balance.note}
                              </span>
                            )}
                            <strong>{money(balance.amount)}</strong>
                            <div className="activity-row-actions">
                              <Button
                                variant="outline"
                                size="small"
                                onClick={() =>
                                  setEditingBalance({
                                    id: balance.id,
                                    date: balance.date.slice(0, 10),
                                    amount: String(balance.amount),
                                    note: balance.note ?? "",
                                  })
                                }
                              >
                                Edit
                              </Button>
                              <Button
                                variant="danger"
                                size="small"
                                onClick={() => deleteBalance(balance)}
                              >
                                Delete
                              </Button>
                            </div>
                          </>
                        )}
                      </div>
                    ))}
                  </div>
                ) : (
                  <EmptyState
                    title="No balance history"
                    text="Add your first balance above to give your account a starting point."
                  />
                )}
              </Panel>
              {running.length > 0 && (
                <Panel title="Running balance" subtitle="Cumulative account balance by date">
                  <div className="data-list">
                    {running
                      .slice(-5)
                      .reverse()
                      .map((item) => {
                        const notes = balances
                          .filter((balance) => balance.date.slice(0, 10) === item.date.slice(0, 10))
                          .map((balance) => balance.note)
                          .filter(Boolean)
                          .join(" · ");
                        return (
                          <div className="data-row" key={item.date}>
                            <div className="date-stamp">{dateLabel(item.date)}</div>
                            {notes && (
                              <span className="min-w-0 flex-1 truncate text-sm text-muted-foreground">
                                {notes}
                              </span>
                            )}
                            <strong>{money(item.cumulativeAmount)}</strong>
                          </div>
                        );
                      })}
                  </div>
                </Panel>
              )}
            </>
          ) : (
            <Card className="welcome-card">
              <span className="welcome-flower">✳</span>
              <h2>Your next step starts here.</h2>
              <p>Create an account, then add a balance to see your finances take shape.</p>
            </Card>
          )}
        </div>
      </section>
    </>
  );
}

function balanceErrorMessage(reason: unknown, action: "update" | "delete" | "load") {
  if (reason instanceof ApiError && reason.status === 404) {
    return "The running Finance API did not find this balance or endpoint. Restart the Finance API, refresh the page, and try again.";
  }

  const detail = reason instanceof Error ? reason.message : `Unable to ${action} balance.`;
  return detail;
}
