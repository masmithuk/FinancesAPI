using System.Security.Claims;
using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Endpoints;

/// <summary>
/// Pot management endpoints.
///
/// These minimal endpoints provide CRUD-like operations for "virtual pots"
/// (lightweight sub-accounts linked to an Account) and allow attaching simple
/// savings goals to pots. An additional endpoint returns running balances
/// for an account (cumulative by date). Endpoints enforce ownership by
/// comparing the current user's id (from the JWT) with the Account.OwnerId.
/// </summary>
public static class PotsEndpoints
{
    public static IEndpointRouteBuilder MapPotsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Registers endpoints:
        // POST /api/accounts/{accountId}/pots      - create a pot for an account
        // GET  /api/accounts/{accountId}/pots      - list pots for an account
        // GET  /api/accounts/{accountId}/pots/{id} - get a single pot
        // POST /api/pots/{potId}/goals             - create a goal on a pot
        // GET  /api/pots/{potId}/goals             - list goals for a pot
        // GET  /api/accounts/{accountId}/running-balances - cumulative balances by date

        endpoints.MapPost("/api/accounts/{accountId}/pots", CreatePotAsync)
            .RequireAuthorization()
            .WithName("CreatePot")
            .WithTags("Pots");

        endpoints.MapGet("/api/accounts/{accountId}/pots", ListPotsAsync)
            .RequireAuthorization()
            .WithName("ListPots")
            .WithTags("Pots");

        endpoints.MapGet("/api/accounts/{accountId}/pots/{id}", GetPotAsync)
            .RequireAuthorization()
            .WithName("GetPot")
            .WithTags("Pots");

        endpoints.MapPost("/api/pots/{potId}/goals", CreateGoalAsync)
            .RequireAuthorization()
            .WithName("CreateGoal")
            .WithTags("Goals");

        endpoints.MapGet("/api/pots/{potId}/goals", ListGoalsAsync)
            .RequireAuthorization()
            .WithName("ListGoals")
            .WithTags("Goals");

        // Running balances for an account (cumulative by date)
        endpoints.MapGet("/api/accounts/{accountId}/running-balances", GetRunningBalancesAsync)
            .RequireAuthorization()
            .WithName("GetRunningBalances")
            .WithTags("Accounts");

        return endpoints;
    }

    /// <summary>
    /// Request payload to create a new pot.
    /// </summary>
    public sealed record CreatePotRequest(string Name);

    /// <summary>
    /// Public representation of a pot returned by the API.
    /// </summary>
    public sealed record PotResponse(Guid Id, string Name, Guid AccountId);

    /// <summary>
    /// Request payload to create a simple savings goal attached to a pot.
    /// </summary>
    public sealed record CreateGoalRequest(decimal TargetAmount, DateTime? DueDate);

    /// <summary>
    /// Public representation of a goal returned by the API.
    /// </summary>
    public sealed record GoalResponse(Guid Id, decimal TargetAmount, DateTime? DueDate);

    /// <summary>
    /// Response representing a running/cumulative balance for a given date.
    /// </summary>
    public sealed record RunningBalanceResponse(DateTime Date, decimal CumulativeAmount);

    private static string GetUserId(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
           ?? string.Empty;

    /// <summary>
    /// Creates a new pot for the specified account. Caller must own the account.
    /// </summary>
    private static async Task<IResult> CreatePotAsync(Guid accountId, CreatePotRequest request, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var pot = new Pot { Name = request.Name, AccountId = accountId };
        db.Pots.Add(pot);
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{accountId}/pots/{pot.Id}", new PotResponse(pot.Id, pot.Name, pot.AccountId));
    }

    /// <summary>
    /// Lists pots for the specified account. Caller must own the account.
    /// </summary>
    private static async Task<IResult> ListPotsAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var pots = await db.Pots
            .Where(p => p.AccountId == accountId)
            .Select(p => new PotResponse(p.Id, p.Name, p.AccountId))
            .ToListAsync();

        return Results.Ok(pots);
    }

    /// <summary>
    /// Returns a single pot by id for the specified account. Caller must own the account.
    /// </summary>
    private static async Task<IResult> GetPotAsync(Guid accountId, Guid id, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var pot = await db.Pots.FindAsync(id);
        if (pot is null || pot.AccountId != accountId) return Results.NotFound();

        return Results.Ok(new PotResponse(pot.Id, pot.Name, pot.AccountId));
    }

    /// <summary>
    /// Creates a goal attached to the specified pot. Caller must own the parent account.
    /// </summary>
    private static async Task<IResult> CreateGoalAsync(Guid potId, CreateGoalRequest request, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);

        var pot = await db.Pots.Include(p => p.Account).FirstOrDefaultAsync(p => p.Id == potId);
        if (pot is null || pot.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var goal = new Goal { PotId = potId, TargetAmount = request.TargetAmount, DueDate = request.DueDate };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();

        return Results.Created($"/api/pots/{potId}/goals/{goal.Id}", new GoalResponse(goal.Id, goal.TargetAmount, goal.DueDate));
    }

    /// <summary>
    /// Lists goals for a pot. Caller must own the parent account.
    /// </summary>
    private static async Task<IResult> ListGoalsAsync(Guid potId, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var pot = await db.Pots.Include(p => p.Account).FirstOrDefaultAsync(p => p.Id == potId);
        if (pot is null || pot.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var goals = await db.Goals
            .Where(g => g.PotId == potId)
            .Select(g => new GoalResponse(g.Id, g.TargetAmount, g.DueDate))
            .ToListAsync();

        return Results.Ok(goals);
    }

    /// <summary>
    /// Returns running (cumulative) balances for the account grouped by date.
    /// Uses account-level balances; pot-specific balances are not included.
    /// Caller must own the account.
    /// </summary>
    private static async Task<IResult> GetRunningBalancesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        // Aggregate balances by date and compute cumulative total
        var grouped = await db.Balances
            .Where(b => b.AccountId == accountId)
            .GroupBy(b => b.Date.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(b => b.Amount) })
            .OrderBy(x => x.Date)
            .ToListAsync();

        var result = new List<RunningBalanceResponse>();
        decimal cumulative = 0m;
        foreach (var item in grouped)
        {
            cumulative += item.Amount;
            result.Add(new RunningBalanceResponse(item.Date, cumulative));
        }

        return Results.Ok(result);
    }
}
