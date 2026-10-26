using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Services;

/// <summary>Provides inferred payday dates and account funding recommendations.</summary>
public interface IPaydayPlanService
{
    Task<PaydayPlanResponse> GetPlanAsync(string ownerId, CancellationToken cancellationToken);
}

/// <summary>Payday timing, cash-flow totals, account outlook, and suggested transfers.</summary>
public sealed record PaydayPlanResponse(
    DateTime? PaydayStartDate,
    DateTime? PaydayWindowEndDate,
    DateTime? NextPaydayDate,
    decimal ScheduledIncome,
    decimal ScheduledExpenses,
    decimal DiscretionaryIncome,
    IReadOnlyList<AccountFundingResponse> Accounts,
    IReadOnlyList<TransferRecommendationResponse> Transfers,
    decimal CurrentBalance = 0m,
    decimal LeftToSpend = 0m);

/// <summary>An account's scheduled expenses and any amount left unfunded.</summary>
public sealed record AccountFundingResponse(
    Guid AccountId,
    string AccountName,
    decimal ExpensesDue,
    decimal UnfundedShortfall,
    bool ReceivesPaydayIncome);

/// <summary>A suggested transfer between two owned accounts.</summary>
public sealed record TransferRecommendationResponse(
    Guid FromAccountId,
    string FromAccountName,
    Guid ToAccountId,
    string ToAccountName,
    decimal Amount);

public sealed class PaydayPlanService(FinanceDbContext db) : IPaydayPlanService
{
    private const int PaydayGroupingDays = 2;
    private const int PlanningHorizonMonths = 18;
    private const int PlanningLookBackMonths = 2;

    private sealed record PayWindow(DateTime Start, DateTime End);
    private sealed record CashFlowEvent(Guid AccountId, DateTime Date, decimal Amount, bool IsIncome);

    public async Task<PaydayPlanResponse> GetPlanAsync(string ownerId, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var accounts = await db.Accounts
            .Where(account => account.OwnerId == ownerId)
            .OrderBy(account => account.Name)
            .ToListAsync(cancellationToken);
        var accountIds = accounts.Select(account => account.Id).ToArray();
        if (accounts.Count == 0)
        {
            return EmptyPlan();
        }

        var schedules = await db.ScheduledItems
            .Where(schedule => accountIds.Contains(schedule.AccountId))
            .ToListAsync(cancellationToken);
        var incomeSchedules = schedules.Where(schedule => schedule.IsIncome).ToList();
        if (incomeSchedules.Count == 0)
        {
            return EmptyPlan();
        }

        var scheduleHorizon = today.AddMonths(PlanningHorizonMonths);
        var payWindows = BuildPayWindows(incomeSchedules, today.AddMonths(-PlanningLookBackMonths), scheduleHorizon);
        // The current pay period starts at the most recent payday, even if that payday was a few days ago.
        var payWindow = payWindows.LastOrDefault(window => window.Start <= today)
            ?? payWindows.FirstOrDefault(window => window.Start >= today);
        if (payWindow is null)
        {
            return EmptyPlan();
        }

        var nextPayday = payWindows.FirstOrDefault(window => window.Start > payWindow.Start);
        if (nextPayday is null)
        {
            return EmptyPlan();
        }

        var planStart = payWindow.Start < today ? today : payWindow.Start;
        var planEnd = nextPayday.Start;
        var occurrences = await db.Occurrences
            .Where(occurrence =>
                accountIds.Contains(occurrence.AccountId) &&
                occurrence.Date.Date >= today &&
                occurrence.Date.Date < planEnd)
            .ToListAsync(cancellationToken);

        var events = BuildEvents(schedules, occurrences, today, planEnd);
        var planEvents = events
            .Where(item => item.Date >= planStart && item.Date < planEnd)
            .ToList();
        var incomeByAccount = accounts.ToDictionary(
            account => account.Id,
            account => planEvents
                .Where(item => item.AccountId == account.Id && item.IsIncome)
                .Sum(item => item.Amount));
        var expensesDueByAccount = accounts.ToDictionary(
            account => account.Id,
            account => -planEvents
                .Where(item => item.AccountId == account.Id && !item.IsIncome)
                .Sum(item => item.Amount));
        var incomeAccountIds = incomeSchedules
            .SelectMany(schedule => GetOccurrences(schedule, payWindow.Start, payWindow.End)
                .Select(_ => schedule.AccountId))
            .ToHashSet();

        var transferNeeded = accounts.ToDictionary(
            account => account.Id,
            account => Math.Max(0m, expensesDueByAccount[account.Id] - incomeByAccount[account.Id]));
        var availableSources = accounts
            .Where(account => incomeAccountIds.Contains(account.Id))
            .ToDictionary(
                account => account.Id,
                account => Math.Max(0m, incomeByAccount[account.Id] - expensesDueByAccount[account.Id]));
        var transfers = new List<TransferRecommendationResponse>();
        var unfunded = transferNeeded.ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var target in accounts
                     .Where(account => transferNeeded[account.Id] > 0)
                     .OrderBy(account => account.Name))
        {
            foreach (var source in accounts.Where(account =>
                         incomeAccountIds.Contains(account.Id) &&
                         account.Id != target.Id &&
                         availableSources[account.Id] > 0))
            {
                var amount = Math.Min(unfunded[target.Id], availableSources[source.Id]);
                if (amount <= 0)
                {
                    continue;
                }

                transfers.Add(new TransferRecommendationResponse(
                    source.Id,
                    source.Name,
                    target.Id,
                    target.Name,
                    amount));
                availableSources[source.Id] -= amount;
                unfunded[target.Id] -= amount;
            }
        }

