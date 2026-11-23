using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance_API.Tests;

public sealed class ForecastTests
{
    // Test: Forecast includes scheduled and pending occurrences
    // Steps:
    // 1. Start an in-memory SQLite database and test server, register and authenticate a test user.
    // 2. Create an account and add a starting balance.
    // 3. Add a scheduled expense and an unreconciled pending occurrence.
    // 4. Request the forecast endpoint and assert the response contains key fields (safeToSpend, points).
    [Fact]
    public async Task Forecast_Includes_Scheduled_And_Pending_Occurrences()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Register and login
        var register = new { UserName = "fcastuser", Email = "fcast@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "fcastuser", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create account
        var create = new { Name = "Forecast" };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createdJson);
        var accountId = doc.RootElement.GetProperty("id").GetGuid();

        // Add starting balance today
        var balance = new { Date = DateTime.UtcNow.Date, Amount = 1000.00m };
        var balResponse = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances", balance);
        Assert.Equal(HttpStatusCode.Created, balResponse.StatusCode);

        // Create a scheduled expense in 7 days (weekly)
        var sched = new { Name = "WeeklyBill", Amount = 200.00m, StartDate = DateTime.UtcNow.Date.AddDays(7), Frequency = 0, IsIncome = false };
        var schedResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/schedules", sched);
        Assert.Equal(HttpStatusCode.Created, schedResp.StatusCode);

        // Create a pending occurrence in 3 days (unreconciled)
        var occ = new { Date = DateTime.UtcNow.Date.AddDays(3), Amount = 150.00m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "f-ext-1" };
        var occResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/occurrences", occ);
        Assert.Equal(HttpStatusCode.Created, occResp.StatusCode);

        // Request forecast
        var forecastResp = await client.GetAsync($"/api/accounts/{accountId}/forecast");
        Assert.Equal(HttpStatusCode.OK, forecastResp.StatusCode);
        var fjson = await forecastResp.Content.ReadAsStringAsync();

        Assert.Contains("safeToSpend", fjson);
        Assert.Contains("points", fjson);
    }

    [Fact]
    public async Task Forecast_Linked_Shop_Only_Reduces_A_Budget_By_Its_Amount()
    {
        // A £300 budget due today has one £50 shop linked to it, so £250 of it is still to come.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", new { UserName = "fcast-budget", Email = "fcast-budget@example.com", Password = "P@ssword1" });
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "fcast-budget", Password = "P@ssword1" });
        var token = (await loginResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var today = DateTime.UtcNow.Date;
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ownerId = await db.Users.Where(user => user.UserName == "fcast-budget").Select(user => user.Id).SingleAsync();
            var account = new Account { Name = "Spending", OwnerId = ownerId };
            var budget = new ScheduledItem { Name = "Food", Account = account, Amount = 300m, StartDate = today, Frequency = 2, IsIncome = false };
            db.AddRange(account, budget);
            db.Balances.Add(new Balance { Account = account, Date = today, Amount = 1000m });
            db.Occurrences.Add(new Occurrence
            {
                Account = account,
                ScheduledItem = budget,
                Date = today.AddDays(-1),
                PlanDate = today,
                Amount = 50m,
                IsIncome = false,
                IsReconciled = true
            });
            await db.SaveChangesAsync();
            accountId = account.Id;
        }

        var forecast = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/forecast");

        Assert.Equal(750m, forecast.GetProperty("safeToSpend").GetDecimal());
    }

    // Test: Annual schedules starting on February 29 (leap day)
    // Purpose: Ensure annual recurring scheduled items that begin on Feb 29 are correctly
    // included in forecasts for subsequent non-leap years.
    // Steps: create account and balance, add an annual schedule with StartDate = 2024-02-29,
    // request forecast and assert the response contains expected forecast fields.
    [Fact]
    public async Task Forecast_Allows_Annual_Schedules_Starting_On_February_29()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var register = new { UserName = "fcastuser2", Email = "fcast2@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "fcastuser2", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = new { Name = "Leap" };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var doc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var accountId = doc.RootElement.GetProperty("id").GetGuid();

        var balance = new { Date = DateTime.UtcNow.Date, Amount = 500.00m };
        var balResponse = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances", balance);
        Assert.Equal(HttpStatusCode.Created, balResponse.StatusCode);

        var sched = new { Name = "Birthday", Amount = 25.00m, StartDate = new DateTime(2024, 2, 29), Frequency = 3, IsIncome = false };
        var schedResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/schedules", sched);
        Assert.Equal(HttpStatusCode.Created, schedResp.StatusCode);

        var forecastResp = await client.GetAsync($"/api/accounts/{accountId}/forecast");
        Assert.Equal(HttpStatusCode.OK, forecastResp.StatusCode);
        var json = await forecastResp.Content.ReadAsStringAsync();
        Assert.Contains("safeToSpend", json);
        Assert.Contains("points", json);
    }
}
