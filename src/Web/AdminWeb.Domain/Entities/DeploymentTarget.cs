namespace AdminWeb.Domain.Entities;

public class DeploymentTarget
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Host { get; set; } = "";     // includes scheme: ftps://...
    public int Puerto { get; set; } = 21;
    public string Usuario { get; set; } = "";
    /// <summary>Cifrada con <c>SharedSecretProtector</c> (portable: la lee cualquier equipo). Puede
    /// quedar algún valor heredado con el cifrado DPAPI viejo, legible solo en la PC que lo guardó,
    /// hasta que <c>SharedSecretMigrationService</c> pase por ahí o se recapture.</summary>
    public string Contrasena { get; set; } = "";
    public string RutaRemota { get; set; } = "/";
    public string? URL { get; set; }
    public DateTime? LastDeployedAt { get; set; }
    public int? LastReleaseId { get; set; }

    /// <summary>
    /// Quién lanzó el último despliegue a ESTE servidor. Sin FK, igual que
    /// <c>DeploymentJob.StartedById</c>: es un dato histórico, y dar de baja a un usuario no debe
    /// borrar ni bloquear la respuesta a «¿quién dejó esta versión aquí?».
    /// </summary>
    public int? LastDeployedById { get; set; }

    /// <summary>Despliegue del que salió la versión que tiene hoy. Es el puente al Historial: con
    /// esto se llega al log completo y a la evidencia de ese despliegue.</summary>
    public int? LastDeploymentJobId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }

    public AppRelease? LastRelease { get; set; }
}
