// Health endpoint demonstrates a small minimal-API route that uses DI to
// receive the application's DbContext and a logger. It performs a lightweight
// readiness check by attempting to connect to the configured database.
using Finance_API.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Finance_API.Endpoints;

public static class HealthEndpoints
{
    // Extension method to register health-related routes on the application's
    // IEndpointRouteBuilder. This keeps Program.cs focused and routes grouped.
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", CheckDatabaseAsync)
            .WithName("GetApiHealth")
            .WithTags("Health")
            .WithSummary("Check API and database readiness")
            .WithDescription("Reports whether the API can connect to its configured database.")
            // Document the possible responses for OpenAPI
            .Produces<ApiHealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    // The health check handler. Dependencies (FinanceDbContext, ILogger) are
    // provided automatically by the DI container when the endpoint is invoked.
    private static async Task<Results<Ok<ApiHealthResponse>, ProblemHttpResult>>
        CheckDatabaseAsync(
            FinanceDbContext database,
            ILogger<Program> logger,
            CancellationToken cancellationToken)
    {
        // Use EF Core's CanConnectAsync to quickly determine whether the
        // configured database is reachable.
        if (await database.Database.CanConnectAsync(cancellationToken))
        {
            return TypedResults.Ok(new ApiHealthResponse("healthy", "available"));
        }

        // Log an error and return a ProblemDetails-style response when the DB
        // cannot be reached.
        logger.LogError("The configured finance database is unavailable.");
        return TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database unavailable",
            detail: "The API cannot connect to its configured database.",
            instance: "/api/health");
    }
}

/// <summary>
/// Reports the readiness state of the API and its database. Using a record keeps
/// the response shape small and immutable.
/// </summary>
public sealed record ApiHealthResponse(string Status, string Database);
