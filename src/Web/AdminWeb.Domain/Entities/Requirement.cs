using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

public class Requirement
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public RequirementStatus Status { get; set; } = RequirementStatus.PorEstimar;
    public RequirementPriority Priority { get; set; } = RequirementPriority.Media;
    public decimal? EstimateHours { get; set; }
    public DateTime? RequestDate { get; set; }
    public DateTime? CommittedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public int ProgressPercent { get; set; } = 0;
    public RequirementSource Source { get; set; } = RequirementSource.Manual;
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }

    /// <summary>
    /// Segundos de trabajo ya reportados al ticket de Azure DevOps (para no volver a sumarlos). Solo
    /// aplica a requerimientos con <see cref="Source"/> = AzureDevOps; cada reporte envía el delta
    /// respecto de este valor.
    /// </summary>
    public int DevOpsReportedSeconds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Momento del último cambio de estado (para medir tiempo en el estado actual).</summary>
    public DateTime? StatusChangedAt { get; set; }
    public byte[]? RowVersion { get; set; }

    /// <summary>Sprint al que está comprometido, o null si está fuera de sprint (el backlog).</summary>
    public int? SprintId { get; set; }
    public Sprint? Sprint { get; set; }

    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