        var accountResponses = accounts
            .Select(account => new AccountFundingResponse(
                account.Id,
                account.Name,
                expensesDueByAccount[account.Id],
                unfunded[account.Id],
                incomeAccountIds.Contains(account.Id)))
            .ToList();

        var currentBalance = await db.Balances
            .Where(balance => accountIds.Contains(balance.AccountId) && balance.Date.Date <= today)
            .SumAsync(balance => (decimal?)balance.Amount, cancellationToken) ?? 0m;

        // Whole pay period, from payday to the next payday: every planned income and expense.
        var periodStart = payWindow.Start;
        var periodPlans = schedules
            .SelectMany(schedule => GetOccurrences(schedule, periodStart, planEnd.AddDays(-1))
                .Select(_ => new { schedule.IsIncome, schedule.Amount }))
            .ToList();
        var periodIncome = periodPlans.Where(plan => plan.IsIncome).Sum(plan => plan.Amount);
        var periodExpenses = periodPlans.Where(plan => !plan.IsIncome).Sum(plan => plan.Amount);

        // Money actually spent in the period, across all accounts. Incoming activity is ignored: it is
        // mostly transfers between the user's own accounts, and real income comes from the income plans.
        var spentActivity = await db.Occurrences
            .Where(occurrence =>
                accountIds.Contains(occurrence.AccountId) &&
                !occurrence.IsIncome &&
                occurrence.Date.Date >= periodStart &&
                occurrence.Date.Date < planEnd)
            .ToListAsync(cancellationToken);
        var spent = spentActivity.Sum(o => o.Amount);

