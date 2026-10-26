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

        endpoints.MapGet("/api/accounts/{accountId}/occurrences/page", ListOccurrencePageAsync)
            .RequireAuthorization()
            .WithName("ListOccurrencePage")
            .WithTags("Occurrences")
            .WithSummary("List activity a page at a time (skip/take), newest first");

        endpoints.MapGet("/api/accounts/{accountId}/occurrences/plan-links", ListPlanLinksAsync)
            .RequireAuthorization()
            .WithName("ListOccurrencePlanLinks")
            .WithTags("Occurrences")
            .WithSummary("List the plan occurrences already paid by linked activity");

        endpoints.MapPost("/api/accounts/{accountId}/occurrences/import", ImportOccurrencesAsync)
            .RequireAuthorization()
            .WithName("ImportOccurrences")
            .WithTags("Occurrences")
            .WithSummary("Bulk import bank statement rows as activity (idempotent per account)");

        endpoints.MapPost("/api/accounts/{accountId}/occurrences/{id}/reconcile", ReconcileOccurrenceAsync)
            .RequireAuthorization()
            .WithName("ReconcileOccurrence")
            .WithTags("Occurrences");

        endpoints.MapPut("/api/accounts/{accountId}/occurrences/{id}", UpdateOccurrenceAsync)
            .RequireAuthorization()
            .WithName("UpdateOccurrence")
            .WithTags("Occurrences")
            .WithSummary("Update a recorded income or expense");

        endpoints.MapPut("/api/accounts/{accountId}/occurrences/{id}/plan", LinkPlanAsync)
            .RequireAuthorization()
            .WithName("LinkOccurrencePlan")
            .WithTags("Occurrences")
            .WithSummary("Link an activity to the plan occurrence it pays, or unlink it");

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
    public sealed record OccurrenceResponse(Guid Id, DateTime Date, decimal Amount, bool IsIncome, bool IsReconciled,     string? ExternalReference, string? Description = null, Guid? ScheduledItemId = null, DateTime? PlanDate = null);

        public sealed record ImportRow(DateTime Date, decimal Amount, string? Name, string? ImportId, Guid? ScheduledItemId = null, DateTime? PlanDate = null);
        public sealed record LinkPlanRequest(Guid? ScheduledItemId, DateTime? PlanDate);
    public sealed record ImportRequest(List<ImportRow>? Rows);
    public sealed record ImportResult(int Imported, int Duplicates, int Skipped);

    private const int MaxImportRows = 20000;

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
        var raw = !string.IsNullOrWhiteSpace(occ.Description) ? occ.Description : occ.ExternalReference;
        var label = string.IsNullOrWhiteSpace(raw) ? (occ.IsIncome ? "Income" : "Expense") : raw.Trim();
        return label.Length > 200 ? label[..200] : label;
    }

    private static async Task ApplyDueAsync(FinanceDbContext db, Guid accountId, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var due = await db.Occurrences
            .Where(o => o.AccountId == accountId && !o.IsReconciled && o.Date <= today)
            .ToListAsync(cancellationToken);
        if (due.Count == 0) return;
        foreach (var item in due) ApplyIfDue(db, item);
        await db.SaveChangesAsync(cancellationToken);
    }

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    /// <summary>A page of activity plus the total number of activity items for the account.</summary>
    public sealed record OccurrencePage(List<OccurrenceResponse> Items, int Total);

    public sealed record PlanLink(Guid ScheduledItemId, DateTime PlanDate);

    private static async Task<IResult> ListOccurrencePageAsync(Guid accountId, int? skip, int? take, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        await ApplyDueAsync(db, accountId, cancellationToken);

        var offset = Math.Max(0, skip ?? 0);
        var size = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
        var query = db.Occurrences.Where(o => o.AccountId == accountId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.Date)
            .ThenByDescending(o => o.Id)
            .Skip(offset)
            .Take(size)
            .Select(o => new OccurrenceResponse(o.Id, o.Date, o.Amount, o.IsIncome, o.IsReconciled, o.ExternalReference, o.Description, o.ScheduledItemId, o.PlanDate))
            .ToListAsync(cancellationToken);

        return Results.Ok(new OccurrencePage(items, total));
    }

    // Plan occurrences already paid by linked activity, so imports don't suggest them again.
    private static async Task<IResult> ListPlanLinksAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var links = await db.Occurrences
            .Where(o => o.AccountId == accountId && o.ScheduledItemId != null)
            .Select(o => new PlanLink(o.ScheduledItemId!.Value, o.PlanDate ?? o.Date))
            .Distinct()
            .ToListAsync(cancellationToken);

        return Results.Ok(links);
    }

    private static async Task<IResult> ListOccurrencesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        await ApplyDueAsync(db, accountId, cancellationToken);

        var list = await db.Occurrences
            .Where(o => o.AccountId == accountId)
            .OrderByDescending(o => o.Date)
            .Select(o => new OccurrenceResponse(o.Id, o.Date, o.Amount, o.IsIncome, o.IsReconciled, o.ExternalReference, o.Description, o.ScheduledItemId, o.PlanDate))
            .ToListAsync(cancellationToken);

        return Results.Ok(list);
    }

    private static async Task<IResult> ImportOccurrencesAsync(Guid accountId, ImportRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var rows = request.Rows;
        if (rows is null || rows.Count == 0)
            return Results.BadRequest(new { errors = new[] { "No rows to import." } });
        if (rows.Count > MaxImportRows)
            return Results.BadRequest(new { errors = new[] { $"Import at most {MaxImportRows} rows at a time." } });

        var existingKeys = (await db.Occurrences
            .Where(o => o.AccountId == accountId && o.ImportId != null)
            .Select(o => o.ImportId!)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var planIds = (await db.ScheduledItems
            .Where(s => s.AccountId == accountId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var minDate = new DateTime(1990, 1, 1);
        var maxDate = DateTime.UtcNow.Date.AddYears(1);
        var repeats = new Dictionary<string, int>(StringComparer.Ordinal);
        int imported = 0, duplicates = 0, skipped = 0;

        foreach (var row in rows)
        {
            var date = row.Date.Date;
            if (row.Amount == 0 || Math.Abs(row.Amount) >= 1_000_000_000m || date < minDate || date > maxDate)
            {
                skipped++;
                continue;
            }

            var name = row.Name?.Trim();
            if (string.IsNullOrEmpty(name)) name = null;
            else if (name.Length > 250) name = name[..250];

            var key = row.ImportId?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                // No bank id: derive one from the content, counting identical rows so genuine repeats are kept.
                var content = $"{date:yyyy-MM-dd}|{row.Amount}|{name}";
                var n = repeats.GetValueOrDefault(content);
                repeats[content] = n + 1;
                key = "h:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{content}|{n}")))[..32];
            }
            else if (key.Length > 100)
            {
                key = key[..100];
            }

            if (!existingKeys.Add(key))
            {
                duplicates++;
                continue;
            }

            var linked = row.ScheduledItemId is { } planId && planIds.Contains(planId);
            var occurrence = new Occurrence
            {
                AccountId = accountId,
                Date = date,
                Amount = Math.Abs(row.Amount),
                IsIncome = row.Amount > 0,
                Description = name,
                ImportId = key,
                ScheduledItemId = linked ? row.ScheduledItemId : null,
                PlanDate = linked ? (row.PlanDate ?? date).Date : null
            };
            db.Occurrences.Add(occurrence);
            ApplyIfDue(db, occurrence);
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ImportResult(imported, duplicates, skipped));
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
            occurrence.ExternalReference,
            occurrence.Description,
            occurrence.ScheduledItemId,
            occurrence.PlanDate));
    }

    private static async Task<IResult> LinkPlanAsync(
        Guid accountId,
        Guid id,
        LinkPlanRequest request,
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

        if (request.ScheduledItemId is null)
        {
            occurrence.ScheduledItemId = null;
            occurrence.PlanDate = null;
        }
        else
        {
            var schedule = await db.ScheduledItems.FirstOrDefaultAsync(
                s => s.Id == request.ScheduledItemId && s.AccountId == accountId,
                cancellationToken);
            if (schedule is null) return Results.NotFound();

            occurrence.ScheduledItemId = schedule.Id;
            occurrence.PlanDate = (request.PlanDate ?? occurrence.Date).Date;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new OccurrenceResponse(
            occurrence.Id,
            occurrence.Date,
            occurrence.Amount,
            occurrence.IsIncome,
            occurrence.IsReconciled,
            occurrence.ExternalReference,
            occurrence.Description,
            occurrence.ScheduledItemId,
            occurrence.PlanDate));
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
