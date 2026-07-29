namespace Administrador_Desarrollo_Web.Models;

public enum RequirementAttachmentKind { Requerimiento = 0, Estimacion = 1, Otro = 2 }

/// <summary>
/// Documento adjunto a un requerimiento (p. ej. el documento de requerimiento o
/// el de estimación). El contenido se guarda inline como BLOB, igual que los
/// demás documentos de la app.
/// </summary>
public class RequirementAttachment
{
    public int Id { get; set; }
    public int RequirementId { get; set; }
    public Requirement Requirement { get; set; } = null!;

    public RequirementAttachmentKind Kind { get; set; } = RequirementAttachmentKind.Requerimiento;
    public string FileName { get; set; } = "";
    public byte[] FileBytes { get; set; } = [];
    public long SizeBytes { get; set; }
    public int? UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
