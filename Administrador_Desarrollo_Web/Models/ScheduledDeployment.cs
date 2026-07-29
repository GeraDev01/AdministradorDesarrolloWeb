namespace Administrador_Desarrollo_Web.Models;

public enum ScheduledDeploymentStatus
{
    /// <summary>Esperando su hora.</summary>
    Programado = 0,
    /// <summary>Alguien lo tomó y lo está ejecutando.</summary>
    EnEjecucion = 1,
    Completado = 2,
    Fallido = 3,
    Cancelado = 4,
    /// <summary>Pasó su hora sin que ninguna aplicación estuviera abierta para ejecutarlo.</summary>
    Perdido = 5
}

/// <summary>
/// Despliegue agendado para una hora concreta.
///
/// Lo ejecuta la aplicación que esté abierta (incluida la que quedó en la bandeja del sistema).
/// Consecuencia que hay que asumir y que la UI advierte: si a esa hora no hay ninguna instancia
/// corriendo, el despliegue NO ocurre y se marca <see cref="ScheduledDeploymentStatus.Perdido"/>
/// en vez de dispararse tarde — desplegar a deshora sin que nadie lo espere es peor que no hacerlo.
/// </summary>
public class ScheduledDeployment
{
    public int Id { get; set; }

    public int AppReleaseId { get; set; }
    public int DeploymentProfileId { get; set; }

    /// <summary>Momento programado, en UTC.</summary>
    public DateTime ScheduledAtUtc { get; set; }

    public ScheduledDeploymentStatus Status { get; set; } = ScheduledDeploymentStatus.Programado;

    /// <summary>
    /// Equipo que tomó la ejecución. Evita que dos aplicaciones abiertas lancen el mismo despliegue.
    /// </summary>
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>Job resultante, cuando llegó a ejecutarse.</summary>
    public int? DeploymentJobId { get; set; }

    /// <summary>
    /// Cuánto se tolera arrancar tarde. Pasado ese margen se considera perdido en vez de ejecutarse
    /// a una hora que ya nadie esperaba.
    /// </summary>
    public int ToleranciaMinutos { get; set; } = 60;

    public string? Notes { get; set; }
    public string? ResultMessage { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppRelease AppRelease { get; set; } = null!;
    public DeploymentProfile Profile { get; set; } = null!;

    public bool EsHora(DateTime nowUtc) =>
        Status == ScheduledDeploymentStatus.Programado && nowUtc >= ScheduledAtUtc;

    public bool SePerdio(DateTime nowUtc) =>
        Status == ScheduledDeploymentStatus.Programado &&
        nowUtc > ScheduledAtUtc.AddMinutes(ToleranciaMinutos);
}
