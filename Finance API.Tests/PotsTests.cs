using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class PotsTests
{
    [Fact]
    public async Task CreatePot_CreateGoal_And_GetRunningBalances()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Register user
        var register = new { UserName = "potuser", Email = "pot@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        // Login
        var login = new { UserName = "potuser", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create account
        var create = new { Name = "Savings" };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createdJson);
        var accountId = doc.RootElement.GetProperty("id").GetGuid();

        // Create a pot
        var potCreate = new { Name = "Holiday" };
        var potResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/pots", potCreate);
        Assert.Equal(HttpStatusCode.Created, potResp.StatusCode);
        var potJson = await potResp.Content.ReadAsStringAsync();
        using var potDoc = JsonDocument.Parse(potJson);
        var potId = potDoc.RootElement.GetProperty("id").GetGuid();

        // List pots
        var listPots = await client.GetAsync($"/api/accounts/{accountId}/pots");
        Assert.Equal(HttpStatusCode.OK, listPots.StatusCode);
        var listPotsJson = await listPots.Content.ReadAsStringAsync();
        Assert.Contains("Holiday", listPotsJson);

        // Add a balance to the account so running balances have data
        var balance = new { Date = DateTime.UtcNow.Date.AddDays(-1), Amount = 50.00m };
        var balResponse = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances", balance);
        Assert.Equal(HttpStatusCode.Created, balResponse.StatusCode);

        // Create a goal on the pot
        var goalCreate = new { TargetAmount = 500.00m, DueDate = (DateTime?)DateTime.UtcNow.Date.AddMonths(3) };
        var goalResp = await client.PostAsJsonAsync($"/api/pots/{potId}/goals", goalCreate);
        Assert.Equal(HttpStatusCode.Created, goalResp.StatusCode);
        var goalJson = await goalResp.Content.ReadAsStringAsync();
        Assert.Contains("500", goalJson);

        // List goals
        var listGoals = await client.GetAsync($"/api/pots/{potId}/goals");
        Assert.Equal(HttpStatusCode.OK, listGoals.StatusCode);
        var listGoalsJson = await listGoals.Content.ReadAsStringAsync();
        Assert.Contains("500", listGoalsJson);

        // Get running balances
        var runResp = await client.GetAsync($"/api/accounts/{accountId}/running-balances");
        Assert.Equal(HttpStatusCode.OK, runResp.StatusCode);
        var runJson = await runResp.Content.ReadAsStringAsync();
        Assert.Contains("50", runJson);
    }
}
