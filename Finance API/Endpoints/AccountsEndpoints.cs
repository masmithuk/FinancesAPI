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

        // Paged balance history, newest first, so large accounts don't load every row at once.
        endpoints.MapGet("/api/accounts/{id}/balances/history", ListBalanceHistoryAsync)
            .RequireAuthorization()
            .WithName("ListBalanceHistory")
            .WithTags("Accounts")
            .WithSummary("List balance entries a page at a time (skip/take), newest first");

        endpoints.MapPut("/api/accounts/{id}/balances/{balanceId}", UpdateBalanceAsync)
            .RequireAuthorization()
            .WithName("UpdateBalance")
            .WithTags("Accounts")
            .WithSummary("Update a dated account balance");

        endpoints.MapDelete("/api/accounts/{id}/balances/{balanceId}", DeleteBalanceAsync)
            .RequireAuthorization()
            .WithName("DeleteBalance")
            .WithTags("Accounts")
            .WithSummary("Delete a dated account balance");

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
    public sealed record AddBalanceRequest(DateTime Date, decimal Amount, string? Note = null);

    /// <summary>
    /// Payload to update a dated account balance.
    /// </summary>
    public sealed record UpdateBalanceRequest(DateTime Date, decimal Amount, string? Note = null);

    /// <summary>
    /// Public representation of a balance returned by the API.
    /// </summary>
    public sealed record BalanceResponse(Guid Id, DateTime Date, decimal Amount, string? Note = null);

    private const int MaxNoteLength = 200;

    private static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Extracts the current user's id from the ClaimsPrincipal. We prefer
    /// ClaimTypes.NameIdentifier but fall back to the JWT "sub" claim.
    /// </summary>
    // User id extraction handled by IUserContextService

    /// <summary>
    /// Creates a new account for the authenticated user.
    /// </summary>
    private static async Task<IResult> CreateAccountAsync(CreateAccountRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = new Account { Name = request.Name, OwnerId = ownerId };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{account.Id}", new AccountResponse(account.Id, account.Name));
    }

    /// <summary>
    /// Lists accounts that belong to the authenticated user.
    /// </summary>
    private static async Task<IResult> ListAccountsAsync(FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var accounts = await db.Accounts
            .Where(a => a.OwnerId == ownerId)
            .Select(a => new AccountResponse(a.Id, a.Name))
            .ToListAsync();

        return Results.Ok(accounts);
    }

    /// <summary>
    /// Retrieves a single account by id if it belongs to the caller.
    /// </summary>
    private static async Task<IResult> GetAccountAsync(Guid id, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var account = await db.Accounts.FindAsync(id);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        return Results.Ok(new AccountResponse(account.Id, account.Name));
    }

    /// <summary>
    /// Adds a dated balance entry to the specified account. Caller must own
    /// the account. The date component of the request is used (time ignored).
    /// </summary>
    private static async Task<IResult> AddBalanceAsync(Guid id, AddBalanceRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var account = await db.Accounts.FindAsync(id);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var note = NormalizeNote(request.Note);
        if (note is { Length: > MaxNoteLength })
            return Results.BadRequest(new { errors = new[] { $"Note must be {MaxNoteLength} characters or fewer." } });

        var balance = new Balance { AccountId = id, Date = request.Date.Date, Amount = request.Amount, Note = note };
        db.Balances.Add(balance);
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{id}/balances/{balance.Id}", new BalanceResponse(balance.Id, balance.Date, balance.Amount, balance.Note));
    }

    /// <summary>
    /// Lists balances for the account, ordered by date (newest first).
    /// </summary>
    private static async Task<IResult> ListBalancesAsync(Guid id, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        var account = await db.Accounts.FindAsync(id);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var balances = await db.Balances
            .Where(b => b.AccountId == id)
            .OrderByDescending(b => b.Date)
            .Select(b => new BalanceResponse(b.Id, b.Date, b.Amount, b.Note))
            .ToListAsync();

        return Results.Ok(balances);
    }

    private const int DefaultHistoryPageSize = 25;
    private const int MaxHistoryPageSize = 100;

    /// <summary>A page of balance entries plus the total number of entries for the account.</summary>
    public sealed record BalanceHistoryPage(List<BalanceResponse> Items, int Total);

    /// <summary>
    /// Lists one page of balances (newest first). <c>take</c> defaults to 25 and is capped at 100.
    /// </summary>
    private static async Task<IResult> ListBalanceHistoryAsync(Guid id, int? skip, int? take, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        var account = await db.Accounts.FindAsync([id], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var offset = Math.Max(0, skip ?? 0);
        var size = Math.Clamp(take ?? DefaultHistoryPageSize, 1, MaxHistoryPageSize);

        var query = db.Balances.Where(b => b.AccountId == id);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => b.Id)
            .Skip(offset)
            .Take(size)
            .Select(b => new BalanceResponse(b.Id, b.Date, b.Amount, b.Note))
            .ToListAsync(cancellationToken);

        return Results.Ok(new BalanceHistoryPage(items, total));
    }

    /// <summary>
    /// Updates a dated balance entry for an account owned by the caller.
    /// </summary>
    private static async Task<IResult> UpdateBalanceAsync(
        Guid id,
        Guid balanceId,
        UpdateBalanceRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([id], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var balance = await db.Balances.FirstOrDefaultAsync(
            item => item.Id == balanceId && item.AccountId == id,
            cancellationToken);
        if (balance is null) return Results.NotFound();
        if (await db.PotContributions.AnyAsync(item => item.BalanceId == balanceId, cancellationToken))
            return Results.Conflict(new { detail = "Update this balance through its linked pot contribution." });

        var note = NormalizeNote(request.Note);
        if (note is { Length: > MaxNoteLength })
            return Results.BadRequest(new { errors = new[] { $"Note must be {MaxNoteLength} characters or fewer." } });

        balance.Date = request.Date.Date;
        balance.Amount = request.Amount;
        balance.Note = note;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new BalanceResponse(balance.Id, balance.Date, balance.Amount, balance.Note));
    }

    /// <summary>
    /// Deletes a dated balance entry from an account owned by the caller.
    /// </summary>
    private static async Task<IResult> DeleteBalanceAsync(
        Guid id,
        Guid balanceId,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([id], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var balance = await db.Balances.FirstOrDefaultAsync(
            item => item.Id == balanceId && item.AccountId == id,
            cancellationToken);
        if (balance is null) return Results.NotFound();
        if (await db.PotContributions.AnyAsync(item => item.BalanceId == balanceId, cancellationToken))
            return Results.Conflict(new { detail = "Delete this balance through its linked pot contribution." });

        db.Balances.Remove(balance);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
