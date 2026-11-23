# Upcoming Payday Transfers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Before payday, show the transfers to make on payday for the next pay period, with each account's sum shown and a prompt to update balances from the bank first.

**Architecture:** `PaydayPlanService` gains an "upcoming" mode and a per-account breakdown, so the server stays the single source of truth. A new `POST /api/accounts/{id}/balances/actual` endpoint records the difference to the bank's figure and stamps `Account.BalanceCheckedOn`. The Overview panel only displays what the server returns, with small pure helpers in `web/src/lib/paydayBreakdown.ts` for the note text and the "needs checking" rule.

**Tech Stack:** .NET 10 Minimal API, EF Core + SQLite, xUnit integration tests (`FinanceApiFactory`); React + TypeScript (Vite+), Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-upcoming-payday-transfers-design.md`

## Global Constraints

- Upcoming mode applies when the next payday window starts **within 7 days** of today (`0 < nextStart − today ≤ 7`) and today is not inside a payday window.
- `toSend = max(0, billsDue − incomeIn − expectedBalance)`.
- `fromExisting = min(max(0, expectedBalance), max(0, billsDue − incomeIn))`.
- Surplus for transfer sources = `max(0, incomeIn + expectedBalance − billsDue)`.
- Bank-balance entries use the note `"Bank balance update"`.
- The Overview note text is exactly: `£{fromExisting} already in this account was counted. If you take it out first, send £{toSend + fromExisting}.`
- Reminder copy is exactly: `Before you transfer, log into each bank and update the balance here.`
- Every new response field is additive. Existing fields (`expensesDue`, `unfundedShortfall`, the totals, `transfers`) keep their names and meaning, so the current tests in `PaydayPlanTests` must still pass unchanged.
- Recommendations stay informational. Nothing moves money.

## Review Focus

- **No past payday in the data (a brand-new user whose salary starts next week).** It should plan from that first payday, as the current tests expect. Pinned by the existing `PaydayPlanTests` staying green (Task 2, Step 4).
- **A bill that falls between today and payday.** It must reduce `expectedBalance` and must not count again in `billsDue`. Test: `Upcoming_Projects_Balance_To_Payday` (Task 2).
- **An account already overdrawn, or projected overdrawn, on payday.** `toSend` grows by the overdraft, `fromExisting` is 0, and it appears in `prePaydayShortfalls`. Test: `Upcoming_Overdrawn_Account_Adds_Overdraft_And_Reports_Shortfall` (Task 2).
- **Typing the same bank balance twice, or one that already matches.** No new balance entry, but `BalanceCheckedOn` still updates. Test: `ActualBalance_Matching_Figure_Adds_No_Entry_But_Stamps_Check` (Task 1).
- **Another user's account id sent to the bank-balance endpoint.** It must return 404 and change nothing. Test: `ActualBalance_Other_Users_Account_Returns_NotFound` (Task 1).

---

### Task 1: "Update to bank balance" endpoint and `BalanceCheckedOn`

**Files:**
- Modify: `Finance API/Persistence/Entities/Account.cs` (add `public DateTime? BalanceCheckedOn { get; set; }`)
- Create: migration `AddAccountBalanceCheckedOn` under `Finance API/Persistence/Migrations/` (via the dotnet-ef command in README)
- Modify: `Finance API/Endpoints/AccountsEndpoints.cs` (route, handler, records)
- Test: `Finance API.Tests/AccountsTests.cs`

**Interfaces:**
- Produces: `POST /api/accounts/{id}/balances/actual`, body `SetActualBalanceRequest(decimal Amount, DateTime? Date = null)` with the same `[Range]` as `AddBalanceRequest`. Returns 200 `ActualBalanceResponse(decimal Difference, Guid? BalanceId, DateTime BalanceCheckedOn)`. Returns 404 when the account isn't owned, and 400 when `Date` is after today (UTC).
- Produces: `Account.BalanceCheckedOn` (date only), read by Task 2.

- [ ] **Step 1: Write the failing tests** in `AccountsTests.cs`, using that file's existing client and registration helpers:
  - `ActualBalance_Records_Difference_So_Total_Matches_Bank`: add balances 100 and 20 dated yesterday; POST `{ amount: 95 }`. Assert `difference == -25` and `balanceId` is not null. `GET /api/accounts/{id}/balances` sums to 95. The new entry's note is `"Bank balance update"` and its date is today.
  - `ActualBalance_Matching_Figure_Adds_No_Entry_But_Stamps_Check`: balance 50; POST `{ amount: 50 }`. Assert `difference == 0`, `balanceId` is null, the balance count is unchanged and `balanceCheckedOn` is today.
  - `ActualBalance_Handles_Negative_Bank_Figure`: no balances; POST `{ amount: -40 }` (a credit card). Assert `difference == -40` and the sum is -40.
  - `ActualBalance_Other_Users_Account_Returns_NotFound`: user B posts to user A's account and gets 404. A's balances are unchanged.
  - `ActualBalance_Future_Date_Returns_BadRequest`: `date = today + 1` returns 400.
- [ ] **Step 2: Run** `dotnet test "Finance API.Tests\Finance API.Tests.csproj" --filter ActualBalance`. Expected: FAIL (404, because the route doesn't exist).
- [ ] **Step 3: Add the column and migration.** Run `dotnet tool run dotnet-ef migrations add AddAccountBalanceCheckedOn --project "Finance API\Finance API.csproj" --startup-project "Finance API\Finance API.csproj" --output-dir Persistence\Migrations`.
- [ ] **Step 4: Implement `SetActualBalanceAsync(Guid id, SetActualBalanceRequest request, FinanceDbContext db, ClaimsPrincipal user, IUserContextService userContext)`.** `date = (request.Date ?? DateTime.UtcNow).Date`. `difference = request.Amount − sum(Balances where AccountId == id && Date.Date <= date)`. Add a `Balance` only when `difference != 0`. Always set `account.BalanceCheckedOn = date`. Do it in one `SaveChangesAsync`. Register it with `.RequireAuthorization().WithName("SetActualBalance").WithTags("Accounts")`.
- [ ] **Step 5: Run** the whole test project. Expected: all PASS.
- [ ] **Step 6: Commit** `feat: record bank balance as a difference and stamp balance check date`.

### Task 2: Upcoming mode and per-account breakdown in `PaydayPlanService`

**Files:**
- Modify: `Finance API/Services/PaydayPlanService.cs`
- Test: `Finance API.Tests/PaydayPlanTests.cs`

**Interfaces:**
- Consumes: `Account.BalanceCheckedOn` (Task 1).
- Produces these response shapes (JSON camelCase), which Task 3 relies on:
  - `PaydayPlanResponse` adds `string Mode = "current"` (`"current" | "upcoming"`), `DateTime? PeriodStartDate = null`, `DateTime? PeriodEndDate = null` (exclusive: the following payday) and `IReadOnlyList<PrePaydayShortfallResponse>? PrePaydayShortfalls = null` (empty list when none).
  - `AccountFundingResponse` adds `decimal IncomeIn = 0m`, `decimal ExpectedBalance = 0m`, `decimal ToSend = 0m`, `decimal FromExisting = 0m` and `DateTime? BalanceCheckedOn = null`. `ExpensesDue` keeps its role as "bills due".
  - New `PrePaydayShortfallResponse(Guid AccountId, string AccountName, decimal LowestBalance, DateTime Date)`.

Mode selection (it replaces the `payWindow`/`nextPayday` choice):

```
current = last window with Start <= today
next    = first window with Start > today
inside  = current != null && today <= current.End
if (!inside && next != null && (next.Start - today).Days <= 7):
    mode = upcoming; target = next; targetEnd = window after next (EmptyPlan if none)
    expectedBalance[a] = balance today + sum(events for a with today <= Date < next.Start)
    prePaydayShortfalls = accounts whose running balance (balance today, then events in date order,
                          today <= Date < next.Start) goes below 0; report the minimum and the first date it is reached
