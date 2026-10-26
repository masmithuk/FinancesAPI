import { useEffect, useState } from "react";
import { api, type FinancialOverview, type SetupStatus } from "../api";
import { money } from "../shared/format";
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
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([api.overview(token), api.setup(token)])
      .then(([overview, status]) => {
        setSummary(overview);
        setSetup(status);
      })
      .catch((reason: unknown) =>
        setError(reason instanceof Error ? reason.message : "Unable to load your overview."),
      )
      .finally(() => setLoading(false));
  }, [token]);

  return (
    <>
      <PageHeading
        eyebrow={new Intl.DateTimeFormat("en-GB", {
          weekday: "long",
          day: "numeric",
          month: "long",
        }).format(new Date())}
        title="Your money, in focus."
        subtitle="Here’s a thoughtful look at your finances today."
        action={
          <Button variant="dark" onClick={() => onNavigate("accounts")}>
            ＋ Add account
          </Button>
        }
      />
      <ErrorMessage message={error} />
      {loading ? (
        <div className="loading-state">Gathering your financial picture…</div>
      ) : (
        <>
          <section className="overview-grid">
            <div className="hero-card">
              <div className="hero-top">
                <span className="hero-label">SAFE TO SPEND</span>
                <span className="hero-orbit">✳</span>
              </div>
              <strong className="hero-amount">{money(summary?.safeToSpend ?? 0)}</strong>
              <p>Your lowest projected balance through the end of this month.</p>
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
          </section>
          {!!summary?.recommendations.length && (
            <Panel
              title="Gentle nudges"
              subtitle="Informational suggestions based on your current plan"
            >
              <div className="recommendations">
                {summary.recommendations.map((item, index) => (
                  <div className="recommendation" key={`${item.toPotId}-${index}`}>
                    <span className="recommendation-icon">✳</span>
                    <div>
                      <strong>{item.explanation}</strong>
                      <small>Suggested amount · {money(item.amount)}</small>
                    </div>
                    <Button
                      variant="link"
                      className="subtle-link"
                      onClick={() => onNavigate("pots")}
                    >
                      View pot →
                    </Button>
                  </div>
                ))}
              </div>
            </Panel>
          )}
        </>
      )}
    </>
  );
}
