import { useEffect, useState } from "react";
import { ApiError, api, type FinancialOverview, type PaydayPlan, type SetupStatus } from "../api";
import { dateLabel, money } from "../shared/format";
import { Button } from "../shared/Controls";
import { EmptyState, ErrorMessage, MetricCard, PageHeading, Panel } from "../shared/Ui";
import type { Page } from "../types";

export default function OverviewPage({
  token,
  onNavigate,
}: {
  token: string;
  onNavigate: (page: Page) => void;
}) {
  const [summary, setSummary] = useState<FinancialOverview | null>(null);
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [paydayPlan, setPaydayPlan] = useState<PaydayPlan | null>(null);
  const [paydayPlanError, setPaydayPlanError] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const reportError = (reason: unknown, resource: string) => {
      const message =
        resource === "payday plan" && reason instanceof ApiError && reason.status === 404
          ? "The payday planner isn't available on the running API. Restart the Finance API, then refresh."
          : reason instanceof Error
            ? reason.message
            : `Unable to load your ${resource}.`;
      if (resource === "payday plan") {
        setPaydayPlanError(message);
      } else {
        setError((current) => (current ? `${current} ${message}` : message));
      }
    };

    Promise.all([
      api
        .overview(token)
        .then(setSummary)
        .catch((reason: unknown) => reportError(reason, "overview")),
      api
        .setup(token)
        .then(setSetup)
        .catch((reason: unknown) => reportError(reason, "setup status")),
      api
        .paydayPlan(token)
        .then((plan) => {
          if (
            !Number.isFinite(plan.discretionaryIncome) ||
            plan.accounts.some((account) => !Number.isFinite(account.expensesDue))
          ) {
            throw new ApiError(
              "The payday planner API is out of date. Restart the Finance API, then refresh.",
              0,
            );
          }
          setPaydayPlan(plan);
        })
        .catch((reason: unknown) => reportError(reason, "payday plan")),
    ]).finally(() => setLoading(false));
  }, [token]);

  const discretionarySuggestions = (() => {
    let remaining = Math.max(0, paydayPlan?.discretionaryIncome ?? 0);
    const result: Array<{ potId: string; potName: string; shortfall: number; amount: number }> =
      [];
    for (const pot of summary?.potShortfalls ?? []) {
      if (remaining < 0.01) break;
      const amount = Math.min(remaining, pot.shortfall);
      if (amount < 0.01) continue;
      result.push({ potId: pot.potId, potName: pot.potName, shortfall: pot.shortfall, amount });
      remaining -= amount;
    }
    return result;
  })();

  return (
    <>
      <PageHeading
        eyebrow={new Intl.DateTimeFormat("en-GB", {
          weekday: "long",
          day: "numeric",
          month: "long",
        }).format(new Date())}
        title="Your payday plan."
        subtitle="See what needs moving, where it needs to go, and what’s left."
        action={
          <Button variant="outline" onClick={() => onNavigate("activity")}>
            Adjust income & expenses
          </Button>
        }
      />
      <ErrorMessage message={error} />
      {loading ? (
        <div className="loading-state">Gathering your financial picture…</div>
      ) : (
        <>
          <Panel
            className="payday-panel mb-5"
            title="Transfers to make"
            subtitle={
              paydayPlan?.paydayStartDate && paydayPlan.nextPaydayDate
                ? `Payday ${dateLabel(paydayPlan.paydayStartDate)}${paydayPlan.paydayWindowEndDate !== paydayPlan.paydayStartDate ? `–${dateLabel(paydayPlan.paydayWindowEndDate!)}` : ""}`
                : "Add a recurring income schedule to calculate your payday plan."
            }
          >
            {paydayPlan?.paydayStartDate ? (
              <div className="grid gap-5">
                <div className="grid gap-3 md:grid-cols-3">
                  <div className="rounded-lg bg-muted/60 p-3">
                    <span className="text-xs text-muted-foreground">Total income</span>
                    <strong className="mt-1 block text-lg">
                      {money(paydayPlan.scheduledIncome)}
                    </strong>
                  </div>
                  <div className="rounded-lg bg-muted/60 p-3">
                    <span className="text-xs text-muted-foreground">Total expenditure</span>
                    <strong className="mt-1 block text-lg">
                      {money(paydayPlan.scheduledExpenses)}
                    </strong>
                  </div>
                  <div className="rounded-lg bg-primary/10 p-3">
                    <span className="text-xs text-muted-foreground">Discretionary income</span>
                    <strong className="mt-1 block text-lg">
                      {money(paydayPlan.discretionaryIncome)}
                    </strong>
                  </div>
                </div>
                {paydayPlan.transfers.length ? (
                  <div className="grid gap-2">
                    <h3 className="text-sm font-semibold">Recommended transfers</h3>
                    {paydayPlan.transfers.map((transfer, index) => (
                      <div
                        className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3"
                        key={`${transfer.fromAccountId}-${transfer.toAccountId}-${index}`}
                      >
                        <span>
                          <strong>{transfer.fromAccountName}</strong>
                          <span className="text-muted-foreground"> → </span>
                          <strong>{transfer.toAccountName}</strong>
                        </span>
                        <strong className="text-base">{money(transfer.amount)}</strong>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    No transfers are needed to cover the scheduled expenses in this pay period.
                  </p>
                )}
                {paydayPlan.accounts.some((account) => account.unfundedShortfall > 0) && (
                  <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm">
                    <strong>Some expenses remain unfunded.</strong>
                    {paydayPlan.accounts
                      .filter((account) => account.unfundedShortfall > 0)
                      .map((account) => (
                        <p key={account.accountId}>
                          {account.accountName}: {money(account.unfundedShortfall)} still needed
                        </p>
                      ))}
                  </div>
                )}
                <div className="grid gap-2">
                  <h3 className="text-sm font-semibold">Expenses by account, and what remains from today until payday</h3>
                  <div className="grid grid-cols-[minmax(0,1fr)_auto_auto] gap-4 border-b pb-2 text-xs text-muted-foreground">
                    <span>Account</span>
                    <span>Total expenditure</span>
                    <span>Remaining after today</span>
                  </div>
                  {paydayPlan.accounts.map((account) => (
                    <div
                      className="grid grid-cols-[minmax(0,1fr)_auto_auto] gap-4 border-b py-2 text-sm last:border-0"
                      key={account.accountId}
                    >
                      <span>{account.accountName}</span>
                      <span>
                        {account.expensesDue === 0 ? money(0) : money(-account.expensesDue)}
                      </span>
                      <strong>{money(account.expensesDue)}</strong>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <div className="grid justify-items-start gap-3">
                <p className="text-sm text-muted-foreground">
                  {paydayPlanError ||
                    "Payday is inferred from recurring income dates and frequencies. Add or adjust your income and expenses to create a plan."}
                </p>
                {!paydayPlanError && (
                  <Button variant="outline" size="small" onClick={() => onNavigate("activity")}>
                    Set up recurring income
                  </Button>
                )}
              </div>
            )}
          </Panel>
          <section className="overview-grid">
            <div className="hero-card">
              <div className="hero-top">
                <span className="hero-label">DISCRETIONARY INCOME</span>
                <span className="hero-orbit">✳</span>
              </div>
              <strong className="hero-amount">
                {paydayPlan ? money(paydayPlan.discretionaryIncome) : "—"}
              </strong>
              <p>Total income less expenses due in this pay period.</p>
              {discretionarySuggestions.length > 0 && (
                <div className="grid gap-1 text-sm">
                  <strong>Suggested use of your discretionary income</strong>
                  {discretionarySuggestions.map((item) => (
                    <span key={item.potId}>
                      {money(item.amount)} to cover pot ‘{item.potName}’
                      {item.amount < item.shortfall &&
                        ` (${money(item.shortfall - item.amount)} still to go)`}
                    </span>
                  ))}
                </div>
              )}
              <Button variant="link" className="hero-link" onClick={() => onNavigate("activity")}>
                Explore your cash flow <span>→</span>
              </Button>
              <div className="hero-decoration" aria-hidden="true">
                <i />
                <i />
                <i />
              </div>
            </div>
            <MetricCard
              icon="▤"
              tone="sage"
              label="ACCOUNTS"
              value={summary?.accounts.length ?? 0}
              description="Connected to your plan"
              actionLabel="View accounts"
              onAction={() => onNavigate("accounts")}
            />
            <MetricCard
              icon="◎"
              tone="peach"
              label="POT SHORTFALLS"
              value={summary?.potShortfalls.length ?? 0}
              description="Goals to give a little attention"
              actionLabel="View pots"
              onAction={() => onNavigate("pots")}
            />
          </section>
          <section className="content-grid">
            <Panel title="Your accounts" subtitle="A quick look at what’s available">
              {summary?.accounts.length ? (
                <div className="account-list">
                  {summary.accounts.map((account) => (
                    <div className="account-row" key={account.accountId}>
                      <span className="account-symbol">£</span>
                      <span className="account-name">
                        {account.name}
                        <small>Available for spending</small>
                      </span>
                      <strong>{money(account.availableForSpending)}</strong>
                    </div>
                  ))}
                  <Button
                    variant="link"
                    className="panel-footer-link"
                    onClick={() => onNavigate("accounts")}
                  >
                    Manage accounts <span>→</span>
                  </Button>
                </div>
              ) : (
                <EmptyState
                  title="Let’s start with an account"
                  text="Add an account and an opening balance to see your finances take shape."
                />
              )}
            </Panel>
            {!(setup?.hasAccount && setup.hasBalance && setup.hasPotIfGoals) && (
            <Panel title="A good place to begin" subtitle="Your setup, at a glance">
              {setup && (
                <div className="checklist">
                  {[
                    { label: "Create your first account", done: setup.hasAccount },
                    { label: "Add an account balance", done: setup.hasBalance },
                    { label: "Set up your pots and goals", done: setup.hasPotIfGoals },
                  ].map((item) => (
                    <div className="checklist-row" key={item.label}>
                      <span className={`check-dot ${item.done ? "done" : ""}`}>
                        {item.done ? "✓" : "·"}
                      </span>
                      <span>{item.label}</span>
                      <span className={`check-status ${item.done ? "done" : ""}`}>
                        {item.done ? "Done" : "To do"}
                      </span>
                    </div>
                  ))}
                  {!setup.hasAccount && (
                    <Button variant="outline" size="small" onClick={() => onNavigate("accounts")}>
                      Add your first account
                    </Button>
                  )}
                  {setup.hasAccount && !setup.hasBalance && (
                    <Button variant="outline" size="small" onClick={() => onNavigate("accounts")}>
                      Add a balance
                    </Button>
                  )}
                </div>
              )}
            </Panel>
            )}
          </section>
        </>
      )}
    </>
  );
}
