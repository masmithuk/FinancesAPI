using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class ForecastTests
{
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
}
