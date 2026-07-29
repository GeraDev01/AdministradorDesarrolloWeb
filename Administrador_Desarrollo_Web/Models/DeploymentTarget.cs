namespace Administrador_Desarrollo_Web.Models;

public class DeploymentTarget
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Host { get; set; } = "";     // includes scheme: ftps://...
    public int Puerto { get; set; } = 21;
    public string Usuario { get; set; } = "";
    public string Contrasena { get; set; } = ""; // DPAPI encrypted
    public string RutaRemota { get; set; } = "/";
    public string? URL { get; set; }
    public DateTime? LastDeployedAt { get; set; }
    public int? LastReleaseId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }

    public AppRelease? LastRelease { get; set; }
}
