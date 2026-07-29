namespace Administrador_Desarrollo_Web.Models;

public enum JobStatus
{
    Pendiente = 0, EnCurso = 1, Completado = 2, Fallido = 3, Cancelado = 4,
    /// <summary>Unos servidores sí y otros no. Antes esto se reportaba como «Completado».</summary>
    Parcial = 5
}

public class DeploymentJob
{
    public int Id { get; set; }
    public int AppReleaseId { get; set; }
    public int DeploymentProfileId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pendiente;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? StartedById { get; set; }
    public string? Notes { get; set; }
    public int TargetsTotal { get; set; }
    public int TargetsOk { get; set; }
    public int TargetsFailed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppRelease AppRelease { get; set; } = null!;
    public DeploymentProfile Profile { get; set; } = null!;
    public ICollection<DeploymentLogEntry> LogEntries { get; set; } = new List<DeploymentLogEntry>();
}
