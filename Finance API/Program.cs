// Import namespaces for endpoints, the EF Core DbContext, and SQLite support
using Finance_API.Endpoints;
using Finance_API.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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
builder.Services.AddCors(options =>
    options.AddPolicy("LocalVite", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()));

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

// Configure ASP.NET Core Identity to use our ApplicationUser and the same
// FinanceDbContext. This registers user management services (UserManager, SignInManager, etc.)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
    })
    .AddEntityFrameworkStores<FinanceDbContext>()
    .AddDefaultTokenProviders();

// Configure JWT authentication using settings from configuration
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required in configuration for authentication.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "FinanceApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "FinanceApiClients";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

// Register forecast service
builder.Services.AddScoped<Finance_API.Services.ForecastService>();
builder.Services.AddScoped<Finance_API.Services.FinancialOverviewService>();
// user context helper
builder.Services.AddSingleton<Finance_API.Services.IUserContextService, Finance_API.Services.UserContextService>();

// Build the WebApplication and configure middleware.
var app = builder.Build();

// Global error handling and status pages
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalVite");
}
else
{
    app.UseHttpsRedirection();
}

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
    var skipMigrations = bool.TryParse(app.Configuration["SkipMigrations"], out var s) && s;
    if (skipMigrations)
    {
        // In tests we prefer to create the schema directly rather than run
        // migrations to avoid conflicts between EnsureCreated and Migrate.
        database.Database.EnsureCreated();
    }
    else
    {
        try
        {
            await database.Database.MigrateAsync();
        }
        catch (InvalidOperationException ex) when (ex.Message?.Contains("PendingModelChangesWarning", StringComparison.OrdinalIgnoreCase) == true
                                                     || ex.Message?.Contains("pending changes", StringComparison.OrdinalIgnoreCase) == true)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Pending model changes detected when applying migrations; falling back to EnsureCreated().");
            database.Database.EnsureCreated();
        }
    }
}

// Map application endpoints (kept in separate files for organization)
app.MapHealthEndpoints();
// Map authentication endpoints (register/login)
app.MapAuthEndpoints();
// Map a small protected endpoint to return the caller's identity for testing
app.MapMeEndpoints();
// Map accounts and balances endpoints
app.MapAccountsEndpoints();
// Map pots/goals endpoints
app.MapPotsEndpoints();
// Map schedules (recurring income/expenses)
app.MapSchedulesEndpoints();
// Map occurrences (actual income/expense recordings)
app.MapOccurrencesEndpoints();
// Map forecast endpoint
app.MapForecastEndpoints();
// Map Financial Overview and setup endpoints
app.MapFinancialOverviewEndpoints();

// Start the web server and listen for incoming HTTP requests
app.Run();

/// <summary>Exposes the application entry point to the integration-test host.</summary>
public partial class Program
{
}