else:
    mode = current; behaviour as today (including the existing fallback to the first future window when
    there is no past one); expectedBalance[a] = balance today
billsDue/incomeIn = events with target.Start (or today in current mode, as now) <= Date < targetEnd
```

The period totals (`ScheduledIncome`, `ScheduledExpenses`, `DiscretionaryIncome`, `LeftToSpend`) use the target period: `periodStart = target.Start`, `planEnd = targetEnd`. Transfers keep the existing greedy loop but take `transferNeeded = ToSend` and the surplus from Global Constraints. `UnfundedShortfall` stays what the loop leaves unfunded.

- [ ] **Step 1: Write the failing tests** in `PaydayPlanTests.cs`, following the existing seeding style. Each one seeds a *past* salary so a current window exists: a £1,000 salary into an Income account, monthly from `today.AddMonths(-1).AddDays(3)`, so the next payday is `today + 3`. The occurrence query and `BuildEvents` must now run to `targetEnd`, not the old `planEnd`.
  - `Upcoming_Mode_Plans_Next_Pay_Period`: Bills account, rent 300 monthly on `today + 10`, Bills balance 0, Income balance 0. Assert `mode == "upcoming"`, `periodStartDate == today+3`, `periodEndDate == today+3 + 1 month`, Bills `expensesDue == 300`, `toSend == 300` and one transfer Income→Bills of 300.
  - `Upcoming_Counts_Money_Already_In_Account`: as above, with Bills balance 100 dated today. Assert Bills `expectedBalance == 100`, `fromExisting == 100`, `toSend == 200` and a transfer of 200.
  - `Upcoming_Projects_Balance_To_Payday`: Bills balance 150, plus a one-off £50 expense in Bills on `today + 1` (an unreconciled `Occurrence`) and rent 300 on `today + 10`. Assert `expectedBalance == 100`, `expensesDue == 300` (the £50 isn't counted again) and `toSend == 200`.
  - `Upcoming_Overdrawn_Account_Adds_Overdraft_And_Reports_Shortfall`: Bills balance 20, a £70 expense on `today + 1` and rent 300 on `today + 10`. Assert `expectedBalance == -50`, `fromExisting == 0`, `toSend == 350`, and one `prePaydayShortfalls` entry for Bills with `lowestBalance == -50` and `date == today+1`.
  - `Current_Mode_Far_From_Payday_Is_Unchanged`: salary monthly from `today.AddDays(-2)`, so the next payday is about 28 days away. Assert `mode == "current"`, `expectedBalance` equals today's balance and `prePaydayShortfalls` is empty.
  - `Breakdown_Includes_BalanceCheckedOn`: set `BalanceCheckedOn = today` on Bills and assert it appears on that account.
- [ ] **Step 2: Run** `dotnet test "Finance API.Tests\Finance API.Tests.csproj" --filter PaydayPlan`. Expected: the new tests FAIL (missing properties).
- [ ] **Step 3: Implement** the mode selection, breakdown, shortfalls and response fields described above in `GetPlanAsync`. Extract `ComputeRunningMinimum(IEnumerable<CashFlowEvent>, decimal start) -> (decimal Lowest, DateTime? Date)` as a private static helper.
- [ ] **Step 4: Run** the whole test project. Expected: all PASS, including the three existing `PaydayPlan_*` tests unchanged.
- [ ] **Step 5: Commit** `feat: plan upcoming payday transfers with per-account breakdown`.

### Task 3: Overview panel, balance reminder and Accounts page field

**Files:**
- Create: `web/src/lib/paydayBreakdown.ts`, `web/src/lib/paydayBreakdown.test.ts`
- Modify: `web/src/api.ts` (the `PaydayPlan` type at around line 118, plus a new `setActualBalance`)
- Modify: `web/src/pages/OverviewPage.tsx` (the "Transfers to make" panel)
- Modify: `web/src/pages/AccountsPage.tsx` (an "Update to bank balance" panel next to "Add a balance")
- Modify: `web/README.md` (add `src/lib/paydayBreakdown.ts` to the test table)

**Interfaces:**
- Consumes: the Task 2 response fields and the Task 1 endpoint.
- Produces: `fundingNote(account: { fromExisting: number; toSend: number }): string | null`, which returns null when `fromExisting <= 0`, and `needsBalanceCheck(balanceCheckedOn: string | null, today: string): boolean`, which is true when the date is null or differs from `today` (an ISO `yyyy-mm-dd` from `shared/format.ts` `today()`). Also `api.setActualBalance(token: string, accountId: string, amount: number, date?: string): Promise<{ difference: number; balanceId: string | null; balanceCheckedOn: string }>`.

- [ ] **Step 1: Write the failing tests** in `paydayBreakdown.test.ts`:
  - `fundingNote({ fromExisting: 100, toSend: 100 })` returns `"£100.00 already in this account was counted. If you take it out first, send £200.00."` (format amounts with the existing `money()` from `shared/format.ts`).
  - `fundingNote({ fromExisting: 0, toSend: 300 })` returns `null`.
  - `needsBalanceCheck(null, "2026-11-23")` returns `true`; `needsBalanceCheck("2026-11-23T00:00:00", "2026-11-23")` returns `false`; `needsBalanceCheck("2026-11-20T00:00:00", "2026-11-23")` returns `true`.
  - In `api.test.ts`: `setActualBalance` POSTs to `/api/accounts/{id}/balances/actual` with body `{ amount, date }`, following that file's existing request-body tests.
- [ ] **Step 2: Run** `npm test` in `web/`. Expected: FAIL (module not found).
- [ ] **Step 3: Implement** the helpers and the `api.ts` additions. Extend `PaydayPlan` with `mode`, `periodStartDate`, `periodEndDate`, `prePaydayShortfalls`, and per-account `incomeIn`, `expectedBalance`, `toSend`, `fromExisting`, `balanceCheckedOn`.
- [ ] **Step 4: Update `OverviewPage.tsx`.** Change the subtitle to `Payday {periodStart} → {periodEnd}` plus an "Upcoming" or "This pay period" badge. Under it, add the reminder line (exact copy from Global Constraints) and a list of accounts where `needsBalanceCheck` is true, each with an amount input and an "Update" button that calls `setActualBalance` and then reloads the plan. Replace the "Expenses by account" grid with one row per account where `expensesDue > 0`, showing Bills due, Income in, Already there (`−fromExisting`), Send (`toSend`) and the `fundingNote` beneath it when it isn't null. Keep "Recommended transfers" and the unfunded warning. When `prePaydayShortfalls` isn't empty, add a destructive-styled warning: `{account}: drops to {money(lowestBalance)} on {dateLabel(date)} before payday`.
- [ ] **Step 5: Update `AccountsPage.tsx`.** Add a Panel titled "Update to bank balance" with the subtitle "Type the balance your bank shows today; we'll record the difference". Give it an Amount (£) field that calls `setActualBalance` and then `loadBalances(selectedId, true)`.
- [ ] **Step 6: Run** `npm run check`, `npm test` and `npm run build` in `web/`. Expected: all pass.
- [ ] **Step 7: Check it manually.** Start the API (HTTP profile) and `npm run dev`. Using a test account, seed a salary whose next date is within 7 days and a bill after it. Confirm the Overview shows Upcoming mode, the breakdown, the note and the reminder, and that updating a balance clears its flag and changes "Already there".
- [ ] **Step 8: Commit** `feat: show upcoming payday breakdown and bank balance reminder`.
