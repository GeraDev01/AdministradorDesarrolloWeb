namespace AdminWeb.Domain.Entities;

/// <summary>
/// Puntos asignados directamente a un EQUIPO (criterios/calificaciones propias del
/// equipo). Son independientes de los puntos individuales: cuentan en el ranking de
/// equipos pero NUNCA influyen en el ranking individual de sus integrantes.
/// </summary>
public class TeamPointEntry
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int CriterionId { get; set; }
    public int Points { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string? Comment { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;

    public byte[]? Screenshot { get; set; }
    public string? ScreenshotFileName { get; set; }

    public Team Team { get; set; } = null!;
    public ScoringCriterion Criterion { get; set; } = null!;
}
