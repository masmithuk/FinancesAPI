using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Services;

/// <summary>
/// Builds the Financial Overview, setup status, and simple transfer recommendations.
///
/// This V1 implementation is deliberately conservative: recommendations are
/// informational only and use a simple greedy allocation from accounts with
/// positive available balances to pots with shortfalls.
/// </summary>
public sealed class FinancialOverviewService
{
    private readonly FinanceDbContext _db;
    private readonly ForecastService _forecast;

    public FinancialOverviewService(FinanceDbContext db, ForecastService forecast)
    {
        _db = db;
        _forecast = forecast;
    }

    public record SetupStatus(bool HasAccount, bool HasBalance, bool HasPotIfGoals, IEnumerable<string> Missing);

    // AvailableForSpending represents the account balance plus near-term scheduled and pending movements
    public record AccountSummary(Guid AccountId, string Name, decimal AvailableForSpending);

    public record PotShortfall(Guid PotId, string PotName, decimal Shortfall);

    // Include the target account id for recommendations so clients can display where the pot resides
    public record TransferSuggestion(Guid FromAccountId, Guid ToAccountId, Guid ToPotId, decimal Amount, string Explanation);

    public record FinancialOverviewSummary(IEnumerable<AccountSummary> Accounts, IEnumerable<PotShortfall> PotShortfalls, decimal SafeToSpend, IEnumerable<TransferSuggestion> Recommendations);

    public async Task<SetupStatus> GetSetupStatusAsync(string ownerId)
    {
        var accounts = await _db.Accounts.Where(a => a.OwnerId == ownerId).ToListAsync();
        var hasAccount = accounts.Any();

        var hasBalance = false;
        if (hasAccount)
        {
            var anyBalances = await _db.Balances.Where(b => accounts.Select(a => a.Id).Contains(b.AccountId)).AnyAsync();
            hasBalance = anyBalances;
        }

        var hasGoals = await _db.Goals.Include(g => g.Pot).ThenInclude(p => p.Account).Where(g => g.Pot != null && g.Pot.Account != null && g.Pot.Account.OwnerId == ownerId).AnyAsync();
        var hasPotIfGoals = !hasGoals || await _db.Pots.Where(p => p.Account != null && p.Account.OwnerId == ownerId).AnyAsync();

        var missing = new List<string>();
        if (!hasAccount) missing.Add("No accounts defined");
        if (!hasBalance) missing.Add("No balances recorded");
        if (!hasPotIfGoals) missing.Add("Goals exist but no pots defined");

        return new SetupStatus(hasAccount, hasBalance, hasPotIfGoals, missing);
    }

    public async Task<FinancialOverviewSummary> GetFinancialOverviewAsync(string ownerId)
    {
        var accounts = await _db.Accounts.Where(a => a.OwnerId == ownerId).ToListAsync();
        var accountSummaries = new List<AccountSummary>();

        var today = DateTime.UtcNow.Date;
        var futureWindowEnd = today.AddMonths(2); // include near-term scheduled items (V1 heuristic)

        foreach (var a in accounts)
        {
            // Starting balance up to today
            var starting = await _db.Balances.Where(b => b.AccountId == a.Id && b.Date <= today).SumAsync(b => (decimal?)b.Amount) ?? 0m;

            // Include scheduled items starting within the near-term window
            var scheduled = await _db.ScheduledItems
                .Where(s => s.AccountId == a.Id && s.StartDate.Date >= today && s.StartDate.Date <= futureWindowEnd)
                .ToListAsync();

            var scheduledSum = scheduled.Sum(s => s.IsIncome ? s.Amount : -s.Amount);

            // Include pending (unreconciled) occurrences in the window
            var pendingSum = await _db.Occurrences
                .Where(o => o.AccountId == a.Id && !o.IsReconciled && o.Date.Date >= today && o.Date.Date <= futureWindowEnd)
                .SumAsync(o => (decimal?)(o.IsIncome ? o.Amount : -o.Amount)) ?? 0m;

            var available = starting + scheduledSum + pendingSum;
            accountSummaries.Add(new AccountSummary(a.Id, a.Name, available));
        }

        // Compute pot shortfalls: goal.TargetAmount - current pot balances (approximate by account available for now)
        var potShortfalls = new List<PotShortfall>();
        var pots = await _db.Pots.Include(p => p.Goals).Include(p => p.Contributions).Include(p => p.Account).Where(p => p.Account != null && p.Account.OwnerId == ownerId).ToListAsync();

        // Build a mapping from pot id to its account id for enrichment of recommendations
        var potToAccount = pots.ToDictionary(pp => pp.Id, pp => pp.AccountId);

        foreach (var p in pots)
        {
            var goal = p.Goals.FirstOrDefault();
            if (goal is not null)
            {
                var saved = p.Contributions.Sum(c => c.Amount);
                var shortfall = Math.Max(0, goal.TargetAmount - saved);
                if (shortfall > 0) potShortfalls.Add(new PotShortfall(p.Id, p.Name, shortfall));
            }
        }

        // Use ForecastService to compute SafeToSpend: pick the minimum across forecasted points
        decimal safeToSpend = 0m;
        if (accounts.Any())
        {
            // pick first account for forecast in V1; a full implementation would aggregate across accounts
            var forecast = await _forecast.GetForecastAsync(accounts.First().Id);
            safeToSpend = forecast.SafeToSpend;
        }

        // Simple transfer recommendations: greedily cover pot shortfalls from accounts with positive available balances
        var recs = new List<TransferSuggestion>();
        var sources = accountSummaries.Where(a => a.AvailableForSpending > 0).OrderByDescending(a => a.AvailableForSpending).Select(a => new AccountSummary(a.AccountId, a.Name, a.AvailableForSpending)).ToList();
        foreach (var pot in potShortfalls)
        {
            var remaining = pot.Shortfall;
            for (int i = 0; i < sources.Count; i++)
            {
                if (remaining <= 0) break;
                var src = sources[i];
                var take = Math.Min(src.AvailableForSpending, remaining);
                if (take <= 0) continue;
                var toAccountId = potToAccount.TryGetValue(pot.PotId, out var ta) ? ta : Guid.Empty;
                recs.Add(new TransferSuggestion(src.AccountId, toAccountId, pot.PotId, take, $"Cover shortfall for pot '{pot.PotName}'"));
                // reduce the available amount
                sources[i] = new AccountSummary(src.AccountId, src.Name, src.AvailableForSpending - take);
                remaining -= take;
            }
        }

        // Apply allocations to account summaries so availableForSpending reflects post-transfer amounts
        var allocations = recs.GroupBy(r => r.FromAccountId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        for (int i = 0; i < accountSummaries.Count; i++)
        {
            var a = accountSummaries[i];
            if (allocations.TryGetValue(a.AccountId, out var alloc))
            {
                accountSummaries[i] = new AccountSummary(a.AccountId, a.Name, a.AvailableForSpending - alloc);
            }
        }

        return new FinancialOverviewSummary(accountSummaries, potShortfalls, safeToSpend, recs);
    }
}
