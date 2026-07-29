namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Estado de aprobación de una entrada de puntos. Las que asigna el jefe (Admin)
/// nacen Aprobado; las que el desarrollador registra por autocalificación nacen
/// Pendiente y solo cuentan en el ranking cuando el jefe las aprueba.
/// </summary>
public enum PointApprovalStatus { Pendiente = 0, Aprobado = 1, Rechazado = 2 }

public class PointEntry
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public int CriterionId { get; set; }
    public int Points { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string? Comment { get; set; }
    public int? AssignedByUserId { get; set; }
    public int? RequirementId { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>Captura de pantalla opcional (PNG/JPG) que respalda la asignación.</summary>
    public byte[]? Screenshot { get; set; }
    public string? ScreenshotFileName { get; set; }

    // ── Flujo de aprobación (autocalificación del desarrollador) ─────────
    /// <summary>Por defecto Aprobado (lo que asigna el jefe). El autoregistro del dev lo pone en Pendiente.</summary>
    public PointApprovalStatus ApprovalStatus { get; set; } = PointApprovalStatus.Aprobado;
    /// <summary>Si el propio desarrollador la registró (autocalificación), su Id. Null si la asignó el jefe.</summary>
    public int? SubmittedByDeveloperId { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }

    public Developer Developer { get; set; } = null!;
    public ScoringCriterion Criterion { get; set; } = null!;
    public Requirement? Requirement { get; set; }
}
