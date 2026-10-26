using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finance_API.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class PotsTests
{
    [Fact]
    public async Task Migrations_CreatePotContributionSchema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new FinanceDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Equal(0, await db.PotContributions.CountAsync());
    }

    [Fact]
    public async Task PotContribution_TracksProgressAndKeepsSourceAccountBalanceInSync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { UserName = "contributor", Email = "contributor@example.com", Password = "P@ssword1" });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { UserName = "contributor", Password = "P@ssword1" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.GetProperty("token").GetString());

        var firstAccountResponse = await client.PostAsJsonAsync(
            "/api/accounts",
            new { Name = "Current account" });
        var firstAccount = await firstAccountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstAccountId = firstAccount.GetProperty("id").GetGuid();

        var secondAccountResponse = await client.PostAsJsonAsync(
            "/api/accounts",
            new { Name = "Savings account" });
        var secondAccount = await secondAccountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondAccountId = secondAccount.GetProperty("id").GetGuid();

        var initialBalanceResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{firstAccountId}/balances",
            new { Date = DateTime.UtcNow.Date, Amount = 1000m });
        Assert.Equal(HttpStatusCode.Created, initialBalanceResponse.StatusCode);

        var potResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{firstAccountId}/pots",
            new { Name = "New car" });
        Assert.Equal(HttpStatusCode.Created, potResponse.StatusCode);
        var pot = await potResponse.Content.ReadFromJsonAsync<JsonElement>();
        var potId = pot.GetProperty("id").GetGuid();

        var goalResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/goals",
            new { TargetAmount = 500m, DueDate = (DateTime?)null });
        Assert.Equal(HttpStatusCode.Created, goalResponse.StatusCode);

        var contributionResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/contributions",
            new { AccountId = secondAccountId, Amount = 600m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.Created, contributionResponse.StatusCode);
        var contribution = await contributionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var contributionId = contribution.GetProperty("id").GetGuid();
        Assert.Equal(secondAccountId, contribution.GetProperty("accountId").GetGuid());
        Assert.Equal("Savings account", contribution.GetProperty("accountName").GetString());

        var contributions = await client.GetFromJsonAsync<JsonElement>(
            $"/api/pots/{potId}/contributions");
        var savedAmount = contributions.EnumerateArray()
            .Sum(item => item.GetProperty("amount").GetDecimal());
        Assert.Equal(600m, savedAmount);
        Assert.True(savedAmount > 500m);

        var initialDebitBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{secondAccountId}/balances");
        Assert.Contains(
            initialDebitBalances.EnumerateArray(),
            balance => balance.GetProperty("amount").GetDecimal() == -600m);

        var invalidAmountResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/contributions",
            new { AccountId = secondAccountId, Amount = 0m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAmountResponse.StatusCode);

        var missingAccountResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/contributions",
            new { AccountId = Guid.NewGuid(), Amount = 10m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.NotFound, missingAccountResponse.StatusCode);

        var contributionsAfterInvalidRequests = await client.GetFromJsonAsync<JsonElement>(
            $"/api/pots/{potId}/contributions");
        Assert.Single(contributionsAfterInvalidRequests.EnumerateArray());

        var existingMoneyResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/contributions",
            new { AccountId = (Guid?)null, Amount = 125m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.Created, existingMoneyResponse.StatusCode);
        var existingMoney = await existingMoneyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var existingMoneyId = existingMoney.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, existingMoney.GetProperty("accountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, existingMoney.GetProperty("accountName").ValueKind);

        var updateFromNowhereResponse = await client.PutAsJsonAsync(
            $"/api/pots/{potId}/contributions/{existingMoneyId}",
            new { AccountId = firstAccountId, Amount = 150m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.OK, updateFromNowhereResponse.StatusCode);
        var balancesWithSourceAccount = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{firstAccountId}/balances");
        Assert.Contains(
            balancesWithSourceAccount.EnumerateArray(),
            balance => balance.GetProperty("amount").GetDecimal() == -150m);

        var removeSourceAccountResponse = await client.PutAsJsonAsync(
            $"/api/pots/{potId}/contributions/{existingMoneyId}",
            new { AccountId = (Guid?)null, Amount = 175m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.OK, removeSourceAccountResponse.StatusCode);
        var balancesWithoutSourceAccount = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{firstAccountId}/balances");
        Assert.Single(balancesWithoutSourceAccount.EnumerateArray());
        Assert.Equal(1000m, balancesWithoutSourceAccount[0].GetProperty("amount").GetDecimal());

        var deleteExistingMoneyResponse = await client.DeleteAsync(
            $"/api/pots/{potId}/contributions/{existingMoneyId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteExistingMoneyResponse.StatusCode);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/pots/{potId}/contributions/{contributionId}",
            new { AccountId = firstAccountId, Amount = 750m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var oldAccountBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{secondAccountId}/balances");
        Assert.Empty(oldAccountBalances.EnumerateArray());

        var updatedAccountBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{firstAccountId}/balances");
        var generatedDebit = updatedAccountBalances.EnumerateArray()
            .Single(balance => balance.GetProperty("amount").GetDecimal() == -750m);
        var generatedBalanceId = generatedDebit.GetProperty("id").GetGuid();

        var directUpdateResponse = await client.PutAsJsonAsync(
            $"/api/accounts/{firstAccountId}/balances/{generatedBalanceId}",
            new { Date = DateTime.UtcNow.Date, Amount = -1m });
        Assert.Equal(HttpStatusCode.Conflict, directUpdateResponse.StatusCode);

        var directDeleteResponse = await client.DeleteAsync(
            $"/api/accounts/{firstAccountId}/balances/{generatedBalanceId}");
        Assert.Equal(HttpStatusCode.Conflict, directDeleteResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync(
            $"/api/pots/{potId}/contributions/{contributionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var remainingContributions = await client.GetFromJsonAsync<JsonElement>(
            $"/api/pots/{potId}/contributions");
        Assert.Empty(remainingContributions.EnumerateArray());

        var remainingBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{firstAccountId}/balances");
        Assert.Single(remainingBalances.EnumerateArray());
        Assert.Equal(1000m, remainingBalances[0].GetProperty("amount").GetDecimal());

        var remainingContributionResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/contributions",
            new { AccountId = secondAccountId, Amount = 25m, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.Created, remainingContributionResponse.StatusCode);

        var deletePotResponse = await client.DeleteAsync(
            $"/api/accounts/{firstAccountId}/pots/{potId}");
        Assert.Equal(HttpStatusCode.NoContent, deletePotResponse.StatusCode);

        var balancesAfterPotDelete = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{secondAccountId}/balances");
        Assert.Empty(balancesAfterPotDelete.EnumerateArray());
    }

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

        var secondAccountResponse = await client.PostAsJsonAsync(
            "/api/accounts",
            new { Name = "Current account" });
        Assert.Equal(HttpStatusCode.Created, secondAccountResponse.StatusCode);
        var secondAccount = await secondAccountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondAccountId = secondAccount.GetProperty("id").GetGuid();

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
        var goalResponse = await goalResp.Content.ReadFromJsonAsync<JsonElement>();
        var goalId = goalResponse.GetProperty("id").GetGuid();
        Assert.Equal(500m, goalResponse.GetProperty("targetAmount").GetDecimal());

        // List goals
        var listGoals = await client.GetAsync($"/api/pots/{potId}/goals");
        Assert.Equal(HttpStatusCode.OK, listGoals.StatusCode);
        var listGoalsJson = await listGoals.Content.ReadAsStringAsync();
        Assert.Contains("500", listGoalsJson);

        var updatePotResponse = await client.PutAsJsonAsync(
            $"/api/accounts/{accountId}/pots/{potId}",
            new { Name = "Summer holiday", AccountId = secondAccountId });
        Assert.Equal(HttpStatusCode.OK, updatePotResponse.StatusCode);
        var updatedPot = await updatePotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Summer holiday", updatedPot.GetProperty("name").GetString());
        Assert.Equal(secondAccountId, updatedPot.GetProperty("accountId").GetGuid());

        var oldAccountPots = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/pots");
        Assert.Empty(oldAccountPots.EnumerateArray());

        var updateGoalResponse = await client.PutAsJsonAsync(
            $"/api/pots/{potId}/goals/{goalId}",
            new
            {
                TargetAmount = 750m,
                DueDate = DateTime.UtcNow.Date.AddMonths(6)
            });
        Assert.Equal(HttpStatusCode.OK, updateGoalResponse.StatusCode);
        var updatedGoal = await updateGoalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(750m, updatedGoal.GetProperty("targetAmount").GetDecimal());

        var deleteGoalResponse = await client.DeleteAsync($"/api/pots/{potId}/goals/{goalId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteGoalResponse.StatusCode);
        var emptyGoals = await client.GetFromJsonAsync<JsonElement>($"/api/pots/{potId}/goals");
        Assert.Empty(emptyGoals.EnumerateArray());

        var extraGoalResponse = await client.PostAsJsonAsync(
            $"/api/pots/{potId}/goals",
            new { TargetAmount = 125m, DueDate = (DateTime?)null });
        Assert.Equal(HttpStatusCode.Created, extraGoalResponse.StatusCode);

        var deletePotResponse = await client.DeleteAsync($"/api/accounts/{secondAccountId}/pots/{potId}");
        Assert.Equal(HttpStatusCode.NoContent, deletePotResponse.StatusCode);
        var remainingPots = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{secondAccountId}/pots");
        Assert.Empty(remainingPots.EnumerateArray());
        var missingPotResponse = await client.GetAsync($"/api/accounts/{secondAccountId}/pots/{potId}");
        Assert.Equal(HttpStatusCode.NotFound, missingPotResponse.StatusCode);

        // Get running balances
        var runResp = await client.GetAsync($"/api/accounts/{accountId}/running-balances");
        Assert.Equal(HttpStatusCode.OK, runResp.StatusCode);
        var runJson = await runResp.Content.ReadAsStringAsync();
        Assert.Contains("50", runJson);
    }
}
