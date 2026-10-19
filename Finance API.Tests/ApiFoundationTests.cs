using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance_API.Endpoints;
using Finance_API.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance_API.Tests;

/// <summary>
/// Integration-style tests that exercise core application behaviors using an
/// in-memory SQLite connection and the test host provided by FinanceApiFactory.
///
/// These tests are intentionally small and focused to show how the application
/// behaves end-to-end: routing, middleware, startup logic, and OpenAPI output.
/// </summary>
public sealed class ApiFoundationTests
{
    /// <summary>
    /// Verifies the /api/health endpoint returns 200 OK and the expected response
    /// shape when the database is reachable. The test uses an in-memory SQLite
    /// connection to avoid touching disk and to keep tests isolated.
    /// </summary>
    [Fact]
    public async Task HealthEndpointReportsDatabaseReadiness()
    {
        // Arrange: create an in-memory SQLite connection and start the test host
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Act: call the health endpoint
        var response = await client.GetAsync("/api/health");
        var health = await response.Content.ReadFromJsonAsync<ApiHealthResponse>();

        // Assert: HTTP 200 and the typed health response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ApiHealthResponse("healthy", "available"), health);
    }

    /// <summary>
    /// Ensures that application startup applies EF Core migrations. The test
    /// triggers startup by calling an endpoint and then inspects the DbContext
    /// to confirm migrations were applied (no pending migrations).
    /// </summary>
    [Fact]
    public async Task StartupAppliesDatabaseMigrations()
    {
        // Arrange: create test host with in-memory DB
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Act: trigger app startup and migrations by calling an endpoint
        await client.GetAsync("/api/health");

        // Inspect the application's service provider for the DbContext
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var appliedMigrations = await database.Database.GetAppliedMigrationsAsync();

        // Assert: at least one migration applied and none pending
        Assert.NotEmpty(appliedMigrations);
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
    }

    /// <summary>
    /// Confirms that requests to unknown routes return a ProblemDetails JSON
    /// response and the expected 404 status code and content-type header.
    /// </summary>
    [Fact]
    public async Task UnknownRouteReturnsProblemDetails()
    {
        // Arrange: prepare test host
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Act: call a route that doesn't exist
        var response = await client.GetAsync("/api/not-a-route");
        var body = await response.Content.ReadAsStringAsync();

        // Assert: 404 Not Found with application/problem+json and 404 in the body
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"status\":404", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates that the generated OpenAPI document includes the health
    /// endpoint and that example/sample endpoints (like weatherforecast) are
    /// omitted from the published document.
    /// </summary>
    [Fact]
    public async Task OpenApiDocumentsHealthEndpointAndOmitsSampleRoute()
    {
        // Arrange: start test host
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Act: fetch the OpenAPI JSON document
        var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadAsStringAsync();

        // Assert: valid response and expected paths
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var openApi = JsonDocument.Parse(document);
        var paths = openApi.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/health", out var healthPath));
        Assert.False(paths.TryGetProperty("/weatherforecast", out _));

        // The health path should declare 200 and 503 responses
        var responses = healthPath.GetProperty("get").GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("503", out _));
    }
}
