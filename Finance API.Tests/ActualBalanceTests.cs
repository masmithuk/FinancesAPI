using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Finance_API.Tests;

/// <summary>"Update to bank balance": the user types what the bank shows and the difference is recorded.</summary>
public sealed class ActualBalanceTests
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    [Fact]
    public async Task ActualBalance_Records_Difference_So_Total_Matches_Bank()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "actual-diff-user");
        var accountId = await CreateAccount(client, "Current");
        await AddBalance(client, accountId, Today.AddDays(-1), 100m);
        await AddBalance(client, accountId, Today.AddDays(-1), 20m);

        var result = await SetActual(client, accountId, new { Amount = 95m });

        Assert.Equal(-25m, result.GetProperty("difference").GetDecimal());
        Assert.Equal(JsonValueKind.String, result.GetProperty("balanceId").ValueKind);
        var balances = await ListBalances(client, accountId);
        Assert.Equal(95m, balances.Sum(b => b.GetProperty("amount").GetDecimal()));
        var entry = Assert.Single(balances, b => b.GetProperty("amount").GetDecimal() == -25m);
        Assert.Equal("Bank balance update", entry.GetProperty("note").GetString());
        Assert.Equal(Today, entry.GetProperty("date").GetDateTime().Date);
    }

    [Fact]
    public async Task ActualBalance_Matching_Figure_Adds_No_Entry_But_Stamps_Check()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "actual-same-user");
        var accountId = await CreateAccount(client, "Current");
        await AddBalance(client, accountId, Today, 50m);

        var result = await SetActual(client, accountId, new { Amount = 50m });

        Assert.Equal(0m, result.GetProperty("difference").GetDecimal());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("balanceId").ValueKind);
        Assert.Equal(Today, result.GetProperty("balanceCheckedOn").GetDateTime().Date);
        Assert.Single(await ListBalances(client, accountId));
    }

    [Fact]
    public async Task ActualBalance_Handles_Negative_Bank_Figure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "actual-negative-user");
        var accountId = await CreateAccount(client, "Credit card");

        var result = await SetActual(client, accountId, new { Amount = -40m });

        Assert.Equal(-40m, result.GetProperty("difference").GetDecimal());
        Assert.Equal(-40m, (await ListBalances(client, accountId)).Sum(b => b.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task ActualBalance_Other_Users_Account_Returns_NotFound()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var owner = await CreateAuthenticatedClient(factory, "actual-owner-user");
        using var other = await CreateAuthenticatedClient(factory, "actual-other-user");
        var accountId = await CreateAccount(owner, "Current");
        await AddBalance(owner, accountId, Today, 10m);

        var response = await other.PostAsJsonAsync($"/api/accounts/{accountId}/balances/actual", new { Amount = 500m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(10m, (await ListBalances(owner, accountId)).Sum(b => b.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task ActualBalance_Future_Date_Returns_BadRequest()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "actual-future-user");
        var accountId = await CreateAccount(client, "Current");

        var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/balances/actual",
            new { Amount = 10m, Date = Today.AddDays(1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<JsonElement> SetActual(HttpClient client, Guid accountId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances/actual", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreateAccount(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task AddBalance(HttpClient client, Guid accountId, DateTime date, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/accounts/{accountId}/balances", new { Date = date, Amount = amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<List<JsonElement>> ListBalances(HttpClient client, Guid accountId)
    {
        var response = await client.GetAsync($"/api/accounts/{accountId}/balances");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private static async Task<HttpClient> CreateAuthenticatedClient(FinanceApiFactory factory, string userName)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            Password = "P@ssword1"
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { UserName = userName, Password = "P@ssword1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
