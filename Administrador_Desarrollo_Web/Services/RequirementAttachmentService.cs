using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Metadatos de un adjunto (sin el contenido binario).</summary>
public record AttachmentMeta(int Id, RequirementAttachmentKind Kind, string FileName, long SizeBytes, DateTime UploadedAtUtc, int? UploadedByUserId);

/// <summary>
/// Gestiona los documentos adjuntos a un requerimiento (documento de requerimiento,
/// de estimación, u otros). Contenido inline en BLOB.
/// </summary>
public class RequirementAttachmentService
{
    public const long MaxSizeBytes = 50L * 1024 * 1024; // 50 MB

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;

    public RequirementAttachmentService(AppDbContext db, AuditService audit, CurrentUserContext currentUser)
    {
        _db = db; _audit = audit; _currentUser = currentUser;
    }

    /// <summary>Lista los adjuntos de un requerimiento sin traer los bytes.</summary>
    public List<AttachmentMeta> GetMetaForRequirement(int requirementId) =>
        _db.RequirementAttachments.AsNoTracking()
            .Where(a => a.RequirementId == requirementId)
            .OrderBy(a => a.Kind).ThenBy(a => a.FileName)
            .Select(a => new AttachmentMeta(a.Id, a.Kind, a.FileName, a.SizeBytes, a.UploadedAtUtc, a.UploadedByUserId))
            .ToList();

    /// <summary>Conteo de adjuntos por requerimiento (para el indicador del grid).</summary>
    public Dictionary<int, int> CountsByRequirement() =>
        _db.RequirementAttachments.AsNoTracking()
            .GroupBy(a => a.RequirementId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionary(x => x.Key, x => x.Count);

    public byte[] GetBytes(int attachmentId) =>
        _db.RequirementAttachments.AsNoTracking()
            .Where(a => a.Id == attachmentId)
            .Select(a => a.FileBytes)
            .FirstOrDefault() ?? [];

    /// <summary>Adjunta un archivo del disco. Devuelve (ok, mensaje de error).</summary>
    public (bool ok, string? error) Add(int requirementId, RequirementAttachmentKind kind, string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return (false, "El archivo no existe.");
        if (info.Length == 0) return (false, "El archivo está vacío.");
        if (info.Length > MaxSizeBytes) return (false, $"El archivo supera el límite de {MaxSizeBytes / (1024 * 1024)} MB.");

        var att = new RequirementAttachment
        {
            RequirementId = requirementId,
            Kind = kind,
            FileName = info.Name,
            FileBytes = File.ReadAllBytes(filePath),
            SizeBytes = info.Length,
            UploadedByUserId = _currentUser.User?.Id,
            UploadedAtUtc = DateTime.UtcNow
        };
        _db.RequirementAttachments.Add(att);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "RequirementAttachment", att.Id.ToString(), $"{KindLabel(kind)}: {att.FileName} (req {requirementId})");
        return (true, null);
    }

    public void Delete(int attachmentId)
    {
        var att = _db.RequirementAttachments.FirstOrDefault(a => a.Id == attachmentId);
        if (att == null) return;
        _db.RequirementAttachments.Remove(att);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "RequirementAttachment", attachmentId.ToString(), att.FileName);
    }

    public static string KindLabel(RequirementAttachmentKind k) => k switch
    {
        RequirementAttachmentKind.Requerimiento => "Requerimiento",
        RequirementAttachmentKind.Estimacion    => "Estimación",
        _                                        => "Otro"
    };
}
