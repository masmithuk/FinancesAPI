using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Finance_API.Tests;

/// <summary>Marking a recommended transfer as done records it on both accounts.</summary>
public sealed class TransfersTests
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    [Fact]
    public async Task Transfer_Records_Money_Out_And_In()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "transfer-user");
        var current = await CreateAccount(client, "Current");
        var joint = await CreateAccount(client, "Joint");

        var response = await client.PostAsJsonAsync("/api/transfers", new { FromAccountId = current, ToAccountId = joint, Amount = 200m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var outEntry = Assert.Single(await ListBalances(client, current));
        Assert.Equal(-200m, outEntry.GetProperty("amount").GetDecimal());
        Assert.Equal("Transfer to Joint", outEntry.GetProperty("note").GetString());
        Assert.Equal(Today, outEntry.GetProperty("date").GetDateTime().Date);
        var inEntry = Assert.Single(await ListBalances(client, joint));
        Assert.Equal(200m, inEntry.GetProperty("amount").GetDecimal());
        Assert.Equal("Transfer from Current", inEntry.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Transfer_From_Another_Users_Account_Returns_NotFound()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var owner = await CreateAuthenticatedClient(factory, "transfer-owner");
        using var other = await CreateAuthenticatedClient(factory, "transfer-other");
        var ownersAccount = await CreateAccount(owner, "Savings");
        var othersAccount = await CreateAccount(other, "Current");

        var response = await other.PostAsJsonAsync("/api/transfers", new { FromAccountId = ownersAccount, ToAccountId = othersAccount, Amount = 50m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await ListBalances(owner, ownersAccount));
        Assert.Empty(await ListBalances(other, othersAccount));
    }

    [Fact]
    public async Task Transfer_To_Same_Account_Returns_BadRequest()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "transfer-same-user");
        var current = await CreateAccount(client, "Current");

        var response = await client.PostAsJsonAsync("/api/transfers", new { FromAccountId = current, ToAccountId = current, Amount = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListBalances(client, current));
    }

    [Fact]
    public async Task Transfer_Of_Zero_Returns_BadRequest()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "transfer-zero-user");
        var current = await CreateAccount(client, "Current");
        var joint = await CreateAccount(client, "Joint");

        var response = await client.PostAsJsonAsync("/api/transfers", new { FromAccountId = current, ToAccountId = joint, Amount = 0m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> CreateAccount(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
