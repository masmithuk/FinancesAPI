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
    public DbSet<Pot> Pots { get; set; } = null!;
    public DbSet<ScheduledItem> ScheduledItems { get; set; } = null!;
    public DbSet<Goal> Goals { get; set; } = null!;
    public DbSet<PotContribution> PotContributions { get; set; } = null!;
    public DbSet<Occurrence> Occurrences { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Configure one-to-many relationships with explicit navigation properties
        builder.Entity<Account>()
            .HasMany(a => a.Balances)
            .WithOne(b => b.Account)
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Account>()
            .HasMany(a => a.Pots)
            .WithOne(p => p.Account)
            .HasForeignKey(p => p.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Account>()
            .HasMany(a => a.ScheduledItems)
            .WithOne(s => s.Account)
            .HasForeignKey(s => s.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // Occurrences (actual recorded income/expense events)
        builder.Entity<Occurrence>()
            .HasIndex(o => new { o.AccountId, o.ExternalReference })
            .IsUnique();

        builder.Entity<Occurrence>()
            .HasIndex(o => new { o.AccountId, o.Date, o.Amount, o.IsIncome });

        builder.Entity<Occurrence>()
            .HasIndex(o => new { o.AccountId, o.ImportId });

        builder.Entity<Account>()
            .HasMany(a => a.Occurrences)
            .WithOne(o => o.Account)
            .HasForeignKey(o => o.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // Pot has many Goals
        builder.Entity<Pot>()
            .HasMany(p => p.Goals)
            .WithOne(g => g.Pot)
            .HasForeignKey(g => g.PotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Pot>()
            .HasMany(p => p.Contributions)
            .WithOne(c => c.Pot)
            .HasForeignKey(c => c.PotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Account>()
            .HasMany(a => a.PotContributions)
            .WithOne(c => c.Account)
            .HasForeignKey(c => c.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<PotContribution>()
            .HasOne(c => c.Balance)
            .WithOne()
            .HasForeignKey<PotContribution>(c => c.BalanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
