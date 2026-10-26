using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
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

        endpoints.MapPut("/api/accounts/{accountId}/occurrences/{id}", UpdateOccurrenceAsync)
            .RequireAuthorization()
            .WithName("UpdateOccurrence")
            .WithTags("Occurrences")
            .WithSummary("Update a recorded income or expense");

        endpoints.MapDelete("/api/accounts/{accountId}/occurrences/{id}", DeleteOccurrenceAsync)
            .RequireAuthorization()
            .WithName("DeleteOccurrence")
            .WithTags("Occurrences")
            .WithSummary("Delete a recorded income or expense");

        return endpoints;
    }

    public sealed record CreateOccurrenceRequest(DateTime Date, decimal Amount, bool IsIncome, Guid? ScheduledItemId, string? ExternalReference);
    /// <summary>Fields for updating a recorded income or expense.</summary>
    public sealed record UpdateOccurrenceRequest
    {
        public required Guid AccountId { get; init; }

        public required DateTime Date { get; init; }

        [Range(typeof(decimal), "0.01", "999999999")]
        public required decimal Amount { get; init; }

        public required bool IsIncome { get; init; }

        [MaxLength(250)]
        public string? ExternalReference { get; init; }
    }
    public sealed record OccurrenceResponse(Guid Id, DateTime Date, decimal Amount, bool IsIncome, bool IsReconciled, string? ExternalReference);

    // User id extraction handled by IUserContextService

    private static async Task<IResult> CreateOccurrenceAsync(Guid accountId, CreateOccurrenceRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        // Idempotency: prefer ExternalReference if provided
        if (!string.IsNullOrEmpty(request.ExternalReference))
        {
            var existing = await db.Occurrences.FirstOrDefaultAsync(
                o => o.ExternalReference == request.ExternalReference,
                cancellationToken);
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
        ApplyIfDue(db, occ);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/accounts/{accountId}/occurrences/{occ.Id}", new OccurrenceResponse(occ.Id, occ.Date, occ.Amount, occ.IsIncome, occ.IsReconciled, occ.ExternalReference));
    }

    // Activity is applied to the account balance automatically once its date has arrived.
    private static void ApplyIfDue(FinanceDbContext db, Occurrence occ)
    {
        if (occ.IsReconciled || occ.Date.Date > DateTime.UtcNow.Date) return;
        db.Balances.Add(new Balance { AccountId = occ.AccountId, Date = occ.Date, Amount = occ.IsIncome ? occ.Amount : -occ.Amount, Note = OccurrenceNote(occ) });
        occ.IsReconciled = true;
    }

    private static string OccurrenceNote(Occurrence occ)
    {
        var label = string.IsNullOrWhiteSpace(occ.ExternalReference) ? (occ.IsIncome ? "Income" : "Expense") : occ.ExternalReference.Trim();
        return label.Length > 200 ? label[..200] : label;
    }

    private static async Task<IResult> ListOccurrencesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var today = DateTime.UtcNow.Date;
        var due = await db.Occurrences
            .Where(o => o.AccountId == accountId && !o.IsReconciled && o.Date <= today)
            .ToListAsync(cancellationToken);
        if (due.Count > 0)
        {
            foreach (var item in due) ApplyIfDue(db, item);
            await db.SaveChangesAsync(cancellationToken);
        }

        var list = await db.Occurrences
            .Where(o => o.AccountId == accountId)
            .OrderByDescending(o => o.Date)
            .Select(o => new OccurrenceResponse(o.Id, o.Date, o.Amount, o.IsIncome, o.IsReconciled, o.ExternalReference))
            .ToListAsync(cancellationToken);

        return Results.Ok(list);
    }

    private static async Task<IResult> ReconcileOccurrenceAsync(Guid accountId, Guid id, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var occ = await db.Occurrences.FindAsync([id], cancellationToken);
        if (occ is null || occ.AccountId != accountId) return Results.NotFound();

        if (occ.IsReconciled)
        {
            // Already reconciled - idempotent
            return Results.Ok(new OccurrenceResponse(occ.Id, occ.Date, occ.Amount, occ.IsIncome, occ.IsReconciled, occ.ExternalReference));
        }

        // Apply to balances exactly once
        var balanceAmount = occ.IsIncome ? occ.Amount : -occ.Amount;
        var balance = new Balance { AccountId = accountId, Date = occ.Date, Amount = balanceAmount, Note = OccurrenceNote(occ) };
        db.Balances.Add(balance);

        occ.IsReconciled = true;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new OccurrenceResponse(occ.Id, occ.Date, occ.Amount, occ.IsIncome, occ.IsReconciled, occ.ExternalReference));
    }

    private static async Task<IResult> UpdateOccurrenceAsync(
        Guid accountId,
        Guid id,
        UpdateOccurrenceRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var sourceAccount = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (sourceAccount is null || sourceAccount.OwnerId != ownerId) return Results.NotFound();

        var occurrence = await db.Occurrences.FirstOrDefaultAsync(
            item => item.Id == id && item.AccountId == accountId,
            cancellationToken);
        if (occurrence is null) return Results.NotFound();

        var targetAccount = await db.Accounts.FindAsync([request.AccountId], cancellationToken);
        if (targetAccount is null || targetAccount.OwnerId != ownerId) return Results.NotFound();
        if (request.Amount <= 0)
        {
            return Results.BadRequest(new { detail = "The amount must be positive." });
        }

        if (!string.IsNullOrEmpty(request.ExternalReference)
            && await db.Occurrences.AnyAsync(
                item => item.ExternalReference == request.ExternalReference && item.Id != id,
                cancellationToken))
        {
            return Results.Conflict(new { detail = "That description reference is already in use." });
        }

        if (occurrence.IsReconciled)
        {
            AddBalanceCorrection(
                db,
                occurrence.AccountId,
                occurrence.Date,
                -SignedAmount(occurrence.Amount, occurrence.IsIncome));
            AddBalanceCorrection(
                db,
                request.AccountId,
                request.Date.Date,
                SignedAmount(request.Amount, request.IsIncome));
        }

        occurrence.AccountId = request.AccountId;
        occurrence.Date = request.Date.Date;
        occurrence.Amount = request.Amount;
        occurrence.IsIncome = request.IsIncome;
        occurrence.ExternalReference = request.ExternalReference;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new OccurrenceResponse(
            occurrence.Id,
            occurrence.Date,
            occurrence.Amount,
            occurrence.IsIncome,
            occurrence.IsReconciled,
            occurrence.ExternalReference));
    }

    private static async Task<IResult> DeleteOccurrenceAsync(
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

        var occurrence = await db.Occurrences.FirstOrDefaultAsync(
            item => item.Id == id && item.AccountId == accountId,
            cancellationToken);
        if (occurrence is null) return Results.NotFound();

        if (occurrence.IsReconciled)
        {
            AddBalanceCorrection(
                db,
                occurrence.AccountId,
                occurrence.Date,
                -SignedAmount(occurrence.Amount, occurrence.IsIncome));
        }

        db.Occurrences.Remove(occurrence);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static decimal SignedAmount(decimal amount, bool isIncome) => isIncome ? amount : -amount;

    private static void AddBalanceCorrection(
        FinanceDbContext db,
        Guid accountId,
        DateTime date,
        decimal amount)
    {
        db.Balances.Add(new Balance { AccountId = accountId, Date = date.Date, Amount = amount, Note = "Correction" });
    }
}
