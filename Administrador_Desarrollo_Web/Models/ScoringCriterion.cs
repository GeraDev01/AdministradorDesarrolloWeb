namespace Administrador_Desarrollo_Web.Models;

/// <summary>Ámbito de un criterio: aplica a personas, a equipos, o a ambos.</summary>
public enum CriterionScope { Individual = 0, Equipo = 1, Ambos = 2 }

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
