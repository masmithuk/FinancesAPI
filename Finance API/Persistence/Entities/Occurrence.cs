using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// Represents an actual income or expense occurrence recorded against an Account.
///
/// Occurrences may be generated from ScheduledItem instances or recorded manually.
/// To support idempotent recording and avoid duplicate reconciliation, clients may
/// supply an ExternalReference which is indexed uniquely. If an ExternalReference
/// is not provided the server will still create the occurrence but idempotency
/// across client retries is not guaranteed.
/// </summary>
public sealed class Occurrence
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AccountId { get; set; }

    /// <summary>
    /// Optional link to the originating scheduled item.
    /// </summary>
    public Guid? ScheduledItemId { get; set; }

    /// <summary>
    /// The date of the plan occurrence this activity pays. It can differ from <see cref="Date"/>
    /// when a bank transaction lands a few days away from the planned date.
    /// </summary>
    public DateTime? PlanDate { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public bool IsIncome { get; set; }

    /// <summary>
    /// Optional external id used by clients to ensure idempotent creation.
    /// Recommended for import flows where the same external event might be
    /// submitted more than once.
    /// </summary>
    public string? ExternalReference { get; set; }

    /// <summary>
    /// Stable key of a row imported from a bank statement, unique per account,
    /// so re-importing an overlapping statement does not create duplicates.
    /// </summary>
    [MaxLength(100)]
    public string? ImportId { get; set; }

    /// <summary>
    /// Display label for the activity, e.g. the merchant name from an imported statement.
    /// </summary>
    [MaxLength(250)]
    public string? Description { get; set; }
    /// <summary>
    /// When true the occurrence has been reconciled against account balances
    /// and should not be applied again.
    /// </summary>
    public bool IsReconciled { get; set; }

    public Account? Account { get; set; }
    public ScheduledItem? ScheduledItem { get; set; }
}
