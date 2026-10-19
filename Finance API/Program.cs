// Import namespaces for endpoints, the EF Core DbContext, and SQLite support
using Finance_API.Endpoints;
using Finance_API.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Create the application builder (minimal hosting model)
var builder = WebApplication.CreateBuilder(args);

// Register common services used across the app. These are small helpers provided
// by the project's shared libraries or packages:
// - OpenAPI (Swagger) for documentation during development
// - Validation helpers for minimal APIs
// - ProblemDetails middleware for standardized error responses
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();

// Read the named connection string from configuration (appsettings.json, env vars, etc.)
var connectionString = builder.Configuration.GetConnectionString("Finance")
    ?? throw new InvalidOperationException(
        "The ConnectionStrings:Finance setting is required.");

// Normalize and validate the SQLite connection string. This ensures relative
// paths are converted to absolute paths inside the project's content root and
// that the directory exists before EF Core opens the file.
var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (string.IsNullOrWhiteSpace(sqliteConnection.DataSource))
{
    throw new InvalidOperationException(
        "The ConnectionStrings:Finance setting must specify a SQLite data source.");
}

// If the connection points to a file (not in-memory), ensure the file path is
// absolute and the directory exists. This avoids runtime errors when the
// process tries to create or open the SQLite file.
if (sqliteConnection.Mode != SqliteOpenMode.Memory
    && sqliteConnection.DataSource != ":memory:"
    && !sqliteConnection.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
{
    var databasePath = Path.GetFullPath(
        sqliteConnection.DataSource,
        builder.Environment.ContentRootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    sqliteConnection.DataSource = databasePath;
    connectionString = sqliteConnection.ToString();
}

// Register the EF Core DbContext with the DI container. AddDbContext manages
// scoped lifetime and allows the framework to inject FinanceDbContext where
// needed (for example into endpoints or services).
builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseSqlite(connectionString));

// Build the WebApplication and configure middleware.
var app = builder.Build();

// Global error handling and status pages
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();

// Only map OpenAPI/Swagger in development to avoid exposing docs in production
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ensure the database schema is up-to-date by applying any pending EF Core
// migrations on startup. This is convenient for development but consider a
// different approach for production deployments (CI-run migrations, backups).
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await database.Database.MigrateAsync();
}

// Map application endpoints (kept in separate files for organization)
app.MapHealthEndpoints();

// Start the web server and listen for incoming HTTP requests
app.Run();

/// <summary>Exposes the application entry point to the integration-test host.</summary>
public partial class Program
{
}
