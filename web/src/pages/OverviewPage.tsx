import { useEffect, useState } from "react";
import { ApiError, api, type FinancialOverview, type PaydayPlan, type SetupStatus } from "../api";
import { dateLabel, money, today } from "../shared/format";
import { buildShareSummary, whatsAppShareUrl } from "../lib/shareSummary";
import { Button } from "../shared/Controls";
import { EmptyState, ErrorMessage, PageHeading, Panel } from "../shared/Ui";
import type { Page } from "../types";
import {
  AccountBreakdown,
  BalanceReminder,
  MoneyLeftAfterTransfers,
  PrePaydayWarning,
  TransferRow,
} from "./PaydayBreakdown";
import {
  defaultPeriod,
  noPaydayYetMessage,
  periodLabel,
  showsBreakdown,
  spareMoneyTip,
  transferGroups,
  type PaydayPeriod,
} from "../lib/paydayBreakdown";

/** Rejects a plan from an API that predates the fields this page relies on. */
function checkPlanShape(plan: PaydayPlan) {
  if (
    !Number.isFinite(plan.discretionaryIncome) ||
    plan.accounts.some(
      (account) =>
        !Number.isFinite(account.expensesDue) ||
        !Number.isFinite(account.toSend) ||
        !Number.isFinite(account.leftAfterTransfers),
    )
  ) {
    throw new ApiError(
      "The payday planner API is out of date. Restart the Finance API, then refresh.",
      0,
    );
  }
  return plan;
}

/** This pay period and the next one, side by side. */
async function loadPlans(token: string): Promise<Record<PaydayPeriod, PaydayPlan | null>> {
  const [current, next] = await Promise.all([
    api.paydayPlan(token, "current"),
    api.paydayPlan(token, "next"),
  ]);
  return { current: checkPlanShape(current), next: checkPlanShape(next) };
}

