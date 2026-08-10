using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

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
