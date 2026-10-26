import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, type Account, type Goal, type Pot } from "../api";
import NameCreateForm from "../NameCreateForm";
import { Card } from "@/components/ui/card";
import { Button, SelectField, TextField } from "../shared/Controls";
import { dateLabel, money } from "../shared/format";
import { ErrorMessage, PageHeading } from "../shared/Ui";

export default function PotsPage({ token }: { token: string }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [accountId, setAccountId] = useState("");
  const [pots, setPots] = useState<Pot[]>([]);
  const [goals, setGoals] = useState<Record<string, Goal[]>>({});
  const [potName, setPotName] = useState("");
  const [goalInputs, setGoalInputs] = useState<Record<string, { amount: string; dueDate: string }>>(
    {},
  );
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const loadPots = useCallback(
    async (id: string) => {
      if (!id) {
        setPots([]);
        setGoals({});
        return;
      }
      const result = await api.pots(token, id);
      setPots(result);
      const goalEntries = await Promise.all(
        result.map(async (pot) => [pot.id, await api.goals(token, pot.id)] as const),
      );
      setGoals(Object.fromEntries(goalEntries));
    },
    [token],
  );

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
    loadPots(accountId).catch((reason: unknown) =>
      setError(reason instanceof Error ? reason.message : "Unable to load pots."),
    );
  }, [accountId, loadPots]);

  async function createPot(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    try {
      await api.createPot(token, accountId, potName.trim());
      setPotName("");
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to create pot.");
    }
  }

  async function createGoal(event: FormEvent<HTMLFormElement>, potId: string) {
    event.preventDefault();
    const input = goalInputs[potId];
    if (!input) return;
    setError("");
    try {
      await api.createGoal(token, potId, Number(input.amount), input.dueDate || null);
      setGoalInputs((current) => ({ ...current, [potId]: { amount: "", dueDate: "" } }));
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to create goal.");
    }
  }

  return (
    <>
      <PageHeading
        eyebrow="PLAN FOR WHAT MATTERS"
        title="Pots & goals"
        subtitle="Give your savings a name and something to look forward to."
      />
      <ErrorMessage message={error} />
      {accounts.length > 0 && (
        <div className="toolbar">
          <SelectField
            id="pot-account"
            label="Account"
            value={accountId}
            onChange={setAccountId}
            options={accounts.map((account) => ({ value: account.id, label: account.name }))}
          />
        </div>
      )}
      {loading ? (
        <div className="loading-state">Loading pots…</div>
      ) : accounts.length ? (
        <div className="pot-grid">
          <Card className="create-pot-card">
            <span className="create-pot-mark">＋</span>
            <h2>Create a pot</h2>
            <p>Set aside a little space for something that matters to you.</p>
            <NameCreateForm
              id="pot-name"
              label="Pot name"
              value={potName}
              onChange={setPotName}
              placeholder="e.g. Weekend away"
              submitLabel="Create pot"
              onSubmit={createPot}
              variant="card"
            />
          </Card>
          {pots.map((pot, index) => (
            <PotCard
              key={pot.id}
              pot={pot}
              goals={goals[pot.id] ?? []}
              index={index}
              input={goalInputs[pot.id] ?? { amount: "", dueDate: "" }}
              onInputChange={(value) =>
                setGoalInputs((current) => ({ ...current, [pot.id]: value }))
              }
              onCreateGoal={createGoal}
            />
          ))}
          {!pots.length && (
            <div className="empty-pot-hint">
              Your first pot can be for anything — a rainy day, a project, or a little treat.
            </div>
          )}
        </div>
      ) : (
        <Card className="welcome-card">
          <span className="welcome-flower">◉</span>
          <h2>Choose an account first.</h2>
          <p>Create an account before setting up pots and savings goals.</p>
        </Card>
      )}
    </>
  );
}

function PotCard({
  pot,
  goals,
  index,
  input,
  onInputChange,
  onCreateGoal,
}: {
  pot: Pot;
  goals: Goal[];
  index: number;
  input: { amount: string; dueDate: string };
  onInputChange: (value: { amount: string; dueDate: string }) => void;
  onCreateGoal: (event: FormEvent<HTMLFormElement>, potId: string) => void;
}) {
  return (
    <Card role="article" className={`pot-card pot-tone-${index % 3}`}>
      <div className="pot-card-top">
        <span className="pot-symbol">◉</span>
        <span className="pot-kicker">YOUR POT</span>
        <span className="pot-menu">✳</span>
      </div>
      <h2>{pot.name}</h2>
      <p>
        {goals.length
          ? `${goals.length} ${goals.length === 1 ? "goal" : "goals"} in mind`
          : "A little space for what matters."}
      </p>
      {goals.length > 0 && (
        <div className="goal-list">
          {goals.map((goal) => (
            <div className="goal-row" key={goal.id}>
              <span>Target {money(goal.targetAmount)}</span>
              <strong>{goal.dueDate ? dateLabel(goal.dueDate) : "No target date"}</strong>
            </div>
          ))}
        </div>
      )}
      <form className="goal-form" onSubmit={(event) => onCreateGoal(event, pot.id)}>
        <span className="goal-form-title">
          {goals.length ? "Add another goal" : "Set a savings goal"}
        </span>
        <TextField
          label="Target amount (£)"
          type="number"
          required
          min="0.01"
          step="0.01"
          value={input.amount}
          onChange={(amount) => onInputChange({ ...input, amount })}
          placeholder="e.g. 500"
        />
        <TextField
          label="Target date"
          type="date"
          value={input.dueDate}
          onChange={(dueDate) => onInputChange({ ...input, dueDate })}
        />
        <Button variant="outline" size="small">
          Add goal <span>→</span>
        </Button>
      </form>
    </Card>
  );
}
