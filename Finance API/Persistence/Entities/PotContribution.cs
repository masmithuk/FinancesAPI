using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// An amount added to a pot from one of the user's accounts.
/// </summary>
public sealed class PotContribution
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PotId { get; set; }

    public Pot? Pot { get; set; }

    public Guid? AccountId { get; set; }

    public Account? Account { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public DateTime Date { get; set; }

    public Guid? BalanceId { get; set; }

    public Balance? Balance { get; set; }
}
