using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Services;

/// <summary>
/// Builds the Home summary, setup status, and simple transfer recommendations.
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

    public record AccountSummary(Guid AccountId, string Name, decimal CurrentBalance);

    public record PotShortfall(Guid PotId, string PotName, decimal Shortfall);

    public record TransferSuggestion(Guid FromAccountId, Guid ToPotId, decimal Amount, string Explanation);

    public record HomeSummary(IEnumerable<AccountSummary> Accounts, IEnumerable<PotShortfall> PotShortfalls, decimal SafeToSpend, IEnumerable<TransferSuggestion> Recommendations);

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

    public async Task<HomeSummary> GetHomeSummaryAsync(string ownerId)
    {
        var accounts = await _db.Accounts.Where(a => a.OwnerId == ownerId).ToListAsync();
        var accountSummaries = new List<AccountSummary>();

        foreach (var a in accounts)
        {
            var current = await _db.Balances.Where(b => b.AccountId == a.Id && b.Date <= DateTime.UtcNow.Date).SumAsync(b => (decimal?)b.Amount) ?? 0m;
            accountSummaries.Add(new AccountSummary(a.Id, a.Name, current));
        }

        // Compute pot shortfalls: goal.TargetAmount - current pot balances (sum of balances linked to account? For simplicity estimate pot current as 0 in V1)
        var potShortfalls = new List<PotShortfall>();
        var pots = await _db.Pots.Include(p => p.Goals).Include(p => p.Account).Where(p => p.Account != null && p.Account.OwnerId == ownerId).ToListAsync();
        foreach (var p in pots)
        {
            var goal = await _db.Goals.Where(g => g.PotId == p.Id).FirstOrDefaultAsync();
            if (goal is not null)
            {
                // Estimate pot current amount by summing balances on the account for now (pot-specific balances not modelled separately)
                var accountBal = accountSummaries.FirstOrDefault(a => a.AccountId == p.AccountId)?.CurrentBalance ?? 0m;
                var shortfall = Math.Max(0, goal.TargetAmount - accountBal);
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

        // Simple transfer recommendations: greedily cover pot shortfalls from accounts with positive current balances
        var recs = new List<TransferSuggestion>();
        var sources = accountSummaries.Where(a => a.CurrentBalance > 0).OrderByDescending(a => a.CurrentBalance).Select(a => new AccountSummary(a.AccountId, a.Name, a.CurrentBalance)).ToList();
        foreach (var pot in potShortfalls)
        {
            var remaining = pot.Shortfall;
            for (int i = 0; i < sources.Count; i++)
            {
                if (remaining <= 0) break;
                var src = sources[i];
                var take = Math.Min(src.CurrentBalance, remaining);
                if (take <= 0) continue;
                recs.Add(new TransferSuggestion(src.AccountId, pot.PotId, take, $"Cover shortfall for pot '{pot.PotName}'"));
                // reduce the available amount
                sources[i] = new AccountSummary(src.AccountId, src.Name, src.CurrentBalance - take);
                remaining -= take;
            }
        }

        return new HomeSummary(accountSummaries, potShortfalls, safeToSpend, recs);
    }
}
