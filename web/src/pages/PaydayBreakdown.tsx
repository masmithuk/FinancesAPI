import { useState, type FormEvent } from "react";
import { api, type PaydayPlan } from "../api";
import {
  alreadyThereLabel,
  fundingNote,
  moneyLeftByAccount,
  moneyLeftCopy,
  needsBalanceCheck,
  transferDestination,
} from "../lib/paydayBreakdown";
import { Button, TextField } from "../shared/Controls";
import { dateLabel, money, today } from "../shared/format";

type PlanAccount = PaydayPlan["accounts"][number];

/** Asks the user to bring balances up to date from their bank before making any transfers. */
export function BalanceReminder({
  token,
  accounts,
  onUpdated,
}: {
  token: string;
  accounts: PlanAccount[];
  onUpdated: () => Promise<void>;
}) {
  const stale = accounts.filter((account) => needsBalanceCheck(account.balanceCheckedOn, today()));
  return (
    <div className="grid gap-2 rounded-lg border p-3 text-sm">
      <strong>Before you transfer, log into each bank and update the balance here.</strong>
      {stale.length === 0 ? (
        <p className="text-muted-foreground">All balances were checked today.</p>
      ) : (
        <>
          <p className="text-muted-foreground">
            Open each banking app, find the account’s current balance, and type it below. We’ll
            record the difference, so the plan uses the money that’s really there.
          </p>
          {stale.map((account) => (
            <BalanceCheckRow
              key={account.accountId}
              token={token}
              account={account}
              onUpdated={onUpdated}
            />
          ))}
        </>
      )}
    </div>
  );
}

function BalanceCheckRow({
  token,
  account,
  onUpdated,
}: {
  token: string;
  account: PlanAccount;
  onUpdated: () => Promise<void>;
}) {
  const [amount, setAmount] = useState("");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setSaving(true);
    try {
      await api.setActualBalance(token, account.accountId, Number(amount));
      setAmount("");
      await onUpdated();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to update the balance.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="flex flex-wrap items-end gap-3" onSubmit={submit}>
      <div className="min-w-40 flex-1">
        <span className="block font-medium">{account.accountName}</span>
        <span className="text-xs text-muted-foreground">
          {account.balanceCheckedOn
            ? `Last checked ${dateLabel(account.balanceCheckedOn)}`
            : "Not checked yet"}
        </span>
      </div>
      <TextField
        label="Bank balance (£)"
        type="number"
        step="0.01"
        required
        value={amount}
        onChange={setAmount}
        placeholder="0.00"
      />
      <Button variant="outline" size="small" disabled={saving}>
        Update
      </Button>
      {error && <p className="w-full text-destructive">{error}</p>}
    </form>
  );
}

/** One account's sum: bills due, less income paid in and money already there, gives what to send. */
export function AccountBreakdown({ account }: { account: PlanAccount }) {
  const note = fundingNote(account);
  return (
    <div className="grid gap-1 border-b py-2 text-sm last:border-0">
      <strong>{account.accountName}</strong>
      <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4">
        <span>Bills due</span>
        <span className="text-right">{money(account.expensesDue)}</span>
        <span>Income in</span>
        <span className="text-right">{money(account.incomeIn)}</span>
        <span>Already there</span>
        <span className="text-right">{alreadyThereLabel(account.fromExisting)}</span>
        {account.overdraftToCover > 0 && (
          <>
            <span>Overdrawn before payday by</span>
            <span className="text-right">{money(account.overdraftToCover)}</span>
          </>
        )}
        <strong>Send</strong>
        <strong className="text-right">{money(account.toSend)}</strong>
      </div>
      {note && <p className="text-xs text-muted-foreground">ⓘ {note}</p>}
    </div>
  );
}

/** A recommended transfer, with a button to record it once it has been made at the bank. */
export function TransferRow({
  token,
  transfer,
  onRecorded,
}: {
  token: string;
  transfer: PaydayPlan["transfers"][number];
  onRecorded: () => Promise<void>;
}) {
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  async function markDone() {
    // A second click while saving would record the transfer twice.
    if (saving) return;
    setError("");
    setSaving(true);
    try {
      if (transfer.toPotId) {
        // Money into a pot leaves the account it is paid from.
        await api.createContribution(
          token,
          transfer.toPotId,
          transfer.fromAccountId,
          transfer.amount,
          today(),
        );
      } else {
        await api.recordTransfer(
          token,
          transfer.fromAccountId,
          transfer.toAccountId,
          transfer.amount,
        );
      }
      await onRecorded();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to record the transfer.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3">
      <span>
        <strong>{transfer.fromAccountName}</strong>
        <span className="text-muted-foreground"> → </span>
        <strong>{transferDestination(transfer)}</strong>
      </span>
      <span className="flex items-center gap-3">
        <strong className="text-base">{money(transfer.amount)}</strong>
        <Button variant="outline" size="small" disabled={saving} onClick={markDone}>
          Mark as done
        </Button>
      </span>
      {error && <p className="w-full text-sm text-destructive">{error}</p>}
    </div>
  );
}

/** Where the spare (discretionary) money sits once bills are paid and transfers are made. */
export function MoneyLeftAfterTransfers({
  accounts,
  mode,
  until,
}: {
  accounts: PlanAccount[];
  mode: PaydayPlan["mode"];
  /** The date the money is counted up to, as `YYYY-MM-DD`. */
  until: string;
}) {
  const holdings = moneyLeftByAccount(accounts);
  const copy = moneyLeftCopy(mode, until);
  return (
    <div className="grid gap-2">
      <div>
        <h3 className="text-sm font-semibold">{copy.title}</h3>
        {holdings.length > 0 && <p className="text-xs text-muted-foreground">{copy.note}</p>}
      </div>
      {holdings.length === 0 ? (
        <p className="text-sm text-muted-foreground">{copy.empty}</p>
      ) : (
        holdings.map((holding) => (
          <div
            className="flex justify-between gap-3 border-b py-1 text-sm last:border-0"
            key={holding.accountId}
          >
            <span>{holding.accountName}</span>
            <strong>{money(holding.amount)}</strong>
          </div>
        ))
      )}
    </div>
  );
}

/** Accounts projected to go below zero before payday arrives. */
export function PrePaydayWarning({
  shortfalls,
}: {
  shortfalls: PaydayPlan["prePaydayShortfalls"];
}) {
  if (shortfalls.length === 0) return null;
  return (
    <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm">
      <strong>Some accounts run short before payday.</strong>
      {shortfalls.map((shortfall) => (
        <p key={shortfall.accountId}>
          {shortfall.accountName}: drops to {money(shortfall.lowestBalance)} on{" "}
          {dateLabel(shortfall.date)} before payday
        </p>
      ))}
    </div>
  );
}
