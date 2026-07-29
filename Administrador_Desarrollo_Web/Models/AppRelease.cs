namespace Administrador_Desarrollo_Web.Models;

public class AppRelease
{
    public int Id { get; set; }
    public int AppSystemId { get; set; }
    public string Version { get; set; } = "";
    public string? Changelog { get; set; }
    public string? ZipLocalPath { get; set; }
    public string? ZipBlobUrl { get; set; }

    /// <summary>
    /// Subcarpeta de destino en Blob Storage (QA, Operaciones, Productivo, un cliente…). Null en
    /// las versiones anteriores a que existieran las carpetas: esas quedaron en la raíz.
    /// </summary>
    public string? TargetFolder { get; set; }
    public string? ZipChecksum { get; set; }
    public long ZipSizeBytes { get; set; }
    public int? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppSystem AppSystem { get; set; } = null!;
    public ICollection<DeploymentJob> Jobs { get; set; } = new List<DeploymentJob>();
}
