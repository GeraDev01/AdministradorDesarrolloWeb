using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

public class VacationRequest
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public VacationStatus Status { get; set; } = VacationStatus.Pendiente;
    public string? Comment { get; set; }
    public string? ReviewComment { get; set; }
    public int? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Documento de respaldo opcional (justificante, constancia, etc.).</summary>
    public byte[]? AttachmentBytes { get; set; }
    public string? AttachmentFileName { get; set; }

    public byte[]? RowVersion { get; set; }

    public Developer Developer { get; set; } = null!;

    public int TotalDays => (EndDate - StartDate).Days + 1;
}
