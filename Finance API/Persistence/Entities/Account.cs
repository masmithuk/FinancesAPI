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

    public ICollection<Balance> Balances { get; set; } = new List<Balance>();
    public ICollection<Pot> Pots { get; set; } = new List<Pot>();
    public ICollection<ScheduledItem> ScheduledItems { get; set; } = new List<ScheduledItem>();
}
