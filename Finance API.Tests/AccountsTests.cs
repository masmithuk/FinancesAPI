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
        var createdBalance = await balResponse.Content.ReadFromJsonAsync<JsonElement>();
        var balanceId = createdBalance.GetProperty("id").GetGuid();

        // List balances
        var listResponse = await client.GetAsync($"/api/accounts/{accountId}/balances");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("123.45", listJson);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/accounts/{accountId}/balances/{balanceId}",
            new { Date = DateTime.UtcNow.Date.AddDays(-1), Amount = -25.50m });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedBalance = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(-25.50m, updatedBalance.GetProperty("amount").GetDecimal());
        Assert.StartsWith(
            DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"),
            updatedBalance.GetProperty("date").GetString(),
            StringComparison.Ordinal);

        var updatedList = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/balances");
        Assert.Single(updatedList.EnumerateArray());
        Assert.Equal(-25.50m, updatedList[0].GetProperty("amount").GetDecimal());

        var deleteResponse = await client.DeleteAsync(
            $"/api/accounts/{accountId}/balances/{balanceId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var emptyBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{accountId}/balances");
        Assert.Empty(emptyBalances.EnumerateArray());

        var missingBalanceResponse = await client.DeleteAsync(
            $"/api/accounts/{accountId}/balances/{balanceId}");
        Assert.Equal(HttpStatusCode.NotFound, missingBalanceResponse.StatusCode);

        var missingBalanceUpdateResponse = await client.PutAsJsonAsync(
            $"/api/accounts/{accountId}/balances/{balanceId}",
            new { Date = DateTime.UtcNow.Date, Amount = 10m });
    }

    [Fact]
    public async Task BalanceHistory_IsPagedNewestFirstAndOwnerScoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new { UserName = "pager", Email = "pager@example.com", Password = "P@ssword1" });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "pager", Password = "P@ssword1" });
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());

        var account = await (await client.PostAsJsonAsync("/api/accounts", new { Name = "Paged" })).Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("id").GetGuid();

        var start = DateTime.UtcNow.Date.AddDays(-30);
        for (var day = 0; day < 7; day++)
        {
            var created = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances", new { Date = start.AddDays(day), Amount = day + 1m });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var first = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/balances/history?skip=0&take=3");
        Assert.Equal(7, first.GetProperty("total").GetInt32());
        var firstItems = first.GetProperty("items");
        Assert.Equal(3, firstItems.GetArrayLength());
        Assert.Equal(7m, firstItems[0].GetProperty("amount").GetDecimal());
        Assert.Equal(5m, firstItems[2].GetProperty("amount").GetDecimal());

        var last = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/balances/history?skip=6&take=3");
        Assert.Equal(1, last.GetProperty("items").GetArrayLength());
        Assert.Equal(1m, last.GetProperty("items")[0].GetProperty("amount").GetDecimal());

        var capped = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/balances/history?take=100000&skip=-5");
        Assert.Equal(7, capped.GetProperty("items").GetArrayLength());

        var other = await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/balances/history");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }
}
