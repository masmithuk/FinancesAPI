using System.Security.Claims;
using Finance_API.Services;
using Finance_API.Persistence;

namespace Finance_API.Endpoints;

/// <summary>
/// Provides endpoints for Home summary and setup status.
/// </summary>
public static class FinancialOverviewEndpoints
{
    public static IEndpointRouteBuilder MapHomeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/home", GetHomeAsync)
            .RequireAuthorization()
            .WithName("GetHome")
            .WithTags("Home");

        endpoints.MapGet("/api/setup", GetSetupAsync)
            .RequireAuthorization()
            .WithName("GetSetup")
            .WithTags("Home");

        return endpoints;
    }

    // User id extraction handled by IUserContextService

    private static async Task<IResult> GetHomeAsync(ForecastService forecast, Finance_API.Services.FinancialOverviewService home, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var summary = await home.GetHomeSummaryAsync(ownerId);
        return Results.Ok(summary);
    }

    private static async Task<IResult> GetSetupAsync(Finance_API.Services.FinancialOverviewService home, ClaimsPrincipal user, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var status = await home.GetSetupStatusAsync(ownerId);
        return Results.Ok(status);
    }
}
