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

    // ── Evidencia de la actividad ────────────────────────────────────────
    /// <summary>
    /// Tiempo dedicado que DECLARA el desarrollador, en minutos. Es distinto del cronómetro
    /// (<see cref="WorkSession"/>): aquí se registra trabajo que puede haber ocurrido sin la
    /// aplicación abierta (una junta, un apoyo puntual). Null = no lo capturó.
    /// </summary>
    public int? MinutesSpent { get; set; }

    /// <summary>
    /// Enlace al item que respalda la actividad: un pull request, un work item o un ticket de
    /// Azure DevOps. Se guarda la URL completa para que el administrador pueda abrirla al revisar.
    /// </summary>
    public string? EvidenceUrl { get; set; }

    // ── Flujo de aprobación (autocalificación del desarrollador) ─────────
    /// <summary>Por defecto Aprobado (lo que asigna el jefe). El autoregistro del dev lo pone en Pendiente.</summary>
    public PointApprovalStatus ApprovalStatus { get; set; } = PointApprovalStatus.Aprobado;
    /// <summary>Si el propio desarrollador la registró (autocalificación), su Id. Null si la asignó el jefe.</summary>
    public int? SubmittedByDeveloperId { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }

    // ── Réplica del desarrollador ────────────────────────────────────────
    /// <summary>
    /// Cuántas veces ha vuelto a revisión después de un rechazo. 0 = nunca se replicó. Le dice al
    /// administrador de un vistazo si está ante una propuesta nueva o ante la tercera insistencia
    /// sobre lo mismo.
    /// </summary>
    public int ReviewRound { get; set; }

    /// <summary>
    /// Bitácora del ida y vuelta: cada rechazo con su motivo y cada réplica con su argumento, en
    /// orden y con fecha. Es un solo campo que solo crece porque <see cref="ReviewComment"/>
    /// guarda únicamente la ÚLTIMA decisión: sin esto, replicar borraría el motivo del rechazo que
    /// se está discutiendo y la conversación quedaría sin la mitad que la explica.
    /// </summary>
    public string? ReviewHistory { get; set; }

    /// <summary>Está rechazada y el desarrollador todavía puede argumentar.</summary>
    public bool AdmiteReplica => ApprovalStatus == PointApprovalStatus.Rechazado && SubmittedByDeveloperId != null;

    public Developer Developer { get; set; } = null!;
    public ScoringCriterion Criterion { get; set; } = null!;
    public Requirement? Requirement { get; set; }
}
