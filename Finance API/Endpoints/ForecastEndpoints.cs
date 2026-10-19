using System.Security.Claims;
using Finance_API.Services;
using Finance_API.Persistence;

namespace Finance_API.Endpoints;

/// <summary>
/// Exposes a forecast endpoint that returns daily projected balances and SafeToSpend.
/// </summary>
public static class ForecastEndpoints
{
    public static IEndpointRouteBuilder MapForecastEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/accounts/{accountId}/forecast", GetForecastAsync)
            .RequireAuthorization()
            .WithName("GetForecast")
            .WithTags("Forecast");

        return endpoints;
    }

    // User id extraction handled by IUserContextService

    private static async Task<IResult> GetForecastAsync(Guid accountId, ForecastService svc, ClaimsPrincipal user, FinanceDbContext db, Finance_API.Services.IUserContextService userContext)
    {
        var ownerId = userContext.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var account = await db.Accounts.FindAsync(accountId);
        if (account is null || account.OwnerId != ownerId) return Results.NotFound();

        var forecast = await svc.GetForecastAsync(accountId);

        return Results.Ok(new
        {
            from = forecast.From,
            to = forecast.To,
            safeToSpend = forecast.SafeToSpend,
            safeToSpendDate = forecast.SafeToSpendDate,
            points = forecast.Points.Select(p => new { date = p.Date, cumulative = p.CumulativeAmount })
        });
    }
}
