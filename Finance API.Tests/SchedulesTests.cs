using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class SchedulesTests
{
    [Fact]
    public async Task CreateListEditMoveAndDeleteSchedule()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Register and login
        var register = new { UserName = "scheduser", Email = "sched@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "scheduser", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create account
        var create = new { Name = "SalaryAccount" };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createdJson);
        var accountId = doc.RootElement.GetProperty("id").GetGuid();

        // Create schedule income
        var sched = new { Name = "Salary", Amount = 2000.00m, StartDate = DateTime.UtcNow.Date, Frequency = 2, IsIncome = true };
        var schedResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/schedules", sched);
        Assert.Equal(HttpStatusCode.Created, schedResp.StatusCode);

        // Create schedule expense
        var schedRent = new { Name = "Rent", Amount = 1000.00m, StartDate = DateTime.UtcNow.Date, Frequency = 1, IsIncome = false };
        var schedRentResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/schedules", schedRent);
        Assert.Equal(HttpStatusCode.Created, schedRentResp.StatusCode);
        var createdExpense = await schedRentResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(createdExpense.GetProperty("isIncome").GetBoolean());

        // List schedules
        var listResp = await client.GetAsync($"/api/accounts/{accountId}/schedules");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = await listResp.Content.ReadAsStringAsync();
        Assert.Contains("Salary", listJson);
        Assert.Contains("Rent", listJson);

        using var schedulesDocument = JsonDocument.Parse(listJson);
        Assert.False(schedulesDocument.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "Rent")
            .GetProperty("isIncome")
            .GetBoolean());
        var salaryId = schedulesDocument.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "Salary")
            .GetProperty("id")
            .GetGuid();

        var secondAccountResponse = await client.PostAsJsonAsync(
            "/api/accounts",
            new { Name = "Savings" });
        Assert.Equal(HttpStatusCode.Created, secondAccountResponse.StatusCode);
        var secondAccount = await secondAccountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondAccountId = secondAccount.GetProperty("id").GetGuid();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/accounts/{accountId}/schedules/{salaryId}",
            new
            {
                accountId = secondAccountId,
                name = "New salary",
                amount = 2500m,
                startDate = "2026-11-01",
                frequency = 1,
                isIncome = false
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedSchedule = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New salary", updatedSchedule.GetProperty("name").GetString());
        Assert.Equal(2500m, updatedSchedule.GetProperty("amount").GetDecimal());
        Assert.StartsWith(
            "2026-11-01",
            updatedSchedule.GetProperty("startDate").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(1, updatedSchedule.GetProperty("frequency").GetInt32());
        Assert.False(updatedSchedule.GetProperty("isIncome").GetBoolean());

        var oldAccountSchedules = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{accountId}/schedules");
        Assert.DoesNotContain(
            oldAccountSchedules.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == salaryId);

        var deleteResponse = await client.DeleteAsync(
            $"/api/accounts/{secondAccountId}/schedules/{salaryId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var remainingSchedules = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{secondAccountId}/schedules");
        Assert.Empty(remainingSchedules.EnumerateArray());
    }
}
