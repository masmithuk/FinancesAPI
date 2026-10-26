using System.ComponentModel.DataAnnotations;
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

        endpoints.MapPut("/api/accounts/{accountId}/pots/{id}", UpdatePotAsync)
            .RequireAuthorization()
            .WithName("UpdatePot")
            .WithTags("Pots")
            .WithSummary("Update a pot's name or account");

        endpoints.MapDelete("/api/accounts/{accountId}/pots/{id}", DeletePotAsync)
            .RequireAuthorization()
            .WithName("DeletePot")
            .WithTags("Pots")
            .WithSummary("Delete a pot and its goals");

        endpoints.MapPost("/api/pots/{potId}/goals", CreateGoalAsync)
            .RequireAuthorization()
            .WithName("CreateGoal")
            .WithTags("Goals");

        endpoints.MapGet("/api/pots/{potId}/goals", ListGoalsAsync)
            .RequireAuthorization()
            .WithName("ListGoals")
            .WithTags("Goals");

        endpoints.MapPut("/api/pots/{potId}/goals/{id}", UpdateGoalAsync)
            .RequireAuthorization()
            .WithName("UpdateGoal")
            .WithTags("Goals")
            .WithSummary("Update a savings goal");

        endpoints.MapDelete("/api/pots/{potId}/goals/{id}", DeleteGoalAsync)
            .RequireAuthorization()
            .WithName("DeleteGoal")
            .WithTags("Goals")
            .WithSummary("Delete a savings goal");

        endpoints.MapGet("/api/pots/{potId}/contributions", ListContributionsAsync)
            .RequireAuthorization()
            .WithName("ListPotContributions")
            .WithTags("Pots")
            .WithSummary("List contributions made to a pot");

        endpoints.MapPost("/api/pots/{potId}/contributions", CreateContributionAsync)
            .RequireAuthorization()
            .WithName("CreatePotContribution")
            .WithTags("Pots")
            .WithSummary("Add money to a pot, optionally debiting a selected account");

        endpoints.MapPut("/api/pots/{potId}/contributions/{id}", UpdateContributionAsync)
            .RequireAuthorization()
            .WithName("UpdatePotContribution")
            .WithTags("Pots")
            .WithSummary("Update a contribution and its linked account debit");

        endpoints.MapDelete("/api/pots/{potId}/contributions/{id}", DeleteContributionAsync)
            .RequireAuthorization()
            .WithName("DeletePotContribution")
            .WithTags("Pots")
            .WithSummary("Delete a contribution and its linked account debit");

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
    public sealed record CreatePotRequest([Required, MaxLength(100)] string Name);

    /// <summary>Request payload to update a pot's name and owning account.</summary>
    public sealed record UpdatePotRequest([Required, MaxLength(100)] string Name, Guid AccountId);

    /// <summary>
    /// Public representation of a pot returned by the API.
    /// </summary>
    public sealed record PotResponse(Guid Id, string Name, Guid AccountId);

    /// <summary>
    /// Request payload to create a simple savings goal attached to a pot.
    /// </summary>
    public sealed record CreateGoalRequest([Range(typeof(decimal), "0.01", "999999999")] decimal TargetAmount, DateTime? DueDate);

    /// <summary>Request payload to update a savings goal.</summary>
    public sealed record UpdateGoalRequest([Range(typeof(decimal), "0.01", "999999999")] decimal TargetAmount, DateTime? DueDate);

    /// <summary>Request payload for adding money to a pot, optionally from an account.</summary>
    public sealed record CreateContributionRequest(Guid? AccountId, [Range(typeof(decimal), "-999999999", "999999999")] decimal Amount, DateTime Date);

    /// <summary>Request payload for updating a pot contribution and its optional source account.</summary>
    public sealed record UpdateContributionRequest(Guid? AccountId, [Range(typeof(decimal), "-999999999", "999999999")] decimal Amount, DateTime Date);

    /// <summary>Represents a contribution made to a pot, optionally from an account.</summary>
    public sealed record ContributionResponse(
        Guid Id,
        Guid? AccountId,
        string? AccountName,
        decimal Amount,
        DateTime Date);

    /// <summary>
    /// Public representation of a goal returned by the API.
    /// </summary>
    public sealed record GoalResponse(Guid Id, decimal TargetAmount, DateTime? DueDate);

    /// <summary>
    /// Response representing a running/cumulative balance for a given date.
    /// </summary>
    public sealed record RunningBalanceResponse(DateTime Date, decimal CumulativeAmount, decimal Change = 0m, int Entries = 0);

    // User id extraction handled by IUserContextService

    /// <summary>
    /// Creates a new pot for the specified account. Caller must own the account.
    /// </summary>
    private static async Task<IResult> CreatePotAsync(Guid accountId, CreatePotRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
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
    private static async Task<IResult> ListPotsAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
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
    private static async Task<IResult> GetPotAsync(Guid accountId, Guid id, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var pot = await db.Pots.FindAsync(id);
        if (pot is null || pot.AccountId != accountId) return Results.NotFound();

        return Results.Ok(new PotResponse(pot.Id, pot.Name, pot.AccountId));
    }

    private static async Task<IResult> UpdatePotAsync(
        Guid accountId,
        Guid id,
        UpdatePotRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { detail = "Provide a pot name." });

        var sourceAccount = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (sourceAccount is null || sourceAccount.OwnerId != ownerId) return Results.NotFound();

        var pot = await db.Pots.FirstOrDefaultAsync(
            item => item.Id == id && item.AccountId == accountId,
            cancellationToken);
        if (pot is null) return Results.NotFound();

        var targetAccount = await db.Accounts.FindAsync([request.AccountId], cancellationToken);
        if (targetAccount is null || targetAccount.OwnerId != ownerId) return Results.NotFound();

        pot.Name = request.Name.Trim();
        pot.AccountId = targetAccount.Id;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new PotResponse(pot.Id, pot.Name, pot.AccountId));
    }

    private static async Task<IResult> DeletePotAsync(
        Guid accountId,
        Guid id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var pot = await db.Pots.FirstOrDefaultAsync(
            item => item.Id == id && item.AccountId == accountId,
            cancellationToken);
        if (pot is null) return Results.NotFound();

        var contributions = await db.PotContributions
            .Include(item => item.Balance)
            .Where(item => item.PotId == id)
            .ToListAsync(cancellationToken);
        db.PotContributions.RemoveRange(contributions);
        db.Balances.RemoveRange(contributions
            .Where(item => item.Balance is not null)
            .Select(item => item.Balance!));
        db.Pots.Remove(pot);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Creates a goal attached to the specified pot. Caller must own the parent account.
    /// </summary>
    private static async Task<IResult> CreateGoalAsync(Guid potId, CreateGoalRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);

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
    private static async Task<IResult> ListGoalsAsync(Guid potId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var pot = await db.Pots.Include(p => p.Account).FirstOrDefaultAsync(p => p.Id == potId);
        if (pot is null || pot.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var goals = await db.Goals
            .Where(g => g.PotId == potId)
            .Select(g => new GoalResponse(g.Id, g.TargetAmount, g.DueDate))
            .ToListAsync();

        return Results.Ok(goals);
    }

    private static async Task<IResult> UpdateGoalAsync(
        Guid potId,
        Guid id,
        UpdateGoalRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
        if (request.TargetAmount <= 0) return Results.BadRequest(new { detail = "The target amount must be greater than zero." });

        var pot = await db.Pots
            .Include(item => item.Account)
            .FirstOrDefaultAsync(item => item.Id == potId, cancellationToken);
        if (pot?.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var goal = await db.Goals.FirstOrDefaultAsync(
            item => item.Id == id && item.PotId == potId,
            cancellationToken);
        if (goal is null) return Results.NotFound();

        goal.TargetAmount = request.TargetAmount;
        goal.DueDate = request.DueDate?.Date;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new GoalResponse(goal.Id, goal.TargetAmount, goal.DueDate));
    }

    private static async Task<IResult> DeleteGoalAsync(
        Guid potId,
        Guid id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var pot = await db.Pots
            .Include(item => item.Account)
            .FirstOrDefaultAsync(item => item.Id == potId, cancellationToken);
        if (pot?.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var goal = await db.Goals.FirstOrDefaultAsync(
            item => item.Id == id && item.PotId == potId,
            cancellationToken);
        if (goal is null) return Results.NotFound();

        db.Goals.Remove(goal);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListContributionsAsync(
        Guid potId,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        var pot = await db.Pots
            .Include(item => item.Account)
            .FirstOrDefaultAsync(item => item.Id == potId, cancellationToken);
        if (pot?.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var contributions = await db.PotContributions
            .Where(item => item.PotId == potId)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .Select(item => new ContributionResponse(
                item.Id,
                item.AccountId,
                item.Account == null ? null : item.Account.Name,
                item.Amount,
                item.Date))
            .ToListAsync(cancellationToken);

        return Results.Ok(contributions);
    }

    private static async Task<IResult> CreateContributionAsync(
        Guid potId,
        CreateContributionRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
        if (request.Amount <= 0 || request.Date == default)
            return Results.BadRequest(new { detail = "Provide a positive amount and a valid date." });

        var pot = await db.Pots
            .Include(item => item.Account)
            .FirstOrDefaultAsync(item => item.Id == potId, cancellationToken);
        if (pot?.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var sourceAccount = request.AccountId is Guid accountId
            ? await db.Accounts.FirstOrDefaultAsync(
                item => item.Id == accountId && item.OwnerId == ownerId,
                cancellationToken)
            : null;
        if (request.AccountId is not null && sourceAccount is null) return Results.NotFound();

        var contribution = new PotContribution
        {
            PotId = potId,
            AccountId = sourceAccount?.Id,
            Amount = request.Amount,
            Date = request.Date.Date
        };
        if (sourceAccount is not null)
        {
            var balance = new Balance
            {
                AccountId = sourceAccount.Id,
                Date = contribution.Date,
                Amount = -contribution.Amount
            };
            contribution.BalanceId = balance.Id;
            db.Balances.Add(balance);
        }

        db.PotContributions.Add(contribution);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/pots/{potId}/contributions/{contribution.Id}",
            new ContributionResponse(
                contribution.Id,
                contribution.AccountId,
                sourceAccount?.Name,
                contribution.Amount,
                contribution.Date));
    }

    private static async Task<IResult> UpdateContributionAsync(
        Guid potId,
        Guid id,
        UpdateContributionRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
        if (request.Amount <= 0 || request.Date == default)
            return Results.BadRequest(new { detail = "Provide a positive amount and a valid date." });

        var pot = await db.Pots
            .Include(item => item.Account)
            .FirstOrDefaultAsync(item => item.Id == potId, cancellationToken);
        if (pot?.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var contribution = await db.PotContributions
            .Include(item => item.Balance)
            .FirstOrDefaultAsync(item => item.Id == id && item.PotId == potId, cancellationToken);
        if (contribution is null) return Results.NotFound();

        var sourceAccount = request.AccountId is Guid accountId
            ? await db.Accounts.FirstOrDefaultAsync(
                item => item.Id == accountId && item.OwnerId == ownerId,
                cancellationToken)
            : null;
        if (request.AccountId is not null && sourceAccount is null) return Results.NotFound();

        contribution.AccountId = sourceAccount?.Id;
        contribution.Amount = request.Amount;
        contribution.Date = request.Date.Date;
        if (sourceAccount is null)
        {
            if (contribution.Balance is not null)
                db.Balances.Remove(contribution.Balance);
            contribution.BalanceId = null;
            contribution.Balance = null;
        }
        else if (contribution.Balance is null)
        {
            var balance = new Balance
            {
                AccountId = sourceAccount.Id,
                Amount = -request.Amount,
                Date = request.Date.Date
            };
            contribution.Balance = balance;
            db.Balances.Add(balance);
        }
        else
        {
            contribution.Balance.AccountId = sourceAccount.Id;
            contribution.Balance.Amount = -request.Amount;
            contribution.Balance.Date = request.Date.Date;
        }
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ContributionResponse(
            contribution.Id,
            contribution.AccountId,
            sourceAccount?.Name,
            contribution.Amount,
            contribution.Date));
    }

    private static async Task<IResult> DeleteContributionAsync(
        Guid potId,
        Guid id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var pot = await db.Pots
            .Include(item => item.Account)
            .FirstOrDefaultAsync(item => item.Id == potId, cancellationToken);
        if (pot?.Account is null || pot.Account.OwnerId != ownerId) return Results.NotFound();

        var contribution = await db.PotContributions
            .Include(item => item.Balance)
            .FirstOrDefaultAsync(item => item.Id == id && item.PotId == potId, cancellationToken);
        if (contribution is null) return Results.NotFound();

        if (contribution.Balance is not null)
            db.Balances.Remove(contribution.Balance);
        db.PotContributions.Remove(contribution);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Returns running (cumulative) balances for the account grouped by date.
    /// Uses account-level balances; pot-specific balances are not included.
    /// Caller must own the account.
    /// </summary>
    private static async Task<IResult> GetRunningBalancesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        // Aggregate balances by date and compute cumulative total
        var grouped = await db.Balances
            .Where(b => b.AccountId == accountId)
            .GroupBy(b => b.Date.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(b => b.Amount), Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync();

        var result = new List<RunningBalanceResponse>();
        decimal cumulative = 0m;
        foreach (var item in grouped)
        {
            cumulative += item.Amount;
            result.Add(new RunningBalanceResponse(item.Date, cumulative, item.Amount, item.Count));
        }

        return Results.Ok(result);
    }
}
