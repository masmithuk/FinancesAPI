using System.ComponentModel.DataAnnotations;

namespace Finance_API.Persistence.Entities;

/// <summary>
/// A virtual pot (sub-account) linked to a parent Account. Pots can have
/// goals attached and are lightweight groupings for budgeting.
/// </summary>
public sealed class Pot
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = null!;

    [Required]
    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    public ICollection<Goal> Goals { get; set; } = new List<Goal>();

    public ICollection<PotContribution> Contributions { get; set; } = new List<PotContribution>();
}
