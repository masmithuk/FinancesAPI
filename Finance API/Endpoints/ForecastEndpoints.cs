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

    private static string GetUserId(System.Security.Claims.ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
           ?? string.Empty;

    private static async Task<IResult> GetForecastAsync(Guid accountId, ForecastService svc, ClaimsPrincipal user, FinanceDbContext db)
    {
        var ownerId = GetUserId(user);
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
