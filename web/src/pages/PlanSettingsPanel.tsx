import { useEffect, useState, type FormEvent } from "react";
import { api, type Account, type AccountRole } from "../api";
import { parseSaveAmount, ROLE_OPTIONS } from "../lib/accountRoles";
import { Button, SelectField, TextField } from "../shared/Controls";
import { Panel } from "../shared/Ui";

/** Sets how the payday plan treats an account: everyday, spending, or savings with an amount to save. */
export function PlanSettingsPanel({
  token,
  account,
  onSaved,
}: {
  token: string;
  account: Account;
  onSaved: () => Promise<void>;
}) {
  const [role, setRole] = useState<AccountRole>(account.role);
  const [saveAmount, setSaveAmount] = useState(account.savePerPayday?.toString() ?? "");
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");

  useEffect(() => {
    setRole(account.role);
    setSaveAmount(account.savePerPayday?.toString() ?? "");
  }, [account.id, account.role, account.savePerPayday]);

  // A different account starts without the last one's "Saved." (a reload of this one keeps it).
  useEffect(() => setMessage(""), [account.id]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    setMessage("");
    try {
      await api.setAccountPlanSettings(
        token,
        account.id,
        role,
        role === "savings" ? parseSaveAmount(saveAmount) : null,
      );
      await onSaved();
      setMessage("Saved.");
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : "Unable to save.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Panel
      title="At payday"
      subtitle="Once bills are covered, savings and pots are paid first, then what’s left goes to your spending account"
    >
      <form className="form-grid" onSubmit={save}>
        <SelectField
          label="This account is for"
          options={ROLE_OPTIONS}
          value={role}
          onChange={(value) => setRole(value as AccountRole)}
        />
        {role === "savings" && (
          <TextField
            label="Save each payday (£)"
            type="number"
            step="0.01"
            min="0"
            value={saveAmount}
            onChange={setSaveAmount}
            placeholder="Leave blank to save 10% of your pay"
          />
        )}
        <Button variant="primary" disabled={saving}>
          Save <span>→</span>
        </Button>
        {message && <p className="text-sm text-muted-foreground">{message}</p>}
      </form>
    </Panel>
  );
}
