using Finance_API.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finance_API.Tests;

/// <summary>
/// A test <see cref="WebApplicationFactory{TEntryPoint}"/> that configures the
/// application to use an in-memory SQLite connection supplied by the test.
///
/// Purpose:
/// - Run the real application's startup code in a test host.
/// - Override the database configuration so tests run against an in-memory
///   SQLite database instance (the <see cref="SqliteConnection"/> passed to
///   the factory). Keeping the connection open ensures the in-memory DB
///   persists for the lifetime of the test session.
///
/// Usage:
/// var connection = new SqliteConnection("Data Source=:memory:");
/// await connection.OpenAsync();
/// using var factory = new FinanceApiFactory(connection);
/// using var client = factory.CreateClient();
///
internal sealed class FinanceApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Force the environment to Development so development-only services
        // (like OpenAPI) are available during tests.
        builder.UseEnvironment("Development");

        // Provide a lightweight configuration override for the connection
        // string. The application's normal configuration still runs, but this
        // in-memory value ensures the app will attempt to use SQLite in-memory.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Finance"] = "Data Source=:memory:"
            }));

        // Replace the application's DbContext configuration with one that
        // uses the provided SqliteConnection. Remove any previously
        // registered DbContextOptions so our registration takes precedence.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FinanceDbContext>>();
            services.AddDbContext<FinanceDbContext>(options =>
                options.UseSqlite(connection));
        });
    }
}
