import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, type Account, type Balance, type Forecast } from "../api";
import NameCreateForm from "../NameCreateForm";
import { Card } from "@/components/ui/card";
import { Button, TextField } from "../shared/Controls";
import { dateLabel, money, today } from "../shared/format";
import { EmptyState, ErrorMessage, PageHeading, Panel } from "../shared/Ui";

export default function AccountsPage({ token }: { token: string }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [balances, setBalances] = useState<Balance[]>([]);
  const [running, setRunning] = useState<Array<{ date: string; cumulativeAmount: number }>>([]);
  const [forecast, setForecast] = useState<Forecast | null>(null);
  const [accountName, setAccountName] = useState("");
  const [balanceDate, setBalanceDate] = useState(today());
  const [amount, setAmount] = useState("");
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
    loadBalances(selectedId).catch((reason: unknown) =>
      setError(reason instanceof Error ? reason.message : "Unable to load balances."),
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
      await api.createBalance(token, selectedId, balanceDate, Number(amount));
      setAmount("");
      await loadBalances(selectedId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to add balance.");
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
                {balances.length ? (
                  <div className="data-list">
                    {balances.map((balance) => (
                      <div className="data-row" key={balance.id}>
                        <div className="date-stamp">{dateLabel(balance.date)}</div>
                        <strong>{money(balance.amount)}</strong>
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
                      .map((item) => (
                        <div className="data-row" key={item.date}>
                          <div className="date-stamp">{dateLabel(item.date)}</div>
                          <strong>{money(item.cumulativeAmount)}</strong>
                        </div>
                      ))}
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
