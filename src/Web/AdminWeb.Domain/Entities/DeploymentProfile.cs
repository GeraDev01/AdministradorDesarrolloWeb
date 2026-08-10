namespace AdminWeb.Domain.Entities;

public class DeploymentProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool AllowedForOperaciones { get; set; } = false;
    /// <summary>Perfil interno creado al vuelo para una «selección directa» de servidores (estilo
    /// Blobup). No se muestra en las listas de perfiles; solo respalda el despliegue ad-hoc.</summary>
    public bool IsAdHoc { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public byte[]? RowVersion { get; set; }

    public ICollection<DeploymentProfileTarget> ProfileTargets { get; set; } = new List<DeploymentProfileTarget>();
}
