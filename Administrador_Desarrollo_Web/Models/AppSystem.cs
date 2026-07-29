namespace Administrador_Desarrollo_Web.Models;

public class AppSystem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Carpeta LOCAL de publish/build por defecto para nuevas versiones (opcional).</summary>
    public string? DefaultSourceFolder { get; set; }
    /// <summary>Subcarpeta de Blob Storage asociada al sistema (QA, Productivo, un cliente…): es la
    /// que se propone como destino al crear una nueva versión, para no elegirla cada vez.</summary>
    public string? DefaultBlobFolder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Equipo responsable del sistema (opcional).</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AppRelease> Releases { get; set; } = new List<AppRelease>();
}
