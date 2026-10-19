using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Finance_API.Persistence.Entities;

// ApplicationUser is declared in the same namespace and used as the user type
// for Identity's tables.

namespace Finance_API.Persistence;

// FinanceDbContext is the EF Core DbContext for the application. Add DbSet<T>
// properties here to represent tables (entities) in the database. The class is
// registered with DI in Program.cs via AddDbContext<T>.
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Accounts and balances for the finance domain
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<Balance> Balances { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure one-to-many relationship: Account has many Balances
        builder.Entity<Account>()
            .HasMany(a => a.Balances)
            .WithOne(b => b.Account)
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
