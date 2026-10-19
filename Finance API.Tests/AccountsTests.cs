using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class AccountsTests
{
    [Fact]
    public async Task CreateAccountAndAddBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Register user
        var register = new { UserName = "acctuser", Email = "acct@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        // Login
        var login = new { UserName = "acctuser", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create account
        var create = new { Name = "Checking" };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createdJson);
        var accountId = doc.RootElement.GetProperty("id").GetGuid();

        // Add balance
        var balance = new { Date = DateTime.UtcNow.Date, Amount = 123.45m };
        var balResponse = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances", balance);
        Assert.Equal(HttpStatusCode.Created, balResponse.StatusCode);

        // List balances
        var listResponse = await client.GetAsync($"/api/accounts/{accountId}/balances");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("123.45", listJson);
    }
}
