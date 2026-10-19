using System.Security.Claims;

namespace Finance_API.Services;

/// <summary>
/// Small helper that encapsulates extracting the current user's id from a
/// ClaimsPrincipal. Centralising this logic keeps endpoints thin and makes
/// it easier to unit test authentication-related behaviour.
/// </summary>
public interface IUserContextService
{
    string GetUserId(ClaimsPrincipal user);
}

public sealed class UserContextService : IUserContextService
{
    public string GetUserId(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
           ?? string.Empty;
}
