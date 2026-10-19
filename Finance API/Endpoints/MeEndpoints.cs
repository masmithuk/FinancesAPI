using System.Security.Claims;
using Finance_API.Persistence;

namespace Finance_API.Endpoints;

/// <summary>
/// Minimal protected endpoint that returns information about the current
/// authenticated user. Useful for testing that JWT tokens are accepted by
/// the API and that authentication is correctly configured.
/// </summary>
public static class FinancialOverviewEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", GetMe)
            .WithName("GetCurrentUser")
            .WithTags("Auth")
            .WithSummary("Returns the authenticated user's id and username")
            .RequireAuthorization();

        return endpoints;
    }

    public sealed record MeResponse(string UserId, string? UserName, IDictionary<string, string?> Claims);

    private static IResult GetMe(ClaimsPrincipal user)
    {
        // Extract the most common identity claims: sub (JWT subject) and name identifier
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                     ?? string.Empty;

        var userName = user.Identity?.Name ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName)?.Value;

        // Materialize claims into a small dictionary for convenience
        var claims = user.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => (string?)g.First().Value);

        return Results.Ok(new MeResponse(userId, userName, claims));
    }
}