export default function OverviewPage({
  token,
  onNavigate,
}: {
  token: string;
  onNavigate: (page: Page) => void;
}) {
  const [summary, setSummary] = useState<FinancialOverview | null>(null);
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [plans, setPlans] = useState<Record<PaydayPeriod, PaydayPlan | null>>({
    current: null,
    next: null,
  });
  const [period, setPeriod] = useState<PaydayPeriod>("current");
  const paydayPlan = plans[period];
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
      loadPlans(token)
        .then((loaded) => {
          setPlans(loaded);
          setPeriod(defaultPeriod(loaded.next?.periodStartDate ?? null, today()));
        })
        .catch((reason: unknown) => reportError(reason, "payday plan")),
    ]).finally(() => setLoading(false));
  }, [token]);

  // After a balance update or a recorded transfer, recalculate both periods and the account totals.
  async function refreshPaydayPlan() {
    const [loaded, overview] = await Promise.all([loadPlans(token), api.overview(token)]);
    setPlans(loaded);
    setSummary(overview);
  }

  const discretionarySuggestions = (() => {
    let remaining = Math.max(0, paydayPlan?.discretionaryIncome ?? 0);
    const result: Array<{ potId: string; potName: string; shortfall: number; amount: number }> = [];
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
          <div className="flex flex-wrap gap-2">
            {paydayPlan?.paydayStartDate && (
              <Button
                variant="outline"
                onClick={() => {
                  const text = buildShareSummary({
                    income: paydayPlan.scheduledIncome,
                    expenditure: paydayPlan.scheduledExpenses,
                    discretionaryIncome: paydayPlan.discretionaryIncome,
                    leftToSpend: paydayPlan.leftToSpend,
                    paydayLabel: dateLabel(paydayPlan.paydayStartDate!),
                  });
                  window.open(whatsAppShareUrl(text), "_blank", "noopener,noreferrer");
                }}
              >
                Share on WhatsApp
              </Button>
            )}
            <Button variant="outline" onClick={() => onNavigate("activity")}>
              Adjust income & expenses
            </Button>
          </div>
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
              paydayPlan?.periodStartDate && paydayPlan.periodEndDate
                ? periodLabel(paydayPlan.mode, paydayPlan.periodStartDate, paydayPlan.periodEndDate)
                : "Add a recurring income schedule to calculate your payday plan."
            }
          >
            {(plans.current?.paydayStartDate || plans.next?.paydayStartDate) && (
              <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Pay period">
                {(["current", "next"] as const).map((option) => (
                  <Button
                    key={option}
                    variant={period === option ? "primary" : "outline"}
                    size="small"
                    aria-pressed={period === option}
                    onClick={() => setPeriod(option)}
                  >
                    {option === "current" ? "This pay period" : "Next pay period"}
                  </Button>
                ))}
              </div>
            )}
            {paydayPlan?.paydayStartDate ? (
              <div className="grid gap-5">
                <BalanceReminder
                  token={token}
                  accounts={paydayPlan.accounts}
                  onUpdated={refreshPaydayPlan}
                />
                <PrePaydayWarning shortfalls={paydayPlan.prePaydayShortfalls} />
                {paydayPlan.noPaydayYet && paydayPlan.periodEndDate ? (
                  <div className="grid justify-items-start gap-3 rounded-lg border p-3 text-sm">
                    <p>{noPaydayYetMessage(paydayPlan.periodEndDate)}</p>
                    <Button variant="outline" size="small" onClick={() => onNavigate("activity")}>
                      Adjust income & expenses
                    </Button>
                  </div>
                ) : (
                  <div className="grid gap-3 md:grid-cols-3">
                    <div className="rounded-lg bg-muted/60 p-3">
                      <span className="payday-summary-label text-xs text-muted-foreground">
                        Total income
                      </span>
                      <strong className="mt-1 block text-lg">
                        {money(paydayPlan.scheduledIncome)}
                      </strong>
                    </div>
                    <div className="rounded-lg bg-muted/60 p-3">
                      <span className="payday-summary-label text-xs text-muted-foreground">
                        Total expenditure
                      </span>
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
                )}
                {paydayPlan.transfers.length ? (
                  <div className="grid gap-3">
                    <h3 className="text-sm font-semibold">Recommended transfers</h3>
                    {transferGroups(paydayPlan.transfers).map((group) => (
                      <div className="grid gap-2" key={group.title}>
                        <h4 className="text-xs font-semibold uppercase text-muted-foreground">
                          {group.title}
                        </h4>
                        {group.transfers.map((transfer, index) => (
                          <TransferRow
                            key={`${transfer.fromAccountId}-${transfer.toAccountId}-${transfer.toPotId ?? ""}-${index}`}
                            token={token}
                            transfer={transfer}
                            onRecorded={refreshPaydayPlan}
                          />
                        ))}
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
                {paydayPlan.accounts.some(showsBreakdown) && (
                  <div className="grid gap-2">
                    <h3 className="text-sm font-semibold">
                      How each account’s transfer is worked out
                    </h3>
                    {paydayPlan.accounts.filter(showsBreakdown).map((account) => (
                      <AccountBreakdown key={account.accountId} account={account} />
                    ))}
                  </div>
                )}
                <MoneyLeftAfterTransfers
                  accounts={paydayPlan.accounts}
                  mode={paydayPlan.mode}
                  // Bills after payday are paid from the next payday's transfers.
                  until={paydayPlan.periodEndDate ?? ""}
                />
                {spareMoneyTip(paydayPlan.mode, paydayPlan.accounts) && (
                  <div className="grid justify-items-start gap-3 rounded-lg border p-3 text-sm">
                    <p>{spareMoneyTip(paydayPlan.mode, paydayPlan.accounts)}</p>
                    <Button variant="outline" size="small" onClick={() => onNavigate("accounts")}>
                      Choose accounts
                    </Button>
                  </div>
                )}
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
                {paydayPlan && !paydayPlan.noPaydayYet
                  ? money(paydayPlan.discretionaryIncome)
                  : "—"}
              </strong>
              <p>
                {paydayPlan?.noPaydayYet
                  ? "No payday has happened yet, so this pay period has no income. Switch to Next pay period, or set your salary plan’s start date to your last payday."
                  : "Total income less expenses due in this pay period."}
              </p>
              {paydayPlan?.leftToSpend !== undefined && !paydayPlan.noPaydayYet && (
                <p className="text-sm">
                  <strong>{money(paydayPlan.leftToSpend)} left to spend</strong> — discretionary
                  income minus everything you’ve spent so far this pay period, across all accounts.
                </p>
              )}
              {/* Pots saved for each payday already appear in the next pay period's transfers. */}
              {discretionarySuggestions.length > 0 &&
                !plans.next?.transfers.some((transfer) => transfer.kind === "pot") && (
                  <div className="grid gap-1 text-sm">
                    <strong>Suggested use of your discretionary income</strong>
                    {discretionarySuggestions.map((item) => (
                      <span key={item.potId}>
                        {money(item.amount)} to cover pot ‘{item.potName}’ ({money(item.shortfall)}{" "}
                        still to go)
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
            <Panel
              title="Goals to give a little attention"
              subtitle={`${summary?.potShortfalls.length ?? 0} ${summary?.potShortfalls.length === 1 ? "goal" : "goals"} need attention`}
              className="pot-shortfall-card"
            >
              {summary?.potShortfalls.length ? (
                <div className="pot-shortfall-list">
                  {summary.potShortfalls.map((pot, index) => {
                    const progress = Math.min((pot.savedAmount / pot.targetAmount) * 100, 100);
                    return (
                      <div className="pot-shortfall-row" key={pot.potId}>
                        <span className={`account-symbol symbol-${index % 3}`} aria-hidden="true">
                          ◎
                        </span>
                        <div className="pot-shortfall-details">
                          <div className="pot-shortfall-heading">
                            <strong>{pot.potName}</strong>
                            <span>{money(pot.shortfall)} to go</span>
                          </div>
                          <div
                            className="pot-shortfall-progress"
                            role="progressbar"
                            aria-label={`${pot.potName} goal progress`}
                            aria-valuemin={0}
                            aria-valuemax={pot.targetAmount}
                            aria-valuenow={Math.min(pot.savedAmount, pot.targetAmount)}
                          >
                            <span style={{ width: `${progress}%` }} />
                          </div>
                          <small>
                            {money(pot.savedAmount)} saved of {money(pot.targetAmount)}
                          </small>
                        </div>
                      </div>
                    );
                  })}
                  <Button
                    variant="link"
                    className="panel-footer-link"
                    onClick={() => onNavigate("pots")}
                  >
                    View pots <span>→</span>
                  </Button>
                </div>
              ) : (
                <EmptyState
                  title="All goals are on track"
                  text="There are no pot shortfalls to catch up on right now."
                />
              )}
            </Panel>
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
                        <small>Balance today</small>
                      </span>
                      <strong>{money(account.balance ?? account.availableForSpending)}</strong>
                    </div>
                  ))}
                  <div className="account-row">
                    <span className="account-symbol">Σ</span>
                    <span className="account-name">
                      Total
                      <small>
                        Across {summary.accounts.length}{" "}
                        {summary.accounts.length === 1 ? "account" : "accounts"}
                      </small>
                    </span>
                    <strong>
                      {money(
                        summary.accounts.reduce(
                          (sum, a) => sum + (a.balance ?? a.availableForSpending),
                          0,
                        ),
                      )}
                    </strong>
                  </div>
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
