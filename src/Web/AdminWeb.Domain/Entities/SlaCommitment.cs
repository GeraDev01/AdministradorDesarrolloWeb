using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

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

    /// <summary>
    /// Cuándo se le avisó por última vez al responsable de que tocaba comentar el ticket.
    ///
    /// <para><b>Es nuevo de la web y sustituye a un HashSet en memoria.</b> En el escritorio, quién
    /// ya había sido avisado vivía en la memoria del proceso (<c>SlaAlertTracker</c>): bastaba con
    /// que la ventana siguiera abierta. En el servidor no vale: cada despliegue reinicia el proceso
    /// y, con el rastro perdido, la siguiente vuelta del trabajo de fondo volvería a avisar de todo
    /// lo que ya se avisó — a todo el equipo, cada vez que se publica una versión. Por eso el rastro
    /// vive en la base, junto al compromiso del que habla.</para>
    ///
    /// <para>Se guarda el MOMENTO y no un booleano porque lo que decide si toca volver a avisar es
    /// compararlo con <see cref="NextReminderAtUtc"/>: un aviso anterior al recordatorio vigente
    /// pertenece a un ciclo ya superado. Así se reproduce, sin estado en memoria, la regla del
    /// escritorio de «se olvida en cuanto sale de pendientes, para que su siguiente recordatorio
    /// vuelva a avisar».</para>
    /// </summary>
    public DateTime? ReminderNotifiedAtUtc { get; set; }

    /// <summary>
    /// Cuándo se le avisó al responsable de que su compromiso quedó FUERA DE PLAZO. Va aparte del
    /// recordatorio porque es información nueva y más grave: en el escritorio también se avisaba una
    /// segunda vez al cruzar la fecha, aunque ya se hubiera avisado del recordatorio.
    /// </summary>
    public DateTime? OverdueNotifiedAtUtc { get; set; }

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

    /// <summary>
    /// Toca avisar del recordatorio: venció el recordatorio programado y el último aviso que se dio
    /// pertenece a un ciclo anterior (o no se ha dado ninguno).
    ///
    /// La comparación contra <see cref="NextReminderAtUtc"/> es la que hace que un aviso no se
    /// repita cada vuelta del trabajo de fondo pero SÍ vuelva a salir cuando el recordatorio se
    /// reprograma —al posponerlo o al comentar el ticket—, que es exactamente lo que el escritorio
    /// conseguía olvidando el compromiso en cuanto salía de la lista de pendientes.
    /// </summary>
    public bool TocaAvisarDelRecordatorio(DateTime nowUtc) =>
        TocaRecordar(nowUtc) &&
        (ReminderNotifiedAtUtc is not DateTime avisado || avisado < NextReminderAtUtc);

    /// <summary>
    /// Toca avisar de que se pasó de la fecha. Una sola vez: mientras siga activo seguirá vencido, y
    /// repetirlo cada cuarto de hora convertiría el aviso en ruido que se cierra por reflejo.
    /// </summary>
    public bool TocaAvisarDeVencimiento(DateTime nowUtc) =>
        EstaVencido(nowUtc) && OverdueNotifiedAtUtc == null;
}
