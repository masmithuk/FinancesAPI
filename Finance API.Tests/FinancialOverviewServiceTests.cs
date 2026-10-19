using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class FinancialOverviewServiceTests
{
    // Test: Get setup status and financial overview summary
    // Purpose: Verify that the service correctly reports whether a user has an account and a balance,
    // and that the financial overview summary contains account entries for created accounts.
    // Steps:
    // 1. Create an in-memory DB and seed a user-owned account, balance and pot.
    // 2. Call GetSetupStatusAsync and assert HasAccount and HasBalance are true.
    // 3. Call GetFinancialOverviewAsync and assert the created account is present in the summary.
    [Fact]
    public async Task GetSetupStatus_And_FinancialOverview_Work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Finance_API.Persistence.FinanceDbContext>();
        var overview = scope.ServiceProvider.GetRequiredService<Finance_API.Services.FinancialOverviewService>();

        // create a user and account owned by user id 'testuser'
        var ownerId = "test-owner";
        var account = new Finance_API.Persistence.Entities.Account { Name = "A", OwnerId = ownerId };
        db.Accounts.Add(account);
        db.Balances.Add(new Finance_API.Persistence.Entities.Balance { AccountId = account.Id, Date = DateTime.UtcNow.Date, Amount = 100m });
        db.Pots.Add(new Finance_API.Persistence.Entities.Pot { AccountId = account.Id, Name = "P1" });
        await db.SaveChangesAsync();

        var status = await overview.GetSetupStatusAsync(ownerId);
        Assert.True(status.HasAccount);
        Assert.True(status.HasBalance);

        var summary = await overview.GetFinancialOverviewAsync(ownerId);
        Assert.NotNull(summary);
        Assert.Contains(summary.Accounts, a => a.AccountId == account.Id);
    }

    // Test: Financial overview summary recommends transfers for payday and reports available spending
    // Purpose: Seed accounts, pots, goals and scheduled items to emulate a payday flow and
    // verify that financial overview recommends moving money to pots (mortgage, food) and shows expected
    // availableForSpending values for accounts (e.g., Santander).
    // Steps:
    // 1. Register and authenticate a user, then seed multiple accounts and pots with goals and schedules.
    // 2. Request the /api/financialoverview endpoint and assert that expected recommendations exist and
    //    that account summaries contain expected availableForSpending amounts.
    [Fact]
    public async Task FinancialOverviewSummary_Recommends_Payday_Pot_Transfers_And_Shows_Santander_Spending_Money()
    {
        // Setup in-memory database and test server
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = factory.CreateClient();

        // Register and authenticate a user
        var register = new { UserName = "paydayuser", Email = "payday@example.com", Password = "P@ssword1" };
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(System.Net.HttpStatusCode.Created, registerResponse.StatusCode);
        using var registeredUser = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        var ownerId = registeredUser.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ownerId));

        // Authenticate the user to obtain a JWT token
        var login = new { UserName = "paydayuser", Password = "P@ssword1" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", login);
        Assert.Equal(System.Net.HttpStatusCode.OK, loginResponse.StatusCode);
        using var auth = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.RootElement.GetProperty("token").GetString());

        // Seed accounts, pots, goals and scheduled items to emulate a payday flow
        var today = DateTime.UtcNow.Date;
        var paydayMonth = new DateTime(today.Year, today.Month, 1);
        if (today.Day >= 25)
        {
            paydayMonth = paydayMonth.AddMonths(1);
        }

        var payday = new DateTime(paydayMonth.Year, paydayMonth.Month, 25);
        var nextMonth = new DateTime(payday.Year, payday.Month, 1).AddMonths(1);
        var mortgageDueDate = nextMonth;
        var foodGoalDueDate = nextMonth.AddMonths(1).AddDays(-1);
        var carDueDate = nextMonth.AddDays(14);

        Guid santanderId;
        Guid nationwideId;
        Guid monzoId;
        Guid mortgagePotId;
        Guid foodPotId;
        
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Finance_API.Persistence.FinanceDbContext>();
            var santander = new Finance_API.Persistence.Entities.Account { Name = "Santander", OwnerId = ownerId! };
            var nationwide = new Finance_API.Persistence.Entities.Account { Name = "Nationwide", OwnerId = ownerId! };
            var monzo = new Finance_API.Persistence.Entities.Account { Name = "Monzo", OwnerId = ownerId! };
            db.Accounts.AddRange(santander, nationwide, monzo);

            var mortgagePot = new Finance_API.Persistence.Entities.Pot { Name = "Mortgage", Account = nationwide };
            var foodPot = new Finance_API.Persistence.Entities.Pot { Name = "Food", Account = monzo };
            db.Pots.AddRange(mortgagePot, foodPot);
            db.Goals.AddRange(
                new Finance_API.Persistence.Entities.Goal
                {
                    Pot = mortgagePot,
                    TargetAmount = 1000m,
                    DueDate = mortgageDueDate
                },
                new Finance_API.Persistence.Entities.Goal
                {
                    Pot = foodPot,
                    TargetAmount = 500m,
                    DueDate = foodGoalDueDate
                });
            db.ScheduledItems.AddRange(
                new Finance_API.Persistence.Entities.ScheduledItem
                {
                    Name = "Payday income",
                    Account = santander,
                    Amount = 3000m,
                    StartDate = payday,
                    Frequency = 2,
                    IsIncome = true
                },
                new Finance_API.Persistence.Entities.ScheduledItem
                {
                    Name = "Car",
                    Account = santander,
                    Amount = 300m,
                    StartDate = carDueDate,
                    Frequency = 2,
                    IsIncome = false
                });

            await db.SaveChangesAsync();
            santanderId = santander.Id;
            nationwideId = nationwide.Id;
            monzoId = monzo.Id;
            mortgagePotId = mortgagePot.Id;
            foodPotId = foodPot.Id;
        }

        var financialOverviewResponse = await client.GetAsync("/api/financialoverview");
        Assert.Equal(System.Net.HttpStatusCode.OK, financialOverviewResponse.StatusCode);
        using var financialOverview = JsonDocument.Parse(await financialOverviewResponse.Content.ReadAsStringAsync());
        var root = financialOverview.RootElement;
        var failures = new List<string>();

        var recommendations = root.GetProperty("recommendations").EnumerateArray().ToList();
        var hasMortgageTransfer = recommendations.Any(recommendation =>
            recommendation.TryGetProperty("fromAccountId", out var fromAccountId)
            && fromAccountId.GetGuid() == santanderId
            && recommendation.TryGetProperty("toAccountId", out var toAccountId)
            && toAccountId.GetGuid() == nationwideId
            && recommendation.TryGetProperty("toPotId", out var toPotId)
            && toPotId.GetGuid() == mortgagePotId
            && recommendation.TryGetProperty("amount", out var amount)
            && amount.GetDecimal() == 1000m);
        if (!hasMortgageTransfer)
        {
            failures.Add("Expected a £1,000 recommendation from Santander to the Nationwide mortgage pot.");
        }

        var hasFoodTransfer = recommendations.Any(recommendation =>
            recommendation.TryGetProperty("fromAccountId", out var fromAccountId)
            && fromAccountId.GetGuid() == santanderId
            && recommendation.TryGetProperty("toAccountId", out var toAccountId)
            && toAccountId.GetGuid() == monzoId
            && recommendation.TryGetProperty("toPotId", out var toPotId)
            && toPotId.GetGuid() == foodPotId
            && recommendation.TryGetProperty("amount", out var amount)
            && amount.GetDecimal() == 500m);
        if (!hasFoodTransfer)
        {
            failures.Add("Expected a £500 recommendation from Santander to the Monzo food pot.");
        }

        var santanderSummary = root.GetProperty("accounts").EnumerateArray().Single(account =>
            account.GetProperty("accountId").GetGuid() == santanderId);
        if (!santanderSummary.TryGetProperty("availableForSpending", out var availableForSpending)
            || availableForSpending.GetDecimal() != 1200m)
        {
            failures.Add("Expected Santander to show £1,200 available for spending after the transfers and car payment.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