        return new PaydayPlanResponse(
            payWindow.Start,
            payWindow.End,
            nextPayday.Start,
            periodIncome,
            periodExpenses,
            periodIncome - periodExpenses,
            accountResponses,
            transfers,
            currentBalance,
            periodIncome - periodExpenses - spent);
    }

    private static PaydayPlanResponse EmptyPlan() =>
        new(null, null, null, 0m, 0m, 0m, [], []);

    private static List<PayWindow> BuildPayWindows(
        IReadOnlyCollection<ScheduledItem> incomeSchedules,
        DateTime from,
        DateTime to)
    {
        var dates = incomeSchedules
            .SelectMany(schedule => GetOccurrences(schedule, from, to))
            .Distinct()
            .Order()
            .ToList();
        // Smaller incomes (e.g. child benefit) must not split the month into several pay periods.
        var largest = incomeSchedules.Max(schedule => schedule.Amount);
        var primaryDates = incomeSchedules
            .Where(schedule => schedule.Amount * 2 >= largest)
            .SelectMany(schedule => GetOccurrences(schedule, from, to))
            .Distinct()
            .Order()
            .ToList();
        var windows = new List<PayWindow>();
        foreach (var date in primaryDates)
        {
            if (windows.Count == 0 || (date - windows[^1].End).Days > PaydayGroupingDays)
            {
                var start = dates
                    .Where(other => other <= date && (date - other).Days <= PaydayGroupingDays)
                    .DefaultIfEmpty(date)
                    .Min();
                windows.Add(new PayWindow(start, date));
            }
            else
            {
                windows[^1] = windows[^1] with { End = date };
            }
        }

        for (var i = 0; i < windows.Count; i++)
        {
            var end = windows[i].End;
            var lateDates = dates.Where(other => other > end && (other - end).Days <= PaydayGroupingDays).ToList();
            if (lateDates.Count > 0)
            {
                windows[i] = windows[i] with { End = lateDates.Max() };
            }
        }

        return windows;
    }

    private static IEnumerable<DateTime> GetOccurrences(
        ScheduledItem schedule,
        DateTime from,
        DateTime to)
    {
        var start = schedule.StartDate.Date;
        var index = 0;
        if (start < from)
        {
            var daysBefore = (from - start).Days;
            index = schedule.Frequency switch
            {
                0 => Math.Max(0, (daysBefore + 6) / 7),
                1 => Math.Max(0, (daysBefore + 13) / 14),
                2 => Math.Max(0, ((from.Year - start.Year) * 12) + from.Month - start.Month),
                3 => Math.Max(0, from.Year - start.Year),
                _ => 0
            };
        }

        var current = OccurrenceAt(schedule, index);
        while (current < from)
        {
            current = OccurrenceAt(schedule, ++index);
        }

        while (current <= to)
        {
            yield return current;
            current = OccurrenceAt(schedule, ++index);
        }
    }

    private static DateTime OccurrenceAt(ScheduledItem schedule, int index)
    {
        var start = schedule.StartDate.Date;
        return schedule.Frequency switch
        {
            0 => start.AddDays(index * 7),
            1 => start.AddDays(index * 14),
            2 => start.AddMonths(index),
            3 => start.AddYears(index),
            _ => start
        };
    }

    private static List<CashFlowEvent> BuildEvents(
        IReadOnlyCollection<ScheduledItem> schedules,
        IReadOnlyCollection<Occurrence> occurrences,
        DateTime from,
        DateTime to)
    {
        var events = new List<CashFlowEvent>();
        foreach (var schedule in schedules)
        {
            foreach (var date in GetOccurrences(schedule, from, to.AddDays(-1)))
            {
                if (occurrences.Any(occurrence =>
                        occurrence.ScheduledItemId == schedule.Id &&
                        (occurrence.PlanDate ?? occurrence.Date).Date == date))
                {
                    continue;
                }

                events.Add(new CashFlowEvent(
                    schedule.AccountId,
                    date,
                    schedule.IsIncome ? schedule.Amount : -schedule.Amount,
                    schedule.IsIncome));
            }
        }

        foreach (var occurrence in occurrences)
        {
            if (occurrence.IsReconciled && occurrence.Date.Date <= from)
            {
                continue;
            }

            events.Add(new CashFlowEvent(
                occurrence.AccountId,
                occurrence.Date.Date,
                occurrence.IsIncome ? occurrence.Amount : -occurrence.Amount,
                occurrence.IsIncome));
        }

        return events;
    }
}
