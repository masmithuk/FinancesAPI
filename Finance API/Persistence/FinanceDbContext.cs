using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

// ApplicationUser is declared in the same namespace and used as the user type
// for Identity's tables.

namespace Finance_API.Persistence;

// FinanceDbContext is the EF Core DbContext for the application. Add DbSet<T>
// properties here to represent tables (entities) in the database. The class is
// registered with DI in Program.cs via AddDbContext<T>.
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
}
