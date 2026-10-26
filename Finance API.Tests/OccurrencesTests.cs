using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class OccurrencesTests
{
    [Fact]
    public async Task ExternalReference_IsScopedPerAccount_NoCrossUserLeak()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);

        async Task<(HttpClient Client, Guid AccountId)> SignUpAsync(string name)
        {
            var client = factory.CreateClient();
            await client.PostAsJsonAsync("/api/auth/register", new { UserName = name, Email = $"{name}@example.com", Password = "P@ssword1" });
            var login = await client.PostAsJsonAsync("/api/auth/login", new { UserName = name, Password = "P@ssword1" });
            var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());
            var account = await (await client.PostAsJsonAsync("/api/accounts", new { Name = "Main" })).Content.ReadFromJsonAsync<JsonElement>();
            return (client, account.GetProperty("id").GetGuid());
        }

        var (alice, aliceAccount) = await SignUpAsync("alice");
        var (bob, bobAccount) = await SignUpAsync("bob");

        var aliceResp = await alice.PostAsJsonAsync($"/api/accounts/{aliceAccount}/occurrences",
            new { Date = DateTime.UtcNow.Date, Amount = 123.45m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "shared-ref" });
        Assert.Equal(HttpStatusCode.Created, aliceResp.StatusCode);
        var aliceId = (await aliceResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var bobResp = await bob.PostAsJsonAsync($"/api/accounts/{bobAccount}/occurrences",
            new { Date = DateTime.UtcNow.Date, Amount = 5m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "shared-ref" });
        Assert.Equal(HttpStatusCode.Created, bobResp.StatusCode);
        var bobBody = await bobResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(aliceId.ToString(), bobBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123.45", bobBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateReconcileEditMoveAndDeleteOccurrence()
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

        var secondAccountResponse = await client.PostAsJsonAsync(
            "/api/accounts",
            new { Name = "Savings" });
        Assert.Equal(HttpStatusCode.Created, secondAccountResponse.StatusCode);
        var secondAccount = await secondAccountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondAccountId = secondAccount.GetProperty("id").GetGuid();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/accounts/{accountId}/occurrences/{occId}",
            new
            {
                accountId = secondAccountId,
                date = "2026-10-15",
                amount = 250m,
                isIncome = true,
                externalReference = "updated-income"
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedOccurrence = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("updated-income", updatedOccurrence.GetProperty("externalReference").GetString());
        Assert.StartsWith(
            "2026-10-15",
            updatedOccurrence.GetProperty("date").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(250m, updatedOccurrence.GetProperty("amount").GetDecimal());
        Assert.True(updatedOccurrence.GetProperty("isIncome").GetBoolean());
        Assert.True(updatedOccurrence.GetProperty("isReconciled").GetBoolean());

        var correctedOldBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{accountId}/balances");
        Assert.Equal(
            0m,
            correctedOldBalances.EnumerateArray()
                .Sum(balance => balance.GetProperty("amount").GetDecimal()));

        var newAccountBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{secondAccountId}/balances");
        Assert.Equal(
            250m,
            newAccountBalances.EnumerateArray()
                .Sum(balance => balance.GetProperty("amount").GetDecimal()));

        var deleteResponse = await client.DeleteAsync(
            $"/api/accounts/{secondAccountId}/occurrences/{occId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedAccountBalances = await client.GetFromJsonAsync<JsonElement>(
            $"/api/accounts/{secondAccountId}/balances");
        Assert.Equal(
            0m,
            deletedAccountBalances.EnumerateArray()
                .Sum(balance => balance.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task ImportOccurrences_IsIdempotentAndAppliesBalances()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new { UserName = "importer", Email = "imp@example.com", Password = "P@ssword1" });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "importer", Password = "P@ssword1" });
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());

        var account = await (await client.PostAsJsonAsync("/api/accounts", new { Name = "Current" })).Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("id").GetGuid();

        var payload = new
        {
            rows = new object[]
            {
                new { date = "2026-09-25", amount = 1250.40m, name = "Alex Example", importId = "tx_1" },
                new { date = "2026-09-25", amount = -41.37m, name = "Examplemart", importId = "tx_2" },
                new { date = "2026-09-26", amount = -3.10m, name = "Coffee", importId = (string?)null },
                new { date = "2026-09-26", amount = -3.10m, name = "Coffee", importId = (string?)null },
                new { date = "2026-09-26", amount = 0m, name = "Zero", importId = "tx_zero" }
            }
        };

        var first = await client.PostAsJsonAsync($"/api/accounts/{accountId}/occurrences/import", payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, firstResult.GetProperty("imported").GetInt32());
        Assert.Equal(1, firstResult.GetProperty("skipped").GetInt32());

        var second = await client.PostAsJsonAsync($"/api/accounts/{accountId}/occurrences/import", payload);
        var secondResult = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, secondResult.GetProperty("imported").GetInt32());
        Assert.Equal(4, secondResult.GetProperty("duplicates").GetInt32());

        var running = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/running-balances");
        Assert.Equal(1250.40m - 41.37m - 3.10m - 3.10m, running[running.GetArrayLength() - 1].GetProperty("cumulativeAmount").GetDecimal());

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences");
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("description").GetString() == "Examplemart");
    }

    [Fact]
    public async Task LinkedImport_IsNotCountedTwiceAndCanBeUnlinked()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new { UserName = "linker", Email = "link@example.com", Password = "P@ssword1" });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "linker", Password = "P@ssword1" });
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());

        var account = await (await client.PostAsJsonAsync("/api/accounts", new { Name = "Current" })).Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("id").GetGuid();

        var today = DateTime.UtcNow.Date;
        var plan = await (await client.PostAsJsonAsync($"/api/accounts/{accountId}/schedules", new { Name = "Gym", Amount = 50m, StartDate = today, Frequency = 2, IsIncome = false })).Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetGuid();

        var unlinked = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/forecast");
        Assert.Equal(-50m, unlinked.GetProperty("safeToSpend").GetDecimal());

        var import = await client.PostAsJsonAsync($"/api/accounts/{accountId}/occurrences/import", new
        {
            rows = new object[] { new { date = today.ToString("yyyy-MM-dd"), amount = -50m, name = "GYM LTD", importId = "tx_gym", scheduledItemId = planId, planDate = today.ToString("yyyy-MM-dd") } }
        });
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        // The imported payment replaces the plan occurrence rather than adding to it.
        var linked = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/forecast");
        Assert.Equal(-50m, linked.GetProperty("safeToSpend").GetDecimal());

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences");
        var item = list.EnumerateArray().Single();
        Assert.Equal(planId, item.GetProperty("scheduledItemId").GetGuid());

        var unlink = await client.PutAsJsonAsync($"/api/accounts/{accountId}/occurrences/{item.GetProperty("id").GetGuid()}/plan", new { scheduledItemId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/forecast");
        Assert.Equal(-100m, after.GetProperty("safeToSpend").GetDecimal());

        var relink = await client.PutAsJsonAsync($"/api/accounts/{accountId}/occurrences/{item.GetProperty("id").GetGuid()}/plan", new { scheduledItemId = planId, planDate = today });
        Assert.Equal(HttpStatusCode.OK, relink.StatusCode);

        var unknown = await client.PutAsJsonAsync($"/api/accounts/{accountId}/occurrences/{item.GetProperty("id").GetGuid()}/plan", new { scheduledItemId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var deletePlan = await client.DeleteAsync($"/api/accounts/{accountId}/schedules/{planId}");
        Assert.Equal(HttpStatusCode.NoContent, deletePlan.StatusCode);
        var remaining = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences");
        Assert.Equal(JsonValueKind.Null, remaining.EnumerateArray().Single().GetProperty("scheduledItemId").ValueKind);
    }

    [Fact]
    public async Task OccurrencePage_IsPagedAndPlanLinksListPaidOccurrences()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new { UserName = "pagerocc", Email = "pagerocc@example.com", Password = "P@ssword1" });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "pagerocc", Password = "P@ssword1" });
        var auth = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());

        var account = await (await client.PostAsJsonAsync("/api/accounts", new { Name = "Current" })).Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("id").GetGuid();
        var today = DateTime.UtcNow.Date;
        var plan = await (await client.PostAsJsonAsync($"/api/accounts/{accountId}/schedules", new { Name = "Gym", Amount = 50m, StartDate = today, Frequency = 2, IsIncome = false })).Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetGuid();

        var rows = Enumerable.Range(1, 7).Select(day => new
        {
            date = today.AddDays(-day).ToString("yyyy-MM-dd"),
            amount = -(decimal)day,
            name = $"Row {day}",
            importId = $"tx_{day}",
            scheduledItemId = day == 1 ? planId : (Guid?)null,
            planDate = day == 1 ? today.ToString("yyyy-MM-dd") : null
        }).ToArray();
        var import = await client.PostAsJsonAsync($"/api/accounts/{accountId}/occurrences/import", new { rows });
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var first = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences/page?skip=0&take=3");
        Assert.Equal(7, first.GetProperty("total").GetInt32());
        Assert.Equal(3, first.GetProperty("items").GetArrayLength());
        Assert.Equal("Row 1", first.GetProperty("items")[0].GetProperty("description").GetString());

        var last = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences/page?skip=6&take=3");
        Assert.Equal(1, last.GetProperty("items").GetArrayLength());

        var capped = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences/page?skip=-1&take=100000");
        Assert.Equal(7, capped.GetProperty("items").GetArrayLength());

        var links = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/occurrences/plan-links");
        Assert.Equal(1, links.GetArrayLength());
        Assert.Equal(planId, links[0].GetProperty("scheduledItemId").GetGuid());

        var other = await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/occurrences/page");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }
}
