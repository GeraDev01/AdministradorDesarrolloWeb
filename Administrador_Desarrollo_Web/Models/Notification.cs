namespace Administrador_Desarrollo_Web.Models;

/// <summary>Tipo de aviso, para el ícono y el enrutamiento al tocar la notificación.</summary>
public enum NotificationKind
{
    General = 0, DevOpsAssigned = 1, RequirementAssigned = 2, FreshDeskAssigned = 3, Comunicado = 4,
    /// <summary>La fecha comprometida de un requerimiento se acerca o ya pasó.</summary>
    CompromisoPorVencer = 5
}

/// <summary>
/// Aviso in-app dirigido a un usuario. Sirve tanto para «te asignaron un ticket en DevOps» como
/// para «el administrador te asignó un requerimiento». Es persistente (sobrevive a reinicios) y se
/// muestra en la pantalla de Avisos, en el contador del menú y como globo en la bandeja.
/// </summary>
public class Notification
{
    public int Id { get; set; }
    /// <summary>Usuario destinatario (no el desarrollador: el aviso se lee al iniciar sesión).</summary>
    public int ForUserId { get; set; }
    public NotificationKind Kind { get; set; } = NotificationKind.General;
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>URL opcional (p. ej. el work item en DevOps) para abrir al tocar el aviso.</summary>
    public string? Url { get; set; }
    /// <summary>Clave para NO repetir el mismo aviso. Nula = sin deduplicación.</summary>
    public string? DedupeKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

/// <summary>
/// Registro de qué work items YA se conocían como asignados a un usuario, para no volver a avisar
/// del mismo y, sobre todo, para no soltar un aluvión de avisos la primera vez (línea base).
/// </summary>
public class DevOpsAssignmentSeen
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ExternalId { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Igual que <see cref="DevOpsAssignmentSeen"/> pero para los tickets de Freshdesk asignados a un
/// usuario. El id de ticket de Freshdesk es <c>long</c>, por eso es una tabla aparte.
///
/// Se guarda también el <see cref="AgentId"/> (el agente de Freshdesk resuelto por /agents/me): la
/// línea base es POR agente, así que rotar la API key a otro agente no dispara un aluvión con todo
/// el backlog del nuevo agente. Las filas de un ticket que deja de estar asignado se BORRAN, para que
/// una reasignación posterior vuelva a avisar.
/// </summary>
public class FreshDeskAssignmentSeen
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public long AgentId { get; set; }
    public long ExternalId { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}
