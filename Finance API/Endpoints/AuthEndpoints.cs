using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Finance_API.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Finance_API.Endpoints;

/// <summary>
/// Provides minimal authentication endpoints for user registration and
/// login. These endpoints are intended as a lightweight example for
/// development and tests. In production you should harden validation,
/// error handling, and claim contents.
/// </summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    /// <summary>
    /// Registers the auth endpoints on the given <see cref="IEndpointRouteBuilder"/>.
    /// Call this from Program.cs to expose /api/auth/register and /api/auth/login.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", RegisterAsync)
            .WithName("RegisterUser")
            .WithTags("Auth")
            .WithSummary("Register a new user account")
            .RequireRateLimiting(RateLimitPolicy);

        endpoints.MapPost("/api/auth/login", LoginAsync)
            .WithName("Login")
            .WithTags("Auth")
            .WithSummary("Obtain a JWT access token using username and password")
            .RequireRateLimiting(RateLimitPolicy);

        return endpoints;
    }

    /// <summary>
    /// Request payload for registering a new user.
    /// </summary>
    public sealed record RegisterRequest(
        [Required, MaxLength(64)] string UserName,
        [Required, EmailAddress, MaxLength(254)] string Email,
        [Required, MaxLength(128)] string Password);

    /// <summary>
    /// Request payload for logging in.
    /// </summary>
    public sealed record LoginRequest(
        [Required, MaxLength(64)] string UserName,
        [Required, MaxLength(128)] string Password);

    /// <summary>
    /// Response returned when login succeeds. Contains a JWT and expiration.
    /// </summary>
    public sealed record AuthResponse(string Token, DateTime ExpiresUtc);

    /// <summary>
    /// Registers a new user using <see cref="UserManager{TUser}"/>. Password
    /// rules are enforced by Identity configuration. Returns 201 Created on
    /// success or 400 Bad Request with error details when creation fails.
    /// </summary>
    /// <param name="request">The registration data (username, email, password).</param>
    /// <param name="userManager">Identity's user manager, injected by DI.</param>
    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager)
    {
        // Build the ApplicationUser object from the incoming request. Keep
        // minimal profile fields here; add more properties to ApplicationUser
        // if you need them.
        var user = new ApplicationUser
        {
            UserName = request.UserName,
            Email = request.Email
        };

        // Create the user with the supplied password. Identity handles hashing
        // and validation based on configured options.
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToArray();
            // Return a simple error payload. In production, avoid leaking
            // sensitive validation details and consider a consistent error shape.
            return Results.BadRequest(new { errors });
        }

        // Return a 201 Created with a small representation of the new user.
        return Results.Created($"/api/users/{user.Id}", new { user.Id, user.UserName, user.Email });
    }

    /// <summary>
    /// Validates user credentials and returns a JWT when successful. The
    /// token contains minimal claims (subject and username) and is signed
    /// using a symmetric key configured in appsettings.
    /// </summary>
    /// <param name="request">The login payload (username & password).</param>
    /// <param name="userManager">Identity's user manager.</param>
    /// <param name="configuration">Application configuration (for Jwt settings).</param>
    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        // Find the user by username
        var user = await userManager.FindByNameAsync(request.UserName);
        if (user is null)
        {
            // Avoid revealing whether the user exists; simply return 401.
            return Results.Unauthorized();
        }

        // Verify the password using Identity's password hasher and validators
        if (await userManager.IsLockedOutAsync(user))
        {
            return Results.Unauthorized();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return Results.Unauthorized();
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // Read JWT configuration values. These should be provided in
        // appsettings.Development.json for local testing or via secure
        // configuration sources in production.
        var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
        var jwtIssuer = configuration["Jwt:Issuer"] ?? "FinanceApi";
        var jwtAudience = configuration["Jwt:Audience"] ?? "FinanceApiClients";
        var expiryMinutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var m) ? m : 60;

        // Create minimal claims. Add roles or custom claims if required.
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new Claim(ClaimTypes.NameIdentifier, user.Id)
        };

        // Build signing credentials and create the token
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Return the token to the caller along with its expiration time
        return Results.Ok(new AuthResponse(tokenString, expires));
    }
}
