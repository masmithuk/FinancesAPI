using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// Represents a user-owned financial account (e.g., bank account, credit card).
/// The OwnerId ties the account to an ApplicationUser.Id for per-user isolation.
/// </summary>
public sealed class Account
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = null!;

    /// <summary>
    /// The user id (ApplicationUser.Id) that owns this account.
    /// </summary>
    [Required]
    public string OwnerId { get; set; } = null!;

    /// <summary>
    /// The date the user last confirmed this account's balance against their bank, if ever.
    /// </summary>
    public DateTime? BalanceCheckedOn { get; set; }

    /// <summary>
    /// How the payday plan treats this account: "everyday" (default), "spending" (where spare money is
    /// sent once bills and savings are covered; at most one per user) or "savings" (never drained for
    /// spending, and only used for bills as a last resort).
    /// </summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = AccountRoles.Everyday;

    /// <summary>For a savings account, how much to pay in each payday, if anything.</summary>
    public decimal? SavePerPayday { get; set; }

    public ICollection<Balance> Balances { get; set; } = new List<Balance>();
    public ICollection<Pot> Pots { get; set; } = new List<Pot>();
    public ICollection<ScheduledItem> ScheduledItems { get; set; } = new List<ScheduledItem>();
    public ICollection<Occurrence> Occurrences { get; set; } = new List<Occurrence>();
    public ICollection<PotContribution> PotContributions { get; set; } = new List<PotContribution>();
}

/// <summary>The roles an account can play in the payday plan.</summary>
public static class AccountRoles
{
    public const string Everyday = "everyday";
    public const string Spending = "spending";
    public const string Savings = "savings";

    public static bool IsValid(string? role) => role is Everyday or Spending or Savings;
}
