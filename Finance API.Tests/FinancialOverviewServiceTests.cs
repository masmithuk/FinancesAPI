using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finance_API.Tests;

public sealed class FinancialOverviewServiceTests
{
    [Fact]
    public async Task GetSetupStatus_And_HomeSummary_Work()
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

        var summary = await overview.GetHomeSummaryAsync(ownerId);
        Assert.NotNull(summary);
        Assert.Contains(summary.Accounts, a => a.AccountId == account.Id);
    }
}
