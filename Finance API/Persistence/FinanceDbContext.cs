using Microsoft.EntityFrameworkCore;

namespace Finance_API.Persistence;

// FinanceDbContext is the EF Core DbContext for the application. Add DbSet<T>
// properties here to represent tables (entities) in the database. The class is
// registered with DI in Program.cs via AddDbContext<T>.
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : DbContext(options)
{
}
