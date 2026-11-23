using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Services;

/// <summary>Provides inferred payday dates and account funding recommendations.</summary>
public interface IPaydayPlanService
{
    Task<PaydayPlanResponse> GetPlanAsync(string ownerId, PaydayPeriod period, CancellationToken cancellationToken);
}

/// <summary>
/// Which pay period to plan: Current runs from today to the next payday, Next from the next payday to the
/// one after, and Auto picks Next within a week of payday and Current otherwise.
/// </summary>
public enum PaydayPeriod
{
    Auto,
    Current,
    Next
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
    decimal LeftToSpend = 0m,
    string Mode = "current",
    DateTime? PeriodStartDate = null,
    DateTime? PeriodEndDate = null,
    IReadOnlyList<PrePaydayShortfallResponse>? PrePaydayShortfalls = null);

/// <summary>
/// An account's bills for the planned period and how they are funded: income paid in, the balance
/// expected at the start of the period, what to send, and how much of the bills existing money covers.
/// </summary>
public sealed record AccountFundingResponse(
    Guid AccountId,
    string AccountName,
    decimal ExpensesDue,
    decimal UnfundedShortfall,
    bool ReceivesPaydayIncome,
    decimal IncomeIn = 0m,
    decimal ExpectedBalance = 0m,
    decimal ToSend = 0m,
    decimal FromExisting = 0m,
    DateTime? BalanceCheckedOn = null,
    decimal LeftAfterTransfers = 0m,
    decimal OverdraftToCover = 0m);

