export interface Account {
  id: string;
  name: string;
}

export interface Balance {
  id: string;
  date: string;
  amount: number;
  note?: string | null;
}

export interface BalanceHistoryPage {
  items: Balance[];
  total: number;
}

export interface Pot {
  id: string;
  name: string;
  accountId: string;
}

export interface Goal {
  id: string;
  targetAmount: number;
  dueDate: string | null;
}

export interface PotContribution {
  id: string;
  accountId: string | null;
  accountName: string | null;
  amount: number;
  date: string;
}

export interface Schedule {
  id: string;
  name: string;
  amount: number;
  startDate: string;
  frequency: number;
  isIncome: boolean;
}

export interface ScheduleUpdate {
  accountId: string;
  name: string;
  amount: number;
  startDate: string;
  frequency: number;
  isIncome: boolean;
}

export interface Occurrence {
  id: string;
  date: string;
  amount: number;
  isIncome: boolean;
  isReconciled: boolean;
  externalReference: string | null;
  description?: string | null;
  scheduledItemId?: string | null;
  planDate?: string | null;
}

export interface OccurrencePage {
  items: Occurrence[];
  total: number;
}

export interface ImportResult {
  imported: number;
  duplicates: number;
  skipped: number;
}

export interface OccurrenceUpdate {
  accountId: string;
  date: string;
  amount: number;
  isIncome: boolean;
  externalReference: string | null;
}

export interface FinancialOverview {
  accounts: Array<{
    accountId: string;
    name: string;
    availableForSpending: number;
    balance?: number;
  }>;
  potShortfalls: Array<{
    potId: string;
    potName: string;
    shortfall: number;
  }>;
  safeToSpend: number;
  recommendations: Array<{
    fromAccountId: string;
    toAccountId: string;
    toPotId: string;
    amount: number;
    explanation: string;
  }>;
}

export interface SetupStatus {
  hasAccount: boolean;
  hasBalance: boolean;
  hasPotIfGoals: boolean;
  missing: string[];
}

export interface PaydayPlan {
  paydayStartDate: string | null;
  paydayWindowEndDate: string | null;
  nextPaydayDate: string | null;
  scheduledIncome: number;
  scheduledExpenses: number;
  discretionaryIncome: number;
  currentBalance?: number;
  leftToSpend?: number;
  accounts: Array<{
    accountId: string;
    accountName: string;
    expensesDue: number;
    unfundedShortfall: number;
    receivesPaydayIncome: boolean;
  }>;
  transfers: Array<{
    fromAccountId: string;
    fromAccountName: string;
    toAccountId: string;
    toAccountName: string;
    amount: number;
  }>;
}

export interface Forecast {
  from: string;
  to: string;
  safeToSpend: number;
  safeToSpendDate: string;
  points: Array<{ date: string; cumulative: number }>;
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, token?: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch {
    throw new ApiError(
      "We can't reach the server right now. Check your connection and try again shortly.",
      0,
    );
  }

  let text: string;
  try {
    text = await response.text();
  } catch {
    throw new ApiError("The connection was interrupted. Please try again.", 0);
  }

  let payload: unknown;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      if (response.ok)
        throw new ApiError("The server sent an unexpected response.", response.status);
    }
  }

  if (!response.ok) {
    if (response.status >= 500) {
      throw new ApiError(
        response.status === 502 || response.status === 503 || response.status === 504
          ? "The service is temporarily unavailable. Please try again shortly."
          : "Something went wrong on our side. Please try again.",
        response.status,
      );
    }

    const details =
      typeof payload === "object" && payload !== null ? (payload as Record<string, unknown>) : {};
    const errors = Array.isArray(details.errors)
      ? details.errors.filter((item): item is string => typeof item === "string")
      : [];
    const message =
      errors.join(" ") ||
      (typeof details.detail === "string" ? details.detail : undefined) ||
      (typeof details.title === "string" ? details.title : undefined) ||
      "The request could not be completed.";
    throw new ApiError(message, response.status);
  }

  return payload as T;
}

