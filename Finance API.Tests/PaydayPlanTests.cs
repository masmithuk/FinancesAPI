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
