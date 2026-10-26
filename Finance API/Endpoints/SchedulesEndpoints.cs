using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Endpoints;

/// <summary>
/// Minimal endpoints to create and list recurring scheduled income/expenses
/// for an Account. These endpoints are intentionally small: they enforce
/// ownership (the authenticated user must own the parent account) and expose
/// only the fields necessary for the scheduling story in issue #5.
///
/// Future work will add occurrence generation, marking occurrences as paid/received,
/// and richer recurrence rules.
/// </summary>
public static class SchedulesEndpoints
{
    public static IEndpointRouteBuilder MapSchedulesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{accountId}/schedules", CreateScheduleAsync)
            .RequireAuthorization()
            .WithName("CreateSchedule")
            .WithTags("Schedules");

        endpoints.MapGet("/api/accounts/{accountId}/schedules", ListSchedulesAsync)
            .RequireAuthorization()
            .WithName("ListSchedules")
            .WithTags("Schedules");

        endpoints.MapPut("/api/accounts/{accountId}/schedules/{id}", UpdateScheduleAsync)
            .RequireAuthorization()
            .WithName("UpdateSchedule")
            .WithTags("Schedules")
            .WithSummary("Update a recurring income or expense schedule");

        endpoints.MapDelete("/api/accounts/{accountId}/schedules/{id}", DeleteScheduleAsync)
            .RequireAuthorization()
            .WithName("DeleteSchedule")
            .WithTags("Schedules")
            .WithSummary("Delete a recurring income or expense schedule");

        return endpoints;
    }

    public sealed record CreateScheduleRequest(string Name, decimal Amount, DateTime StartDate, int Frequency, bool IsIncome);
    /// <summary>Fields for updating a recurring income or expense schedule.</summary>
    public sealed record UpdateScheduleRequest
    {
        public required Guid AccountId { get; init; }

        [Required, MaxLength(100)]
        public required string Name { get; init; }

        [Range(typeof(decimal), "0.01", "999999999")]
        public required decimal Amount { get; init; }

        public required DateTime StartDate { get; init; }

        [Range(0, 3)]
        public required int Frequency { get; init; }

        public required bool IsIncome { get; init; }
    }
    public sealed record ScheduleResponse(Guid Id, string Name, decimal Amount, DateTime StartDate, int Frequency, bool IsIncome);

    // User id extraction handled by IUserContextService

    private static async Task<IResult> CreateScheduleAsync(Guid accountId, CreateScheduleRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var schedule = new ScheduledItem
        {
            Name = request.Name,
            AccountId = accountId,
            Amount = request.Amount,
            StartDate = request.StartDate.Date,
            Frequency = request.Frequency,
            IsIncome = request.IsIncome
        };

        db.ScheduledItems.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/accounts/{accountId}/schedules/{schedule.Id}", new ScheduleResponse(schedule.Id, schedule.Name, schedule.Amount, schedule.StartDate, schedule.Frequency, schedule.IsIncome));
    }

    private static async Task<IResult> ListSchedulesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext, CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var schedules = await db.ScheduledItems
            .Where(s => s.AccountId == accountId)
            .Select(s => new ScheduleResponse(s.Id, s.Name, s.Amount, s.StartDate, s.Frequency, s.IsIncome))
            .ToListAsync(cancellationToken);

        return Results.Ok(schedules);
    }

    private static async Task<IResult> UpdateScheduleAsync(
        Guid accountId,
        Guid id,
        UpdateScheduleRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var sourceAccount = await db.Accounts.FindAsync([accountId], cancellationToken);
        if (sourceAccount is null || sourceAccount.OwnerId != ownerId) return Results.NotFound();

        var schedule = await db.ScheduledItems.FirstOrDefaultAsync(
            item => item.Id == id && item.AccountId == accountId,
            cancellationToken);
        if (schedule is null) return Results.NotFound();

        var targetAccount = await db.Accounts.FindAsync([request.AccountId], cancellationToken);
        if (targetAccount is null || targetAccount.OwnerId != ownerId) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Amount <= 0 || request.Frequency is < 0 or > 3)
        {
            return Results.BadRequest(new { detail = "Provide a name, a positive amount, and a supported frequency." });
        }

        schedule.AccountId = request.AccountId;
        schedule.Name = request.Name.Trim();
        schedule.Amount = request.Amount;
        schedule.StartDate = request.StartDate.Date;
        schedule.Frequency = request.Frequency;
        schedule.IsIncome = request.IsIncome;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ScheduleResponse(
            schedule.Id,
            schedule.Name,
            schedule.Amount,
            schedule.StartDate,
            schedule.Frequency,
            schedule.IsIncome));
    }

    private static async Task<IResult> DeleteScheduleAsync(
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

        var schedule = await db.ScheduledItems.FirstOrDefaultAsync(
            item => item.Id == id && item.AccountId == accountId,
            cancellationToken);
        if (schedule is null) return Results.NotFound();

        // Keep linked activity, but detach it from the deleted plan.
        var linked = await db.Occurrences.Where(o => o.ScheduledItemId == id).ToListAsync(cancellationToken);
        foreach (var occurrence in linked)
        {
            occurrence.ScheduledItemId = null;
            occurrence.PlanDate = null;
        }

        db.ScheduledItems.Remove(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