function post<T>(path: string, token: string | undefined, body: unknown) {
  return request<T>(path, token, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export const api = {
  login: (userName: string, password: string) =>
    post<{ token: string; expiresUtc: string }>("/api/auth/login", undefined, {
      userName,
      password,
    }),
  register: (userName: string, email: string, password: string) =>
    post<{ id: string; userName: string; email: string }>("/api/auth/register", undefined, {
      userName,
      email,
      password,
    }),
  me: (token: string) => request<{ userId: string; userName: string | null }>("/api/me", token),
  accounts: (token: string) => request<Account[]>("/api/accounts", token),
  createAccount: (token: string, name: string) => post<Account>("/api/accounts", token, { name }),
  balances: (token: string, accountId: string) =>
    request<Balance[]>(`/api/accounts/${accountId}/balances`, token),
  balanceHistory: (token: string, accountId: string, skip: number, take: number) =>
    request<BalanceHistoryPage>(
      `/api/accounts/${accountId}/balances/history?skip=${skip}&take=${take}`,
      token,
    ),
  runningBalances: (token: string, accountId: string) =>
    request<Array<{ date: string; cumulativeAmount: number; change?: number; entries?: number }>>(
      `/api/accounts/${accountId}/running-balances`,
      token,
    ),
  createBalance: (token: string, accountId: string, date: string, amount: number, note?: string) =>
    post<Balance>(`/api/accounts/${accountId}/balances`, token, {
      date,
      amount,
      note,
    }),
  updateBalance: (
    token: string,
    accountId: string,
    balanceId: string,
    date: string,
    amount: number,
    note?: string,
  ) =>
    request<Balance>(`/api/accounts/${accountId}/balances/${balanceId}`, token, {
      method: "PUT",
      body: JSON.stringify({ date, amount, note }),
    }),
  deleteBalance: (token: string, accountId: string, balanceId: string) =>
    request<void>(`/api/accounts/${accountId}/balances/${balanceId}`, token, {
      method: "DELETE",
    }),
  pots: (token: string, accountId: string) =>
    request<Pot[]>(`/api/accounts/${accountId}/pots`, token),
  createPot: (token: string, accountId: string, name: string) =>
    post<Pot>(`/api/accounts/${accountId}/pots`, token, { name }),
  updatePot: (
    token: string,
    accountId: string,
    potId: string,
    name: string,
    targetAccountId: string,
  ) =>
    request<Pot>(`/api/accounts/${accountId}/pots/${potId}`, token, {
      method: "PUT",
      body: JSON.stringify({ name, accountId: targetAccountId }),
    }),
  deletePot: (token: string, accountId: string, potId: string) =>
    request<void>(`/api/accounts/${accountId}/pots/${potId}`, token, { method: "DELETE" }),
  goals: (token: string, potId: string) => request<Goal[]>(`/api/pots/${potId}/goals`, token),
  createGoal: (token: string, potId: string, targetAmount: number, dueDate: string | null) =>
    post<Goal>(`/api/pots/${potId}/goals`, token, { targetAmount, dueDate }),
  updateGoal: (
    token: string,
    potId: string,
    goalId: string,
    targetAmount: number,
    dueDate: string | null,
  ) =>
    request<Goal>(`/api/pots/${potId}/goals/${goalId}`, token, {
      method: "PUT",
      body: JSON.stringify({ targetAmount, dueDate }),
    }),
  deleteGoal: (token: string, potId: string, goalId: string) =>
    request<void>(`/api/pots/${potId}/goals/${goalId}`, token, { method: "DELETE" }),
  contributions: (token: string, potId: string) =>
    request<PotContribution[]>(`/api/pots/${potId}/contributions`, token),
  createContribution: (
    token: string,
    potId: string,
    accountId: string | null,
    amount: number,
    date: string,
  ) =>
    post<PotContribution>(`/api/pots/${potId}/contributions`, token, { accountId, amount, date }),
  updateContribution: (
    token: string,
    potId: string,
    contributionId: string,
    accountId: string | null,
    amount: number,
    date: string,
  ) =>
    request<PotContribution>(`/api/pots/${potId}/contributions/${contributionId}`, token, {
      method: "PUT",
      body: JSON.stringify({ accountId, amount, date }),
    }),
  deleteContribution: (token: string, potId: string, contributionId: string) =>
    request<void>(`/api/pots/${potId}/contributions/${contributionId}`, token, {
      method: "DELETE",
    }),
  schedules: (token: string, accountId: string) =>
    request<Schedule[]>(`/api/accounts/${accountId}/schedules`, token),
  createSchedule: (token: string, accountId: string, schedule: Omit<Schedule, "id">) =>
    post<Schedule>(`/api/accounts/${accountId}/schedules`, token, schedule),
  updateSchedule: (
    token: string,
    accountId: string,
    scheduleId: string,
    schedule: ScheduleUpdate,
  ) =>
    request<Schedule>(`/api/accounts/${accountId}/schedules/${scheduleId}`, token, {
      method: "PUT",
      body: JSON.stringify(schedule),
    }),
  deleteSchedule: (token: string, accountId: string, scheduleId: string) =>
    request<void>(`/api/accounts/${accountId}/schedules/${scheduleId}`, token, {
      method: "DELETE",
    }),
  occurrences: (token: string, accountId: string) =>
    request<Occurrence[]>(`/api/accounts/${accountId}/occurrences`, token),
  occurrencePage: (token: string, accountId: string, skip: number, take: number) =>
    request<OccurrencePage>(
      `/api/accounts/${accountId}/occurrences/page?skip=${skip}&take=${take}`,
      token,
    ),
  occurrencePlanLinks: (token: string, accountId: string) =>
    request<Array<{ scheduledItemId: string; planDate: string }>>(
      `/api/accounts/${accountId}/occurrences/plan-links`,
      token,
    ),
  createOccurrence: (
    token: string,
    accountId: string,
    occurrence: {
      date: string;
      amount: number;
      isIncome: boolean;
      scheduledItemId: string | null;
      externalReference: string | null;
    },
  ) => post<Occurrence>(`/api/accounts/${accountId}/occurrences`, token, occurrence),
  updateOccurrence: (
    token: string,
    accountId: string,
    occurrenceId: string,
    occurrence: OccurrenceUpdate,
  ) =>
    request<Occurrence>(`/api/accounts/${accountId}/occurrences/${occurrenceId}`, token, {
      method: "PUT",
      body: JSON.stringify(occurrence),
    }),
  importOccurrences: (
    token: string,
    accountId: string,
    rows: Array<{
      date: string;
      amount: number;
      name: string;
      importId: string;
      scheduledItemId?: string | null;
      planDate?: string | null;
    }>,
  ) => post<ImportResult>(`/api/accounts/${accountId}/occurrences/import`, token, { rows }),
  linkOccurrencePlan: (
    token: string,
    accountId: string,
    occurrenceId: string,
    link: { scheduledItemId: string | null; planDate?: string | null },
  ) =>
    request<Occurrence>(`/api/accounts/${accountId}/occurrences/${occurrenceId}/plan`, token, {
      method: "PUT",
      body: JSON.stringify(link),
    }),
  deleteOccurrence: (token: string, accountId: string, occurrenceId: string) =>
    request<void>(`/api/accounts/${accountId}/occurrences/${occurrenceId}`, token, {
      method: "DELETE",
    }),
  reconcile: (token: string, accountId: string, occurrenceId: string) =>
    post<Occurrence>(`/api/accounts/${accountId}/occurrences/${occurrenceId}/reconcile`, token, {}),
  overview: (token: string) => request<FinancialOverview>("/api/financialoverview", token),
  setup: (token: string) => request<SetupStatus>("/api/setup", token),
  paydayPlan: (token: string) => request<PaydayPlan>("/api/payday-plan", token),
  forecast: (token: string, accountId: string) =>
    request<Forecast>(`/api/accounts/${accountId}/forecast`, token),
};
