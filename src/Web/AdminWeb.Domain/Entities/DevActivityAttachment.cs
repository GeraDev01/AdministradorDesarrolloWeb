namespace AdminWeb.Domain.Entities;

/// <summary>
/// Evidencia adjunta a una actividad libre: una captura, un PDF, el correo que la pidió.
///
/// Es una tabla propia y no un par de columnas en <see cref="DevActivity"/> —como sí lo son en
/// <c>LeaveRequest</c>— porque una actividad libre se trabaja durante días y acumula varias
/// pruebas: la captura de hoy no debería sustituir a la de ayer. Mismo motivo por el que las
/// imágenes del foro viven en <c>ForumAttachment</c>.
///
/// El contenido va inline como BLOB, igual que el resto de documentos de la aplicación: la
/// evidencia tiene que seguir ahí aunque no haya red hacia ningún almacén externo.
/// </summary>
public class DevActivityAttachment
{
    public int Id { get; set; }

    public int ActivityId { get; set; }

    /// <summary>Nombre con el que se adjuntó, para abrirlo o guardarlo después.</summary>
    public string FileName { get; set; } = "";

    /// <summary>image/png, application/pdf… Sirve para decidir el icono y cómo abrirlo.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    public byte[] Bytes { get; set; } = [];

    public long SizeBytes { get; set; }

    /// <summary>Qué aporta esta evidencia. Un archivo suelto llamado «captura3.png» no dice nada.</summary>
    public string? Description { get; set; }

    public int UploadedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DevActivity? Activity { get; set; }

    /// <summary>¿Se puede previsualizar como imagen? Lo demás se abre con el programa asociado.</summary>
    public bool EsImagen => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
