using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Evaluación de un desarrollador hecha por su líder: fortalezas, debilidades, calificación y
/// comentarios de un periodo. Alimenta el reporte en PDF.
/// </summary>
public class DeveloperEvaluation
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public Developer? Developer { get; set; }

    /// <summary>Usuario (líder) que evaluó, y su nombre como quedó al momento de evaluar.</summary>
    public int? EvaluatorUserId { get; set; }
    public string? EvaluatorName { get; set; }

    public DateTime EvaluationDate { get; set; } = DateTime.Today;
    /// <summary>Etiqueta del periodo, libre: «2026 Q3», «Semestre 1», etc.</summary>
    public string? PeriodLabel { get; set; }
    /// <summary>Calificación global 1..5 (opcional).</summary>
    public int? OverallRating { get; set; }

    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Hito o logro de un desarrollador (una entrega importante, una certificación, un reconocimiento…).</summary>
public class DeveloperMilestone
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public Developer? Developer { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public MilestoneKind Kind { get; set; } = MilestoneKind.Logro;

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
