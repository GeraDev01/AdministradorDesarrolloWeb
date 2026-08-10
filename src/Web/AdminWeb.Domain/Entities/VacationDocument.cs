using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Documento (DOCX/PDF) de una solicitud de vacaciones: generado desde la
/// plantilla y, una vez firmado por el gerente, exportado a PDF.
/// El DOCX (pocos KB) se guarda inline; el PDF firmado se guarda inline por
/// defecto (SQLite mono-usuario) o se descarga a Azure Blob cuando está
/// configurado, siguiendo la convención de AppRelease (Blob + checksum).
/// </summary>
public class VacationDocument
{
    public int Id { get; set; }

    public int VacationRequestId { get; set; }
    public VacationRequest VacationRequest { get; set; } = null!;

    public VacationDocSource Source { get; set; } = VacationDocSource.Generado;
    public VacationDocStatus Status { get; set; } = VacationDocStatus.Borrador;

    public string FileName { get; set; } = "";

    /// <summary>DOCX ya con los datos rellenados (y la firma insertada si aplica).</summary>
    public byte[]? DocxBytes { get; set; }

    /// <summary>PDF final firmado (inline cuando no hay Blob configurado).</summary>
    public byte[]? SignedPdfBytes { get; set; }
    public string? PdfBlobUrl { get; set; }
    public string? PdfChecksum { get; set; }

    public int? SignatureProfileId { get; set; }
    public SignatureProfile? SignatureProfile { get; set; }

    public int? SignedByUserId { get; set; }
    public DateTime? SignedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public byte[]? RowVersion { get; set; }
}
