namespace Administrador_Desarrollo_Web.Models;

public enum DeployLogLevel { Info = 0, Exito = 1, Advertencia = 2, Error = 3 }

public class DeploymentLogEntry
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int? TargetId { get; set; }
    public string? TargetName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = "";
    public DeployLogLevel Level { get; set; } = DeployLogLevel.Info;

    public DeploymentJob Job { get; set; } = null!;
}
