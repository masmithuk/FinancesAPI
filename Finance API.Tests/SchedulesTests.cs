using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class SchedulesTests
{
    [Fact]
    public async Task CreateAndListSchedules()
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

        // List schedules
        var listResp = await client.GetAsync($"/api/accounts/{accountId}/schedules");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = await listResp.Content.ReadAsStringAsync();
        Assert.Contains("Salary", listJson);
        Assert.Contains("Rent", listJson);
    }
}
