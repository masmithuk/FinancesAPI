/** The parts of a recurring plan needed to match bank transactions against it. */
export interface PlanLike {
  id: string;
  name: string;
  amount: number;
  startDate: string;
  frequency: number;
  isIncome: boolean;
}

export interface TransactionLike {
  date: string;
  amount: number;
  name: string;
}

/** A suggested link between a transaction and one occurrence of a plan. */
export interface PlanMatch {
  planId: string;
  /** The plan occurrence (YYYY-MM-DD) the transaction pays. */
  planDate: string;
  reason: "name and amount" | "amount" | "name";
}

const DAY = 86_400_000;
const AMOUNT_WINDOW_DAYS = 3;
const MAX_OCCURRENCES = 5000;

const GENERIC_WORDS = new Set([
  "the",
  "and",
  "for",
  "ltd",
  "limited",
  "bill",
  "bills",
  "payment",
  "payments",
  "card",
  "direct",
  "debit",
  "monthly",
  "weekly",
  "annual",
  "transfer",
  "faster",
]);

function toUtc(date: string) {
  const [year, month, day] = date.slice(0, 10).split("-").map(Number);
  return Date.UTC(year, month - 1, day);
}

function format(time: number) {
  return new Date(time).toISOString().slice(0, 10);
}

function addMonths(startDate: string, months: number) {
  const [year, month, day] = startDate.slice(0, 10).split("-").map(Number);
  const target = new Date(Date.UTC(year, month - 1 + months, 1));
  const lastDay = new Date(
    Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0),
  ).getUTCDate();
  target.setUTCDate(Math.min(day, lastDay));
  return target.getTime();
}

/** The nth occurrence of a plan, mirroring the server's schedule expansion. */
function occurrenceAt(plan: PlanLike, index: number) {
  switch (plan.frequency) {
    case 0:
      return toUtc(plan.startDate) + index * 7 * DAY;
    case 1:
      return toUtc(plan.startDate) + index * 14 * DAY;
    case 2:
      return addMonths(plan.startDate, index);
    case 3:
      return addMonths(plan.startDate, index * 12);
    default:
      return toUtc(plan.startDate);
  }
}

/** Dates (YYYY-MM-DD) on which the plan falls between `from` and `to`, inclusive. */
export function planDates(plan: PlanLike, from: string, to: string): string[] {
  const start = toUtc(from);
  const end = toUtc(to);
  const dates: string[] = [];
  if (![0, 1, 2, 3].includes(plan.frequency)) {
    const only = toUtc(plan.startDate);
    return only >= start && only <= end ? [format(only)] : dates;
  }
  for (let index = 0; index < MAX_OCCURRENCES; index++) {
    const time = occurrenceAt(plan, index);
    if (time > end) break;
    if (time >= start) dates.push(format(time));
  }
  return dates;
}

/** Significant lower-case words from a name, ignoring generic ones such as "payment". */
export function nameTokens(name: string): string[] {
  return name
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter((token) => token.length >= 3 && !GENERIC_WORDS.has(token));
}

/** True when the transaction name shares a significant word with the plan name (partial matches count). */
export function namesMatch(planName: string, transactionName: string): boolean {
  const compactPlan = nameTokens(planName).join("");
  const compactTransaction = transactionName.toLowerCase().replace(/[^a-z0-9]/g, "");
  if (compactPlan.length >= 5 && compactTransaction.includes(compactPlan)) return true;

  const planWords = nameTokens(planName);
  const transactionWords = nameTokens(transactionName);
  return planWords.some((planWord) =>
    transactionWords.some(
      (word) =>
        word === planWord ||
        (planWord.length >= 4 && word.startsWith(planWord)) ||
        (word.length >= 4 && planWord.startsWith(word)),
    ),
  );
}

/** Key identifying one plan occurrence, used to track which are already paid. */
export function claimKey(planId: string, planDate: string) {
  return `${planId}|${planDate.slice(0, 10)}`;
}

/**
 * The plan occurrence nearest a date. Occurrences are projected backwards before the plan's
 * start date too, so older statement rows can still be linked to a plan created later.
 */
export function nearestPlanDate(plan: PlanLike, date: string): string {
  const target = toUtc(date);
  const start = toUtc(plan.startDate);
  if (plan.frequency === 0 || plan.frequency === 1) {
    const step = (plan.frequency === 0 ? 7 : 14) * DAY;
    return format(start + Math.round((target - start) / step) * step);
  }
  if (plan.frequency === 2 || plan.frequency === 3) {
    const months = plan.frequency === 2 ? 1 : 12;
    const [startYear, startMonth] = plan.startDate.split("-").map(Number);
    const [year, month] = date.split("-").map(Number);
    const base = Math.floor(((year - startYear) * 12 + (month - startMonth)) / months);
    let best = start;
    let bestDistance = Infinity;
    for (let index = base - 1; index <= base + 2; index++) {
      const candidate = addMonths(plan.startDate, index * months);
      const distance = Math.abs(candidate - target);
      if (distance < bestDistance) {
        best = candidate;
        bestDistance = distance;
      }
    }
    return format(best);
  }
  return format(start);
}

/**
 * Suggests which plan each transaction belongs to.
 *
 * A transaction matches a plan of the same direction when its name shares a word with the plan.
 * Any number of transactions can match one plan, so a "Tesco / Lidl" budget takes every shop.
 * A transaction with the same amount as a plan, landing within a few days of an occurrence, also
 * matches. Those amount-only matches pay one occurrence each, and occurrences already in
 * `claimed` are never suggested. Transactions with no sensible match are left out. The result is
 * keyed by the transaction's index in `rows`.
 */
export function suggestPlanMatches(
  rows: TransactionLike[],
  plans: PlanLike[],
  claimed: Set<string> = new Set(),
): Map<number, PlanMatch> {
  const matches = new Map<number, PlanMatch>();
  if (!rows.length || !plans.length) return matches;

  const times = rows.map((row) => toUtc(row.date));
  const taken = new Set(claimed);

  // Oldest first, so earlier transactions claim earlier plan occurrences.
  const order = rows.map((_, index) => index).sort((a, b) => times[a] - times[b] || a - b);
  for (const index of order) {
    const row = rows[index];
    let best: { match: PlanMatch; score: number; distance: number } | null = null;

    for (const plan of plans) {
      if (plan.isIncome !== row.amount > 0) continue;
      const nameHit = namesMatch(plan.name, row.name);
      const amountHit = Math.abs(Math.abs(row.amount) - plan.amount) < 0.005;
      if (!nameHit && !amountHit) continue;

      const date = nearestPlanDate(plan, row.date);
      const distance = Math.abs(toUtc(date) - times[index]) / DAY;
      const byAmount =
        amountHit && distance <= AMOUNT_WINDOW_DAYS && !taken.has(claimKey(plan.id, date));
      if (!nameHit && !byAmount) continue;

      const score = (nameHit ? 2 : 0) + (byAmount ? 1 : 0);
      if (!best || score > best.score || (score === best.score && distance < best.distance)) {
        best = {
          score,
          distance,
          match: {
            planId: plan.id,
            planDate: date,
            reason: nameHit && byAmount ? "name and amount" : nameHit ? "name" : "amount",
          },
        };
      }
    }

    if (best) {
      matches.set(index, best.match);
      if (best.match.reason === "amount")
        taken.add(claimKey(best.match.planId, best.match.planDate));
    }
  }
  return matches;
}
