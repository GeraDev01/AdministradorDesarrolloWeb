namespace Administrador_Desarrollo_Web.Models;

public enum SlaStatus
{
    /// <summary>Vigente: aún no vence, o venció pero sigue exigiéndose.</summary>
    Activo = 0,
    /// <summary>El administrador lo dio por atendido.</summary>
    Cumplido = 1,
    /// <summary>Pasó la fecha límite sin cerrarse.</summary>
    Vencido = 2,
    /// <summary>Se dejó sin efecto.</summary>
    Cancelado = 3
}

/// <summary>
/// Compromiso de atención con fecha límite sobre un requerimiento o una actividad libre, con
/// recordatorios periódicos para que el desarrollador deje constancia comentando el ticket de
/// Azure DevOps correspondiente.
///
/// Igual que <see cref="WorkSession"/>, el objetivo es un requerimiento O una actividad, nunca
/// ambos ni ninguno: así el SLA cubre todo lo que un desarrollador puede tener en curso sin
/// duplicar los mismos campos en dos entidades.
/// </summary>
public class SlaCommitment
{
    public int Id { get; set; }

    public int? RequirementId { get; set; }
    public int? ActivityId { get; set; }

    /// <summary>A quién se le exige el compromiso.</summary>
    public int DeveloperId { get; set; }

    /// <summary>Número de work item en Azure DevOps que debe comentarse. Null = sin ticket ligado.</summary>
    public int? DevOpsTicketExternalId { get; set; }
    public string? DevOpsTicketUrl { get; set; }

    /// <summary>Fecha y hora límite (UTC).</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>Cada cuántas horas recordar que hay que comentar. 0 = solo avisar al vencer.</summary>
    public int ReminderEveryHours { get; set; } = 24;

    /// <summary>Próximo recordatorio (UTC). Null cuando ya no hay que recordar.</summary>
    public DateTime? NextReminderAtUtc { get; set; }

    /// <summary>Último comentario publicado en el ticket desde la aplicación.</summary>
    public DateTime? LastCommentAtUtc { get; set; }
    public int CommentCount { get; set; }

    public SlaStatus Status { get; set; } = SlaStatus.Activo;

    /// <summary>Cuándo se avisó al administrador del incumplimiento; evita repetir el correo.</summary>
    public DateTime? BreachNotifiedAtUtc { get; set; }

    public string? Notes { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Requirement? Requirement { get; set; }
    public DevActivity? Activity { get; set; }
    public Developer Developer { get; set; } = null!;

    /// <summary>Exactamente uno de los dos objetivos debe estar puesto.</summary>
    public bool EsValido => RequirementId.HasValue ^ ActivityId.HasValue;

    public bool EstaVencido(DateTime nowUtc) => Status == SlaStatus.Activo && nowUtc > DueAtUtc;

    /// <summary>Toca recordar: hay recordatorio programado y ya pasó su hora.</summary>
    public bool TocaRecordar(DateTime nowUtc) =>
        Status == SlaStatus.Activo && NextReminderAtUtc is DateTime n && nowUtc >= n;
}
