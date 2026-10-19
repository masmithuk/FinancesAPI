using System.Security.Claims;
using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Endpoints;

/// <summary>
/// Endpoints to record actual income/expense occurrences and to reconcile
/// them against account balances exactly once.
///
/// POST /api/accounts/{accountId}/occurrences   - create an occurrence (idempotent via ExternalReference)
/// GET  /api/accounts/{accountId}/occurrences   - list occurrences for an account
/// POST /api/accounts/{accountId}/occurrences/{id}/reconcile - mark occurrence reconciled and apply a Balance once
/// </summary>
public static class OccurrencesEndpoints
{
    public static IEndpointRouteBuilder MapOccurrencesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{accountId}/occurrences", CreateOccurrenceAsync)
            .RequireAuthorization()
            .WithName("CreateOccurrence")
            .WithTags("Occurrences");

        endpoints.MapGet("/api/accounts/{accountId}/occurrences", ListOccurrencesAsync)
            .RequireAuthorization()
            .WithName("ListOccurrences")
            .WithTags("Occurrences");

        endpoints.MapPost("/api/accounts/{accountId}/occurrences/{id}/reconcile", ReconcileOccurrenceAsync)
            .RequireAuthorization()
            .WithName("ReconcileOccurrence")
            .WithTags("Occurrences");

        return endpoints;
    }

    public sealed record CreateOccurrenceRequest(DateTime Date, decimal Amount, bool IsIncome, Guid? ScheduledItemId, string? ExternalReference);
    public sealed record OccurrenceResponse(Guid Id, DateTime Date, decimal Amount, bool IsIncome, bool IsReconciled, string? ExternalReference);

    private static string GetUserId(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
           ?? string.Empty;

    private static async Task<IResult> CreateOccurrenceAsync(Guid accountId, CreateOccurrenceRequest request, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        // Idempotency: prefer ExternalReference if provided
        if (!string.IsNullOrEmpty(request.ExternalReference))
        {
            var existing = await db.Occurrences.FirstOrDefaultAsync(o => o.ExternalReference == request.ExternalReference);
            if (existing is not null) return Results.Ok(new OccurrenceResponse(existing.Id, existing.Date, existing.Amount, existing.IsIncome, existing.IsReconciled, existing.ExternalReference));
        }

        var occ = new Occurrence
        {
            AccountId = accountId,
            ScheduledItemId = request.ScheduledItemId,
            Date = request.Date.Date,
            Amount = request.Amount,
            IsIncome = request.IsIncome,
            ExternalReference = request.ExternalReference,
            IsReconciled = false
        };

        db.Occurrences.Add(occ);
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{accountId}/occurrences/{occ.Id}", new OccurrenceResponse(occ.Id, occ.Date, occ.Amount, occ.IsIncome, occ.IsReconciled, occ.ExternalReference));
    }

    private static async Task<IResult> ListOccurrencesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var list = await db.Occurrences
            .Where(o => o.AccountId == accountId)
            .OrderByDescending(o => o.Date)
            .Select(o => new OccurrenceResponse(o.Id, o.Date, o.Amount, o.IsIncome, o.IsReconciled, o.ExternalReference))
            .ToListAsync();

        return Results.Ok(list);
    }

    private static async Task<IResult> ReconcileOccurrenceAsync(Guid accountId, Guid id, FinanceDbContext db, ClaimsPrincipal user)
    {
        var ownerId = GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var occ = await db.Occurrences.FindAsync(id);
        if (occ is null || occ.AccountId != accountId) return Results.NotFound();

        if (occ.IsReconciled)
        {
            // Already reconciled - idempotent
            return Results.Ok(new OccurrenceResponse(occ.Id, occ.Date, occ.Amount, occ.IsIncome, occ.IsReconciled, occ.ExternalReference));
        }

        // Apply to balances exactly once
        var balanceAmount = occ.IsIncome ? occ.Amount : -occ.Amount;
        var balance = new Balance { AccountId = accountId, Date = occ.Date, Amount = balanceAmount };
        db.Balances.Add(balance);

        occ.IsReconciled = true;
        await db.SaveChangesAsync();

        return Results.Ok(new OccurrenceResponse(occ.Id, occ.Date, occ.Amount, occ.IsIncome, occ.IsReconciled, occ.ExternalReference));
    }
}
