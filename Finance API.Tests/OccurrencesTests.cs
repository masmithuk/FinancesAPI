using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class OccurrencesTests
{
    [Fact]
    public async Task Create_List_And_Reconcile_Occurrence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Register and login
        var register = new { UserName = "occuser", Email = "occ@example.com", Password = "P@ssword1" };
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var login = new { UserName = "occuser", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create account
        var create = new { Name = "Expenses" };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createdJson);
        var accountId = doc.RootElement.GetProperty("id").GetGuid();

        // Create occurrence
        var occ = new { Date = DateTime.UtcNow.Date, Amount = 100.00m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "ext-123" };
        var occResp = await client.PostAsJsonAsync($"/api/accounts/{accountId}/occurrences", occ);
        Assert.Equal(HttpStatusCode.Created, occResp.StatusCode);
        var occJson = await occResp.Content.ReadAsStringAsync();
        Assert.Contains("ext-123", occJson);

        // List occurrences
        var listResp = await client.GetAsync($"/api/accounts/{accountId}/occurrences");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = await listResp.Content.ReadAsStringAsync();
        Assert.Contains("ext-123", listJson);

        // Reconcile occurrence
        using var occDoc = JsonDocument.Parse(occJson);
        var occId = occDoc.RootElement.GetProperty("id").GetGuid();
        var recResp = await client.PostAsync($"/api/accounts/{accountId}/occurrences/{occId}/reconcile", null);
        Assert.Equal(HttpStatusCode.OK, recResp.StatusCode);

        // Verify a balance was added (reconciled once)
        var balances = await client.GetAsync($"/api/accounts/{accountId}/balances");
        Assert.Equal(HttpStatusCode.OK, balances.StatusCode);
        var balancesJson = await balances.Content.ReadAsStringAsync();
        // Expense of 100 becomes -100 when applied to balance
        Assert.Contains("-100", balancesJson);
    }
}
