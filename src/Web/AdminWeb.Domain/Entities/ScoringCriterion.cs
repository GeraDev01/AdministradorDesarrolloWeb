using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

public class ScoringCriterion
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int DefaultPoints { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public CriterionScope Scope { get; set; } = CriterionScope.Individual;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
