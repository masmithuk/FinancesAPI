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

public sealed class PaydayPlanTests
{
    [Fact]
    public async Task PaydayPlan_Groups_Income_Dates_And_Recommends_Transfer_For_Expense_Account()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "payday-plan-user");

        var today = DateTime.UtcNow.Date;
        var payday = today.AddDays(5);
        Guid incomeAccountId;
        Guid expenseAccountId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ownerId = await db.Users
                .Where(user => user.UserName == "payday-plan-user")
                .Select(user => user.Id)
                .SingleAsync();
            var incomeAccount = new Account { Name = "Income", OwnerId = ownerId };
            var billsAccount = new Account { Name = "Bills", OwnerId = ownerId };
            db.Accounts.AddRange(incomeAccount, billsAccount);
            db.Balances.AddRange(
                new Balance { Account = incomeAccount, Date = today, Amount = 50m },
                new Balance { Account = billsAccount, Date = today, Amount = 100m });
            db.ScheduledItems.AddRange(
                new ScheduledItem
                {
                    Name = "Salary",
                    Account = incomeAccount,
                    Amount = 1000m,
                    StartDate = payday,
                    Frequency = 2,
                    IsIncome = true
                },
                new ScheduledItem
                {
                    Name = "Second payment",
                    Account = incomeAccount,
                    Amount = 100m,
                    StartDate = payday.AddDays(2),
                    Frequency = 2,
                    IsIncome = true
                },
                new ScheduledItem
                {
                    Name = "Rent",
                    Account = billsAccount,
                    Amount = 300m,
                    StartDate = payday.AddDays(7),
                    Frequency = 2,
                    IsIncome = false
                });
            await db.SaveChangesAsync();
            incomeAccountId = incomeAccount.Id;
            expenseAccountId = billsAccount.Id;
        }

        var response = await client.GetAsync("/api/payday-plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(payday, root.GetProperty("paydayStartDate").GetDateTime().Date);
        Assert.Equal(payday.AddDays(2), root.GetProperty("paydayWindowEndDate").GetDateTime().Date);
        Assert.Equal(payday.AddMonths(1), root.GetProperty("nextPaydayDate").GetDateTime().Date);
        Assert.Equal(1100m, root.GetProperty("scheduledIncome").GetDecimal());
        Assert.Equal(300m, root.GetProperty("scheduledExpenses").GetDecimal());
        Assert.Equal(800m, root.GetProperty("discretionaryIncome").GetDecimal());

        var transfer = Assert.Single(root.GetProperty("transfers").EnumerateArray());
        Assert.Equal(incomeAccountId, transfer.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(expenseAccountId, transfer.GetProperty("toAccountId").GetGuid());
        Assert.Equal(200m, transfer.GetProperty("amount").GetDecimal());

        var accounts = root.GetProperty("accounts").EnumerateArray().ToList();
        var bills = Assert.Single(accounts, account =>
            account.GetProperty("accountId").GetGuid() == expenseAccountId);
        Assert.Equal(300m, bills.GetProperty("expensesDue").GetDecimal());
        Assert.Equal(0m, bills.GetProperty("unfundedShortfall").GetDecimal());
    }

    [Fact]
    public async Task PaydayPlan_Reports_Shortfall_When_Income_Accounts_Cannot_Fund_Expenses()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "shortfall-plan-user");

        var today = DateTime.UtcNow.Date;
        var payday = today.AddDays(5);
        Guid expenseAccountId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ownerId = await db.Users
                .Where(user => user.UserName == "shortfall-plan-user")
                .Select(user => user.Id)
                .SingleAsync();
            var incomeAccount = new Account { Name = "Income", OwnerId = ownerId };
            var billsAccount = new Account { Name = "Bills", OwnerId = ownerId };
            db.Accounts.AddRange(incomeAccount, billsAccount);
            db.ScheduledItems.AddRange(
                new ScheduledItem
                {
                    Name = "Pay",
                    Account = incomeAccount,
                    Amount = 100m,
                    StartDate = payday,
                    Frequency = 2,
                    IsIncome = true
                },
                new ScheduledItem
                {
                    Name = "Rent",
                    Account = billsAccount,
                    Amount = 300m,
                    StartDate = payday.AddDays(7),
                    Frequency = 2,
                    IsIncome = false
                });
            await db.SaveChangesAsync();
            expenseAccountId = billsAccount.Id;
        }

        var response = await client.GetAsync("/api/payday-plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var transfer = Assert.Single(root.GetProperty("transfers").EnumerateArray());
        Assert.Equal(100m, transfer.GetProperty("amount").GetDecimal());

        var bills = Assert.Single(root.GetProperty("accounts").EnumerateArray(), account =>
            account.GetProperty("accountId").GetGuid() == expenseAccountId);
        Assert.Equal(200m, bills.GetProperty("unfundedShortfall").GetDecimal());
    }

    [Fact]
    public async Task PaydayPlan_Summarizes_PerAccount_Expenses_And_Discretionary_Income()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "expense-breakdown-user");

        var today = DateTime.UtcNow.Date;
        var paydayMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
        var benefitDate = paydayMonth.AddDays(22);
        var salaryDate = paydayMonth.AddDays(24);
        Guid nationwideId;
        Guid santanderId;
        Guid monzoId;
        Guid jointId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ownerId = await db.Users
                .Where(user => user.UserName == "expense-breakdown-user")
                .Select(user => user.Id)
                .SingleAsync();
            var nationwide = new Account { Name = "Nationwide", OwnerId = ownerId };
            var santander = new Account { Name = "Santander", OwnerId = ownerId };
            var monzo = new Account { Name = "Monzo", OwnerId = ownerId };
            var joint = new Account { Name = "Nationwide Joint", OwnerId = ownerId };
            db.Accounts.AddRange(nationwide, santander, monzo, joint);
            db.ScheduledItems.AddRange(
                new ScheduledItem
                {
                    Name = "Salary",
                    Account = santander,
                    Amount = 4169.79m,
                    StartDate = salaryDate,
                    Frequency = 2,
                    IsIncome = true
                },
                new ScheduledItem
                {
                    Name = "Child Benefit",
                    Account = santander,
                    Amount = 179.80m,
                    StartDate = benefitDate,
                    Frequency = 2,
                    IsIncome = true
                },
                new ScheduledItem
                {
                    Name = "Car Finance",
                    Account = santander,
                    Amount = 380.19m,
                    StartDate = paydayMonth.AddDays(2),
                    Frequency = 2,
                    IsIncome = false
                },
                new ScheduledItem
                {
                    Name = "Monthly expenses",
                    Account = monzo,
                    Amount = 835.12m,
                    StartDate = paydayMonth,
                    Frequency = 2,
                    IsIncome = false
                },
                new ScheduledItem
                {
                    Name = "Joint bills",
                    Account = joint,
                    Amount = 676.15m,
                    StartDate = paydayMonth,
                    Frequency = 2,
                    IsIncome = false
                },
                new ScheduledItem
                {
                    Name = "Mortgage",
                    Account = joint,
                    Amount = 1490.60m,
                    StartDate = paydayMonth,
                    Frequency = 2,
                    IsIncome = false
                });
            db.Balances.Add(new Balance { Account = santander, Amount = 5000m, Date = today.AddDays(-1) });
            await db.SaveChangesAsync();
            nationwideId = nationwide.Id;
            santanderId = santander.Id;
            monzoId = monzo.Id;
            jointId = joint.Id;
        }

        var response = await client.GetAsync("/api/payday-plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(5000m, root.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(967.53m, root.GetProperty("leftToSpend").GetDecimal());
        Assert.Equal(4349.59m, root.GetProperty("scheduledIncome").GetDecimal());
        Assert.Equal(3382.06m, root.GetProperty("scheduledExpenses").GetDecimal());
        Assert.Equal(967.53m, root.GetProperty("discretionaryIncome").GetDecimal());

        var accounts = root.GetProperty("accounts").EnumerateArray().ToList();
        Assert.Equal(
            0m,
            Assert.Single(accounts, account =>
                account.GetProperty("accountId").GetGuid() == nationwideId)
            .GetProperty("expensesDue")
            .GetDecimal());

        var transfers = root.GetProperty("transfers").EnumerateArray().ToList();
        Assert.Equal(2, transfers.Count);
        Assert.Contains(transfers, transfer =>
            transfer.GetProperty("fromAccountId").GetGuid() == santanderId &&
            transfer.GetProperty("toAccountId").GetGuid() == monzoId &&
            transfer.GetProperty("amount").GetDecimal() == 835.12m);
        Assert.Contains(transfers, transfer =>
            transfer.GetProperty("fromAccountId").GetGuid() == santanderId &&
            transfer.GetProperty("toAccountId").GetGuid() == jointId &&
            transfer.GetProperty("amount").GetDecimal() == 2166.75m);
        Assert.Equal(3001.87m, transfers.Sum(transfer => transfer.GetProperty("amount").GetDecimal()));
        Assert.Equal(
            3969.40m,
            transfers.Sum(transfer => transfer.GetProperty("amount").GetDecimal()) +
            root.GetProperty("discretionaryIncome").GetDecimal());
        Assert.Equal(
            380.19m,
            Assert.Single(accounts, account =>
                account.GetProperty("accountId").GetGuid() == santanderId)
            .GetProperty("expensesDue")
            .GetDecimal());
        Assert.Equal(
            835.12m,
            Assert.Single(accounts, account =>
                account.GetProperty("accountId").GetGuid() == monzoId)
            .GetProperty("expensesDue")
            .GetDecimal());
        Assert.Equal(
            2166.75m,
            Assert.Single(accounts, account =>
                account.GetProperty("accountId").GetGuid() == jointId)
            .GetProperty("expensesDue")
            .GetDecimal());
    }

    // Upcoming mode: a fortnightly £1,000 salary that was last paid 11 days ago, so the next payday is
    // today + 3 and the pay period after it runs to today + 17. Rent of £300 is due monthly from today + 10.
    private sealed record UpcomingSeed(Guid IncomeId, Guid BillsId);

    private static async Task<UpcomingSeed> SeedUpcomingAsync(
        FinanceApiFactory factory,
        string userName,
        decimal billsBalance,
        decimal? oneOffExpenseTomorrow = null,
        int salaryStartOffsetDays = -11,
        DateTime? billsCheckedOn = null,
        decimal incomeBalance = 0m,
        DateTime? incomeCheckedOn = null)
    {
        var today = DateTime.UtcNow.Date;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var ownerId = await db.Users.Where(user => user.UserName == userName).Select(user => user.Id).SingleAsync();
        var income = new Account { Name = "Income", OwnerId = ownerId, BalanceCheckedOn = incomeCheckedOn };
        var bills = new Account { Name = "Bills", OwnerId = ownerId, BalanceCheckedOn = billsCheckedOn };
        db.Accounts.AddRange(income, bills);
        if (incomeBalance != 0m)
        {
            db.Balances.Add(new Balance { Account = income, Date = today, Amount = incomeBalance });
        }
        if (billsBalance != 0m)
        {
            db.Balances.Add(new Balance { Account = bills, Date = today, Amount = billsBalance });
        }

        db.ScheduledItems.AddRange(
            new ScheduledItem
            {
                Name = "Salary",
                Account = income,
                Amount = 1000m,
                StartDate = today.AddDays(salaryStartOffsetDays),
                Frequency = 1,
                IsIncome = true
            },
            new ScheduledItem
            {
                Name = "Rent",
                Account = bills,
                Amount = 300m,
                StartDate = today.AddDays(10),
                Frequency = 2,
                IsIncome = false
            });
        if (oneOffExpenseTomorrow is { } expense)
        {
            db.Occurrences.Add(new Occurrence
            {
                Account = bills,
                Date = today.AddDays(1),
                Amount = expense,
                IsIncome = false,
                Description = "One-off"
            });
        }

        await db.SaveChangesAsync();
        return new UpcomingSeed(income.Id, bills.Id);
    }

    private static async Task<JsonElement> GetPlanAsync(HttpClient client, string? period = null)
    {
        var response = await client.GetAsync(period is null ? "/api/payday-plan" : $"/api/payday-plan?period={period}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement AccountIn(JsonElement plan, Guid accountId) =>
        Assert.Single(plan.GetProperty("accounts").EnumerateArray(), account =>
            account.GetProperty("accountId").GetGuid() == accountId);

    [Fact]
    public async Task Upcoming_Mode_Plans_Next_Pay_Period()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "upcoming-mode-user");
        var seed = await SeedUpcomingAsync(factory, "upcoming-mode-user", 0m);
        var today = DateTime.UtcNow.Date;

        var plan = await GetPlanAsync(client);

        Assert.Equal("upcoming", plan.GetProperty("mode").GetString());
        Assert.Equal(today.AddDays(3), plan.GetProperty("periodStartDate").GetDateTime().Date);
        Assert.Equal(today.AddDays(17), plan.GetProperty("periodEndDate").GetDateTime().Date);
        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(300m, bills.GetProperty("expensesDue").GetDecimal());
        Assert.Equal(300m, bills.GetProperty("toSend").GetDecimal());
        var transfer = Assert.Single(plan.GetProperty("transfers").EnumerateArray());
        Assert.Equal(seed.IncomeId, transfer.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(seed.BillsId, transfer.GetProperty("toAccountId").GetGuid());
        Assert.Equal(300m, transfer.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Upcoming_Counts_Money_Already_In_Account()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "upcoming-existing-user");
        var seed = await SeedUpcomingAsync(factory, "upcoming-existing-user", 100m);

        var plan = await GetPlanAsync(client);

        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(100m, bills.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal(100m, bills.GetProperty("fromExisting").GetDecimal());
        Assert.Equal(200m, bills.GetProperty("toSend").GetDecimal());
        Assert.Equal(200m, Assert.Single(plan.GetProperty("transfers").EnumerateArray()).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Upcoming_Projects_Balance_To_Payday()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "upcoming-project-user");
        var seed = await SeedUpcomingAsync(factory, "upcoming-project-user", 150m, oneOffExpenseTomorrow: 50m);

        var plan = await GetPlanAsync(client);

        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(100m, bills.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal(300m, bills.GetProperty("expensesDue").GetDecimal());
        Assert.Equal(200m, bills.GetProperty("toSend").GetDecimal());
    }

    [Fact]
    public async Task Upcoming_Overdrawn_Account_Adds_Overdraft_And_Reports_Shortfall()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "upcoming-overdrawn-user");
        var seed = await SeedUpcomingAsync(factory, "upcoming-overdrawn-user", 20m, oneOffExpenseTomorrow: 70m);
        var today = DateTime.UtcNow.Date;

        var plan = await GetPlanAsync(client);

        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(-50m, bills.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal(0m, bills.GetProperty("fromExisting").GetDecimal());
        Assert.Equal(350m, bills.GetProperty("toSend").GetDecimal());
        var shortfall = Assert.Single(plan.GetProperty("prePaydayShortfalls").EnumerateArray());
        Assert.Equal(seed.BillsId, shortfall.GetProperty("accountId").GetGuid());
        Assert.Equal(-50m, shortfall.GetProperty("lowestBalance").GetDecimal());
        Assert.Equal(today.AddDays(1), shortfall.GetProperty("date").GetDateTime().Date);
    }

    [Fact]
    public async Task Current_Mode_Far_From_Payday_Is_Unchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "current-mode-user");
        var seed = await SeedUpcomingAsync(
            factory, "current-mode-user", 120m, oneOffExpenseTomorrow: 500m, salaryStartOffsetDays: -2);

        var plan = await GetPlanAsync(client);

        Assert.Equal("current", plan.GetProperty("mode").GetString());
        Assert.Equal(120m, AccountIn(plan, seed.BillsId).GetProperty("expectedBalance").GetDecimal());
        Assert.Empty(plan.GetProperty("prePaydayShortfalls").EnumerateArray());
    }

    [Fact]
    public async Task Current_Mode_Negative_Balance_Reports_No_PrePayday_Shortfall()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "current-negative-user");
        await SeedUpcomingAsync(factory, "current-negative-user", -40m, salaryStartOffsetDays: -2);

        var plan = await GetPlanAsync(client);

        Assert.Equal("current", plan.GetProperty("mode").GetString());
        Assert.Empty(plan.GetProperty("prePaydayShortfalls").EnumerateArray());
    }

    [Fact]
    public async Task Upcoming_Steady_Negative_Balance_Is_Not_A_Shortfall()
    {
        // A credit card that is simply below zero, with nothing due before payday, isn't running short.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "upcoming-steady-negative-user");
        await SeedUpcomingAsync(factory, "upcoming-steady-negative-user", -40m);

        var plan = await GetPlanAsync(client);

        Assert.Equal("upcoming", plan.GetProperty("mode").GetString());
        Assert.Empty(plan.GetProperty("prePaydayShortfalls").EnumerateArray());
    }

    [Fact]
    public async Task Upcoming_Bill_Paid_Early_Is_Not_Taken_Off_Again()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "paid-early-user");
        var today = DateTime.UtcNow.Date;
        Guid billsId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ownerId = await db.Users.Where(user => user.UserName == "paid-early-user").Select(user => user.Id).SingleAsync();
            var income = new Account { Name = "Income", OwnerId = ownerId };
            var bills = new Account { Name = "Bills", OwnerId = ownerId };
            db.Accounts.AddRange(income, bills);
            db.ScheduledItems.Add(new ScheduledItem
            {
                Name = "Salary",
                Account = income,
                Amount = 1000m,
                StartDate = today.AddDays(-11),
                Frequency = 1,
                IsIncome = true
            });
            var rent = new ScheduledItem
            {
                Name = "Rent",
                Account = bills,
                Amount = 300m,
                StartDate = today.AddDays(2),
                Frequency = 2,
                IsIncome = false
            };
            db.ScheduledItems.Add(rent);
            // Rent due in two days was paid yesterday; the balance already reflects it.
            db.Occurrences.Add(new Occurrence
            {
                Account = bills,
                ScheduledItem = rent,
                Date = today.AddDays(-1),
                PlanDate = today.AddDays(2),
                Amount = 300m,
                IsIncome = false,
                IsReconciled = true
            });
            db.Balances.Add(new Balance { Account = bills, Date = today, Amount = 50m });
            await db.SaveChangesAsync();
            billsId = bills.Id;
        }

        var plan = await GetPlanAsync(client);

        var bills2 = AccountIn(plan, billsId);
        Assert.Equal(50m, bills2.GetProperty("expectedBalance").GetDecimal());
        Assert.Empty(plan.GetProperty("prePaydayShortfalls").EnumerateArray());
    }

    [Fact]
    public async Task Period_Current_Close_To_Payday_Plans_Until_Payday()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "period-current-user");
        var seed = await SeedUpcomingAsync(factory, "period-current-user", 100m, oneOffExpenseTomorrow: 50m);
        var today = DateTime.UtcNow.Date;

        var plan = await GetPlanAsync(client, "current");

        Assert.Equal("current", plan.GetProperty("mode").GetString());
        Assert.Equal(today.AddDays(3), plan.GetProperty("periodEndDate").GetDateTime().Date);
        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(50m, bills.GetProperty("expensesDue").GetDecimal());
        Assert.Equal(100m, bills.GetProperty("expectedBalance").GetDecimal());
    }

    [Fact]
    public async Task Period_Next_Far_From_Payday_Plans_Next_Period()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "period-next-user");
        // Payday is 12 days away; rent on day 10 falls before it.
        var seed = await SeedUpcomingAsync(factory, "period-next-user", 120m, salaryStartOffsetDays: -2);
        var today = DateTime.UtcNow.Date;

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal("upcoming", plan.GetProperty("mode").GetString());
        Assert.Equal(today.AddDays(12), plan.GetProperty("periodStartDate").GetDateTime().Date);
        Assert.Equal(today.AddDays(26), plan.GetProperty("periodEndDate").GetDateTime().Date);
        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(-180m, bills.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal(0m, bills.GetProperty("expensesDue").GetDecimal());
        Assert.Equal(180m, bills.GetProperty("toSend").GetDecimal());
    }

    [Fact]
    public async Task Unknown_Period_Returns_BadRequest()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "period-bad-user");

        var response = await client.GetAsync("/api/payday-plan?period=someday");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LeftAfterTransfers_Shows_Where_Money_Sits()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "left-after-user");
        var seed = await SeedUpcomingAsync(factory, "left-after-user", 100m);

        var plan = await GetPlanAsync(client, "next");

        Assert.Equal(800m, AccountIn(plan, seed.IncomeId).GetProperty("leftAfterTransfers").GetDecimal());
        Assert.Equal(0m, AccountIn(plan, seed.BillsId).GetProperty("leftAfterTransfers").GetDecimal());
    }

    [Fact]
    public async Task Checked_Today_Treats_Todays_Planned_Income_As_Already_In_Balance()
    {
        // Payday is today; the user typed the bank balance after the salary landed.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "checked-payday-user");
        var today = DateTime.UtcNow.Date;
        var seed = await SeedUpcomingAsync(
            factory, "checked-payday-user", 0m, salaryStartOffsetDays: 0,
            incomeBalance: 1000m, incomeCheckedOn: today);

        var plan = await GetPlanAsync(client, "current");

        var income = AccountIn(plan, seed.IncomeId);
        Assert.Equal(0m, income.GetProperty("incomeIn").GetDecimal());
        Assert.Equal(700m, income.GetProperty("leftAfterTransfers").GetDecimal());
        Assert.Equal(300m, Assert.Single(plan.GetProperty("transfers").EnumerateArray()).GetProperty("amount").GetDecimal());
    }

    // Payday is today + 3 (fortnightly salary into Income, last paid 11 days ago). Each extra account is
    // (name, balance today, a monthly bill amount or 0, the day offset of that bill).
    private static async Task<Dictionary<string, Guid>> SeedAccountsAsync(
        FinanceApiFactory factory,
        string userName,
        params (string Name, decimal Balance, decimal Bill, int BillDay)[] accounts)
    {
        var today = DateTime.UtcNow.Date;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var ownerId = await db.Users.Where(user => user.UserName == userName).Select(user => user.Id).SingleAsync();
        var income = new Account { Name = "Income", OwnerId = ownerId };
        db.Accounts.Add(income);
        db.ScheduledItems.Add(new ScheduledItem
        {
            Name = "Salary",
            Account = income,
            Amount = 1000m,
            StartDate = today.AddDays(-11),
            Frequency = 1,
            IsIncome = true
        });
        var ids = new Dictionary<string, Guid> { ["Income"] = income.Id };
        foreach (var (name, balance, bill, billDay) in accounts)
        {
            var account = new Account { Name = name, OwnerId = ownerId };
            db.Accounts.Add(account);
            if (balance != 0m)
            {
                db.Balances.Add(new Balance { Account = account, Date = today, Amount = balance });
            }

            if (bill > 0m)
            {
                db.ScheduledItems.Add(new ScheduledItem
                {
                    Name = $"{name} bill",
                    Account = account,
                    Amount = bill,
                    StartDate = today.AddDays(billDay),
                    Frequency = 2,
                    IsIncome = false
                });
            }

            ids[name] = account.Id;
        }

        await db.SaveChangesAsync();
        return ids;
    }

    [Fact]
    public async Task Current_Period_Keeps_Money_The_Next_Period_Needs()
    {
        // Joint holds £900 for a mortgage due after payday; Bills runs £20 short before payday.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "reserve-user");
        var ids = await SeedAccountsAsync(factory, "reserve-user", ("Joint", 900m, 900m, 5), ("Bills", 20m, 40m, 1));

        var plan = await GetPlanAsync(client, "current");

        Assert.DoesNotContain(plan.GetProperty("transfers").EnumerateArray(), transfer =>
            transfer.GetProperty("fromAccountId").GetGuid() == ids["Joint"]);
        Assert.Equal(20m, AccountIn(plan, ids["Bills"]).GetProperty("unfundedShortfall").GetDecimal());
    }

    [Fact]
    public async Task Current_Period_Can_Use_Money_Not_Needed_Later()
    {
        // Joint holds £1,000 but only needs £900 after payday, so £20 can cover Bills now.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "reserve-spare-user");
        var ids = await SeedAccountsAsync(factory, "reserve-spare-user", ("Joint", 1000m, 900m, 5), ("Bills", 20m, 40m, 1));

        var plan = await GetPlanAsync(client, "current");

        var transfer = Assert.Single(plan.GetProperty("transfers").EnumerateArray());
        Assert.Equal(ids["Joint"], transfer.GetProperty("fromAccountId").GetGuid());
        Assert.Equal(20m, transfer.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Negative_Balance_With_No_Bills_Gets_No_Transfer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "credit-card-user");
        var ids = await SeedAccountsAsync(factory, "credit-card-user", ("Credit card", -500m, 0m, 0));

        foreach (var period in new[] { "current", "next" })
        {
            var plan = await GetPlanAsync(client, period);

            var card = AccountIn(plan, ids["Credit card"]);
            Assert.Equal(0m, card.GetProperty("toSend").GetDecimal());
            Assert.Equal(0m, card.GetProperty("overdraftToCover").GetDecimal());
            Assert.Empty(plan.GetProperty("transfers").EnumerateArray());
        }
    }

    [Fact]
    public async Task Existing_Overdraft_Is_Not_Topped_Up_But_Bills_Are()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "existing-overdraft-user");
        var seed = await SeedUpcomingAsync(factory, "existing-overdraft-user", -50m);

        var plan = await GetPlanAsync(client, "next");

        var bills = AccountIn(plan, seed.BillsId);
        Assert.Equal(-50m, bills.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal(300m, bills.GetProperty("toSend").GetDecimal());
        Assert.Equal(0m, bills.GetProperty("overdraftToCover").GetDecimal());
    }

    [Fact]
    public async Task Breakdown_Includes_BalanceCheckedOn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new FinanceApiFactory(connection);
        using var client = await CreateAuthenticatedClient(factory, "checked-on-user");
        var today = DateTime.UtcNow.Date;
        var seed = await SeedUpcomingAsync(factory, "checked-on-user", 0m, billsCheckedOn: today);

        var plan = await GetPlanAsync(client);

        Assert.Equal(today, AccountIn(plan, seed.BillsId).GetProperty("balanceCheckedOn").GetDateTime().Date);
        Assert.Equal(JsonValueKind.Null, AccountIn(plan, seed.IncomeId).GetProperty("balanceCheckedOn").ValueKind);
    }

    private static async Task<HttpClient> CreateAuthenticatedClient(FinanceApiFactory factory, string userName)
    {
        var client = factory.CreateClient();
        var register = new
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            Password = "P@ssword1"
        };
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = userName,
            Password = "P@ssword1"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var auth = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.RootElement.GetProperty("token").GetString());
        return client;
    }
}
