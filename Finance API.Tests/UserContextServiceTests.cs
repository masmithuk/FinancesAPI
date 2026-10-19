using System.Security.Claims;
using Finance_API.Services;

namespace Finance_API.Tests;

public sealed class UserContextServiceTests
{
    [Fact]
    public void GetUserId_Returns_NameIdentifier_WhenPresent()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "user-123") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var svc = new UserContextService();

        var id = svc.GetUserId(principal);

        Assert.Equal("user-123", id);
    }

    [Fact]
    public void GetUserId_FallsBack_To_Sub_Claim()
    {
        var claims = new[] { new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, "sub-456") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var svc = new UserContextService();

        var id = svc.GetUserId(principal);

        Assert.Equal("sub-456", id);
    }
}
