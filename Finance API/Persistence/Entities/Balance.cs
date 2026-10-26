using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// A dated balance for an account. Amount uses decimal to represent money.
/// </summary>
public sealed class Balance
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The date the balance applies to (date component typically used).
    /// </summary>
    [Required]
    public DateTime Date { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public Guid AccountId { get; set; }

    /// <summary>
    /// Optional free-text note describing what the amount was for.
    /// </summary>
    [MaxLength(200)]
    public string? Note { get; set; }

    public Account? Account { get; set; }
}
