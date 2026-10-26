using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

namespace Finance_API.Tests;

/// <summary>Attacker-perspective tests: authentication bypass, tenant isolation, input abuse and hardening.</summary>
public sealed class SecurityTests
{
    private const string Password = "P@ssword1";

    private static async Task<HttpClient> SignUpAsync(FinanceApiFactory factory, string name)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new { UserName = name, Email = $"{name}@example.com", Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { UserName = name, Password });
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());
        return client;
    }

    private static async Task<Guid> IdOf(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"setup call failed: {(int)response.StatusCode}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Anonymous_Requests_Are_Rejected_On_Every_Protected_Route()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();
        var g = Guid.NewGuid();

        var routes = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, "/api/me"),
            (HttpMethod.Get, "/api/accounts"),
            (HttpMethod.Post, "/api/accounts"),
            (HttpMethod.Get, $"/api/accounts/{g}"),
            (HttpMethod.Get, $"/api/accounts/{g}/balances"),
            (HttpMethod.Get, $"/api/accounts/{g}/occurrences"),
            (HttpMethod.Get, $"/api/accounts/{g}/schedules"),
            (HttpMethod.Get, $"/api/accounts/{g}/pots"),
            (HttpMethod.Get, $"/api/accounts/{g}/forecast"),
            (HttpMethod.Get, $"/api/accounts/{g}/running-balances"),
            (HttpMethod.Get, $"/api/pots/{g}/goals"),
            (HttpMethod.Get, $"/api/pots/{g}/contributions"),
            (HttpMethod.Get, "/api/financialoverview"),
            (HttpMethod.Get, "/api/setup"),
            (HttpMethod.Get, "/api/payday-plan"),
            (HttpMethod.Delete, $"/api/accounts/{g}/occurrences/{g}"),
        };

        foreach (var (method, url) in routes)
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, url)
            {
                Content = method == HttpMethod.Get || method == HttpMethod.Delete ? null : JsonContent.Create(new { })
            });
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {url} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Forged_Or_Tampered_Tokens_Are_Rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        var good = await SignUpAsync(factory, "victim");
        var goodToken = good.DefaultRequestHeaders.Authorization!.Parameter!;
        var userId = new JwtSecurityTokenHandler().ReadJwtToken(goodToken).Subject;

        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        string Sign(string key, string issuer = "FinanceApiDev", string audience = "FinanceApiClients", DateTime? expires = null)
        {
            var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(issuer, audience, [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(JwtRegisteredClaimNames.Sub, userId)],
                expires: expires ?? DateTime.UtcNow.AddHours(1), signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        var parts = goodToken.Split('.');
        var tokens = new Dictionary<string, string>
        {
            ["alg none"] = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.",
            ["wrong key"] = Sign("an-attacker-guessed-key-that-is-long-enough-123"),
            ["wrong issuer"] = Sign("dev-secret-change-me-REPLACE_IN_PRODUCTION", issuer: "evil"),
            ["wrong audience"] = Sign("dev-secret-change-me-REPLACE_IN_PRODUCTION", audience: "evil"),
            ["tampered payload"] = $"{parts[0]}.{B64("{\"sub\":\"someone-else\"}")}.{parts[2]}",
            ["garbage"] = "not.a.jwt",
        };

        foreach (var (label, token) in tokens)
        {
            using var attacker = factory.CreateClient();
            attacker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await attacker.GetAsync("/api/me");
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{label} token returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Other_Users_Cannot_Touch_Foreign_Resources()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        var alice = await SignUpAsync(factory, "alice");
        var bob = await SignUpAsync(factory, "bob");

        var acc = await IdOf(await alice.PostAsJsonAsync("/api/accounts", new { Name = "Alice" }));
        var balance = await IdOf(await alice.PostAsJsonAsync($"/api/accounts/{acc}/balances", new { Date = DateTime.UtcNow.Date, Amount = 50m }));
        var schedule = await IdOf(await alice.PostAsJsonAsync($"/api/accounts/{acc}/schedules",
            new { Name = "Rent", Amount = 10m, StartDate = DateTime.UtcNow.Date, Frequency = 1, IsIncome = false }));
        var pot = await IdOf(await alice.PostAsJsonAsync($"/api/accounts/{acc}/pots", new { Name = "Holiday" }));
        var goal = await IdOf(await alice.PostAsJsonAsync($"/api/pots/{pot}/goals", new { TargetAmount = 100m, DueDate = (DateTime?)null }));
        var contribution = await IdOf(await alice.PostAsJsonAsync($"/api/pots/{pot}/contributions", new { AccountId = acc, Amount = 5m, Date = DateTime.UtcNow.Date }));
        var occ = await IdOf(await alice.PostAsJsonAsync($"/api/accounts/{acc}/occurrences",
            new { Date = DateTime.UtcNow.Date.AddDays(5), Amount = 9m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "a-1" }));

        var bobAccount = await IdOf(await bob.PostAsJsonAsync("/api/accounts", new { Name = "Bob" }));
        var today = DateTime.UtcNow.Date;
        var balancesBefore = (await alice.GetFromJsonAsync<JsonElement>($"/api/accounts/{acc}/balances")).GetArrayLength();

        var attacks = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, $"/api/accounts/{acc}", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/balances", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/balances/history", null),
            (HttpMethod.Post, $"/api/accounts/{acc}/balances", new { Date = today, Amount = 1m }),
            (HttpMethod.Put, $"/api/accounts/{acc}/balances/{balance}", new { Date = today, Amount = 1m }),
            (HttpMethod.Delete, $"/api/accounts/{acc}/balances/{balance}", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/schedules", null),
            (HttpMethod.Post, $"/api/accounts/{acc}/schedules", new { Name = "x", Amount = 1m, StartDate = today, Frequency = 1, IsIncome = false }),
            (HttpMethod.Put, $"/api/accounts/{acc}/schedules/{schedule}", new { Name = "x", Amount = 1m, StartDate = today, Frequency = 1, IsIncome = false, AccountId = bobAccount }),
            (HttpMethod.Delete, $"/api/accounts/{acc}/schedules/{schedule}", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/pots", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/pots/{pot}", null),
            (HttpMethod.Put, $"/api/accounts/{acc}/pots/{pot}", new { Name = "x", AccountId = bobAccount }),
            (HttpMethod.Delete, $"/api/accounts/{acc}/pots/{pot}", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/running-balances", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/forecast", null),
            (HttpMethod.Get, $"/api/pots/{pot}/goals", null),
            (HttpMethod.Post, $"/api/pots/{pot}/goals", new { TargetAmount = 1m, DueDate = (DateTime?)null }),
            (HttpMethod.Put, $"/api/pots/{pot}/goals/{goal}", new { TargetAmount = 1m, DueDate = (DateTime?)null }),
            (HttpMethod.Delete, $"/api/pots/{pot}/goals/{goal}", null),
            (HttpMethod.Get, $"/api/pots/{pot}/contributions", null),
            (HttpMethod.Post, $"/api/pots/{pot}/contributions", new { AccountId = bobAccount, Amount = 1m, Date = today }),
            (HttpMethod.Put, $"/api/pots/{pot}/contributions/{contribution}", new { AccountId = bobAccount, Amount = 1m, Date = today }),
            (HttpMethod.Delete, $"/api/pots/{pot}/contributions/{contribution}", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/occurrences", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/occurrences/page", null),
            (HttpMethod.Get, $"/api/accounts/{acc}/occurrences/plan-links", null),
            (HttpMethod.Post, $"/api/accounts/{acc}/occurrences", new { Date = today, Amount = 1m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "b-1" }),
            (HttpMethod.Post, $"/api/accounts/{acc}/occurrences/import", new { Rows = new[] { new { Date = today, Amount = 1m, Name = "x", ImportId = "i1" } } }),
            (HttpMethod.Post, $"/api/accounts/{acc}/occurrences/{occ}/reconcile", null),
            (HttpMethod.Put, $"/api/accounts/{acc}/occurrences/{occ}", new { AccountId = bobAccount, Date = today, Amount = 1m, IsIncome = false }),
            (HttpMethod.Put, $"/api/accounts/{acc}/occurrences/{occ}/plan", new { ScheduledItemId = schedule, PlanDate = today }),
            (HttpMethod.Delete, $"/api/accounts/{acc}/occurrences/{occ}", null),
        };

        foreach (var (method, url, body) in attacks)
        {
            var response = await bob.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) });
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Bob {method} {url} returned {(int)response.StatusCode}");
        }

        var listed = await bob.GetFromJsonAsync<JsonElement>("/api/accounts");
        Assert.Equal(1, listed.GetArrayLength());

        // Alice's data must be untouched after the attack run.
        var aliceBalances = await alice.GetFromJsonAsync<JsonElement>($"/api/accounts/{acc}/balances");
        Assert.Equal(balancesBefore, aliceBalances.GetArrayLength());
    }

    [Fact]
    public async Task Occurrence_Cannot_Be_Linked_To_A_Foreign_Schedule()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        var alice = await SignUpAsync(factory, "alice2");
        var bob = await SignUpAsync(factory, "bob2");

        var aliceAcc = await IdOf(await alice.PostAsJsonAsync("/api/accounts", new { Name = "A" }));
        var aliceSchedule = await IdOf(await alice.PostAsJsonAsync($"/api/accounts/{aliceAcc}/schedules",
            new { Name = "Rent", Amount = 10m, StartDate = DateTime.UtcNow.Date, Frequency = 1, IsIncome = false }));
        var bobAcc = await IdOf(await bob.PostAsJsonAsync("/api/accounts", new { Name = "B" }));

        var response = await bob.PostAsJsonAsync($"/api/accounts/{bobAcc}/occurrences",
            new { Date = DateTime.UtcNow.Date.AddDays(3), Amount = 1m, IsIncome = false, ScheduledItemId = aliceSchedule, ExternalReference = "x" });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Abusive_Input_Is_Rejected_With_Client_Errors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        var user = await SignUpAsync(factory, "fuzzer");
        var acc = await IdOf(await user.PostAsJsonAsync("/api/accounts", new { Name = "Main" }));
        var pot = await IdOf(await user.PostAsJsonAsync($"/api/accounts/{acc}/pots", new { Name = "Pot" }));
        var huge = new string('A', 100_000);
        var today = DateTime.UtcNow.Date;

        var bad = new (HttpMethod Method, string Url, object Body)[]
        {
            (HttpMethod.Post, "/api/accounts", new { Name = huge }),
            (HttpMethod.Post, "/api/accounts", new { Name = "  " }),
            (HttpMethod.Post, $"/api/accounts/{acc}/pots", new { Name = huge }),
            (HttpMethod.Post, $"/api/accounts/{acc}/balances", new { Date = today, Amount = 1m, Note = huge }),
            (HttpMethod.Post, $"/api/accounts/{acc}/balances", new { Date = today, Amount = 79228162514264337593543950335m }),
            (HttpMethod.Post, $"/api/accounts/{acc}/schedules", new { Name = huge, Amount = 1m, StartDate = today, Frequency = 1, IsIncome = false }),
            (HttpMethod.Post, $"/api/accounts/{acc}/schedules", new { Name = "ok", Amount = -5m, StartDate = today, Frequency = 1, IsIncome = false }),
            (HttpMethod.Post, $"/api/accounts/{acc}/schedules", new { Name = "ok", Amount = 1m, StartDate = today, Frequency = 999, IsIncome = false }),
            (HttpMethod.Post, $"/api/accounts/{acc}/occurrences", new { Date = today, Amount = 1m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = huge }),
            (HttpMethod.Post, $"/api/accounts/{acc}/occurrences", new { Date = today, Amount = -1m, IsIncome = false, ScheduledItemId = (Guid?)null, ExternalReference = "neg" }),
            (HttpMethod.Post, $"/api/pots/{pot}/goals", new { TargetAmount = -1m, DueDate = (DateTime?)null }),
            (HttpMethod.Post, $"/api/pots/{pot}/contributions", new { AccountId = acc, Amount = 79228162514264337593543950335m, Date = today }),
        };

        foreach (var (method, url, body) in bad)
        {
            var response = await user.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) });
            var status = (int)response.StatusCode;
            Assert.True(status is >= 400 and < 500, $"{method} {url} {JsonSerializer.Serialize(body).Length}b returned {status}");
        }
    }

    [Fact]
    public async Task Registration_Rejects_Oversized_And_Malformed_Identity_Fields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var cases = new object[]
        {
            new { UserName = new string('u', 5000), Email = "a@example.com", Password },
            new { UserName = "okname", Email = new string('e', 5000) + "@example.com", Password },
            new { UserName = "okname2", Email = "not-an-email", Password },
        };

        foreach (var body in cases)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", body);
            Assert.True((int)response.StatusCode is >= 400 and < 500, $"returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Injection_Strings_Do_Not_Break_Login_Or_Search()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { UserName = "' OR '1'='1' --", Password = "' OR '1'='1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Responses_Carry_Security_Headers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").SingleOrDefault());
        Assert.Contains("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("Cache-Control"));
    }

    [Fact]
    public async Task Oversized_Request_Bodies_Are_Refused()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        var user = await SignUpAsync(factory, "bigbody");
        var acc = await IdOf(await user.PostAsJsonAsync("/api/accounts", new { Name = "Main" }));

        var payload = new StringContent("{\"Rows\":[],\"Pad\":\"" + new string('x', 5 * 1024 * 1024) + "\"}", Encoding.UTF8, "application/json");
        var response = await user.PostAsync($"/api/accounts/{acc}/occurrences/import", payload);

        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, $"returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Error_Responses_Do_Not_Leak_Internals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        var user = await SignUpAsync(factory, "prober");

        var response = await user.PostAsync("/api/accounts", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode is >= 400 and < 500, $"returned {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }
}
