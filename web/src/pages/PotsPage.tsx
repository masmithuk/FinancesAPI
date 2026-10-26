import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, type Account, type Goal, type Pot, type PotContribution } from "../api";
import NameCreateForm from "../NameCreateForm";
import { Card } from "@/components/ui/card";
import { Button, SelectField, TextField } from "../shared/Controls";
import { dateLabel, money } from "../shared/format";
import { ErrorMessage, PageHeading } from "../shared/Ui";

type PotEdit = { id: string; sourceAccountId: string; accountId: string; name: string };
type GoalEdit = { potId: string; id: string; targetAmount: string; dueDate: string };
type ContributionEdit = {
  potId: string;
  id: string;
  accountId: string;
  amount: string;
  date: string;
};
type ContributionInput = { accountId: string; amount: string; date: string };

function today() {
  const date = new Date();
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 10);
}

export default function PotsPage({ token }: { token: string }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [accountId, setAccountId] = useState("");
  const [pots, setPots] = useState<Pot[]>([]);
  const [goals, setGoals] = useState<Record<string, Goal[]>>({});
  const [contributions, setContributions] = useState<Record<string, PotContribution[]>>({});
  const [potName, setPotName] = useState("");
  const [goalInputs, setGoalInputs] = useState<Record<string, { amount: string; dueDate: string }>>(
    {},
  );
  const [editingPot, setEditingPot] = useState<PotEdit | null>(null);
  const [editingGoal, setEditingGoal] = useState<GoalEdit | null>(null);
  const [contributionInputs, setContributionInputs] = useState<Record<string, ContributionInput>>(
    {},
  );
  const [editingContribution, setEditingContribution] = useState<ContributionEdit | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const loadPots = useCallback(
    async (id: string) => {
      if (!id) {
        setPots([]);
        setGoals({});
        setContributions({});
        return;
      }
      const result = await api.pots(token, id);
      setPots(result);
      const [goalEntries, contributionEntries] = await Promise.all([
        Promise.all(result.map(async (pot) => [pot.id, await api.goals(token, pot.id)] as const)),
        Promise.all(
          result.map(async (pot) => [pot.id, await api.contributions(token, pot.id)] as const),
        ),
      ]);
      setGoals(Object.fromEntries(goalEntries));
      setContributions(Object.fromEntries(contributionEntries));
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

  async function createContribution(event: FormEvent<HTMLFormElement>, potId: string) {
    event.preventDefault();
    const input = contributionInputs[potId];
    if (!input) return;
    setError("");
    try {
      await api.createContribution(
        token,
        potId,
        input.accountId || null,
        Number(input.amount),
        input.date,
      );
      setContributionInputs((current) => ({
        ...current,
        [potId]: { ...input, amount: "", date: today() },
      }));
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to add money to the pot.");
    }
  }

  async function saveContribution(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingContribution) return;
    setError("");
    try {
      await api.updateContribution(
        token,
        editingContribution.potId,
        editingContribution.id,
        editingContribution.accountId || null,
        Number(editingContribution.amount),
        editingContribution.date,
      );
      setEditingContribution(null);
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update contribution.");
    }
  }

  async function deleteContribution(potId: string, contribution: PotContribution) {
    if (
      !window.confirm(
        `Delete this ${money(contribution.amount)} contribution${
          contribution.accountName ? ` and its ${contribution.accountName} balance entry` : ""
        }?`,
      )
    ) {
      return;
    }
    setError("");
    try {
      await api.deleteContribution(token, potId, contribution.id);
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to delete contribution.");
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

  async function savePot(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingPot) return;
    setError("");
    try {
      await api.updatePot(
        token,
        editingPot.sourceAccountId,
        editingPot.id,
        editingPot.name.trim(),
        editingPot.accountId,
      );
      setEditingPot(null);
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update pot.");
    }
  }

  async function deletePot(pot: Pot) {
    if (
      !window.confirm(
        `Delete ${pot.name}, its savings goals, and its contribution balance entries? This cannot be undone.`,
      )
    ) {
      return;
    }
    setError("");
    try {
      await api.deletePot(token, accountId, pot.id);
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to delete pot.");
    }
  }

  async function saveGoal(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingGoal) return;
    setError("");
    try {
      await api.updateGoal(
        token,
        editingGoal.potId,
        editingGoal.id,
        Number(editingGoal.targetAmount),
        editingGoal.dueDate || null,
      );
      setEditingGoal(null);
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update goal.");
    }
  }

  async function deleteGoal(potId: string, goal: Goal) {
    if (!window.confirm("Delete this savings goal? This cannot be undone.")) return;
    setError("");
    try {
      await api.deleteGoal(token, potId, goal.id);
      await loadPots(accountId);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to delete goal.");
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
              contributions={contributions[pot.id] ?? []}
              index={index}
              input={goalInputs[pot.id] ?? { amount: "", dueDate: "" }}
              onInputChange={(value) =>
                setGoalInputs((current) => ({ ...current, [pot.id]: value }))
              }
              onCreateGoal={createGoal}
              contributionInput={
                contributionInputs[pot.id] ?? {
                  accountId: pot.accountId || accounts[0].id,
                  amount: "",
                  date: today(),
                }
              }
              onContributionInputChange={(value) =>
                setContributionInputs((current) => ({ ...current, [pot.id]: value }))
              }
              onCreateContribution={createContribution}
              editingContribution={
                editingContribution?.potId === pot.id ? editingContribution : null
              }
              onEditContribution={(contribution) =>
                setEditingContribution({
                  potId: pot.id,
                  id: contribution.id,
                  accountId: contribution.accountId ?? "",
                  amount: String(contribution.amount),
                  date: contribution.date.slice(0, 10),
                })
              }
              onContributionEditChange={setEditingContribution}
              onSaveContribution={saveContribution}
              onCancelContributionEdit={() => setEditingContribution(null)}
              onDeleteContribution={(contribution) => deleteContribution(pot.id, contribution)}
              accounts={accounts}
              editingPot={editingPot?.id === pot.id ? editingPot : null}
              onEditPot={() =>
                setEditingPot({
                  id: pot.id,
                  sourceAccountId: pot.accountId,
                  accountId: pot.accountId,
                  name: pot.name,
                })
              }
              onPotEditChange={setEditingPot}
              onSavePot={savePot}
              onCancelPotEdit={() => setEditingPot(null)}
              onDeletePot={() => deletePot(pot)}
              editingGoal={editingGoal?.potId === pot.id ? editingGoal : null}
              onEditGoal={(goal) =>
                setEditingGoal({
                  potId: pot.id,
                  id: goal.id,
                  targetAmount: String(goal.targetAmount),
                  dueDate: goal.dueDate?.slice(0, 10) ?? "",
                })
              }
              onGoalEditChange={setEditingGoal}
              onSaveGoal={saveGoal}
              onCancelGoalEdit={() => setEditingGoal(null)}
              onDeleteGoal={(goal) => deleteGoal(pot.id, goal)}
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
  contributions,
  index,
  input,
  onInputChange,
  onCreateGoal,
  contributionInput,
  onContributionInputChange,
  onCreateContribution,
  editingContribution,
  onEditContribution,
  onContributionEditChange,
  onSaveContribution,
  onCancelContributionEdit,
  onDeleteContribution,
  accounts,
  editingPot,
  onEditPot,
  onPotEditChange,
  onSavePot,
  onCancelPotEdit,
  onDeletePot,
  editingGoal,
  onEditGoal,
  onGoalEditChange,
  onSaveGoal,
  onCancelGoalEdit,
  onDeleteGoal,
}: {
  pot: Pot;
  goals: Goal[];
  contributions: PotContribution[];
  index: number;
  input: { amount: string; dueDate: string };
  onInputChange: (value: { amount: string; dueDate: string }) => void;
  onCreateGoal: (event: FormEvent<HTMLFormElement>, potId: string) => void;
  contributionInput: ContributionInput;
  onContributionInputChange: (value: ContributionInput) => void;
  onCreateContribution: (event: FormEvent<HTMLFormElement>, potId: string) => void;
  editingContribution: ContributionEdit | null;
  onEditContribution: (contribution: PotContribution) => void;
  onContributionEditChange: (value: ContributionEdit) => void;
  onSaveContribution: (event: FormEvent<HTMLFormElement>) => void;
  onCancelContributionEdit: () => void;
  onDeleteContribution: (contribution: PotContribution) => void;
  accounts: Account[];
  editingPot: PotEdit | null;
  onEditPot: () => void;
  onPotEditChange: (value: PotEdit) => void;
  onSavePot: (event: FormEvent<HTMLFormElement>) => void;
  onCancelPotEdit: () => void;
  onDeletePot: () => void;
  editingGoal: GoalEdit | null;
  onEditGoal: (goal: Goal) => void;
  onGoalEditChange: (value: GoalEdit) => void;
  onSaveGoal: (event: FormEvent<HTMLFormElement>) => void;
  onCancelGoalEdit: () => void;
  onDeleteGoal: (goal: Goal) => void;
}) {
  const totalContributed = contributions.reduce((sum, item) => sum + item.amount, 0);

  return (
    <Card role="article" className={`pot-card pot-tone-${index % 3}`}>
      <div className="pot-card-top">
        <span className="pot-symbol">◉</span>
        <span className="pot-kicker">YOUR POT</span>
        <span className="pot-menu">✳</span>
      </div>
      {editingPot ? (
        <form className="grid gap-3" onSubmit={onSavePot}>
          <TextField
            label="Pot name"
            required
            maxLength={100}
            value={editingPot.name}
            onChange={(name) => onPotEditChange({ ...editingPot, name })}
          />
          <SelectField
            label="Account"
            value={editingPot.accountId}
            onChange={(accountId) => onPotEditChange({ ...editingPot, accountId })}
            options={accounts.map((account) => ({ value: account.id, label: account.name }))}
          />
          <div className="flex gap-2">
            <Button variant="primary" size="small">
              Save pot
            </Button>
            <Button variant="outline" size="small" type="button" onClick={onCancelPotEdit}>
              Cancel
            </Button>
          </div>
        </form>
      ) : (
        <>
          <h2>{pot.name}</h2>
          <div className="flex gap-2">
            <Button variant="outline" size="small" onClick={onEditPot}>
              Edit pot
            </Button>
            <Button variant="danger" size="small" onClick={onDeletePot}>
              Delete pot
            </Button>
          </div>
        </>
      )}
      <p>
        {goals.length
          ? `${goals.length} ${goals.length === 1 ? "goal" : "goals"} in mind`
          : "A little space for what matters."}
      </p>
      <div className="grid gap-1 rounded-lg bg-muted/50 p-3">
        <span className="text-sm text-muted-foreground">Saved in this pot</span>
        <strong className="text-lg">{money(totalContributed)}</strong>
      </div>
      {goals.length > 0 && (
        <div className="goal-list">
          {goals.map((goal) => (
            <div className="goal-row" key={goal.id}>
              {editingGoal?.id === goal.id ? (
                <form className="grid w-full gap-3" onSubmit={onSaveGoal}>
                  <TextField
                    label="Target amount (£)"
                    type="number"
                    required
                    min="0.01"
                    step="0.01"
                    value={editingGoal.targetAmount}
                    onChange={(targetAmount) => onGoalEditChange({ ...editingGoal, targetAmount })}
                  />
                  <TextField
                    label="Target date"
                    type="date"
                    value={editingGoal.dueDate}
                    onChange={(dueDate) => onGoalEditChange({ ...editingGoal, dueDate })}
                  />
                  <div className="flex gap-2">
                    <Button variant="primary" size="small">
                      Save goal
                    </Button>
                    <Button variant="outline" size="small" type="button" onClick={onCancelGoalEdit}>
                      Cancel
                    </Button>
                  </div>
                </form>
              ) : (
                <div className="grid w-full gap-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span>Target {money(goal.targetAmount)}</span>
                    <strong>{goal.dueDate ? dateLabel(goal.dueDate) : "No target date"}</strong>
                  </div>
                  <div
                    role="progressbar"
                    aria-label={`Progress toward ${money(goal.targetAmount)} goal`}
                    aria-valuemin={0}
                    aria-valuemax={goal.targetAmount}
                    aria-valuenow={Math.min(totalContributed, goal.targetAmount)}
                    className="h-2 overflow-hidden rounded-full bg-muted"
                  >
                    <div
                      className="h-full rounded-full bg-primary transition-[width]"
                      style={{
                        width: `${Math.min((totalContributed / goal.targetAmount) * 100, 100)}%`,
                      }}
                    />
                  </div>
                  <span className="text-sm text-muted-foreground">
                    {money(totalContributed)} saved
                    {totalContributed > goal.targetAmount
                      ? ` · ${money(totalContributed - goal.targetAmount)} above target`
                      : ` of ${money(goal.targetAmount)}`}
                  </span>
                  <div className="flex gap-2">
                    <Button variant="outline" size="small" onClick={() => onEditGoal(goal)}>
                      Edit
                    </Button>
                    <Button variant="danger" size="small" onClick={() => onDeleteGoal(goal)}>
                      Delete
                    </Button>
                  </div>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
      <form
        className="grid gap-3 border-t pt-4"
        onSubmit={(event) => onCreateContribution(event, pot.id)}
      >
        <span className="text-sm font-semibold">Add money to this pot</span>
        <SelectField
          label="From account (optional)"
          value={contributionInput.accountId}
          onChange={(accountId) => onContributionInputChange({ ...contributionInput, accountId })}
          options={[
            { value: "", label: "Already saved (from nowhere)" },
            ...accounts.map((account) => ({ value: account.id, label: account.name })),
          ]}
        />
        <TextField
          label="Amount (£)"
          type="number"
          required
          min="0.01"
          step="0.01"
          value={contributionInput.amount}
          onChange={(amount) => onContributionInputChange({ ...contributionInput, amount })}
          placeholder="e.g. 50"
        />
        <TextField
          label="Date"
          type="date"
          required
          value={contributionInput.date}
          onChange={(date) => onContributionInputChange({ ...contributionInput, date })}
        />
        <Button variant="primary" size="small">
          Add contribution
        </Button>
      </form>
      {contributions.length > 0 && (
        <div className="grid gap-3 border-t pt-4">
          <h3 className="text-sm font-semibold">Contribution history</h3>
          {contributions.map((contribution) => (
            <div key={contribution.id} className="grid gap-2 rounded-lg border p-3">
              {editingContribution?.id === contribution.id ? (
                <form className="grid gap-3" onSubmit={onSaveContribution}>
                  <SelectField
                    label="From account (optional)"
                    value={editingContribution.accountId}
                    onChange={(accountId) =>
                      onContributionEditChange({ ...editingContribution, accountId })
                    }
                    options={[
                      { value: "", label: "Already saved (from nowhere)" },
                      ...accounts.map((account) => ({
                        value: account.id,
                        label: account.name,
                      })),
                    ]}
                  />
                  <TextField
                    label="Amount (£)"
                    type="number"
                    required
                    min="0.01"
                    step="0.01"
                    value={editingContribution.amount}
                    onChange={(amount) =>
                      onContributionEditChange({ ...editingContribution, amount })
                    }
                  />
                  <TextField
                    label="Date"
                    type="date"
                    required
                    value={editingContribution.date}
                    onChange={(date) => onContributionEditChange({ ...editingContribution, date })}
                  />
                  <div className="flex gap-2">
                    <Button variant="primary" size="small">
                      Save contribution
                    </Button>
                    <Button
                      variant="outline"
                      size="small"
                      type="button"
                      onClick={onCancelContributionEdit}
                    >
                      Cancel
                    </Button>
                  </div>
                </form>
              ) : (
                <>
                  <div className="flex flex-wrap justify-between gap-2">
                    <strong>{money(contribution.amount)}</strong>
                    <span className="text-sm text-muted-foreground">
                      {dateLabel(contribution.date)}
                    </span>
                  </div>
                  <span className="text-sm text-muted-foreground">
                    {contribution.accountName
                      ? `Debited from ${contribution.accountName}`
                      : "Added from nowhere (already saved)"}
                  </span>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="small"
                      onClick={() => onEditContribution(contribution)}
                    >
                      Edit
                    </Button>
                    <Button
                      variant="danger"
                      size="small"
                      onClick={() => onDeleteContribution(contribution)}
                    >
                      Delete
                    </Button>
                  </div>
                </>
              )}
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
