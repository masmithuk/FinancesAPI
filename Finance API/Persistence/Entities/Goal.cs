using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// A financial goal attached to a Pot. Contains a target amount and an optional due date.
/// </summary>
public sealed class Goal
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PotId { get; set; }

    public Pot? Pot { get; set; }

    [Required]
    public decimal TargetAmount { get; set; }

    public DateTime? DueDate { get; set; }
}
