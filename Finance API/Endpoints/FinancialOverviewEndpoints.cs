using System.Security.Claims;
using Finance_API.Services;
using Finance_API.Persistence;

namespace Finance_API.Endpoints;

/// <summary>
/// Provides endpoints for Financial overview and setup status.
/// </summary>
public static class FinancialOverviewEndpoints
{
    public static IEndpointRouteBuilder MapFinancialOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/financialoverview", GetFinancialOverviewAsync)
            .RequireAuthorization()
            .WithName("GetFinancialOverview")
            .WithTags("FinancialOverview");

        endpoints.MapGet("/api/setup", GetSetupAsync)
            .RequireAuthorization()
            .WithName("GetSetup")
            .WithTags("FinancialOverview");

        endpoints.MapGet("/api/payday-plan", GetPaydayPlanAsync)
            .RequireAuthorization()
            .WithName("GetPaydayPlan")
            .WithTags("FinancialOverview")
            .WithSummary("Get the upcoming payday funding plan")
            .WithDescription("Infers payday windows from recurring income schedules and recommends transfers for upcoming expense shortfalls.");

        return endpoints;
    }

    // User id extraction handled by IUserContextService

    private static async Task<IResult> GetFinancialOverviewAsync(ForecastService forecast, Finance_API.Services.FinancialOverviewService financialOverview, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var summary = await financialOverview.GetFinancialOverviewAsync(ownerId);
        return Results.Ok(summary);
    }

    private static async Task<IResult> GetSetupAsync(Finance_API.Services.FinancialOverviewService financialOverview, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var status = await financialOverview.GetSetupStatusAsync(ownerId);
        return Results.Ok(status);
    }

    private static async Task<IResult> GetPaydayPlanAsync(
        Finance_API.Services.IPaydayPlanService paydayPlan,
        ClaimsPrincipal user,
        Finance_API.Services.IUserContextService userContext,
        CancellationToken cancellationToken,
        string? period = null)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        Finance_API.Services.PaydayPeriod paydayPeriod;
        switch (period?.ToLowerInvariant())
        {
            case null:
                paydayPeriod = Finance_API.Services.PaydayPeriod.Auto;
                break;
            case "current":
                paydayPeriod = Finance_API.Services.PaydayPeriod.Current;
                break;
            case "next":
                paydayPeriod = Finance_API.Services.PaydayPeriod.Next;
                break;
            default:
                return Results.BadRequest(new { errors = new[] { "Period must be 'current' or 'next'." } });
        }

        var plan = await paydayPlan.GetPlanAsync(ownerId, paydayPeriod, cancellationToken);
        return Results.Ok(plan);
    }
}
