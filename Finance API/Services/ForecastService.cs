using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Services;

/// <summary>
/// Provides simple dated cash-flow forecasting for an Account.
///
/// Current behaviour (V1):
/// - Uses account-level Balances to determine historical and starting amounts.
/// - Expands ScheduledItem entries into concrete occurrences within the date range
///   (supports weekly, fortnightly, monthly, annual frequencies).
/// - Includes unreconciled Occurrence entries as pending movements.
/// - Produces a daily cumulative projection from today through month-end and
///   computes SafeToSpend as the minimum cumulative value in that window.
///
/// Note: Pot funding prioritisation and partial funding rules are out of scope
/// for V1 and may be added later.
/// </summary>
public sealed class ForecastService
{
    private readonly FinanceDbContext _db;

    public ForecastService(FinanceDbContext db)
    {
        _db = db;
    }

    public record DailyPoint(DateTime Date, decimal CumulativeAmount);

    public record ForecastResult(DateTime From, DateTime To, IEnumerable<DailyPoint> Points, decimal SafeToSpend, DateTime SafeToSpendDate);

    public async Task<ForecastResult> GetForecastAsync(Guid accountId)
    {
        var today = DateTime.UtcNow.Date;
        var end = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        // Validate account exists
        var account = await _db.Accounts.FindAsync(accountId);
        if (account is null) throw new InvalidOperationException("Account not found");

        // Compute starting cumulative amount as sum of balances up to today
        var startingSum = await _db.Balances
            .Where(b => b.AccountId == accountId && b.Date.Date <= today)
            .SumAsync(b => (decimal?)b.Amount) ?? 0m;

        // Gather scheduled item occurrences within the range
        var schedules = await _db.ScheduledItems.Where(s => s.AccountId == accountId).ToListAsync();
        // Plan occurrences already paid by a linked activity must not be counted again.
        var linkedOccurrences = (await _db.Occurrences
            .Where(o => o.AccountId == accountId && o.ScheduledItemId != null)
            .Select(o => new { o.ScheduledItemId, o.Date, o.PlanDate })
            .ToListAsync())
            .Select(o => (o.ScheduledItemId, Date: (o.PlanDate ?? o.Date).Date))
            .ToHashSet();

        var scheduleEvents = new List<(DateTime Date, decimal Amount)>();
        foreach (var s in schedules)
        {
            var occurrences = ExpandSchedule(s, today, end);
            foreach (var d in occurrences)
            {
                if (linkedOccurrences.Contains((s.Id, d.Date))) continue;
                var amt = s.IsIncome ? s.Amount : -s.Amount;
                scheduleEvents.Add((d, amt));
            }
        }

        // Include unreconciled occurrences (they are pending until reconciled)
        var pendingOccurrences = await _db.Occurrences
            .Where(o => o.AccountId == accountId && !o.IsReconciled && o.Date.Date >= today && o.Date.Date <= end)
            .Select(o => new { o.Date, o.Amount, o.IsIncome })
            .ToListAsync();

        var pendingEvents = pendingOccurrences.Select(o => (o.Date.Date, Amount: o.IsIncome ? o.Amount : -o.Amount));

        // Combine events by date
        var eventsByDate = scheduleEvents
            .Concat(pendingEvents)
            .GroupBy(e => e.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var points = new List<DailyPoint>();
        decimal cumulative = startingSum;

        for (var d = today; d <= end; d = d.AddDays(1))
        {
            if (eventsByDate.TryGetValue(d, out var delta)) cumulative += delta;
            points.Add(new DailyPoint(d, cumulative));
        }

        var minPoint = points.OrderBy(p => p.CumulativeAmount).First();

        return new ForecastResult(today, end, points, minPoint.CumulativeAmount, minPoint.Date);
    }

    private static IEnumerable<DateTime> ExpandSchedule(ScheduledItem s, DateTime from, DateTime to)
    {
        var list = new List<DateTime>();
        var start = s.StartDate.Date;
        if (start > to) return list;

        // Determine first occurrence on or after 'from'
        var current = start;
        if (current < from)
        {
            current = GetNextOccurrenceOnOrAfter(s, from);
        }

        while (current <= to)
        {
            list.Add(current);
            current = AdvanceByFrequency(current, s.Frequency);
        }

        return list;
    }

    private static DateTime GetNextOccurrenceOnOrAfter(ScheduledItem s, DateTime date)
    {
        var cur = s.StartDate.Date;
        if (s.Frequency == 0) // weekly
        {
            var days = (int)((date - cur).TotalDays);
            var steps = Math.Max(0, (days + 6) / 7);
            return cur.AddDays(steps * 7);
        }

        if (s.Frequency == 1) // fortnightly
        {
            var days = (int)((date - cur).TotalDays);
            var steps = Math.Max(0, (days + 13) / 14);
            return cur.AddDays(steps * 14);
        }

        if (s.Frequency == 2) // monthly
        {
            var y = cur.Year; var m = cur.Month;
            while (new DateTime(y, m, ClampDayOfMonth(cur.Day, y, m)) < date)
            {
                m++;
                if (m > 12) { m = 1; y++; }
            }
            var day = ClampDayOfMonth(cur.Day, y, m);
            return new DateTime(y, m, day);
        }

        // annual
        if (s.Frequency == 3)
        {
            var y = cur.Year;
            while (new DateTime(y, cur.Month, ClampDayOfMonth(cur.Day, y, cur.Month)) < date) y++;
            return new DateTime(y, cur.Month, ClampDayOfMonth(cur.Day, y, cur.Month));
        }

        return cur;
    }

    private static int ClampDayOfMonth(int day, int year, int month)
    {
        return Math.Min(day, DateTime.DaysInMonth(year, month));
    }

    private static DateTime AdvanceByFrequency(DateTime date, int frequency)
    {
        return frequency switch
        {
            0 => date.AddDays(7),
            1 => date.AddDays(14),
            2 => date.AddMonths(1),
            3 => date.AddYears(1),
            _ => date.AddMonths(1)
        };
    }
}
