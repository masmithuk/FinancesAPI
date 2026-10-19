using System.Security.Claims;
using Finance_API.Persistence.Entities;
using Finance_API.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Account management endpoints.
///
/// These minimal endpoints demonstrate a per-user account model and
/// dated balances. Endpoints are protected and enforce ownership by
/// comparing the current user's id (from the JWT) with the Account.OwnerId.
/// </summary>

namespace Finance_API.Endpoints;

public static class AccountsEndpoints
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Create a new account owned by the authenticated caller.
        endpoints.MapPost("/api/accounts", CreateAccountAsync)
            .RequireAuthorization()
            .WithName("CreateAccount")
            .WithTags("Accounts");

        // List accounts belonging to the authenticated caller.
        endpoints.MapGet("/api/accounts", ListAccountsAsync)
            .RequireAuthorization()
            .WithName("ListAccounts")
            .WithTags("Accounts");

        // Get a single account by id if it belongs to the caller.
        endpoints.MapGet("/api/accounts/{id}", GetAccountAsync)
            .RequireAuthorization()
            .WithName("GetAccount")
            .WithTags("Accounts");

        // Add a dated balance to an account the caller owns.
        endpoints.MapPost("/api/accounts/{id}/balances", AddBalanceAsync)
            .RequireAuthorization()
            .WithName("AddBalance")
            .WithTags("Accounts");

        // List balances for a specific account (caller must own the account).
        endpoints.MapGet("/api/accounts/{id}/balances", ListBalancesAsync)
            .RequireAuthorization()
            .WithName("ListBalances")
            .WithTags("Accounts");

        return endpoints;
    }

    /// <summary>
    /// Payload to create a new account.
    /// </summary>
    public sealed record CreateAccountRequest(string Name);

    /// <summary>
    /// Public representation of an account returned by the API.
    /// </summary>
    public sealed record AccountResponse(Guid Id, string Name);

    /// <summary>
    /// Payload to add a dated balance to an account.
    /// </summary>
    public sealed record AddBalanceRequest(DateTime Date, decimal Amount);

    /// <summary>
    /// Public representation of a balance returned by the API.
    /// </summary>
    public sealed record BalanceResponse(Guid Id, DateTime Date, decimal Amount);

    /// <summary>
    /// Extracts the current user's id from the ClaimsPrincipal. We prefer
    /// ClaimTypes.NameIdentifier but fall back to the JWT "sub" claim.
    /// </summary>
    private static string GetUserId(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
           ?? string.Empty;

    /// <summary>
    /// Creates a new account for the authenticated user.
    /// </summary>
    private static async Task<IResult> CreateAccountAsync(CreateAccountRequest request, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = new Account { Name = request.Name, OwnerId = ownerId };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{account.Id}", new AccountResponse(account.Id, account.Name));
    }

    /// <summary>
    /// Lists accounts that belong to the authenticated user.
    /// </summary>
    private static async Task<IResult> ListAccountsAsync(FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var accounts = await db.Accounts
            .Where(a => a.OwnerId == ownerId)
            .Select(a => new AccountResponse(a.Id, a.Name))
            .ToListAsync();

        return Results.Ok(accounts);
    }

    /// <summary>
    /// Retrieves a single account by id if it belongs to the caller.
    /// </summary>
    private static async Task<IResult> GetAccountAsync(Guid id, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var account = await db.Accounts.FindAsync(id);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        return Results.Ok(new AccountResponse(account.Id, account.Name));
    }

    /// <summary>
    /// Adds a dated balance entry to the specified account. Caller must own
    /// the account. The date component of the request is used (time ignored).
    /// </summary>
    private static async Task<IResult> AddBalanceAsync(Guid id, AddBalanceRequest request, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var account = await db.Accounts.FindAsync(id);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var balance = new Balance { AccountId = id, Date = request.Date.Date, Amount = request.Amount };
        db.Balances.Add(balance);
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{id}/balances/{balance.Id}", new BalanceResponse(balance.Id, balance.Date, balance.Amount));
    }

    /// <summary>
    /// Lists balances for the account, ordered by date (newest first).
    /// </summary>
    private static async Task<IResult> ListBalancesAsync(Guid id, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        var account = await db.Accounts.FindAsync(id);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var balances = await db.Balances
            .Where(b => b.AccountId == id)
            .OrderByDescending(b => b.Date)
            .Select(b => new BalanceResponse(b.Id, b.Date, b.Amount))
            .ToListAsync();

        return Results.Ok(balances);
    }
}
