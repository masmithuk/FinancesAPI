using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance_API.Endpoints;
using Microsoft.Data.Sqlite;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Finance_API.Tests;

/// <summary>
/// Integration tests for the authentication endpoints. These tests run against
/// an in-memory SQLite database using FinanceApiFactory so they exercise the
/// real startup pipeline and Identity integration.
/// </summary>
public sealed class AuthTests
{
    [Fact]
    public async Task Login_LocksOutAfterRepeatedFailures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register",
            new { UserName = "lockuser", Email = "lock@example.com", Password = "P@ssword1" });

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "lockuser", Password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "lockuser", Password = "P@ssword1" });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        HttpStatusCode last = default;
        for (var i = 0; i < 15; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "nobody", Password = "wrong" });
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Register_ReturnsCreated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var register = new { UserName = "testuser1", Email = "test1@example.com", Password = "P@ssword1" };

        var response = await client.PostAsJsonAsync("/api/auth/register", register);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("testuser1", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ReturnsToken()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var register = new { UserName = "testuser2", Email = "test2@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "testuser2", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(auth.TryGetProperty("token", out var tokenElement));
        var token = tokenElement.GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Token_ValidatesAndContainsClaims()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var register = new { UserName = "testuser3", Email = "test3@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "testuser3", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Validate token using the same configuration as the app
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var jwtKey = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing in test config");
        var jwtIssuer = config["Jwt:Issuer"] ?? "FinanceApi";
        var jwtAudience = config["Jwt:Audience"] ?? "FinanceApiClients";

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var validationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
        Assert.NotNull(principal);

        // Ensure subject / name identifier claim exists
        var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
        if (sub == null)
        {
            sub = principal.FindFirst(ClaimTypes.NameIdentifier);
        }

        Assert.False(string.IsNullOrWhiteSpace(sub?.Value));
    }

    [Fact]
    public async Task MeEndpoint_ReturnsAuthenticatedUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var register = new { UserName = "testuser4", Email = "test4@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "testuser4", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var meResponse = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var body = await meResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var userName = root.GetProperty("userName").GetString();
        var userId = root.GetProperty("userId").GetString();

        Assert.Equal("testuser4", userName);
        Assert.False(string.IsNullOrWhiteSpace(userId));
    }
}
