using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// Represents a recurring scheduled income or expense linked to an Account.
///
/// The model is intentionally minimal for V1: a schedule has a start date,
/// a recurrence frequency, a nominal amount, and a flag indicating whether it
/// is an income (true) or expense (false). Actual occurrences and reconciliation
/// are implemented in later issues.
/// </summary>
public sealed class ScheduledItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = null!;

    [Required]
    public Guid AccountId { get; set; }

    /// <summary>
    /// The nominal amount for the scheduled item. Positive for incomes,
    /// negative for expenses is not required; use IsIncome to distinguish.
    /// </summary>
    [Required]
    public decimal Amount { get; set; }

    /// <summary>
    /// The date the schedule starts from. Occurrences are generated from this date
    /// according to the Frequency.
    /// </summary>
    [Required]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Recurrence frequency: 0 = Weekly, 1 = Fortnightly, 2 = Monthly, 3 = Annual
    /// Stored as an integer by EF Core to keep migrations simple.
    /// </summary>
    [Required]
    public int Frequency { get; set; }

    [Required]
    public bool IsIncome { get; set; }

    public Account? Account { get; set; }
}
