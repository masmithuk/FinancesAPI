using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance_API.Persistence;

// Design-time factory used by EF Core tools (migrations, scaffolding) to
// instantiate a FinanceDbContext outside of the running application. This
// provides a simple SQLite configuration for design-time commands.
public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlite("Data Source=finance-api-design-time.db")
            .Options;

        return new FinanceDbContext(options);
    }
}