/// <summary>An account whose balance is projected to go below zero before the next payday.</summary>
public sealed record PrePaydayShortfallResponse(
    Guid AccountId,
    string AccountName,
    decimal LowestBalance,
    DateTime Date);

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
    private const int UpcomingLookAheadDays = 7;
    private const string CurrentMode = "current";
    private const string UpcomingMode = "upcoming";

    private sealed record PayWindow(DateTime Start, DateTime End);
    private sealed record CashFlowEvent(Guid AccountId, DateTime Date, decimal Amount, bool IsIncome);

    public async Task<PaydayPlanResponse> GetPlanAsync(string ownerId, PaydayPeriod period, CancellationToken cancellationToken)
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
        var currentWindow = payWindows.LastOrDefault(window => window.Start <= today);
        var upcomingWindow = payWindows.FirstOrDefault(window => window.Start > today);
        var insidePayday = currentWindow is not null && today <= currentWindow.End;
        // Asked for, or shortly before payday: plan the pay period that starts on the next payday.
        var isUpcoming = upcomingWindow is not null && period switch
        {
            PaydayPeriod.Next => true,
            PaydayPeriod.Current => false,
            _ => !insidePayday && (upcomingWindow.Start - today).Days <= UpcomingLookAheadDays
        };

        // Otherwise the current pay period starts at the most recent payday, even if that was a few days ago.
        // Before the first payday there is none: asked for this pay period, it runs from today to that
        // payday (left to choose, the first payday's period is planned instead, as before).
        var beforeFirstPayday = !isUpcoming &&
                                period == PaydayPeriod.Current &&
                                currentWindow is null &&
                                upcomingWindow is not null;
        var payWindow = isUpcoming
            ? upcomingWindow
            : beforeFirstPayday
                ? new PayWindow(today, today)
                : currentWindow ?? payWindows.FirstOrDefault(window => window.Start >= today);
        if (payWindow is null)
        {
            return EmptyPlan();
        }

        var nextPayday = beforeFirstPayday
            ? upcomingWindow
            : payWindows.FirstOrDefault(window => window.Start > payWindow.Start);
        if (nextPayday is null)
        {
            return EmptyPlan();
        }

        var planStart = payWindow.Start < today ? today : payWindow.Start;
        var planEnd = nextPayday.Start;
        // This pay period looks on to the end of the next one, so it doesn't move money needed after payday.
        var eventsEnd = isUpcoming
            ? planEnd
            : payWindows.FirstOrDefault(window => window.Start > nextPayday.Start)?.Start ?? planEnd;
        var occurrences = await db.Occurrences
            .Where(occurrence =>
                accountIds.Contains(occurrence.AccountId) &&
                occurrence.Date.Date >= today &&
                occurrence.Date.Date < eventsEnd)
            .ToListAsync(cancellationToken);
        // How much linked activity has already paid towards each plan date, whenever it happened (a bill
        // can be paid early). A plan date only counts what is still to come: a £50 shop linked to a £700
        // food budget leaves £650. This matches the forecast's rule so the two never disagree.
        var paidPlanDates = (await db.Occurrences
                .Where(occurrence =>
                    accountIds.Contains(occurrence.AccountId) &&
                    occurrence.ScheduledItemId != null)
                .Select(occurrence => new { occurrence.ScheduledItemId, occurrence.PlanDate, occurrence.Date, occurrence.Amount })
                .ToListAsync(cancellationToken))
            .GroupBy(item => (item.ScheduledItemId!.Value, (item.PlanDate ?? item.Date).Date))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        // A balance checked against the bank today already includes anything planned for today.
        var checkedToday = accounts
            .Where(account => account.BalanceCheckedOn?.Date == today)
            .Select(account => account.Id)
            .ToHashSet();
        var events = BuildEvents(schedules, occurrences, paidPlanDates, checkedToday, today, eventsEnd);
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

        var balanceByAccount = (await db.Balances
                .Where(balance => accountIds.Contains(balance.AccountId) && balance.Date.Date <= today)
                .GroupBy(balance => balance.AccountId)
                .Select(group => new { AccountId = group.Key, Total = group.Sum(balance => balance.Amount) })
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.AccountId, item => item.Total);

        // Before payday, project each balance to payday: today's balance plus everything due in between.
        var prePaydayEvents = isUpcoming
            ? events.Where(item => item.Date < payWindow.Start).ToList()
            : [];
        var expectedBalanceByAccount = accounts.ToDictionary(
            account => account.Id,
            account => balanceByAccount.GetValueOrDefault(account.Id) +
                       prePaydayEvents.Where(item => item.AccountId == account.Id).Sum(item => item.Amount));
        var prePaydayShortfalls = new List<PrePaydayShortfallResponse>();
        foreach (var account in accounts)
        {
            // Only movements before payday count as running short: a balance that is simply below zero
            // today (a credit card, say) isn't running short and isn't topped up.
            var (lowest, date) = ComputeRunningMinimum(
                prePaydayEvents.Where(item => item.AccountId == account.Id),
                balanceByAccount.GetValueOrDefault(account.Id));
            if (date is { } lowestDate && lowest < 0m)
            {
                prePaydayShortfalls.Add(new PrePaydayShortfallResponse(account.Id, account.Name, lowest, lowestDate));
            }
        }

        // A balance already below zero today (a credit card, say) isn't topped up: only an overdraft that
        // bills before payday add on top of today's balance needs covering, and money coming in only
        // counts once the balance is back above zero.
        var fundingBalanceByAccount = accounts.ToDictionary(
            account => account.Id,
            account =>
            {
                var expected = expectedBalanceByAccount[account.Id];
                var existingOverdraft = Math.Min(0m, balanceByAccount.GetValueOrDefault(account.Id));
                return Math.Min(expected - existingOverdraft, Math.Max(expected, 0m));
            });
        // The part of today's overdraft left in place (zero or negative).
        var untoppedOverdraft = accounts.ToDictionary(
            account => account.Id,
            account => expectedBalanceByAccount[account.Id] - fundingBalanceByAccount[account.Id]);

        // Money already in an account (including salary already received) counts towards its bills.
        var fundsByAccount = accounts.ToDictionary(
            account => account.Id,
            account => fundingBalanceByAccount[account.Id] + incomeByAccount[account.Id]);
        var transferNeeded = accounts.ToDictionary(
            account => account.Id,
            account => Math.Max(0m, expensesDueByAccount[account.Id] - fundsByAccount[account.Id]));
        // This pay period can only give away money the account won't need before the end of the next one:
        // its lowest projected balance over that time, before any transfers.
        var availableSources = accounts.ToDictionary(
            account => account.Id,
            account =>
            {
                // An overdraft that isn't topped up still uses up money arriving in that account.
                var spare = Math.Max(
                    0m,
                    fundsByAccount[account.Id] - expensesDueByAccount[account.Id] + untoppedOverdraft[account.Id]);
                if (isUpcoming)
                {
                    return spare;
                }

                var (lowest, _) = ComputeRunningMinimum(
                    events.Where(item => item.AccountId == account.Id),
                    balanceByAccount.GetValueOrDefault(account.Id));
                return Math.Min(spare, Math.Max(0m, lowest));
            });
        var transfers = new List<TransferRecommendationResponse>();
        var unfunded = transferNeeded.ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var target in accounts
                     .Where(account => transferNeeded[account.Id] > 0)
                     .OrderBy(account => account.Name))
        {
            foreach (var source in accounts.Where(account =>
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
                incomeAccountIds.Contains(account.Id),
                incomeByAccount[account.Id],
                expectedBalanceByAccount[account.Id],
                transferNeeded[account.Id],
                Math.Min(
                    Math.Max(0m, fundingBalanceByAccount[account.Id]),
                    Math.Max(0m, expensesDueByAccount[account.Id] - incomeByAccount[account.Id])),
                account.BalanceCheckedOn,
                // Where the spare money sits once bills are paid and the suggested transfers are made: what
                // is really there, and in this pay period never money the account needs after payday.
                Math.Min(
                    fundsByAccount[account.Id] - expensesDueByAccount[account.Id] + untoppedOverdraft[account.Id] +
                        transfers.Where(transfer => transfer.ToAccountId == account.Id).Sum(transfer => transfer.Amount) -
                        transfers.Where(transfer => transfer.FromAccountId == account.Id).Sum(transfer => transfer.Amount),
                    availableSources[account.Id]),
                // Overdraft that bills before payday add to today's balance; it is part of what to send.
                Math.Max(0m, -fundingBalanceByAccount[account.Id])))
            .ToList();

        var currentBalance = balanceByAccount.Values.Sum();

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
            periodIncome - periodExpenses - spent,
            isUpcoming ? UpcomingMode : CurrentMode,
            periodStart,
            planEnd,
            prePaydayShortfalls);
    }

    private static PaydayPlanResponse EmptyPlan() =>
        new(null, null, null, 0m, 0m, 0m, [], [], PrePaydayShortfalls: []);

    /// <summary>
    /// Walks an account's balance day by day and returns its lowest point below the starting balance and the
    /// first date it is reached (no date when it never drops below where it started).
    /// Movements on the same day are netted first, so the order of entries within a day doesn't matter.
    /// </summary>
    private static (decimal Lowest, DateTime? Date) ComputeRunningMinimum(IEnumerable<CashFlowEvent> events, decimal start)
    {
        var running = start;
        var lowest = start;
        DateTime? lowestDate = null;
        foreach (var day in events.GroupBy(item => item.Date).OrderBy(group => group.Key))
        {
            running += day.Sum(item => item.Amount);
            if (running < lowest)
            {
                lowest = running;
                lowestDate = day.Key;
            }
        }

        return (lowest, lowestDate);
    }

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
        IReadOnlyDictionary<(Guid ScheduledItemId, DateTime PlanDate), decimal> paidPlanDates,
        IReadOnlySet<Guid> checkedOnFromDate,
        DateTime from,
        DateTime to)
    {
        var events = new List<CashFlowEvent>();
        foreach (var schedule in schedules)
        {
            foreach (var date in GetOccurrences(schedule, from, to.AddDays(-1)))
            {
                var stillToCome = schedule.Amount - paidPlanDates.GetValueOrDefault((schedule.Id, date));
                if (stillToCome <= 0m || (date == from && checkedOnFromDate.Contains(schedule.AccountId)))
                {
                    continue;
                }

                events.Add(new CashFlowEvent(
                    schedule.AccountId,
                    date,
                    schedule.IsIncome ? stillToCome : -stillToCome,
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
