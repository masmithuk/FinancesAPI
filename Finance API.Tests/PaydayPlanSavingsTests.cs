using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Finance_API.Persistence;
using Finance_API.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance_API.Tests;

/// <summary>
/// Spare money in the next pay period: savings accounts and pots get their amount, and what is left
/// goes to the spending account.
/// </summary>
public sealed class PaydayPlanSavingsTests
{
    // Payday is today + 3: a fortnightly £1,000 salary into Income, last paid 11 days ago, so paydays
    // fall on +3, +17, +31… Rent of £300 is due from Bills on today + 10, leaving £700 spare in Income.
    private sealed class Seed
    {
        public Guid IncomeId { get; init; }
        public Guid BillsId { get; init; }
        public Dictionary<string, Guid> Ids { get; } = new();
    }

    private static async Task<Seed> SeedAsync(
        FinanceApiFactory factory,
        string userName,
        Action<FinanceDbContext, string, Account, Account, Dictionary<string, Guid>>? extra = null)
    {
        var today = DateTime.UtcNow.Date;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var ownerId = await db.Users.Where(user => user.UserName == userName).Select(user => user.Id).SingleAsync();
        var income = new Account { Name = "Income", OwnerId = ownerId };
        var bills = new Account { Name = "Bills", OwnerId = ownerId };
        db.Accounts.AddRange(income, bills);
        db.ScheduledItems.AddRange(
            new ScheduledItem
            {
                Name = "Salary", Account = income, Amount = 1000m,
                StartDate = today.AddDays(-11), Frequency = 1, IsIncome = true
            },
            new ScheduledItem
            {
                Name = "Rent", Account = bills, Amount = 300m,
                StartDate = today.AddDays(10), Frequency = 2, IsIncome = false
            });
        var seed = new Seed { IncomeId = income.Id, BillsId = bills.Id };
        extra?.Invoke(db, ownerId, income, bills, seed.Ids);
        await db.SaveChangesAsync();
        return seed;
    }

    private static Guid AddAccount(
        FinanceDbContext db, string ownerId, Dictionary<string, Guid> ids, string name, string role,
        decimal balance = 0m, decimal? savePerPayday = null)
    {
        var account = new Account { Name = name, OwnerId = ownerId, Role = role, SavePerPayday = savePerPayday };
        db.Accounts.Add(account);
        if (balance != 0m)
        {
            db.Balances.Add(new Balance { Account = account, Date = DateTime.UtcNow.Date, Amount = balance });
        }

        ids[name] = account.Id;
        return account.Id;
    }

    private static List<JsonElement> Transfers(JsonElement plan) =>
        plan.GetProperty("transfers").EnumerateArray().ToList();

    private static JsonElement TransferOf(JsonElement plan, string kind) =>
        Assert.Single(Transfers(plan), transfer => transfer.GetProperty("kind").GetString() == kind);

    private static JsonElement AccountIn(JsonElement plan, Guid accountId) =>
        Assert.Single(plan.GetProperty("accounts").EnumerateArray(), account =>
            account.GetProperty("accountId").GetGuid() == accountId);

    [Fact]
    public async Task Bill_Transfers_Are_Marked_As_Bills()
    {
        var (factory, client, connection) = await StartAsync("kind-bills-user");
        await using var _ = connection;
        using var __ = factory;
        await SeedAsync(factory, "kind-bills-user");

        var plan = await GetPlanAsync(client, "next");

        var bills = TransferOf(plan, "bills");
        Assert.Equal(300m, bills.GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, bills.GetProperty("toPotId").ValueKind);
    }

