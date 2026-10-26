/** Formats an amount as GBP, e.g. `1250.40` becomes `£1,250.40` and `-41.37` becomes `-£41.37`. */
export function money(amount: number) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: "GBP",
  }).format(amount);
}

/**
 * Formats an ISO date (`YYYY-MM-DD`, optionally with a time) as e.g. `25 Sep 2026`.
 * The date is built from its parts so it never shifts across timezones.
 */
export function dateLabel(date: string) {
  const [year, month, day] = date.slice(0, 10).split("-").map(Number);
  return new Intl.DateTimeFormat("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  }).format(new Date(year, month - 1, day));
}

/** Today's date in the user's local timezone as `YYYY-MM-DD`, for date inputs. */
export function today() {
  const now = new Date();
  const localDate = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
  return localDate.toISOString().slice(0, 10);
}
