using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un permiso: lo solicita el desarrollador y lo resuelve el administrador.
///
/// Nació como una libreta del administrador —él capturaba el permiso ya concedido— y por eso
/// <see cref="ApprovedBy"/> es texto libre. Se conserva porque el histórico está escrito ahí; lo
/// que decide hoy es <see cref="Status"/>, y quién resolvió queda en <see cref="ReviewedById"/>.
/// </summary>
public class LeaveRequest
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;
    public LeaveType Type { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int DaysCount { get; set; } = 1;
    public string? Reason { get; set; }

    /// <summary>
    /// Texto libre heredado de cuando esta pantalla era el registro manual del administrador.
    /// Se sigue mostrando para no perder el histórico, pero ya no es lo que decide: eso es
    /// <see cref="Status"/>.
    /// </summary>
    public string? ApprovedBy { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Flujo de solicitud y resolución ──────────────────────────────────────
    /// <summary>
    /// Las que registra el administrador nacen <see cref="LeaveStatus.Aprobada"/> (está
    /// concediéndolas en ese acto); las que pide el desarrollador nacen Pendiente.
    /// </summary>
    public LeaveStatus Status { get; set; } = LeaveStatus.Pendiente;

    /// <summary>Si la pidió el propio desarrollador, su Id. Null si la capturó el administrador.</summary>
    public int? RequestedByDeveloperId { get; set; }

    public int? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Motivo del rechazo o comentario de la aprobación. Es lo que el solicitante lee.</summary>
    public string? ReviewComment { get; set; }

    // ── Respaldo ─────────────────────────────────────────────────────────────
    /// <summary>Justificante opcional: receta, constancia, captura del correo que lo autoriza…</summary>
    public byte[]? AttachmentBytes { get; set; }
    public string? AttachmentFileName { get; set; }

    /// <summary>
    /// Sello de concurrencia optimista. Es nuevo de la web: aquí el solicitante y el administrador
    /// pueden tener el mismo permiso abierto en dos navegadores a la vez —uno editando el motivo,
    /// el otro resolviéndolo—, algo que en el escritorio no pasaba. Con el sello, el segundo en
    /// guardar recibe un error de concurrencia en vez de pisar en silencio lo del primero.
    /// Solo se mapea contra SQL Server; en SQLite se ignora.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>Última fecha cubierta por el permiso, contando el día de inicio.</summary>
    public DateTime EndDate => Date.Date.AddDays(Math.Max(1, DaysCount) - 1);

    public bool EsSolicitudDelDesarrollador => RequestedByDeveloperId != null;
}