    [Fact]
    public async Task Spending_Account_Receives_The_Spare_Money()
    {
        var (factory, client, connection) = await StartAsync("spending-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "spending-user",
            (db, owner, _, _, ids) => AddAccount(db, owner, ids, "Spending", AccountRoles.Spending));

        var plan = await GetPlanAsync(client, "next");

        var spending = TransferOf(plan, "spending");
        Assert.Equal(seed.IncomeId, spending.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(seed.Ids["Spending"], spending.GetProperty("toAccountId").GetGuid());
        Assert.Equal(700m, spending.GetProperty("amount").GetDecimal());
        Assert.Equal(0m, AccountIn(plan, seed.IncomeId).GetProperty("leftAfterTransfers").GetDecimal());
        Assert.Equal(700m, AccountIn(plan, seed.Ids["Spending"]).GetProperty("leftAfterTransfers").GetDecimal());
        Assert.Equal("spending", AccountIn(plan, seed.Ids["Spending"]).GetProperty("role").GetString());
        Assert.Equal("everyday", AccountIn(plan, seed.IncomeId).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Savings_Account_Gets_Its_Amount_Before_Spending()
    {
        var (factory, client, connection) = await StartAsync("savings-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "savings-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "Savings", AccountRoles.Savings, savePerPayday: 200m);
        });

        var plan = await GetPlanAsync(client, "next");

        var savings = TransferOf(plan, "savings");
        Assert.Equal(seed.IncomeId, savings.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(seed.Ids["Savings"], savings.GetProperty("toAccountId").GetGuid());
        Assert.Equal(200m, savings.GetProperty("amount").GetDecimal());
        Assert.Equal(500m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Savings_Account_With_No_Amount_Gets_A_Tenth_Of_The_Pay()
    {
        // £1,000 paid in at this payday, so £100 is suggested for savings and £600 is left to spend.
        var (factory, client, connection) = await StartAsync("savings-default-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "savings-default-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "Savings", AccountRoles.Savings);
        });

        var plan = await GetPlanAsync(client, "next");

        var savings = TransferOf(plan, "savings");
        Assert.Equal(seed.IncomeId, savings.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(seed.Ids["Savings"], savings.GetProperty("toAccountId").GetGuid());
        Assert.Equal(100m, savings.GetProperty("amount").GetDecimal());
        Assert.Equal(600m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Savings_Accounts_With_No_Amount_Share_The_Tenth()
    {
        var (factory, client, connection) = await StartAsync("savings-share-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "savings-share-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "Savings A", AccountRoles.Savings);
            AddAccount(db, owner, ids, "Savings B", AccountRoles.Savings);
            AddAccount(db, owner, ids, "Savings C", AccountRoles.Savings, savePerPayday: 200m);
        });

        var plan = await GetPlanAsync(client, "next");

        decimal SentTo(string name) => Transfers(plan)
            .Where(transfer => transfer.GetProperty("toAccountId").GetGuid() == seed.Ids[name])
            .Sum(transfer => transfer.GetProperty("amount").GetDecimal());
        Assert.Equal(50m, SentTo("Savings A"));
        Assert.Equal(50m, SentTo("Savings B"));
        Assert.Equal(200m, SentTo("Savings C"));
        Assert.Equal(400m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Savings_Are_Capped_At_The_Spare_Money()
    {
        var (factory, client, connection) = await StartAsync("savings-cap-user");
        await using var _ = connection;
        using var __ = factory;
        await SeedAsync(factory, "savings-cap-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "Savings", AccountRoles.Savings, savePerPayday: 900m);
        });

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(700m, TransferOf(plan, "savings").GetProperty("amount").GetDecimal());
        Assert.DoesNotContain(Transfers(plan), transfer => transfer.GetProperty("kind").GetString() == "spending");
    }

    [Fact]
    public async Task Savings_Account_Is_Not_Swept_To_Spending_Or_Used_First_For_Bills()
    {
        // "A Savings" sorts before "Income", so name order alone would take the rent from savings.
        var (factory, client, connection) = await StartAsync("savings-safe-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "savings-safe-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "A Savings", AccountRoles.Savings, balance: 5000m);
        });

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(seed.IncomeId, TransferOf(plan, "bills").GetProperty("fromAccountId").GetGuid());
        Assert.DoesNotContain(Transfers(plan), transfer =>
            transfer.GetProperty("fromAccountId").GetGuid() == seed.Ids["A Savings"]);
        Assert.Equal(600m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Pot_Gets_Its_Amount_From_Its_Own_Account()
    {
        var (factory, client, connection) = await StartAsync("pot-user");
        await using var _ = connection;
        using var __ = factory;
        var potId = Guid.Empty;
        var seed = await SeedAsync(factory, "pot-user", (db, owner, income, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            var pot = new Pot { Name = "Holiday", Account = income, SavePerPayday = 100m };
            db.Pots.Add(pot);
            potId = pot.Id;
        });

        var plan = await GetPlanAsync(client, "next");

        var pot = TransferOf(plan, "pot");
        Assert.Equal(seed.IncomeId, pot.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(seed.IncomeId, pot.GetProperty("toAccountId").GetGuid());
        Assert.Equal(potId, pot.GetProperty("toPotId").GetGuid());
        Assert.Equal("Holiday", pot.GetProperty("toPotName").GetString());
        Assert.Equal(100m, pot.GetProperty("amount").GetDecimal());
        Assert.Equal(600m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, AccountIn(plan, seed.IncomeId).GetProperty("leftAfterTransfers").GetDecimal());
    }

    [Fact]
    public async Task Pot_Goal_With_A_Due_Date_Is_Spread_Over_The_Paydays_Left()
    {
        // £600 still to save by today + 32: paydays on +3, +17 and +31 are left, so £200 each.
        var (factory, client, connection) = await StartAsync("pot-goal-user");
        await using var _ = connection;
        using var __ = factory;
        await SeedAsync(factory, "pot-goal-user", (db, _, income, _, _) =>
        {
            var pot = new Pot { Name = "Car", Account = income };
            db.Pots.Add(pot);
            db.Goals.Add(new Goal { Pot = pot, TargetAmount = 600m, DueDate = DateTime.UtcNow.Date.AddDays(32) });
        });

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(200m, TransferOf(plan, "pot").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Pot_Amount_Stops_At_What_The_Goal_Still_Needs()
    {
        var (factory, client, connection) = await StartAsync("pot-goal-cap-user");
        await using var _ = connection;
        using var __ = factory;
        await SeedAsync(factory, "pot-goal-cap-user", (db, _, income, _, _) =>
        {
            var pot = new Pot { Name = "Car", Account = income, SavePerPayday = 100m };
            db.Pots.Add(pot);
            db.Goals.Add(new Goal { Pot = pot, TargetAmount = 500m });
            // Saved long before the last payday, so it only counts towards the goal.
            db.PotContributions.Add(new PotContribution { Pot = pot, Amount = 460m, Date = DateTime.UtcNow.Date.AddDays(-30) });
        });

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(40m, TransferOf(plan, "pot").GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData(100, null)]
    [InlineData(40, 60)]
    public async Task Pot_Payment_Made_Before_Payday_Counts_Towards_It(int paid, int? stillSuggested)
    {
        var user = $"pot-paid-{paid}-user";
        var (factory, client, connection) = await StartAsync(user);
        await using var _ = connection;
        using var __ = factory;
        var potId = Guid.Empty;
        var seed = await SeedAsync(factory, user, (db, owner, income, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            var pot = new Pot { Name = "Holiday", Account = income, SavePerPayday = 100m };
            db.Pots.Add(pot);
            potId = pot.Id;
        });
        var contribution = await client.PostAsJsonAsync($"/api/pots/{potId}/contributions",
            new { AccountId = seed.IncomeId, Amount = (decimal)paid, Date = DateTime.UtcNow.Date });
        Assert.Equal(HttpStatusCode.Created, contribution.StatusCode);

        var plan = await GetPlanAsync(client, "next");

        var pots = Transfers(plan).Where(transfer => transfer.GetProperty("kind").GetString() == "pot").ToList();
        if (stillSuggested is { } amount)
        {
            Assert.Equal(amount, Assert.Single(pots).GetProperty("amount").GetDecimal());
        }
        else
        {
            Assert.Empty(pots);
        }

        // The £700 spare was cut by what was paid in, and by what is still to pay.
        Assert.Equal(600m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Pot_Payment_On_The_Last_Payday_Does_Not_Count_Towards_The_Next()
    {
        var (factory, client, connection) = await StartAsync("pot-last-payday-user");
        await using var _ = connection;
        using var __ = factory;
        await SeedAsync(factory, "pot-last-payday-user", (db, _, income, _, _) =>
        {
            var pot = new Pot { Name = "Holiday", Account = income, SavePerPayday = 100m };
            db.Pots.Add(pot);
            db.PotContributions.Add(new PotContribution { Pot = pot, Amount = 100m, Date = DateTime.UtcNow.Date.AddDays(-11) });
        });

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(100m, TransferOf(plan, "pot").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Savings_Transfer_Made_Before_Payday_Counts_Towards_It()
    {
        var (factory, client, connection) = await StartAsync("savings-paid-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "savings-paid-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "Savings", AccountRoles.Savings, savePerPayday: 200m);
        });
        var transfer = await client.PostAsJsonAsync("/api/transfers",
            new { FromAccountId = seed.IncomeId, ToAccountId = seed.Ids["Savings"], Amount = 150m });
        Assert.True(transfer.IsSuccessStatusCode);

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(50m, TransferOf(plan, "savings").GetProperty("amount").GetDecimal());
        Assert.Equal(500m, TransferOf(plan, "spending").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task This_Pay_Period_Suggests_No_Savings_Or_Spending_Transfers()
    {
        var (factory, client, connection) = await StartAsync("current-savings-user");
        await using var _ = connection;
        using var __ = factory;
        await SeedAsync(factory, "current-savings-user", (db, owner, _, _, ids) =>
        {
            AddAccount(db, owner, ids, "Spending", AccountRoles.Spending);
            AddAccount(db, owner, ids, "Savings", AccountRoles.Savings, savePerPayday: 200m);
        });

        var plan = await GetPlanAsync(client, "current");

        Assert.All(Transfers(plan), transfer => Assert.Equal("bills", transfer.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Plan_Settings_Set_Role_And_Only_One_Spending_Account()
    {
        var (factory, client, connection) = await StartAsync("settings-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "settings-user");

        var first = await client.PutAsJsonAsync($"/api/accounts/{seed.IncomeId}/plan-settings",
            new { Role = "spending" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await client.PutAsJsonAsync($"/api/accounts/{seed.BillsId}/plan-settings",
            new { Role = "spending" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var accounts = await client.GetFromJsonAsync<JsonElement>("/api/accounts");
        string RoleOf(Guid id) => accounts.EnumerateArray()
            .Single(account => account.GetProperty("id").GetGuid() == id)
            .GetProperty("role").GetString()!;
        Assert.Equal("everyday", RoleOf(seed.IncomeId));
        Assert.Equal("spending", RoleOf(seed.BillsId));
    }

    [Fact]
    public async Task Plan_Settings_Keep_A_Savings_Amount_Only_For_Savings_Accounts()
    {
        var (factory, client, connection) = await StartAsync("settings-savings-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "settings-savings-user");

        var saved = await client.PutAsJsonAsync($"/api/accounts/{seed.BillsId}/plan-settings",
            new { Role = "savings", SavePerPayday = 250m });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(250m, body.GetProperty("savePerPayday").GetDecimal());

        var everyday = await client.PutAsJsonAsync($"/api/accounts/{seed.BillsId}/plan-settings",
            new { Role = "everyday", SavePerPayday = 250m });
        var cleared = await everyday.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("savePerPayday").ValueKind);
    }

    [Theory]
    [InlineData("pocket", null)]
    [InlineData("savings", -1)]
    public async Task Plan_Settings_Reject_Bad_Values(string role, int? savePerPayday)
    {
        var (factory, client, connection) = await StartAsync($"settings-bad-{role}-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, $"settings-bad-{role}-user");

        var response = await client.PutAsJsonAsync($"/api/accounts/{seed.BillsId}/plan-settings",
            new { Role = role, SavePerPayday = (decimal?)savePerPayday });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plan_Settings_For_Someone_Elses_Account_Is_Not_Found()
    {
        var (factory, client, connection) = await StartAsync("settings-owner-user");
        await using var _ = connection;
        using var __ = factory;
        var seed = await SeedAsync(factory, "settings-owner-user");
        using var other = await LoginAsync(factory, "settings-other-user");

        var response = await other.PutAsJsonAsync($"/api/accounts/{seed.BillsId}/plan-settings",
            new { Role = "spending" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pot_Saving_Amount_Can_Be_Set_And_Cleared()
    {
        var (factory, client, connection) = await StartAsync("pot-saving-user");
        await using var _ = connection;
        using var __ = factory;
        var potId = Guid.Empty;
        var seed = await SeedAsync(factory, "pot-saving-user", (db, _, income, _, _) =>
        {
            var pot = new Pot { Name = "Holiday", Account = income };
            db.Pots.Add(pot);
            potId = pot.Id;
        });

        var set = await client.PutAsJsonAsync($"/api/pots/{potId}/saving", new { SavePerPayday = 75m });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var pots = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{seed.IncomeId}/pots");
        Assert.Equal(75m, pots.EnumerateArray().Single().GetProperty("savePerPayday").GetDecimal());

        var cleared = await client.PutAsJsonAsync($"/api/pots/{potId}/saving", new { SavePerPayday = (decimal?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{seed.IncomeId}/pots");
        Assert.Equal(JsonValueKind.Null, after.EnumerateArray().Single().GetProperty("savePerPayday").ValueKind);

        var negative = await client.PutAsJsonAsync($"/api/pots/{potId}/saving", new { SavePerPayday = -5m });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    private static async Task<JsonElement> GetPlanAsync(HttpClient client, string period)
    {
        var response = await client.GetAsync($"/api/payday-plan?period={period}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(FinanceApiFactory Factory, HttpClient Client, SqliteConnection Connection)> StartAsync(
        string userName)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new FinanceApiFactory(connection);
        return (factory, await LoginAsync(factory, userName), connection);
    }

    private static async Task<HttpClient> LoginAsync(FinanceApiFactory factory, string userName)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            Password = "P@ssword1"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { UserName = userName, Password = "P@ssword1" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var auth = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", auth.RootElement.GetProperty("token").GetString());
        return client;
    }
}
