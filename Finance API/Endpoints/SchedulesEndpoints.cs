using System.Security.Claims;
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

        return endpoints;
    }

    public sealed record CreateScheduleRequest(string Name, decimal Amount, DateTime StartDate, int Frequency, bool IsIncome);
    public sealed record ScheduleResponse(Guid Id, string Name, decimal Amount, DateTime StartDate, int Frequency, bool IsIncome);

    // User id extraction handled by IUserContextService

    private static async Task<IResult> CreateScheduleAsync(Guid accountId, CreateScheduleRequest request, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
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
        await db.SaveChangesAsync();

        return Results.Created($"/api/accounts/{accountId}/schedules/{schedule.Id}", new ScheduleResponse(schedule.Id, schedule.Name, schedule.Amount, schedule.StartDate, schedule.Frequency, schedule.IsIncome));
    }

    private static async Task<IResult> ListSchedulesAsync(Guid accountId, FinanceDbContext db, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var schedules = await db.ScheduledItems
            .Where(s => s.AccountId == accountId)
            .Select(s => new ScheduleResponse(s.Id, s.Name, s.Amount, s.StartDate, s.Frequency, s.IsIncome))
            .ToListAsync();

        return Results.Ok(schedules);
    }
}
